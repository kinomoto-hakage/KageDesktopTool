using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Kage.Workspace;

namespace Kage.Desktop;

// 人工 Win+D 验收使用独立工作区；托盘退出后清理随机夹具。
internal static class ManualDesktopChecks
{
    internal static int Run()
    {
        var fixture = Path.Combine(Path.GetTempPath(), "Kage-manual-desktop-" + Guid.NewGuid().ToString("N"));
        var evidence = Path.Combine(Environment.CurrentDirectory, ".scratch", "desktop-folder", "verification");
        Directory.CreateDirectory(evidence);
        var log = Path.Combine(evidence, "manual-desktop-session.txt");
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Runtime? runtime = null;
        var exit = 1;
        app.Startup += async (_, _) =>
        {
            try
            {
                IDesktopWorkspace workspace = new DesktopWorkspace(new JsonWorkspaceStore(Path.Combine(fixture, "状态")), new NoStartup());
                await Require(workspace.InitializeAsync(WindowsDesktop.Displays()));
                await Require(workspace.SelectRootAsync(Path.Combine(fixture, "内容")));
                await Require(workspace.CreateFolderAsync("人工验收 A"));
                await Require(workspace.CreateFolderAsync("人工验收 B"));
                foreach (var folder in workspace.Snapshot.Folders)
                {
                    File.WriteAllText(Path.Combine(folder.ActualPath, "仅用于验收.txt"), "随机隔离内容，可测试打开和拖放。");
                    await Require(workspace.ToggleFolderAsync(folder.Folder.Id));
                }
                await Require(workspace.RefreshAsync(WindowsDesktop.Displays()));
                runtime = new Runtime(workspace);
                File.WriteAllText(log, $"人工夹具就绪：{DateTimeOffset.Now:O}\n{fixture}\n");
                exit = 0;
            }
            catch (Exception e) { File.AppendAllText(log, "失败：" + e + "\n"); app.Shutdown(); }
        };
        try { app.Run(); }
        finally
        {
            runtime?.Dispose();
            var full = Path.GetFullPath(fixture);
            if (!full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(full).StartsWith("Kage-manual-desktop-", StringComparison.Ordinal))
                throw new IOException("拒绝清理隔离目录之外的内容。");
            if (Directory.Exists(full)) Directory.Delete(full, true);
            File.AppendAllText(log, $"夹具已退出并清理：{DateTimeOffset.Now:O}\n");
        }
        return exit;
    }

    private static async Task Require(Task<OperationResult> operation)
    { var result = await operation; if (!result.Succeeded) throw new IOException(result.Message); }
    private sealed class NoStartup : IStartupRegistration
    {
        public string LaunchCommand => "人工隔离验收不注册自启";
        public string? ReadCommand() => null;
        public void WriteCommand(string? command) => throw new InvalidOperationException("人工验收不应修改自启。");
    }
}
