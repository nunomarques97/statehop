namespace Statehop.Core.Model;

/// <summary>
/// Processes that are inaccessible to any unelevated app, packaged or not.
///
/// docs/adr/001-packaging.md measured these empirically: the same 20 processes
/// denied MainModule access in both packaged and unpackaged runs. They are a
/// property of Windows integrity levels, not of this app, so the B0.2 number
/// excludes them — counting them would inflate the figure with processes no
/// design choice can reach.
/// </summary>
public static class SystemProcesses
{
    private static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    {
        "Idle",
        "System",
        "Secure System",
        "Registry",
        "smss",
        "csrss",
        "wininit",
        "services",
        "lsass",
        "winlogon",
        "MemCompression",
        "Memory Compression",
    };

    public static bool IsProtected(string processName) => Protected.Contains(processName);
}
