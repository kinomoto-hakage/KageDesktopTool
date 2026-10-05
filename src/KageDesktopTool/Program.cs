using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Kage.Workspace;

namespace Kage.Desktop;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--publish-check")) return PublishChecks.Run();
        if (args.Contains("--release-workflow-check")) return ReleaseWorkflowChecks.Run().GetAwaiter().GetResult();
        if (args.Contains("--session-check")) return SessionChecks.Run(args.Contains("--instance-only"));
        if (args.Contains("--desktop-recovery-check")) return DesktopRecoveryChecks.Run();
        if (args.Contains("--manual-desktop-check")) return ManualDesktopChecks.Run();
        if (args.Contains("--content-layout-check")) return ContentLayoutChecks.Run();
        if (args.Contains("--layout-input-check")) return LayoutInputChecks.Run();
        if (args.Contains("--content-input-check")) return ContentInputChecks.Run(args.Contains("--identity-only"), args.Contains("--refresh-only"), args.Contains("--menu-placement-only"), args.Contains("--menu-latency-only"));
        if (args.Contains("--shell-diagnostics-check")) return ShellDiagnosticsChecks.Run(args.Contains("--icons-only"), args.Contains("--cancel-only"));
        if (args.Contains("--shell-image-check")) return ShellImageChecks.Run();
        if (args.Contains("--display-dpi-check")) return DisplayDpiChecks.Run();
        if (args.Contains("--appearance-check")) return AppearanceChecks.Run();
        if (args.Contains("--settings-notification-check")) return SettingsNotificationChecks.Run(args.Contains("--notification-only"), args.Contains("--settings-only"));
        if (args.Contains("--file-move-check")) return FileMoveChecks.Run();
        if (args.Contains("--folder-action-check")) return FolderActionChecks.Run();
        if (args.Contains("--root-migration-check")) return RootMigrationChecks.Run();
        if (args.Contains("--migration-recovery-check")) return MigrationRecoveryChecks.Run();
        if (args.Length == 2 && args[0] == "--open-target-check")
        {
            File.WriteAllText(args[1], "隔离目标已被 Windows Shell 打开");
            return 0;
        }
        if (args.Length == 2 && args[0] == "--instance-check")
        {
            using var check = new SingleInstance(args[1]);
            if (check.IsOwner) return 2;
            check.SendSettingsAsync().GetAwaiter().GetResult();
            return 0;
        }
        if (args.Length == 3 && args[0] == "--instance-result-check" && Guid.TryParse(args[2], out var resultId))
        {
            using var check = new SingleInstance(args[1]);
            if (check.IsOwner) return 2;
            check.SendResultAsync(resultId).GetAwaiter().GetResult();
            return 0;
        }
        var stateDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KageDesktopTool");
        Runtime? runtime = null;
        var activation = new TaskCompletionSource<Guid?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var toastLaunch = false;
        string? notificationRegistrationError = null;
        try
        {
            WindowsNotifications.Register(argument =>
            {
                var id = WindowsNotifications.ResultId(argument);
                activation.TrySetResult(id);
                runtime?.Dispatch(() => runtime.ShowResults(id));
            });
            toastLaunch = WindowsNotifications.WasActivated;
        }
        catch (Exception e) { notificationRegistrationError = e.Message; }
        using var instance = new SingleInstance(stateDirectory);
        if (!instance.IsOwner)
        {
            try
            {
                if (toastLaunch)
                {
                    var id = activation.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                    instance.SendResultAsync(id).GetAwaiter().GetResult();
                }
                else instance.SendSettingsAsync().GetAwaiter().GetResult();
                return 0;
            }
            catch (Exception e) { MessageBox.Show($"程序已运行，但设置请求未送达：{e.Message}", "Kage 桌面整理"); return 1; }
        }
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        application.DispatcherUnhandledException += (_, e) =>
        {
            runtime?.Complete("操作异常", new OperationResult(Outcome.Failed, e.Exception.Message));
            e.Handled = true;
        };
        application.Startup += async (_, _) =>
        {
            var workspace = new DesktopWorkspace(new JsonWorkspaceStore(stateDirectory),
                new WindowsStartup(Path.Combine(AppContext.BaseDirectory, "KageDesktopTool.exe")));
            await workspace.InitializeAsync(WindowsDesktop.Displays());
            runtime = new Runtime(workspace, stateDirectory);
            if (notificationRegistrationError != null)
                runtime.Complete("通知注册", new OperationResult(Outcome.Failed, "系统通知注册不可用；操作结果仍会保存：" + notificationRegistrationError));
            instance.Listen(() => runtime.Dispatch(runtime.ShowSettings), message => runtime.Dispatch(() => runtime.ReportStatus(message)), id => runtime.Dispatch(() => runtime.ShowResults(id)));
            if (activation.Task.IsCompletedSuccessfully) runtime.ShowResults(activation.Task.Result);
            else if (!toastLaunch && (workspace.Snapshot.Notices.Count != 0 || workspace.Snapshot.RecoveryRequired)) runtime.ShowSettings();
        };
        try { application.Run(); }
        finally { runtime?.Dispose(); WindowsNotifications.Unregister(); }
        return 0;
    }
}
