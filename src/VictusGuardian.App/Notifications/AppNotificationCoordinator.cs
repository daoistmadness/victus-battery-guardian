using System;
using System.Threading.Tasks;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using VictusGuardian.App.Diagnostics;
using VictusGuardian.Core.Alerts;
using VictusGuardian.Core.Configuration;
using VictusGuardian.Windows.Notifications;

namespace VictusGuardian.App.Notifications;

public interface IAppNotificationCoordinator
{
    void AttachNotificationManager(INotificationManager manager);
    Task DispatchAlertAsync(BatteryAlert alert, Action? onRestoreWindow = null);
}

public class AppNotificationCoordinator : IAppNotificationCoordinator
{
    private readonly IWindowsDesktopNotificationService _windowsNotificationService;
    private readonly ISettingsService _settingsService;
    private INotificationManager? _inAppManager;

    public AppNotificationCoordinator(
        IWindowsDesktopNotificationService windowsNotificationService,
        ISettingsService settingsService)
    {
        _windowsNotificationService = windowsNotificationService;
        _settingsService = settingsService;
    }

    public void AttachNotificationManager(INotificationManager manager)
    {
        _inAppManager = manager;
    }

    public async Task DispatchAlertAsync(BatteryAlert alert, Action? onRestoreWindow = null)
    {
        if (!_settingsService.CurrentSettings.EnableNotifications)
        {
            return;
        }

        AppLogger.LogInfo($"Dispatching alert notification: [{alert.Level}] {alert.Title} - {alert.Message}");

        // 1. Try Windows native toast notification
        bool toastDelivered = false;
        try
        {
            toastDelivered = await _windowsNotificationService.ShowToastAsync(alert.Title, alert.Message, alert.Level);
        }
        catch (Exception ex)
        {
            AppLogger.LogWarning($"Native toast notification failed: {ex.Message}");
        }

        // 2. Fall back to or augment with in-app notification if available
        if (!toastDelivered && _inAppManager != null)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var notificationType = alert.Level switch
                {
                    BatteryAlertLevel.Critical => NotificationType.Error,
                    BatteryAlertLevel.Warning => NotificationType.Warning,
                    _ => NotificationType.Information
                };

                _inAppManager.Show(new Notification(
                    alert.Title,
                    alert.Message,
                    notificationType,
                    TimeSpan.FromSeconds(6),
                    onClick: onRestoreWindow));
            });
        }
    }
}
