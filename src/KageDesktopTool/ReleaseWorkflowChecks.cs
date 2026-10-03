using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kage.Workspace;

namespace Kage.Desktop;

// 从正式发布 EXE 执行组合流程；仅对随机隔离内容、状态及自启项产生副作用。
internal static class ReleaseWorkflowChecks
{
    internal static async Task<int> Run()
    {
        var fixture = Path.Combine(Path.GetTempPath(), "Kage-release-" + Guid.NewGuid().ToString("N"));
        var evidence = Path.Combine(Environment.CurrentDirectory, ".scratch", "desktop-folder", "verification");
        Directory.CreateDirectory(evidence);
        Directory.CreateDirectory(fixture);
        var log = Path.Combine(evidence, "release-workflow.txt");
        File.WriteAllText(log, $"13 发布包组合流程：{Environment.ProcessPath}\n");
        var startup = new WindowsStartup(Path.Combine(AppContext.BaseDirectory, "KageDesktopTool.exe"), "KageDesktopTool.ReleaseCheck." + Guid.NewGuid().ToString("N"));
        var desktop = Path.Combine(fixture, "隔离桌面");
        Directory.CreateDirectory(desktop);
        var shell = new WindowsFolderShell(desktop);
        var store = new JsonWorkspaceStore(Path.Combine(fixture, "状态"));
        var transfer = new InterruptedTransfer();
        var displays = WindowsDesktop.Displays();
        var exit = 1;
        try
        {
            IDesktopWorkspace workspace = new DesktopWorkspace(store, startup, shell, transfer);
            await Require(workspace.InitializeAsync(displays));
            Check(workspace.Snapshot.Folders.Count == 0 && workspace.Snapshot.IconChoice == "d", "首次启动无示例，默认 D");
            var root = Path.Combine(fixture, "内容");
            await Require(workspace.SelectRootAsync(root));
            await Require(workspace.CreateFolderAsync("工作"));
            await Require(workspace.CreateFolderAsync("灵感"));
            var first = workspace.Snapshot.Folders.Single(f => f.Folder.Name == "工作");
            var second = workspace.Snapshot.Folders.Single(f => f.Folder.Name == "灵感");
            var input = Path.Combine(fixture, "输入");
            Directory.CreateDirectory(Path.Combine(input, "子目录"));
            File.WriteAllBytes(Path.Combine(input, "内容.bin"), [0, 13, 255, 72]);
            File.WriteAllText(Path.Combine(input, "子目录", "嵌套.txt"), "嵌套内容保持");
            shell.CreateShortcut(Path.Combine(input, "实际链接.lnk"), second.ActualPath);
            var moved = await workspace.MoveAsync([Path.Combine(input, "内容.bin"), Path.Combine(input, "子目录"), Path.Combine(input, "实际链接.lnk")], MoveTarget.Folder(first.Folder.Id));
            Check(moved.Items.All(item => item.Outcome == Outcome.Success) && moved.StateCommit.Succeeded
                && Directory.GetFileSystemEntries(input).Length == 0, "批量移入文件、真实快捷方式及子目录，源消失");
            File.WriteAllText(Path.Combine(second.ActualPath, "内容.bin"), "原目标不得覆盖");
            moved = await workspace.MoveAsync([Path.Combine(first.ActualPath, "内容.bin"), Path.Combine(first.ActualPath, "子目录")],
                MoveTarget.Folder(second.Folder.Id), _ => Task.FromResult(ConflictChoice.KeepBoth));
            Check(moved.Items.All(item => item.Outcome == Outcome.Success)
                && File.ReadAllBytes(Path.Combine(second.ActualPath, "内容 (2).bin")).SequenceEqual(new byte[] { 0, 13, 255, 72 })
                && File.ReadAllText(Path.Combine(second.ActualPath, "内容.bin")) == "原目标不得覆盖", "Folder 间批量移动及编号冲突保持两份字节");
            moved = await workspace.MoveAsync([Path.Combine(second.ActualPath, "内容 (2).bin"), Path.Combine(second.ActualPath, "子目录")], MoveTarget.Directory(input));
            Check(moved.Items.All(item => item.Outcome == Outcome.Success)
                && File.ReadAllText(Path.Combine(input, "子目录", "嵌套.txt")) == "嵌套内容保持"
                && !File.Exists(Path.Combine(second.ActualPath, "内容 (2).bin")) && !Directory.Exists(Path.Combine(second.ActualPath, "子目录")), "批量移出后核对目录、字节与源消失");
            await Require(workspace.ToggleFolderAsync(first.Folder.Id));
            await Require(workspace.SetViewAsync(first.Folder.Id, false));
            var appearance = workspace.BeginAppearance(first.Folder.Id)!;
            await Require(Task.FromResult(appearance.SetHex("#235A81")));
            await Require(Task.FromResult(appearance.SetOpacity(.55)));
            await Require(workspace.ApplyAppearanceAsync(appearance));
            await Require(workspace.SetIconAsync("b"));
            await Require(workspace.RenameFolderAsync(first.Folder.Id, "工作已改名"));
            first = workspace.Snapshot.Folders.Single(f => f.Folder.Id == first.Folder.Id);
            Check(first.Folder.Color == "#235A81" && !first.Folder.Grid && first.Folder.Expanded
                && shell.ShortcutTargets(Path.Combine(first.ActualPath, "实际链接.lnk"), second.ActualPath), "改名保留展开、列表、外观及链接原目标");
            await Require(workspace.DeleteFolderAsync(second.Folder.Id, FolderDeleteChoice.KeepContents));
            var link = Path.Combine(desktop, "灵感.lnk");
            Check(shell.ShortcutTargets(link, second.ActualPath), "保留内容生成真实 Shell 快捷方式");
            var newRoot = Path.Combine(fixture, "迁移后内容");
            await Require(workspace.MigrateRootAsync(newRoot));
            Check(!Directory.Exists(first.ActualPath) && !Directory.Exists(second.ActualPath)
                && shell.ShortcutTargets(link, Path.Combine(newRoot, "灵感")), "根迁移覆盖活动及保留内容，链接更新");
            await Require(workspace.SetStartupAsync(true));
            Check(startup.ReadCommand() == $"\"{Path.Combine(AppContext.BaseDirectory, "KageDesktopTool.exe")}\" --background", "随机真实自启项指向当前发布包完整路径");
            var records = workspace.Snapshot.Folders.Select(f => f.Folder).ToArray();
            workspace = new DesktopWorkspace(store, startup, shell, transfer);
            await Require(workspace.InitializeAsync(displays));
            Check(workspace.Snapshot.Root == newRoot && workspace.Snapshot.IconChoice == "b" && workspace.Snapshot.StartupEnabled
                && workspace.Snapshot.Folders.Select(f => f.Folder).SequenceEqual(records), "重启读取持久根目录、稳定标识、布局、外观与自启");
            await Require(workspace.SetStartupAsync(false));
            transfer.Fail = true;
            var interrupted = await workspace.MigrateRootAsync(Path.Combine(fixture, "中断目标"));
            Check(interrupted.Outcome == Outcome.RecoveryRequired, "指定文件系统边界移动后失败、恢复失败留下持久日志");
            workspace = new DesktopWorkspace(store, startup, shell);
            Check((await workspace.InitializeAsync(displays)).Outcome == Outcome.RecoveryRequired
                && workspace.Snapshot.MigrationRecovery.Count > 0
                && !(await workspace.CreateFolderAsync("恢复前禁止创建")).Succeeded, "重启按实际位置核对，未恢复时暂停修改");
            await Require(workspace.RecoverRootMigrationAsync());
            Check(workspace.Snapshot.Root == newRoot && !workspace.Snapshot.RecoveryRequired
                && File.ReadAllText(Path.Combine(newRoot, "灵感", "内容.bin")) == "原目标不得覆盖"
                && shell.ShortcutTargets(link, Path.Combine(newRoot, "灵感")), "恢复旧根目录、活动及保留内容、真实链接");
            Check(File.ReadAllBytes(Path.Combine(input, "内容 (2).bin")).SequenceEqual(new byte[] { 0, 13, 255, 72 }), "组合操作结束原输入字节仍完整");
            exit = 0;
        }
        catch (Exception e) { File.AppendAllText(log, "失败：" + e + "\n"); }
        finally
        {
            try { startup.WriteCommand(null); Check(startup.ReadCommand() == null, "随机自启项已清理"); }
            catch (Exception e) { File.AppendAllText(log, "清理失败：" + e + "\n"); exit = 1; }
            var full = Path.GetFullPath(fixture);
            if (!full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(full).StartsWith("Kage-release-", StringComparison.Ordinal))
                throw new IOException("拒绝清理隔离目录之外的内容。");
            Directory.Delete(full, true);
        }
        return exit;

        void Check(bool condition, string message)
        { if (!condition) throw new IOException(message); File.AppendAllText(log, "通过：" + message + "\n"); }
        async Task Require(Task<OperationResult> operation)
        { var result = await operation; Check(result.Succeeded, result.Message); }
    }

    private sealed class InterruptedTransfer : IRootDirectoryTransfer
    {
        private readonly WindowsDirectoryTransfer real = new();
        internal bool Fail { get; set; }
        public void Move(string source, string destination, CancellationToken cancellation)
        {
            real.Move(source, destination, cancellation);
            if (Fail) throw new IOException("组合验收：指定真实目录移动后中断。");
        }
        public void Restore(string source, string destination, CancellationToken cancellation)
        {
            if (Fail) throw new IOException("组合验收：暂时无法恢复，供重启核对。");
            real.Restore(source, destination, cancellation);
        }
    }
}
