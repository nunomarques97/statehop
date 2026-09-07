using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Statehop.App.Services;
using Statehop.Core.Observation;
using Statehop.Storage;
using Statehop.App.Tray;
using Statehop.Observation.Interop;
using Statehop.Observation.Watchers;

namespace Statehop.App;

/// <summary>
/// Application entry point.
///
/// Statehop starts in the notification area, not in a window: observation is
/// the product, the window is only a viewer. So OnLaunched wires up the tray
/// icon, the observation layer and the global hotkey, and deliberately does
/// not activate the main window.
/// </summary>
public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\Statehop.SingleInstance";

    private Mutex? _instanceMutex;
    private HiddenMessageWindow? _messageWindow;
    private TrayIcon? _trayIcon;
    private GlobalHotkey? _hotkey;
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
        Notifications = new NotificationService(Privileges);
        UnhandledException += OnUnhandledException;
    }

    public static new App Current => (App)Application.Current;

    /// <summary>The observation layer. Null only before startup finishes.</summary>
    public ObservationService? Observation { get; private set; }

    public StartupTaskService StartupTask { get; } = new();

    /// <summary>Privilege the process actually got, elevation included.</summary>
    public HostPrivilegeState Privileges { get; } = HostPrivileges.Current();

    public NotificationService Notifications { get; }

    /// <summary>Which hotkey combination actually registered, for display.</summary>
    public string HotkeyStatus { get; private set; } = "não registada";

    /// <summary>Unhandled XAML exceptions seen this session — a Phase 0 gate signal.</summary>
    public int UnhandledExceptions { get; private set; }

    public DispatcherQueue? Dispatcher { get; private set; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // A second instance would put a second icon in the tray and a second
        // writer on the database. The mutex is released when the process ends.
        _instanceMutex = new Mutex(true, SingleInstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            Exit();
            return;
        }

        Dispatcher = DispatcherQueue.GetForCurrentThread();

        // Created but not activated: the window exists so the XAML application
        // always has one, and the user sees nothing until they ask for it.
        _window = new MainWindow();

        // Registered before anything else can raise a notification, and before
        // any activation argument is read, as the platform requires.
        Notifications.Register(ShowMainWindow);

        // Hosts the tray callback and WM_HOTKEY. Created on the UI thread,
        // whose message loop dispatches to it.
        _messageWindow = new HiddenMessageWindow("StatehopMessageWindow");

        // Composition root. The observation layer itself lives in
        // Statehop.Core and knows nothing about WinUI or about SQLite; this is
        // the only place where the concrete Win32 watchers and the concrete
        // store are named.
        Observation = new ObservationService(
            new SqliteActivityStore(AppPaths.DatabaseFile),
            new ForegroundWatcher(),
            new IdleWatcher(ObservationService.IdleThreshold),
            new ProcessWatcher(ObservationService.ProcessPollInterval),
            new WindowOwnerProbeRunner(ObservationService.AccessProbeInterval));
        Observation.Start();

        SetUpTray();
        SetUpHotkey();

        _ = InitialiseStartupTaskAsync();

        Notifications.Show(
            "Statehop está a observar",
            "A correr na área de notificações. Ctrl+Alt+S abre a janela.");
    }

    private void SetUpTray()
    {
        _trayIcon = new TrayIcon(
            _messageWindow!,
            AppPaths.TrayIconFile,
            "Statehop — a observar");

        _trayIcon.OpenRequested += (_, _) => ShowMainWindow();
        _trayIcon.ExitRequested += (_, _) => Shutdown();
        _trayIcon.StartupToggleRequested += async (_, _) =>
        {
            await StartupTask.ToggleAsync();
            _trayIcon.StartupEnabled = StartupTask.IsEnabled;
        };

        Observation!.Updated += (_, _) => Dispatcher?.TryEnqueue(() =>
            _trayIcon?.SetTooltip($"Statehop — {Observation.CurrentForeground}"));
    }

    private void SetUpHotkey()
    {
        // Global hotkeys are first-come-first-served across the whole session,
        // so a single hard-coded combination can simply be unavailable. Try a
        // few and report which one won rather than failing silently.
        (HotkeyModifiers Modifiers, uint Key, string Label)[] candidates =
        [
            (HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x53, "Ctrl+Alt+S"),
            (HotkeyModifiers.Control | HotkeyModifiers.Shift | HotkeyModifiers.Alt, 0x53, "Ctrl+Shift+Alt+S"),
            (HotkeyModifiers.Windows | HotkeyModifiers.Alt, 0x53, "Win+Alt+S"),
        ];

        foreach (var (modifiers, key, label) in candidates)
        {
            try
            {
                _hotkey = new GlobalHotkey(_messageWindow!, (uint)modifiers, key, label);
                _hotkey.Pressed += (_, _) => ShowMainWindow();
                HotkeyStatus = label;
                return;
            }
            catch (InvalidOperationException)
            {
                // Combination already owned by another process; try the next.
            }
        }

        HotkeyStatus = "nenhuma combinação disponível";
    }

    private async Task InitialiseStartupTaskAsync()
    {
        await StartupTask.RefreshAsync();
        if (_trayIcon is not null)
        {
            _trayIcon.StartupEnabled = StartupTask.IsEnabled;
        }
    }

    /// <summary>Brings the viewer window up, creating nothing new.</summary>
    public void ShowMainWindow()
    {
        if (_window is null)
        {
            return;
        }

        _window.AppWindow.Show();
        _window.Activate();
    }

    /// <summary>Real exit, from the tray menu only. Closing the window just hides it.</summary>
    public void Shutdown()
    {
        _hotkey?.Dispose();
        _trayIcon?.Dispose();
        Observation?.Dispose();
        Notifications.Dispose();
        _messageWindow?.Dispose();
        _instanceMutex?.Dispose();
        Exit();
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        // Counted for the Phase 0 reliability report. An observer that dies
        // halfway through the day fails the gate whatever else it does.
        UnhandledExceptions++;
    }
}
