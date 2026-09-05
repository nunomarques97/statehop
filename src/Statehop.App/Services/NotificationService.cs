using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Statehop.Observation.Interop;

namespace Statehop.App.Services;

/// <summary>
/// Local toast notifications through the Windows App SDK.
///
/// Phase 0 only needs to prove the channel works end to end, because later
/// phases depend on it: a suggestion the user never sees is the same as no
/// suggestion. Nothing here leaves the machine — an app notification is
/// rendered by the local shell, not sent anywhere.
///
/// Notification text must follow the UX rule in docs/PRODUCT.md: probabilistic
/// and reversible wording, never "X is inactive, so it is safe to close".
/// </summary>
public sealed class NotificationService : IDisposable
{
    private readonly HostPrivilegeState _privileges;

    private bool _registered;

    public NotificationService(HostPrivilegeState privileges) => _privileges = privileges;

    /// <summary>Last failure, for display in the main window.</summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// Registers the notification channel.
    ///
    /// Order matters: the invoked handler has to be attached before Register,
    /// or a click that arrives during startup is lost.
    /// </summary>
    /// <param name="onInvoked">Called when the user clicks a notification.</param>
    public void Register(Action onInvoked)
    {
        if (!_privileges.CanShowNotifications)
        {
            // Windows drops notifications from elevated processes without
            // raising an error, so say so up front instead of letting every
            // later Show report a meaningless refusal.
            LastError = "processo elevado — o Windows não entrega notificações a apps elevadas";
            return;
        }

        try
        {
            AppNotificationManager.Default.NotificationInvoked += (_, _) => onInvoked();
            AppNotificationManager.Default.Register();
            _registered = true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
    }

    /// <summary>Shows a notification. Returns false and records why on failure.</summary>
    public bool Show(string title, string body)
    {
        if (!_registered)
        {
            LastError ??= "gestor de notificações não registado";
            return false;
        }

        try
        {
            var notification = new AppNotificationBuilder()
                .AddText(title)
                .AddText(body)
                .BuildNotification();

            AppNotificationManager.Default.Show(notification);

            // The Show call is fire-and-forget; Id stays 0 when the platform
            // rejected it, which is the only signal available synchronously.
            if (notification.Id == 0)
            {
                LastError = "a plataforma recusou a notificação (Id 0)";
                return false;
            }

            LastError = null;
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return false;
        }
    }

    public void Dispose()
    {
        if (!_registered)
        {
            return;
        }

        try
        {
            AppNotificationManager.Default.Unregister();
        }
        catch (Exception)
        {
            // Shutting down.
        }

        _registered = false;
    }
}
