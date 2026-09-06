namespace Statehop.Core.Model;

/// <summary>
/// Decides how much of an executable path may be persisted.
///
/// PRIVACY: the full path
/// is kept ONLY for executables that live under a system install directory.
/// For everything else only the file name is kept, never the directory.
///
/// Why this exists: the Phase 0 gate recorded paths such as
/// "...\repos\sample-app\src-tauri\target\release\build\..." — the
/// directory alone leaks project names and folder structure, and on a work
/// machine would leak client names. B1.2 banned window titles; it did not
/// cover this, and the file name is all the product needs for identity.
///
/// The rule is deliberately an allowlist. A denylist of "sensitive" folders
/// would have to guess what is sensitive, and would be wrong on the first
/// folder nobody thought of.
/// </summary>
public static class ExecutablePathPolicy
{
    /// <summary>
    /// Install roots whose full path is safe to keep. Resolved from the
    /// environment so this is not hard-coded to a C: drive, with literal
    /// fallbacks for the case where a variable is missing.
    ///
    /// "Program Files\WindowsApps" is where packaged (MSIX) apps install, so
    /// it is already covered by Program Files; it is named here anyway because
    /// packaged apps are the common case. The execution-alias folder
    /// "%LOCALAPPDATA%\Microsoft\WindowsApps" is deliberately NOT a root: it
    /// sits inside the user profile, and the stubs in it carry no identity
    /// that the file name does not already carry.
    /// </summary>
    public static IReadOnlyList<string> InstallRoots { get; } = BuildInstallRoots();

    /// <summary>
    /// Applies the policy. Null and empty stay as they are — a denied identity
    /// has no path at all, and that is not something to invent one for.
    /// </summary>
    public static string? Apply(string? executablePath) =>
        Apply(executablePath, InstallRoots);

    /// <summary>Policy against an explicit root list, so tests do not depend on the host.</summary>
    public static string? Apply(string? executablePath, IReadOnlyList<string> installRoots)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return executablePath;
        }

        if (IsUnderInstallRoot(executablePath, installRoots))
        {
            return executablePath;
        }

        // GetFileName returns the whole string when there is no separator,
        // which is exactly right for an already-bare name.
        var fileName = Path.GetFileName(executablePath.TrimEnd('\\', '/'));
        return string.IsNullOrEmpty(fileName) ? null : fileName;
    }

    /// <summary>Whether the full path may be kept.</summary>
    public static bool IsUnderInstallRoot(string executablePath) =>
        IsUnderInstallRoot(executablePath, InstallRoots);

    public static bool IsUnderInstallRoot(string executablePath, IReadOnlyList<string> installRoots)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        var candidate = Normalise(executablePath);

        foreach (var root in installRoots)
        {
            var normalisedRoot = Normalise(root).TrimEnd('\\');
            if (normalisedRoot.Length == 0)
            {
                continue;
            }

            // The trailing separator matters: without it "C:\Program Files"
            // would also match "C:\Program FilesEvil\payload.exe".
            if (candidate.StartsWith(normalisedRoot + "\\", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string Normalise(string path) =>
        path.Replace('/', '\\').ToLowerInvariant();

    private static List<string> BuildInstallRoots()
    {
        var roots = new List<string>();

        void Add(string? value)
        {
            if (!string.IsNullOrWhiteSpace(value) && !roots.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                roots.Add(value);
            }
        }

        var windows = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
        var programFiles = Environment.GetEnvironmentVariable("ProgramFiles") ?? @"C:\Program Files";
        var programFilesX86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)") ?? @"C:\Program Files (x86)";

        Add(programFiles);
        Add(programFilesX86);
        // Set only inside 32-bit processes, where %ProgramFiles% is redirected.
        Add(Environment.GetEnvironmentVariable("ProgramW6432"));
        Add(Path.Combine(programFiles, "WindowsApps"));
        // System32 and SysWOW64 are both inside the Windows directory, so one
        // root covers them; they need no separate rules.
        Add(windows);

        return roots;
    }
}
