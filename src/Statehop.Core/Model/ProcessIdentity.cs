namespace Statehop.Core.Model;

/// <summary>
/// Whether this app (running unelevated, by design)
/// was able to read the identity of an observed process.
/// </summary>
public enum ProcessAccessState
{
    /// <summary>Executable path was readable.</summary>
    Accessible,

    /// <summary>Access denied — the target runs at a higher integrity level.</summary>
    Denied,

    /// <summary>The process ended before it could be inspected.</summary>
    Exited,
}

/// <summary>
/// The identity of an observed process. Deliberately narrow: process name,
/// executable path and access state only.
///
/// PRIVACY: window titles are never part of process
/// identity and are never persisted. A title can carry client names, file
/// names, URLs and email subjects.
/// </summary>
/// <param name="Name">Process name without extension, e.g. "devenv".</param>
/// <param name="ExecutablePath">Full path, or null when access was denied.</param>
/// <param name="AccessState">Whether the executable path could be read.</param>
public sealed record ProcessIdentity(
    string Name,
    string? ExecutablePath,
    ProcessAccessState AccessState)
{
    public static ProcessIdentity Denied(string name) =>
        new(name, null, ProcessAccessState.Denied);
}
