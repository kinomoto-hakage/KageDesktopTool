using System;
using System.IO;
using System.Linq;
using System.Windows;
using Kage.Workspace;

namespace Kage.Desktop;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--session-check")) return SessionChecks.Run();
        if (args.Contains("--content-layout-check")) return ContentLayoutChecks.Run();
        if (args.Contains("--appearance-check")) return AppearanceChecks.Run();
        if (args.Contains("--file-move-check")) return FileMoveChecks.Run();
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
        var stateDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KageDesktopTool");
        using var instance = new SingleInstance(stateDirectory);
        if (!instance.IsOwner)
        {
            try { instance.SendSettingsAsync().GetAwaiter().GetResult(); return 0; }
            catch (Exception e) { MessageBox.Show($"程序已运行，但设置请求未送达：{e.Message}", "Kage 桌面整理"); return 1; }
        }
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Runtime? runtime = null;
        application.DispatcherUnhandledException += (_, e) =>
        {
            MessageBox.Show(e.Exception.Message, "Kage 桌面整理");
            e.Handled = true;
        };
        application.Startup += async (_, _) =>
        {
            var workspace = new DesktopWorkspace(new JsonWorkspaceStore(stateDirectory),
                new WindowsStartup(Path.Combine(AppContext.BaseDirectory, "KageDesktopTool.exe")));
            await workspace.InitializeAsync(WindowsDesktop.Displays());
            runtime = new Runtime(workspace);
            instance.Listen(() => runtime.Dispatch(runtime.ShowSettings), message => runtime.Dispatch(() => runtime.Balloon(message)));
            if (workspace.Snapshot.Notices.Count != 0 || workspace.Snapshot.RecoveryRequired) runtime.ShowSettings();
        };
        try { application.Run(); }
        finally { runtime?.Dispose(); }
        return 0;
    }
}
