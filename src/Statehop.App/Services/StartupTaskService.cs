using Windows.ApplicationModel;

namespace Statehop.App.Services;

/// <summary>
/// Start with Windows, through the MSIX StartupTask extension declared in
/// Package.appxmanifest.
///
/// This is the only mechanism that works for a packaged app: the HKCU Run key
/// and the Startup folder are ignored (docs/adr/001-packaging.md). It is also
/// the more honest one — the entry shows up in Task Manager's Startup tab, so
/// the user can revoke it without going near Statehop. Once the user disables
/// it there, Windows reports DisabledByUser and refuses further programmatic
/// enabling, which this class surfaces rather than retrying.
/// </summary>
public sealed class StartupTaskService
{
    private const string TaskId = "StatehopStartupTask";

    /// <summary>Whether the app currently launches with Windows.</summary>
    public bool IsEnabled { get; private set; }

    /// <summary>Human-readable state, including why a toggle was refused.</summary>
    public string StatusText { get; private set; } = "por determinar";

    /// <summary>True when this build has no package identity, so there is nothing to toggle.</summary>
    public bool IsAvailable { get; private set; }

    public async Task RefreshAsync()
    {
        try
        {
            var task = await StartupTask.GetAsync(TaskId);
            IsAvailable = true;
            Apply(task.State);
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            IsEnabled = false;
            StatusText = $"indisponível ({ex.GetType().Name})";
        }
    }

    /// <summary>Turns start-with-Windows on or off, mirroring the current state.</summary>
    public async Task ToggleAsync()
    {
        try
        {
            var task = await StartupTask.GetAsync(TaskId);
            IsAvailable = true;

            if (task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy)
            {
                task.Disable();
                Apply((await StartupTask.GetAsync(TaskId)).State);
                return;
            }

            // May show a system consent prompt the first time, and returns the
            // resulting state rather than throwing when the user says no.
            Apply(await task.RequestEnableAsync());
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            IsEnabled = false;
            StatusText = $"falhou ({ex.GetType().Name})";
        }
    }

    private void Apply(StartupTaskState state)
    {
        IsEnabled = state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
        StatusText = state switch
        {
            StartupTaskState.Disabled => "desligado",
            StartupTaskState.DisabledByUser => "desligado pelo utilizador (reativar no Gestor de Tarefas)",
            StartupTaskState.DisabledByPolicy => "bloqueado por política do sistema",
            StartupTaskState.Enabled => "ligado",
            StartupTaskState.EnabledByPolicy => "ligado por política do sistema",
            _ => state.ToString(),
        };
    }
}
