using System.Runtime.InteropServices;
using Statehop.Observation.Interop;

namespace Statehop.Observation.Watchers;

/// <summary>
/// Registers a system-wide hotkey against a message-only window.
///
/// RegisterHotKey is per-HWND and the owning window must pump messages, so the
/// caller supplies a <see cref="HiddenMessageWindow"/> created on the UI thread.
/// Registration fails if another process already owns the combination — that
/// is reported rather than swallowed, because a silently dead hotkey is the
/// kind of unreliability Phase 0 exists to catch.
/// </summary>
public sealed class GlobalHotkey : IDisposable
{
    private const int HotkeyId = 0xA71;

    private readonly HiddenMessageWindow _window;
    private bool _registered;
    private bool _disposed;

    public event EventHandler? Pressed;

    /// <summary>Human-readable combination, for display in the UI.</summary>
    public string Combination { get; }

    public GlobalHotkey(HiddenMessageWindow window, uint modifiers, uint virtualKey, string combination)
    {
        _window = window;
        Combination = combination;

        _window.MessageReceived += OnMessage;

        if (!NativeMethods.RegisterHotKey(
                window.Handle, HotkeyId, modifiers | NativeMethods.MOD_NOREPEAT, virtualKey))
        {
            var error = Marshal.GetLastWin32Error();
            _window.MessageReceived -= OnMessage;
            throw new InvalidOperationException(
                $"RegisterHotKey failed for {combination} (Win32 error {error}). " +
                "The combination is probably already owned by another process.");
        }

        _registered = true;
    }

    private void OnMessage(object? sender, WindowMessageEventArgs e)
    {
        if (e.Message == NativeMethods.WM_HOTKEY && e.WParam.ToInt32() == HotkeyId)
        {
            e.Handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
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
        if (_registered)
        {
            NativeMethods.UnregisterHotKey(_window.Handle, HotkeyId);
            _registered = false;
        }
    }
}
