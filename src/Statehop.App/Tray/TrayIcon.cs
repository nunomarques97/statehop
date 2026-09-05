using Statehop.Observation.Interop;

namespace Statehop.App.Tray;

/// <summary>
/// The notification-area icon and its context menu.
///
/// Statehop lives here rather than in a window: the main window is a viewer
/// the user opens occasionally, while observation runs whether or not
/// anything is on screen.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const uint IconId = 1;

    private const int CommandOpen = 1;
    private const int CommandStartup = 2;
    private const int CommandExit = 3;

    private readonly HiddenMessageWindow _window;
    private readonly uint _taskbarCreatedMessage;

    private IntPtr _iconHandle;
    private string _tooltip;
    private bool _added;
    private bool _disposed;

    /// <summary>The user asked for the main window.</summary>
    public event EventHandler? OpenRequested;

    /// <summary>The user toggled "start with Windows".</summary>
    public event EventHandler? StartupToggleRequested;

    /// <summary>The user chose Exit.</summary>
    public event EventHandler? ExitRequested;

    /// <summary>Drives the check mark on the startup menu item.</summary>
    public bool StartupEnabled { get; set; }

    public TrayIcon(HiddenMessageWindow window, string iconPath, string tooltip)
    {
        _window = window;
        _tooltip = tooltip;

        // Explorer can restart (or crash) during a long session, which wipes
        // every tray icon. Explorer then broadcasts TaskbarCreated so apps can
        // put theirs back. Without this the icon silently disappears for the
        // rest of the day.
        _taskbarCreatedMessage = TrayInterop.RegisterWindowMessage("TaskbarCreated");

        _iconHandle = TrayInterop.LoadImage(
            IntPtr.Zero,
            iconPath,
            TrayInterop.IMAGE_ICON,
            0,
            0,
            TrayInterop.LR_LOADFROMFILE | TrayInterop.LR_DEFAULTSIZE);

        _window.MessageReceived += OnMessage;
        Add();
    }

    private void Add()
    {
        var data = CreateData(TrayInterop.NIF_MESSAGE | TrayInterop.NIF_ICON
            | TrayInterop.NIF_TIP | TrayInterop.NIF_SHOWTIP);
        _added = TrayInterop.Shell_NotifyIcon(TrayInterop.NIM_ADD, ref data);

        if (!_added)
        {
            return;
        }

        // Opt into version-4 callbacks. Windows 11 routes tray icons through
        // the hidden-icons flyout, which reports interactions as NIN_SELECT
        // and WM_CONTEXTMENU rather than raw mouse messages, and hands back
        // the anchor point to open the menu at. Without this the icon looks
        // fine and simply does nothing when clicked.
        var version = CreateData(0);
        version.uVersion = TrayInterop.NOTIFYICON_VERSION_4;
        TrayInterop.Shell_NotifyIcon(TrayInterop.NIM_SETVERSION, ref version);
    }

    /// <summary>Updates the hover text, e.g. with what is currently in front.</summary>
    public void SetTooltip(string tooltip)
    {
        // szTip is a fixed 128-character buffer; overflowing it fails the call.
        _tooltip = tooltip.Length > 127 ? tooltip[..127] : tooltip;

        if (!_added)
        {
            return;
        }

        var data = CreateData(TrayInterop.NIF_TIP | TrayInterop.NIF_SHOWTIP);
        TrayInterop.Shell_NotifyIcon(TrayInterop.NIM_MODIFY, ref data);
    }

    private TrayInterop.NOTIFYICONDATA CreateData(uint flags) => new()
    {
        cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<TrayInterop.NOTIFYICONDATA>(),
        hWnd = _window.Handle,
        uID = IconId,
        uFlags = flags,
        uCallbackMessage = TrayInterop.WM_TRAY_CALLBACK,
        hIcon = _iconHandle,
        szTip = _tooltip,
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
    };

    private void OnMessage(object? sender, WindowMessageEventArgs e)
    {
        if (e.Message == _taskbarCreatedMessage)
        {
            Add();
            return;
        }

        if (e.Message != TrayInterop.WM_TRAY_CALLBACK)
        {
            return;
        }

        e.Handled = true;

        var lParam = (uint)e.LParam.ToInt64();
        var notification = (int)(lParam & 0xFFFF);
        var iconId = (lParam >> 16) & 0xFFFF;

        // Version-4 puts the icon id in the high word of lParam and the anchor
        // point in wParam. Legacy callbacks put the mouse message in lParam and
        // the icon id in wParam, and are still possible if NIM_SETVERSION was
        // refused, so both shapes are accepted.
        var isVersion4 = iconId == IconId;

        var wParam = (uint)e.WParam.ToInt64();
        var anchorX = (short)(wParam & 0xFFFF);
        var anchorY = (short)((wParam >> 16) & 0xFFFF);

        switch (notification)
        {
            case TrayInterop.NIN_SELECT when isVersion4:
            case TrayInterop.NIN_KEYSELECT when isVersion4:
            case TrayInterop.WM_LBUTTONUP when !isVersion4:
                OpenRequested?.Invoke(this, EventArgs.Empty);
                break;

            case TrayInterop.WM_CONTEXTMENU when isVersion4:
                ShowMenu(anchorX, anchorY);
                break;

            case TrayInterop.WM_RBUTTONUP when !isVersion4:
                TrayInterop.GetCursorPos(out var cursor);
                ShowMenu(cursor.X, cursor.Y);
                break;
        }
    }

    private void ShowMenu(int x, int y)
    {
        var menu = TrayInterop.CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return;
        }

        try
        {
            TrayInterop.AppendMenu(menu, TrayInterop.MF_STRING, (UIntPtr)CommandOpen, "Abrir Statehop");
            TrayInterop.AppendMenu(menu, TrayInterop.MF_SEPARATOR, UIntPtr.Zero, null);
            TrayInterop.AppendMenu(
                menu,
                TrayInterop.MF_STRING | (StartupEnabled ? TrayInterop.MF_CHECKED : TrayInterop.MF_UNCHECKED),
                (UIntPtr)CommandStartup,
                "Arrancar com o Windows");
            TrayInterop.AppendMenu(menu, TrayInterop.MF_SEPARATOR, UIntPtr.Zero, null);
            TrayInterop.AppendMenu(menu, TrayInterop.MF_STRING, (UIntPtr)CommandExit, "Sair");

            // Required dance for tray menus: foreground the owner first, or the
            // menu will not close when the user clicks away; post a dummy
            // message afterwards so the menu loop lets go of the queue.
            TrayInterop.SetForegroundWindow(_window.Handle);

            var command = TrayInterop.TrackPopupMenuEx(
                menu,
                TrayInterop.TPM_RIGHTBUTTON | TrayInterop.TPM_RETURNCMD | TrayInterop.TPM_NONOTIFY,
                x,
                y,
                _window.Handle,
                IntPtr.Zero);

            TrayInterop.PostMessage(_window.Handle, TrayInterop.WM_NULL, IntPtr.Zero, IntPtr.Zero);

            switch (command)
            {
                case CommandOpen:
                    OpenRequested?.Invoke(this, EventArgs.Empty);
                    break;

                case CommandStartup:
                    StartupToggleRequested?.Invoke(this, EventArgs.Empty);
                    break;

                case CommandExit:
                    ExitRequested?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }
        finally
        {
            TrayInterop.DestroyMenu(menu);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _window.MessageReceived -= OnMessage;

        if (_added)
        {
            var data = CreateData(0);
            TrayInterop.Shell_NotifyIcon(TrayInterop.NIM_DELETE, ref data);
            _added = false;
        }

        if (_iconHandle != IntPtr.Zero)
        {
            TrayInterop.DestroyIcon(_iconHandle);
            _iconHandle = IntPtr.Zero;
        }
    }
}
