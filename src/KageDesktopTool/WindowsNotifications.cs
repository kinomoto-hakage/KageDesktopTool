using System;
using System.IO;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Microsoft.Windows.AppLifecycle;

namespace Kage.Desktop;

internal static class WindowsNotifications
{
    internal static void Register(Action<string> activated)
    {
        AppNotificationManager.Default.NotificationInvoked += (_, args) => activated(args.Argument);
        AppNotificationManager.Default.Register();
    }

    internal static bool WasActivated => AppInstance.GetCurrent().GetActivatedEventArgs().Kind == ExtendedActivationKind.AppNotification;

    internal static void Send(OperationDetails result)
    {
        if (AppNotificationManager.Default.Setting != AppNotificationSetting.Enabled)
            throw new IOException("Windows 已关闭或限制本应用通知：" + AppNotificationManager.Default.Setting);
        var notification = new AppNotificationBuilder().AddArgument("result", result.Id.ToString("N"))
            .AddText("Kage 桌面工具 · " + result.Title)
            .AddText(result.Summary.Length > 240 ? result.Summary[..240] + "…" : result.Summary)
            .AddText("点击查看结果详情")
            .BuildNotification();
        AppNotificationManager.Default.Show(notification);
        if (notification.Id == 0) throw new IOException("Windows 未接受通知。");
    }

    internal static void Unregister()
    {
        try { AppNotificationManager.Default.Unregister(); }
        catch (Exception) { }
    }

    internal static Guid? ResultId(string arguments)
    {
        try
        {
            var argument = Array.Find(arguments.Split('&'), item => item.StartsWith("result=", StringComparison.Ordinal));
            return argument != null && Guid.TryParse(Uri.UnescapeDataString(argument[7..]), out var id) ? id : null;
        }
        catch (Exception) { return null; }
    }
}
