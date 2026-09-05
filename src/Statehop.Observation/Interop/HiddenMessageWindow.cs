using System.Runtime.InteropServices;

namespace Statehop.Observation.Interop;

/// <summary>
/// A hidden top-level window used to receive Win32 messages the app has no
/// other way to get: WM_HOTKEY from RegisterHotKey, and the tray icon
/// callback from Shell_NotifyIcon.
///
/// It is a real top-level window with no visible style, not an HWND_MESSAGE
/// window. HWND_MESSAGE would be the obvious choice and does deliver both
/// message types, but such a window belongs to no desktop and can never be
/// foregrounded — so TrackPopupMenu on it silently draws nothing, and the tray
/// context menu never appears. WS_EX_TOOLWINDOW keeps it out of the taskbar
/// and Alt+Tab.
///
/// Create it on a thread that already pumps messages. In this app that is the
/// WinUI UI thread, whose DispatcherQueue runs a standard Win32 message loop,
/// so this window's WndProc is called without any extra plumbing.
/// </summary>
public sealed class HiddenMessageWindow : IDisposable
{
    private readonly NativeMethods.WndProc _wndProc;
    private readonly string _className;
    private bool _disposed;

    /// <summary>
    /// Raised for every message. Set <c>Handled</c> and <c>Result</c> to stop
    /// the message reaching DefWindowProc.
    /// </summary>
    public event EventHandler<WindowMessageEventArgs>? MessageReceived;

    public IntPtr Handle { get; }

    public HiddenMessageWindow(string classNamePrefix)
    {
        // The delegate must stay rooted for as long as the window exists;
        // if it is collected, the next dispatched message crashes the process.
        _wndProc = OnMessage;
        _className = $"{classNamePrefix}_{Guid.NewGuid():N}";

        var wc = new NativeMethods.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEX>(),
            lpfnWndProc = _wndProc,
            hInstance = Marshal.GetHINSTANCE(typeof(HiddenMessageWindow).Module),
            lpszClassName = _className,
        };

        if (NativeMethods.RegisterClassEx(ref wc) == 0)
        {
            throw new InvalidOperationException(
                $"RegisterClassEx failed: {Marshal.GetLastWin32Error()}");
        }

        Handle = NativeMethods.CreateWindowEx(
            NativeMethods.WS_EX_TOOLWINDOW,
            _className,
            null,
            NativeMethods.WS_POPUP,
            0, 0, 0, 0,
            IntPtr.Zero,
            IntPtr.Zero,
            wc.hInstance,
            IntPtr.Zero);

        if (Handle == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"CreateWindowEx failed: {Marshal.GetLastWin32Error()}");
        }
    }

    private IntPtr OnMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        var args = new WindowMessageEventArgs(msg, wParam, lParam);
        MessageReceived?.Invoke(this, args);
        return args.Handled
            ? args.Result
            : NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (Handle != IntPtr.Zero)
        {
            NativeMethods.DestroyWindow(Handle);
        }
    }
}

public sealed class WindowMessageEventArgs(uint message, IntPtr wParam, IntPtr lParam) : EventArgs
{
    public uint Message { get; } = message;

    public IntPtr WParam { get; } = wParam;

    public IntPtr LParam { get; } = lParam;

    public bool Handled { get; set; }

    public IntPtr Result { get; set; }
}
