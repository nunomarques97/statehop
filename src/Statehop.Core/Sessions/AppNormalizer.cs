namespace Statehop.Core.Sessions;

/// <summary>The result of turning a raw process name into an application.</summary>
/// <param name="Key">
/// Stable, case-insensitive identity of the application. Colour assignment and
/// every aggregation key off this, never off the display name.
/// </param>
public sealed record NormalizedApp(string Key, string DisplayName, AppKind Kind);

/// <summary>
/// Turns the process name behind a foreground window into
/// the application a person would say they were using.
///
/// The rules here are grounded in one real measured day (6 Sep 2026,
/// 1 862 foreground events over 29 process names), not invented:
///
///   - <c>LockApp</c> held the foreground for 95 minutes in a single event.
///     A locked screen is absence; counting it as an application would have
///     made it the third "app" of the day.
///   - <c>SearchHost</c>, <c>StartMenuExperienceHost</c>, <c>ShellHost</c>,
///     <c>PickerHost</c> and friends take the foreground when the user opens
///     the Start menu or a file dialog. The user did not switch application.
///   - <c>explorer</c> took the foreground 480 times for 20.8 minutes total,
///     and <c>SnippingTool</c> 157 times for 3.8 minutes. Those are real
///     applications with real jitter, so they are NOT special-cased here —
///     the short-switch rule in <see cref="SessionBuilder"/> handles them.
///     A name list would have to grow forever; a duration rule does not.
///
/// Anything unknown is an application under its own name. The display table
/// only ever improves a label, never decides behaviour.
/// </summary>
public static class AppNormalizer
{
    private static readonly HashSet<string> LockScreens = new(StringComparer.OrdinalIgnoreCase)
    {
        "LockApp",
        "LogonUI",
    };

    private static readonly HashSet<string> ShellSurfaces = new(StringComparer.OrdinalIgnoreCase)
    {
        "SearchHost",
        "SearchApp",
        "StartMenuExperienceHost",
        "ShellExperienceHost",
        "ShellHost",
        "PickerHost",
        "OpenWith",
        "TextInputHost",
        "ApplicationFrameHost",
    };

    // Labels only. An entry missing here costs a nicer name and nothing else.
    private static readonly Dictionary<string, string> DisplayNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Code"] = "Visual Studio Code",
            ["devenv"] = "Visual Studio",
            ["chrome"] = "Google Chrome",
            ["msedge"] = "Microsoft Edge",
            ["firefox"] = "Firefox",
            ["explorer"] = "Explorador de Ficheiros",
            ["WindowsTerminal"] = "Terminal",
            ["powershell"] = "PowerShell",
            ["pwsh"] = "PowerShell",
            ["cmd"] = "Linha de Comandos",
            ["mintty"] = "Git Bash",
            ["qemu-system-x86_64"] = "QEMU",
            ["SnippingTool"] = "Ferramenta de Captura",
            ["Notepad"] = "Bloco de Notas",
            ["Photos"] = "Fotografias",
            ["Statehop.App"] = "Statehop",
            ["claude"] = "Claude Code",
        };

    public static NormalizedApp Normalize(string processName)
    {
        var name = (processName ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            return new NormalizedApp(string.Empty, "Desconhecida", AppKind.Application);
        }

        var key = name.ToLowerInvariant();

        if (LockScreens.Contains(name))
        {
            return new NormalizedApp(key, "Ecrã bloqueado", AppKind.LockScreen);
        }

        var kind = ShellSurfaces.Contains(name) ? AppKind.ShellSurface : AppKind.Application;
        var display = DisplayNames.TryGetValue(name, out var better) ? better : name;

        return new NormalizedApp(key, display, kind);
    }
}
