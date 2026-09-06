using Statehop.Core.Model;

namespace Statehop.Tests;

/// <summary>
/// The rule that decides how much of a path reaches disk.
/// The roots are passed in explicitly
/// so these assertions do not depend on how the host machine is laid out.
/// </summary>
[TestClass]
public sealed class ExecutablePathPolicyTests
{
    private static readonly string[] Roots =
    [
        @"C:\Program Files",
        @"C:\Program Files (x86)",
        @"C:\Program Files\WindowsApps",
        @"C:\Windows",
    ];

    [TestMethod]
    public void PathUnderProgramFiles_IsKeptWhole()
    {
        const string path = @"C:\Program Files\Google\Chrome\Application\chrome.exe";

        Assert.AreEqual(path, ExecutablePathPolicy.Apply(path, Roots));
    }

    [TestMethod]
    public void PathUnderSystem32_IsKeptWhole()
    {
        const string path = @"C:\Windows\System32\ApplicationFrameHost.exe";

        Assert.AreEqual(path, ExecutablePathPolicy.Apply(path, Roots));
    }

    [TestMethod]
    public void PathInAUserProject_LosesItsDirectory()
    {
        // The real leak found by the Phase 0 gate: the directory names the
        // project, and on a work machine would name the client.
        const string path =
            @"C:\Users\alex\source\repos\sample-app\src-tauri\target\release\build\x\build-script-build.exe";

        Assert.AreEqual("build-script-build.exe", ExecutablePathPolicy.Apply(path, Roots));
    }

    [TestMethod]
    public void PathInTheUserProfile_LosesItsDirectory()
    {
        Assert.AreEqual(
            "claude.exe",
            ExecutablePathPolicy.Apply(@"C:\Users\alex\AppData\Local\AnthropicClaude\app-1\claude.exe", Roots));
    }

    [TestMethod]
    public void RootMatching_RespectsDirectoryBoundaries()
    {
        // Without the trailing separator "C:\Program Files" would also swallow
        // this one, and the whole directory would be kept.
        Assert.AreEqual(
            "payload.exe",
            ExecutablePathPolicy.Apply(@"C:\Program FilesEvil\payload.exe", Roots));
    }

    [TestMethod]
    public void MatchingIsCaseAndSeparatorInsensitive()
    {
        const string path = @"c:/PROGRAM FILES/Git/bin/bash.exe";

        Assert.AreEqual(path, ExecutablePathPolicy.Apply(path, Roots));
    }

    [TestMethod]
    public void NullPath_StaysNull()
    {
        // A denied identity has no path; inventing one would be worse.
        Assert.IsNull(ExecutablePathPolicy.Apply(null, Roots));
    }

    [TestMethod]
    public void BareFileName_IsLeftAlone()
    {
        Assert.AreEqual("sleep.exe", ExecutablePathPolicy.Apply("sleep.exe", Roots));
    }

    [TestMethod]
    public void HostRoots_CoverTheWindowsAndProgramFilesDirectories()
    {
        // The default roots come from the environment, so this is the one
        // assertion that checks the machine and not the algorithm.
        Assert.IsTrue(ExecutablePathPolicy.IsUnderInstallRoot(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "cmd.exe")));
        Assert.IsTrue(ExecutablePathPolicy.IsUnderInstallRoot(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "App", "app.exe")));
        Assert.IsFalse(ExecutablePathPolicy.IsUnderInstallRoot(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Clients", "tool.exe")));
    }
}
