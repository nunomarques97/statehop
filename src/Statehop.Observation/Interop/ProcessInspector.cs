using System.Diagnostics;
using System.Text;
using Statehop.Core.Model;

namespace Statehop.Observation.Interop;

/// <summary>
/// Resolves a pid into the narrow identity the product records: process name,
/// executable path, and whether this unelevated app could read it.
///
/// Identity is read through OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION) +
/// QueryFullProcessImageName rather than Process.MainModule. Both answer the
/// same question, but the limited-information right is the one Windows grants
/// across integrity levels, so it fails on far fewer processes and needs no
/// module enumeration. docs/adr/001-packaging.md measured MainModule; the B0.2
/// probe reports both so the two numbers stay comparable.
/// </summary>
public static class ProcessInspector
{
    /// <summary>Reads identity for a pid, never throwing.</summary>
    public static ProcessIdentity Resolve(int pid, string? knownName = null)
    {
        var name = knownName ?? SafeProcessName(pid);
        if (name is null)
        {
            return new ProcessIdentity($"pid:{pid}", null, ProcessAccessState.Exited);
        }

        var path = TryGetImagePath(pid);
        return path is null
            ? new ProcessIdentity(name, null, ProcessAccessState.Denied)
            : new ProcessIdentity(name, path, ProcessAccessState.Accessible);
    }

    /// <summary>Full image path, or null when access was denied or the process ended.</summary>
    public static string? TryGetImagePath(int pid)
    {
        var handle = NativeMethods.OpenProcess(
            NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var buffer = new StringBuilder(1024);
            var size = (uint)buffer.Capacity;
            return NativeMethods.QueryFullProcessImageName(handle, 0, buffer, ref size)
                ? buffer.ToString(0, (int)size)
                : null;
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    /// <summary>
    /// Whether Process.MainModule is readable. Kept only so the B0.2 report can
    /// be compared with the ADR 001 measurement; the product does not use it.
    /// </summary>
    public static bool CanReadMainModule(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.MainModule?.FileName is not null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Process creation time from GetProcessTimes, when readable.</summary>
    public static DateTime? TryGetStartTimeUtc(int pid)
    {
        var handle = NativeMethods.OpenProcess(
            NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return NativeMethods.GetProcessTimes(handle, out var creation, out _, out _, out _)
                ? DateTime.FromFileTimeUtc(creation)
                : null;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    private static string? SafeProcessName(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.ProcessName;
        }
        catch
        {
            return null;
        }
    }
}
