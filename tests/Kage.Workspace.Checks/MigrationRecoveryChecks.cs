using Kage.Workspace;
using System.Diagnostics;

static class MigrationRecoveryChecks
{
    public static async Task BeforeExecution()
    {
        using var fixture = new Fixture();
        IDesktopWorkspace workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
        var initialized = await workspace.InitializeAsync([new(0, 0, 1500, 1000)]);
        Check(initialized.Succeeded, initialized.Message);
        var selected = await workspace.SelectRootAsync(fixture.Content);
        Check(selected.Succeeded, selected.Message);
        var created = await workspace.CreateFolderAsync("原内容");
        Check(created.Succeeded, created.Message);
        var folder = workspace.Snapshot.Folders.Single();
        File.WriteAllBytes(Path.Combine(folder.ActualPath, "字节.bin"), [0, 255, 42]);
        var target = Path.Combine(fixture.Home, "新根");
        var original = fixture.Store.Read().State;
        fixture.Store.Save(original with { PendingRootMigration = new(Guid.NewGuid(), original.Root, target,
            [new(folder.Folder.Id, folder.ActualPath, Path.Combine(target, folder.Folder.Name), null)]) });
        workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
        Check((await workspace.InitializeAsync([new(0, 0, 1500, 1000)])).Outcome == Outcome.RecoveryRequired,
            "启动先暂停修改并核对清单");
        Check((await workspace.RecoverRootMigrationAsync()).Succeeded && !workspace.Snapshot.RecoveryRequired
            && workspace.Snapshot.RootMigration == null, "执行前中断也能核对全部原内容并退出恢复状态");
        Check(workspace.Snapshot.Folders.Single().Folder == folder.Folder
            && File.ReadAllBytes(Path.Combine(folder.ActualPath, "字节.bin")).SequenceEqual(new byte[] { 0, 255, 42 })
            && !Directory.Exists(target), "稳定记录和真实字节不变，无额外复制");
        Check((await workspace.RecoverRootMigrationAsync()).Succeeded, "重复恢复无副作用");
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

    public static async Task ObserveActualPaths()
    {
        using var fixture = new Fixture();
        var shell = new WindowsFolderShell(Path.Combine(fixture.Home, "桌面"));
        Directory.CreateDirectory(shell.DesktopDirectory);
        IDesktopWorkspace workspace = new DesktopWorkspace(fixture.Store, new TestStartup(), shell);
        await workspace.InitializeAsync([new(0, 0, 1500, 1000)]);
        await workspace.SelectRootAsync(fixture.Content);
        await workspace.CreateFolderAsync("保留");
        var folder = workspace.Snapshot.Folders.Single();
        File.WriteAllBytes(Path.Combine(folder.ActualPath, "字节.bin"), [77, 255]);
        await workspace.DeleteFolderAsync(folder.Folder.Id, FolderDeleteChoice.KeepContents);
        var original = fixture.Store.Read().State;
        var target = Path.Combine(fixture.Home, "新根");
        Directory.CreateDirectory(target);
        var item = new RootMigrationItem(folder.Folder.Id, folder.ActualPath, Path.Combine(target, "保留"),
            original.RetainedFolders.Single().ShortcutPath, MigrationPhase.Moving);
        fixture.Store.Save(original with { PendingRootMigration = new(Guid.NewGuid(), original.Root, target, [item]) });
        new WindowsDirectoryTransfer().Move(item.SourcePath, item.DestinationPath, CancellationToken.None);
        workspace = new DesktopWorkspace(fixture.Store, new TestStartup(), shell);
        await workspace.InitializeAsync([new(0, 0, 1500, 1000)]);
        var observed = workspace.Snapshot.MigrationRecovery.Single();
        Check(observed.SourceStatus == MigrationPathStatus.Missing && observed.DestinationStatus == MigrationPathStatus.Owned
            && observed.ShortcutTarget == item.SourcePath && !observed.Restored, "陈旧阶段按真实路径和断开链接目标核对，保留内容也可定位");
        Check((await workspace.RecoverRootMigrationAsync()).Succeeded && shell.ShortcutTargets(item.ShortcutPath!, item.SourcePath)
            && workspace.Snapshot.Folders.Count == 0 && File.ReadAllBytes(Path.Combine(item.SourcePath, "字节.bin")).SequenceEqual(new byte[] { 77, 255 }),
            "恢复字节和保留链接，不复活入口");
    }

    public static async Task ProcessInterruptions()
    {
        foreach (var stage in new[] { "执行前", "目标创建后", "目录部分复制", "目录移除后", "源清理末尾", "链接更新前", "链接更新后", "配置提交前", "配置提交后", "恢复创建后", "恢复移除后", "恢复清理末尾" })
        {
            using var fixture = new Fixture();
            var shell = new WindowsFolderShell(Path.Combine(fixture.Home, "桌面"));
            Directory.CreateDirectory(shell.DesktopDirectory);
            IDesktopWorkspace workspace = new DesktopWorkspace(fixture.Store, new TestStartup(), shell);
            await workspace.InitializeAsync([new(0, 0, 1500, 1000)]);
            await workspace.SelectRootAsync(fixture.Content);
            await workspace.CreateFolderAsync("活动");
            var active = workspace.Snapshot.Folders.Single();
            File.WriteAllBytes(Path.Combine(active.ActualPath, "内容.bin"), [0, 255, 17, 98]);
            Directory.CreateDirectory(Path.Combine(active.ActualPath, "子目录"));
            File.WriteAllText(Path.Combine(active.ActualPath, "子目录", "文档.txt"), "进程中断保留嵌套内容");
            File.WriteAllText(Path.Combine(active.ActualPath, "内容.bin") + ":备注", "命名流");
            await workspace.CreateFolderAsync("保留");
            var retained = workspace.Snapshot.Folders.Single(item => item.Folder.Name == "保留");
            File.WriteAllBytes(Path.Combine(retained.ActualPath, "内容.bin"), [77, 255, 0]);
            Check((await workspace.DeleteFolderAsync(retained.Folder.Id, FolderDeleteChoice.KeepContents)).Succeeded, "准备真实保留链接");
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true };
            if (Path.GetFileNameWithoutExtension(Environment.ProcessPath!).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                start.ArgumentList.Add(typeof(MigrationRecoveryChecks).Assembly.Location);
            start.ArgumentList.Add("--interrupt-migration");
            start.ArgumentList.Add(fixture.Home);
            start.ArgumentList.Add(stage);
            using var child = Process.Start(start)!;
            var output = child.StandardOutput.ReadToEndAsync();
            var errors = child.StandardError.ReadToEndAsync();
            try { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(35)); }
            catch { child.Kill(true); throw; }
            Check(child.ExitCode == 73, $"{stage} 应真实终止子进程：{child.ExitCode} {await output} {await errors}");
            var primary = Path.Combine(fixture.Home, "状态", "workspace.json");
            var savedBytes = File.ReadAllBytes(primary);
            workspace = new DesktopWorkspace(fixture.Store, new TestStartup(), shell);
            var loaded = await workspace.InitializeAsync([new(0, 0, 1500, 1000)]);
            var committed = stage == "配置提交后";
            Check(loaded.Outcome == (committed ? Outcome.Success : Outcome.RecoveryRequired), $"{stage} 启动状态与提交结果一致");
            if (!committed)
            {
                Check(savedBytes.SequenceEqual(File.ReadAllBytes(primary)), $"{stage} 启动只读核对，不提前修复日志");
                var located = workspace.Snapshot.Folders.Single().ActualPath;
                Check(Directory.Exists(located), $"{stage} 活动入口定位真实目录");
                Check(!(await workspace.SelectRootAsync(Path.Combine(fixture.Home, "另选"))).Succeeded, "恢复期间不能更改根目录");
                var restored = await workspace.RecoverRootMigrationAsync();
                Check(restored.Succeeded, $"{stage} 重试恢复成功：{restored.Message}");
            }
            var root = committed ? Path.Combine(fixture.Home, "新根") : fixture.Content;
            Check(workspace.Snapshot.Root == root && workspace.Snapshot.Folders.Single().Folder == active.Folder
                && workspace.Snapshot.RootMigration == null && !workspace.Snapshot.RecoveryRequired, $"{stage} 稳定标识布局和活动状态正确");
            Check(File.ReadAllBytes(Path.Combine(root, "活动", "内容.bin")).SequenceEqual(new byte[] { 0, 255, 17, 98 })
                && File.ReadAllText(Path.Combine(root, "活动", "内容.bin") + ":备注") == "命名流"
                && File.ReadAllText(Path.Combine(root, "活动", "子目录", "文档.txt")) == "进程中断保留嵌套内容"
                && File.ReadAllBytes(Path.Combine(root, "保留", "内容.bin")).SequenceEqual(new byte[] { 77, 255, 0 }), $"{stage} 原字节和命名流完整");
            Check(shell.ShortcutTargets(Path.Combine(shell.DesktopDirectory, "保留.lnk"), Path.Combine(root, "保留")), $"{stage} 链接实际目标正确");
            var otherRoot = committed ? fixture.Content : Path.Combine(fixture.Home, "新根");
            Check(!Directory.Exists(Path.Combine(otherRoot, "活动")) && !Directory.Exists(Path.Combine(otherRoot, "保留")), $"{stage} 无残留副本");
            workspace = new DesktopWorkspace(fixture.Store, new TestStartup(), shell);
            Check((await workspace.InitializeAsync([new(0, 0, 1500, 1000)])).Succeeded
                && (await workspace.RecoverRootMigrationAsync()).Succeeded, $"{stage} 重复启动及恢复无额外效果");
        }
    }

    // 独立子进程只在指定持久提交／真实文件效果后退出，绕过当场异常恢复。
    public static async Task<int> InterruptChild(string home, string stage)
    {
        var store = new FailingStore(new JsonWorkspaceStore(Path.Combine(home, "状态")));
        var shell = new InterruptShell(new WindowsFolderShell(Path.Combine(home, "桌面")), stage);
        var transfer = new InterruptTransfer(stage);
        IDesktopWorkspace workspace = new DesktopWorkspace(store, new TestStartup(), shell, transfer);
        await workspace.InitializeAsync([new(0, 0, 1500, 1000)]);
        store.AfterSave = saved =>
        {
            var pending = saved.PendingRootMigration;
            if (stage == "执行前" && pending?.Items.All(item => item.Phase == MigrationPhase.Planned) == true
                || stage == "链接更新前" && pending?.Items.Any(item => item.Phase == MigrationPhase.UpdatingShortcut) == true
                || stage == "配置提交前" && pending?.Items.All(item => item.Phase == MigrationPhase.Completed) == true
                || stage == "配置提交后" && pending == null && saved.Root == Path.Combine(home, "新根")) Environment.Exit(73);
        };
        if (stage.StartsWith("恢复", StringComparison.Ordinal))
        {
            using var cancellation = new CancellationTokenSource();
            await workspace.MigrateRootAsync(Path.Combine(home, "新根"), new Observer(update =>
            {
                if (update.Completed == update.Total) cancellation.Cancel();
            }), cancellation.Token);
        }
        else await workspace.MigrateRootAsync(Path.Combine(home, "新根"));
        return 2;
    }

    sealed class Observer(Action<RootMigrationProgress> action) : IProgress<RootMigrationProgress>
    {
        public void Report(RootMigrationProgress value) => action(value);
    }

    public static async Task RetryFailures()
    {
        using var fixture = new Fixture();
        var store = new FailingStore(fixture.Store);
        var transfer = new RetryTransfer();
        IDesktopWorkspace workspace = new DesktopWorkspace(store, new TestStartup(), rootTransfer: transfer);
        await workspace.InitializeAsync([new(0, 0, 1500, 1000)]);
        await workspace.SelectRootAsync(fixture.Content);
        foreach (var name in new[] { "先恢复", "失败项" })
        {
            await workspace.CreateFolderAsync(name);
            File.WriteAllText(Path.Combine(fixture.Content, name, "内容.txt"), name);
        }
        var original = fixture.Store.Read().State;
        var target = Path.Combine(fixture.Home, "新根");
        Directory.CreateDirectory(target);
        var items = original.Folders.Select(folder => new RootMigrationItem(folder.Id, Path.Combine(original.Root, folder.Name),
            Path.Combine(target, folder.Name), null, MigrationPhase.Completed)).ToArray();
        fixture.Store.Save(original with { PendingRootMigration = new(Guid.NewGuid(), original.Root, target, items) });
        foreach (var item in items) new WindowsDirectoryTransfer().Move(item.SourcePath, item.DestinationPath, CancellationToken.None);
        workspace = new DesktopWorkspace(store, new TestStartup(), rootTransfer: transfer);
        await workspace.InitializeAsync([new(0, 0, 1500, 1000)]);
        Check((await workspace.RecoverRootMigrationAsync()).Outcome == Outcome.RecoveryRequired, "逐项失败不能伪造正常工作区");
        Check(workspace.Snapshot.MigrationRecovery.Count(item => !item.Restored) == 1
            && File.ReadAllText(Path.Combine(fixture.Content, "先恢复", "内容.txt")) == "先恢复"
            && File.ReadAllText(Path.Combine(target, "失败项", "内容.txt")) == "失败项", "已恢复项和失败项按真实路径区分");
        Check(!(await workspace.RenameFolderAsync(original.Folders[0].Id, "扩散")).Succeeded, "失败期间禁止改名");
        transfer.Fail = false;
        store.AfterSave = state => { if (state.PendingRootMigration?.Items.All(item => item.Phase == MigrationPhase.Restored) == true) store.FailOnSave = store.Saves + 1; };
        Check((await workspace.RecoverRootMigrationAsync()).Outcome == Outcome.RecoveryRequired
            && workspace.Snapshot.MigrationRecovery.All(item => item.Restored), "目录已恢复但清理配置失败仍保留恢复状态");
        var restoredBytes = File.ReadAllBytes(Path.Combine(fixture.Content, "先恢复", "内容.txt"));
        store.AfterSave = null;
        store.FailOnSave = -1;
        workspace = new DesktopWorkspace(store, new TestStartup(), rootTransfer: new NoTransfer());
        await workspace.InitializeAsync([new(0, 0, 1500, 1000)]);
        Check((await workspace.RecoverRootMigrationAsync()).Succeeded
            && restoredBytes.SequenceEqual(File.ReadAllBytes(Path.Combine(fixture.Content, "先恢复", "内容.txt"))),
            "重启后只补提交，已恢复内容不重复移动");
    }

    public static async Task ConflictingContents()
    {
        using var fixture = new Fixture();
        IDesktopWorkspace workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
        await workspace.InitializeAsync([new(0, 0, 1500, 1000)]);
        await workspace.SelectRootAsync(fixture.Content);
        await workspace.CreateFolderAsync("冲突");
        var folder = workspace.Snapshot.Folders.Single();
        File.WriteAllText(Path.Combine(folder.ActualPath, "内容.txt"), "源原文");
        var original = fixture.Store.Read().State;
        var target = Path.Combine(fixture.Home, "新根");
        var destination = Path.Combine(target, "冲突");
        Directory.CreateDirectory(destination);
        File.Copy(Path.Combine(folder.ActualPath, ".kage-folder-id"), Path.Combine(destination, ".kage-folder-id"));
        File.WriteAllText(Path.Combine(destination, "内容.txt"), "目标不同内容");
        fixture.Store.Save(original with { PendingRootMigration = new(Guid.NewGuid(), original.Root, target,
            [new(folder.Folder.Id, folder.ActualPath, destination, null, MigrationPhase.Restoring)]) });
        workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
        await workspace.InitializeAsync([new(0, 0, 1500, 1000)]);
        foreach (var attempt in Enumerable.Range(0, 2))
            Check((await workspace.RecoverRootMigrationAsync()).Outcome == Outcome.RecoveryRequired
                && File.ReadAllText(Path.Combine(folder.ActualPath, "内容.txt")) == "源原文"
                && File.ReadAllText(Path.Combine(destination, "内容.txt")) == "目标不同内容", "重复恢复保留同名不同字节，不覆盖或清理");
        File.Delete(Path.Combine(destination, "内容.txt"));
        File.Delete(Path.Combine(destination, ".kage-folder-id"));
        File.WriteAllText(destination + ":未确认流", "目录命名流不可删除");
        Check((await workspace.RecoverRootMigrationAsync()).Outcome == Outcome.RecoveryRequired
            && File.ReadAllText(destination + ":未确认流") == "目录命名流不可删除", "无条目但含命名流的未知目录不能按空目录清理");
        File.WriteAllText(Path.Combine(destination, ".kage-folder-id"), Guid.NewGuid().ToString("N"));
        Check((await workspace.RecoverRootMigrationAsync()).Outcome == Outcome.RecoveryRequired
            && workspace.Snapshot.MigrationRecovery.Single().DestinationStatus == MigrationPathStatus.Unverified, "其他归属保留两边证据");
        File.Delete(Path.Combine(folder.ActualPath, "内容.txt"));
        Check(Path.GetFullPath(folder.ActualPath).StartsWith(Path.GetFullPath(fixture.Home) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "清理仅限隔离夹具内容");
        Directory.Delete(folder.ActualPath, true);
        Check((await workspace.RecoverRootMigrationAsync()).Outcome == Outcome.RecoveryRequired
            && !workspace.Snapshot.Folders.Single().Visible && workspace.Snapshot.Folders.Single().Entries.Count == 0,
            "没有可信内容位置时不展示其他归属为正常 Folder");
    }

    sealed class RetryTransfer : IRootDirectoryTransfer
    {
        private readonly WindowsDirectoryTransfer real = new();
        public bool Fail { get; set; } = true;
        public void Move(string source, string destination, CancellationToken token) => real.Move(source, destination, token);
        public void Restore(string source, string destination, CancellationToken token)
        {
            if (Fail && Path.GetFileName(source) == "失败项") throw new IOException("指定项暂不能恢复");
            real.Restore(source, destination, token);
        }
    }

    sealed class NoTransfer : IRootDirectoryTransfer
    {
        public void Move(string source, string destination, CancellationToken token) => throw new IOException("已恢复内容不应重新移动");
        public void Restore(string source, string destination, CancellationToken token) => throw new IOException("已恢复内容不应重新复制");
    }

    sealed class InterruptTransfer(string stage) : IRootDirectoryTransfer
    {
        private readonly WindowsDirectoryTransfer real = new();
        public void Move(string source, string destination, CancellationToken cancellation)
        {
            if (stage == "目标创建后")
            {
                Directory.CreateDirectory(destination);
                Environment.Exit(73);
            }
            if (stage == "目录部分复制")
            {
                Directory.CreateDirectory(destination);
                File.Copy(Path.Combine(source, ".kage-folder-id"), Path.Combine(destination, ".kage-folder-id"));
                File.Copy(Path.Combine(source, "内容.bin"), Path.Combine(destination, "内容.bin"));
                Environment.Exit(73);
            }
            real.Move(source, destination, cancellation);
            if (stage == "目录移除后") Environment.Exit(73);
            if (stage == "源清理末尾")
            {
                Directory.CreateDirectory(source);
                Environment.Exit(73);
            }
        }
        public void Restore(string source, string destination, CancellationToken cancellation)
        {
            if (stage == "恢复创建后")
            {
                Directory.CreateDirectory(source);
                Environment.Exit(73);
            }
            real.Restore(source, destination, cancellation);
            if (stage == "恢复移除后") Environment.Exit(73);
            if (stage == "恢复清理末尾")
            {
                Directory.CreateDirectory(destination);
                Environment.Exit(73);
            }
        }
    }

    sealed class InterruptShell(IFolderShell real, string stage) : IFolderShell
    {
        public string DesktopDirectory => real.DesktopDirectory;
        public void CreateShortcut(string path, string target) => real.CreateShortcut(path, target);
        public bool ShortcutTargets(string path, string target) => real.ShortcutTargets(path, target);
        public string? ReadShortcutTarget(string path, Guid id) => real.ReadShortcutTarget(path, id);
        public OperationResult Recycle(string path, Guid id) => real.Recycle(path, id);
        public string? FindRecycledFolder(string path, Guid id) => real.FindRecycledFolder(path, id);
        public void RetargetShortcut(string path, string previous, string target, Guid id)
        {
            real.RetargetShortcut(path, previous, target, id);
            if (stage == "链接更新后") Environment.Exit(73);
        }
    }
}
