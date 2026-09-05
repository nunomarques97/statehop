using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Statehop.Observation.Interop;

/// <summary>Windows integrity level of a process, lowest to highest.</summary>
public enum IntegrityLevel
{
    Unknown = 0,
    Untrusted = 1,
    Low = 2,
    Medium = 3,
    High = 4,
    System = 5,
}

/// <summary>
/// How much privilege this process is running with.
/// </summary>
/// <param name="IsElevated">Token reports elevation.</param>
/// <param name="Integrity">Integrity level of the process token.</param>
/// <param name="UacEnabled">Whether UAC is on for the machine.</param>
public sealed record HostPrivilegeState(bool IsElevated, IntegrityLevel Integrity, bool UacEnabled)
{
    /// <summary>
    /// Whether the B0.2 measurement means anything.
    ///
    /// The question B0.2 asks is how much of the user's world is invisible to
    /// an app that never elevates. Measured from an elevated process the
    /// answer is always "none", because an elevated observer can read
    /// everything — a comforting number that says nothing about the product.
    /// </summary>
    public bool CanMeasureElevatedAccess => !IsElevated && Integrity <= IntegrityLevel.Medium;

    /// <summary>
    /// App notifications are not delivered for elevated processes; Windows
    /// drops them without an error. Documented platform behaviour, not a bug
    /// in this app.
    /// </summary>
    public bool CanShowNotifications => !IsElevated;
}

/// <summary>
/// Reads the privilege the process was granted.
///
/// Statehop never asks for Administrator, but it can still inherit elevation
/// from whatever launched it — and on a machine with UAC switched off every
/// process runs high, whether it wanted to or not. Both cases silently distort
/// what the app observes, so they are detected and reported rather than
/// assumed away.
/// </summary>
public static class HostPrivileges
{
    private const uint TOKEN_QUERY = 0x0008;
    private const int TokenElevation = 20;
    private const int TokenIntegrityLevel = 25;

    public static HostPrivilegeState Current()
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();

        if (!OpenProcessToken(process.Handle, TOKEN_QUERY, out var token))
        {
            return new HostPrivilegeState(false, IntegrityLevel.Unknown, IsUacEnabled());
        }

        try
        {
            return new HostPrivilegeState(ReadElevation(token), ReadIntegrity(token), IsUacEnabled());
        }
        finally
        {
            CloseHandle(token);
        }
    }

    private static bool ReadElevation(IntPtr token) =>
        GetTokenInformation(token, TokenElevation, out var elevated, sizeof(uint), out _) && elevated != 0;

    private static IntegrityLevel ReadIntegrity(IntPtr token)
    {
        GetTokenInformation(token, TokenIntegrityLevel, IntPtr.Zero, 0, out var size);
        if (size == 0)
        {
            return IntegrityLevel.Unknown;
        }

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (!GetTokenInformation(token, TokenIntegrityLevel, buffer, size, out _))
            {
                return IntegrityLevel.Unknown;
            }

            // TOKEN_MANDATORY_LABEL starts with a SID_AND_ATTRIBUTES whose Sid
            // pointer is the first field; the last sub-authority is the RID.
            var sid = Marshal.ReadIntPtr(buffer);
            var subAuthorityCount = Marshal.ReadByte(sid, 1);
            var rid = (uint)Marshal.ReadInt32(sid, 8 + ((subAuthorityCount - 1) * 4));

            return rid switch
            {
                >= 0x4000 => IntegrityLevel.System,
                >= 0x3000 => IntegrityLevel.High,
                >= 0x2000 => IntegrityLevel.Medium,
                >= 0x1000 => IntegrityLevel.Low,
                _ => IntegrityLevel.Untrusted,
            };
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static bool IsUacEnabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System");
            return key?.GetValue("EnableLUA") is int value && value != 0;
        }
        catch (Exception)
        {
            // Reading the policy is a diagnostic, never worth failing over.
            return true;
        }
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(
        IntPtr token, int infoClass, out uint info, uint length, out uint returned);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(
        IntPtr token, int infoClass, IntPtr info, uint length, out uint returned);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
