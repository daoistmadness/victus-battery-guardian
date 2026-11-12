using System;
using System.Diagnostics;
using System.Threading.Tasks;
using VictusGuardian.Core.Alerts;

namespace VictusGuardian.Windows.Notifications;

public interface IWindowsDesktopNotificationService
{
    bool IsSupported { get; }
    string DeliveryMethod { get; }
    Task<bool> ShowToastAsync(string title, string message, BatteryAlertLevel level);
}

public class WindowsDesktopNotificationService : IWindowsDesktopNotificationService
{
    public bool IsSupported => OperatingSystem.IsWindows();

    public string DeliveryMethod => OperatingSystem.IsWindows()
        ? "Windows Action Center (Toast Notification)"
        : "In-App Notification Banner";

    public async Task<bool> ShowToastAsync(string title, string message, BatteryAlertLevel level)
    {
        if (!OperatingSystem.IsWindows())
            return false;

        try
        {
            var cleanTitle = title.Replace("'", "").Replace('\"', ' ');
            var cleanMessage = message.Replace("'", "").Replace('\"', ' ');

            var script = 
                "$ErrorActionPreference = 'Stop'; " +
                "[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null; " +
                "$template = [Windows.UI.Notifications.ToastNotificationManager]::GetTemplateContent([Windows.UI.Notifications.ToastTemplateType]::ToastText02); " +
                "$strings = $template.GetElementsByTagName('text'); " +
                $"$strings[0].AppendChild($template.CreateTextNode('{cleanTitle}')) | Out-Null; " +
                $"$strings[1].AppendChild($template.CreateTextNode('{cleanMessage}')) | Out-Null; " +
                "$toast = [Windows.UI.Notifications.ToastNotification]::new($template); " +
                "[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('Victus Battery Guardian').Show($toast);";

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{script}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var proc = Process.Start(psi);
            if (proc == null) return false;

            var completed = await Task.Run(() => proc.WaitForExit(3000));
            return completed && proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
