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
        if (path is null)
        {
            return new ProcessIdentity(name, null, ProcessAccessState.Denied);
        }

        // The directory is dropped here, at the moment of capture, so a full
        // path outside the install directories never reaches memory that
        // outlives this call, the window, or the store
        // (see ExecutablePathPolicy).
        return new ProcessIdentity(
            name, ExecutablePathPolicy.Apply(path), ProcessAccessState.Accessible);
    }

    /// <summary>
    /// Raw full image path, or null when access was denied or the process
    /// ended. Callers that persist or display this must put it through
    /// <see cref="ExecutablePathPolicy"/> first; <see cref="Resolve"/> does.
    /// </summary>
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
