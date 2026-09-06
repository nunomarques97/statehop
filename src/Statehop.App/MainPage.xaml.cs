using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Statehop.App.Services;

namespace Statehop.App;

/// <summary>
/// The Phase 0 viewer: what is being observed right now, what the spike had to
/// prove, and the two numbers the gate asks for (B0.2 and database growth).
///
/// This is not the product's timeline — that is Phase 1. It exists so the
/// user can see, without a debugger, that observation is actually running.
/// </summary>
public sealed partial class MainPage : Page
{
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    private bool _suppressToggleEvent;

    public MainPage()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _refreshTimer.Tick += (_, _) => Refresh();
        _refreshTimer.Start();
        Refresh();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => _refreshTimer.Stop();

    private void Refresh()
    {
        var app = App.Current;
        var observation = app.Observation;
        if (observation is null)
        {
            return;
        }

        UpdatePrivilegeWarning(app);

        ForegroundValue.Text = observation.CurrentForeground;
        IdleValue.Text = observation.IsIdle ? "inativo" : "ativo";
        ProcessesValue.Text = observation.ProcessesInLastSnapshot.ToString();

        HotkeyValue.Text = app.HotkeyStatus;

        StartupValue.Text = app.StartupTask.StatusText;
        StartupToggle.IsEnabled = app.StartupTask.IsAvailable;
        if (StartupToggle.IsOn != app.StartupTask.IsEnabled)
        {
            // Setting IsOn raises Toggled, which would bounce straight back
            // into the service and fight the user.
            _suppressToggleEvent = true;
            StartupToggle.IsOn = app.StartupTask.IsEnabled;
            _suppressToggleEvent = false;
        }

        NotificationValue.Text = app.Notifications.LastError is null ? "a funcionar" : "com erro";

        var elevated = observation.Store.GetElevatedAccessReport();
        ElevatedHeadline.Text = elevated.WindowOwnersObserved == 0
            ? "ainda sem observações"
            : app.Privileges.CanMeasureElevatedAccess
                ? $"{elevated.Denied} de {elevated.WindowOwnersObserved} apps com janela ({elevated.DeniedPercent:0.0} %)"
                : $"medição inválida — processo elevado ({elevated.WindowOwnersObserved} apps observadas)";
        ElevatedDetail.Text = elevated.Denied == 0
            ? $"Nenhuma app com janela recusou leitura de identidade. {elevated.SystemProtectedExcluded} processos SYSTEM/protegidos excluídos."
            : $"Sem identidade legível: {string.Join(", ", elevated.DeniedNames)}. "
              + $"{elevated.SystemProtectedExcluded} processos SYSTEM/protegidos excluídos.";

        var stats = observation.Store.GetStats();
        StatsValue.Text =
            $"{stats.ForegroundEvents} foreground · {stats.IdleEvents} idle · "
            + $"{stats.LifetimeEvents} processos · {stats.DistinctProcesses} apps distintas · "
            + $"{stats.DatabaseBytes / 1024.0:0.0} KB";

        // Classification, not filtering: these rows are all still on disk. The
        // number is here because it is the one that says how much of the store
        // is toolchain churn.
        var ephemeralShare = stats.LifetimeEvents == 0
            ? 0
            : 100.0 * stats.EphemeralLifetimeEvents / stats.LifetimeEvents;
        EphemeralValue.Text =
            $"Classificados como efémeros: {stats.EphemeralProcesses} de {stats.DistinctProcesses} apps · "
            + $"{stats.EphemeralLifetimeEvents} de {stats.LifetimeEvents} eventos de processo ({ephemeralShare:0.0} %). "
            + "Nada é descartado — a marca existe para que um leitor possa filtrar.";

        DatabasePathValue.Text = observation.Store.DatabasePath;

        var uptime = DateTime.UtcNow - observation.StartedAtUtc;
        ReliabilityValue.Text =
            $"{uptime.TotalMinutes:0} min a observar · {observation.RecordingErrors} erros de escrita · "
            + $"{app.UnhandledExceptions} exceções não tratadas";

        var lastError = observation.LastErrorMessage ?? app.Notifications.LastError;
        LastErrorValue.Text = lastError ?? string.Empty;
        LastErrorValue.Visibility = lastError is null ? Visibility.Collapsed : Visibility.Visible;

        ActivityList.ItemsSource = observation.RecentActivity();
    }

    /// <summary>
    /// An elevated Statehop still observes, but two of its answers stop being
    /// true: B0.2 always reads zero, and notifications never arrive. Saying so
    /// in the window is the difference between a measurement and a number.
    /// </summary>
    private void UpdatePrivilegeWarning(App app)
    {
        var privileges = app.Privileges;
        if (privileges.CanMeasureElevatedAccess && privileges.CanShowNotifications)
        {
            PrivilegeWarning.Visibility = Visibility.Collapsed;
            return;
        }

        PrivilegeWarning.Visibility = Visibility.Visible;
        PrivilegeWarningDetail.Text =
            $"Integridade do processo: {privileges.Integrity}. "
            + (privileges.UacEnabled
                ? "O Statehop nunca pede Administrador, mas herdou elevação de quem o lançou. "
                : "O UAC está desligado nesta máquina, por isso todos os processos correm elevados. ")
            + "Enquanto isto se mantiver, a medição B0.2 dá sempre zero (um observador elevado vê tudo) "
            + "e o Windows não entrega notificações. Observação, hotkey e gravação continuam válidas.";
    }

    private async void OnStartupToggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToggleEvent)
        {
            return;
        }

        await App.Current.StartupTask.ToggleAsync();
        Refresh();
    }

    private void OnTestNotification(object sender, RoutedEventArgs e)
    {
        // Wording follows the UX rule in docs/PRODUCT.md even in a test toast:
        // suggestions are probabilistic, never "safe to close".
        // The toast header already carries the app name, so the first line is a
        // real title rather than "Statehop" a second time.
        App.Current.Notifications.Show(
            "Canal de notificações a funcionar",
            "Nesta fase o Statehop só observa — não sugere nem age.");
        Refresh();
    }

    private void OnExportReport(object sender, RoutedEventArgs e)
    {
        var observation = App.Current.Observation;
        if (observation is null)
        {
            return;
        }

        try
        {
            var path = Path.Combine(AppPaths.DataFolder, "statehop-gate-report.md");
            File.WriteAllText(path, BuildReport(), new UTF8Encoding(false));
            ExportStatus.Text = path;
        }
        catch (Exception ex)
        {
            ExportStatus.Text = $"falhou: {ex.Message}";
        }
    }

    /// <summary>
    /// The numbers gathered at the end of a day-long run, written to a file so
    /// they can be pasted into a report without transcribing them by hand.
    /// </summary>
    private static string BuildReport()
    {
        var app = App.Current;
        var observation = app.Observation!;
        var stats = observation.Store.GetStats();
        var elevated = observation.Store.GetElevatedAccessReport();
        var uptime = DateTime.UtcNow - observation.StartedAtUtc;

        var report = new StringBuilder();
        report.AppendLine("# Statehop — relatório do gate da Phase 0");
        report.AppendLine();
        report.AppendLine($"Gerado: {DateTime.Now:yyyy-MM-dd HH:mm}");
        report.AppendLine($"Tempo de observação nesta sessão: {uptime.TotalHours:0.0} h");
        report.AppendLine();
        report.AppendLine("## B0.2 — apps invisíveis a um observador não elevado");
        report.AppendLine();
        if (!app.Privileges.CanMeasureElevatedAccess)
        {
            report.AppendLine(
                $"> **NÚMERO INVÁLIDO.** O processo correu elevado (integridade "
                + $"{app.Privileges.Integrity}, UAC "
                + (app.Privileges.UacEnabled ? "ligado" : "desligado")
                + "). Um observador elevado lê a identidade de tudo, por isso a "
                + "contagem abaixo dá sempre zero e não responde a B0.2.");
            report.AppendLine();
        }

        report.AppendLine($"- Processos com janela visível observados: {elevated.WindowOwnersObserved}");
        report.AppendLine($"- Com identidade inacessível: {elevated.Denied} ({elevated.DeniedPercent:0.0} %)");
        report.AppendLine($"- Nomes: {(elevated.Denied == 0 ? "nenhum" : string.Join(", ", elevated.DeniedNames))}");
        report.AppendLine($"- Processos SYSTEM/protegidos excluídos da contagem: {elevated.SystemProtectedExcluded}");
        report.AppendLine();
        report.AppendLine("## B1.1 — crescimento dos dados");
        report.AppendLine();
        report.AppendLine($"- Ficheiro: {observation.Store.DatabasePath}");
        report.AppendLine($"- Tamanho: {stats.DatabaseBytes / 1024.0:0.0} KB");
        report.AppendLine($"- Eventos de foreground: {stats.ForegroundEvents}");
        report.AppendLine($"- Eventos de idle: {stats.IdleEvents}");
        report.AppendLine($"- Eventos de ciclo de vida de processos: {stats.LifetimeEvents}");
        report.AppendLine($"- Apps distintas: {stats.DistinctProcesses}");
        report.AppendLine($"- Classificadas como efémeras: {stats.EphemeralProcesses}");
        report.AppendLine(
            $"- Eventos de processo de apps efémeras: {stats.EphemeralLifetimeEvents} "
            + $"(de {stats.LifetimeEvents}) — classificação, nada foi descartado");
        report.AppendLine();
        report.AppendLine("## Fiabilidade");
        report.AppendLine();
        report.AppendLine($"- Erros de escrita: {observation.RecordingErrors}");
        report.AppendLine($"- Exceções não tratadas: {app.UnhandledExceptions}");
        report.AppendLine($"- Hotkey global: {app.HotkeyStatus}");
        report.AppendLine($"- Arranque com o Windows: {app.StartupTask.StatusText}");
        report.AppendLine($"- Notificações: {app.Notifications.LastError ?? "sem erros"}");
        report.AppendLine($"- Integridade do processo: {app.Privileges.Integrity} "
            + $"(elevado: {(app.Privileges.IsElevated ? "sim" : "não")}, "
            + $"UAC: {(app.Privileges.UacEnabled ? "ligado" : "desligado")})");
        report.AppendLine();
        report.AppendLine(
            "Nota de privacidade: nenhum título de janela foi lido ou guardado. O caminho "
            + "do executável só é guardado inteiro dentro de diretórios de instalação do "
            + "sistema; fora daí guarda-se só o nome do ficheiro.");

        return report.ToString();
    }
}
