using Kage.Workspace;

static class RootMigrationChecks
{
    public static async Task Complete()
    {
        using var fixture = new Fixture();
        var shell = new WindowsFolderShell(Path.Combine(fixture.Home, "桌面"));
        Directory.CreateDirectory(shell.DesktopDirectory);
        IDesktopWorkspace workspace = new DesktopWorkspace(fixture.Store, new TestStartup(), shell);
        await workspace.InitializeAsync([new(0, 0, 1500, 1000)]);
        await workspace.SelectRootAsync(fixture.Content);
        await workspace.CreateFolderAsync("活动");
        var active = workspace.Snapshot.Folders.Single();
        Directory.CreateDirectory(Path.Combine(active.ActualPath, "子目录"));
        File.WriteAllBytes(Path.Combine(active.ActualPath, "子目录", "字节.bin"), [0, 255, 12, 34]);
        await workspace.CreateFolderAsync("保留");
        var retained = workspace.Snapshot.Folders.Single(item => item.Folder.Name == "保留");
        File.WriteAllText(Path.Combine(retained.ActualPath, "保留.txt"), "迁移后仍可访问");
        Check((await workspace.DeleteFolderAsync(retained.Folder.Id, FolderDeleteChoice.KeepContents)).Succeeded, "真实保留快捷方式");
        var root = Path.Combine(fixture.Home, "新根目录");
        var result = await workspace.MigrateRootAsync(root);
        Check(result.Succeeded, "完整清单迁移应成功：" + result.Message);
        Check(workspace.Snapshot.Root == root && workspace.Snapshot.Folders.Single().Folder == active.Folder, "根目录切换保留标识布局和外观");
        Check(!Directory.Exists(active.ActualPath) && !Directory.Exists(retained.ActualPath), "源目录已实际移除");
        Check(File.ReadAllBytes(Path.Combine(root, "活动", "子目录", "字节.bin")).SequenceEqual(new byte[] { 0, 255, 12, 34 }), "嵌套字节完全一致");
        Check(shell.ShortcutTargets(Path.Combine(shell.DesktopDirectory, "保留.lnk"), Path.Combine(root, "保留")), "工具快捷方式指向新内容");
        workspace = new DesktopWorkspace(fixture.Store, new TestStartup(), shell);
        Check((await workspace.InitializeAsync([new(0, 0, 1500, 1000)])).Succeeded && workspace.Snapshot.Root == root
            && workspace.Snapshot.Folders.Count == 1, "重启新映射，不复活保留入口");
    }

    public static async Task Cancel()
    {
        using var fixture = new Fixture();
        IDesktopWorkspace workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
        await workspace.InitializeAsync([new(0, 0, 1500, 1000)]);
        await workspace.SelectRootAsync(fixture.Content);
        await workspace.CreateFolderAsync("先迁移");
        await workspace.CreateFolderAsync("后迁移");
        var originals = workspace.Snapshot.Folders.ToArray();
        foreach (var folder in originals) File.WriteAllText(Path.Combine(folder.ActualPath, "内容.txt"), folder.Folder.Name);
        using var cancellation = new CancellationTokenSource();
        Task<OperationResult>? queued = null;
        var progress = new ObserveProgress(update =>
        {
            if (update.Completed != 1 || queued != null) return;
            Check(workspace.Snapshot.Root == fixture.Content, "完成一项不能提前切换配置");
            queued = workspace.RenameFolderAsync(originals[1].Folder.Id, "排队改名");
            Check(!queued.IsCompleted, "迁移清单独占修改次序");
            cancellation.Cancel();
        });
        var target = Path.Combine(fixture.Home, "取消目标");
        var result = await workspace.MigrateRootAsync(target, progress, cancellation.Token);
        Check(result.Outcome == Outcome.Cancelled && workspace.Snapshot.Root == fixture.Content && !workspace.Snapshot.RecoveryRequired,
            "取消后恢复旧配置且可继续操作：" + result.Message);
        Check(queued != null && (await queued).Succeeded, "迁移结束后再执行排队修改");
        Check(File.ReadAllText(Path.Combine(originals[0].ActualPath, "内容.txt")) == "先迁移"
            && File.ReadAllText(Path.Combine(fixture.Content, "排队改名", "内容.txt")) == "后迁移", "实际字节恢复并保持排队改名");
        Check(!Directory.Exists(Path.Combine(target, "先迁移")) && !Directory.Exists(Path.Combine(target, "后迁移")), "取消无剩余迁移内容");
        using var before = new CancellationTokenSource();
        before.Cancel();
        Check((await workspace.MigrateRootAsync(target, cancellation: before.Token)).Outcome == Outcome.Cancelled, "开始前取消无文件效果");
    }

    public static async Task TransferFailures()
    {
        using var fixture = new Fixture();
        var transfer = new FailingTransfer();
        IDesktopWorkspace workspace = new DesktopWorkspace(fixture.Store, new TestStartup(), rootTransfer: transfer);
        await workspace.InitializeAsync([new(0, 0, 1500, 1000)]);
        await workspace.SelectRootAsync(fixture.Content);
        await workspace.CreateFolderAsync("真实目录");
        var original = workspace.Snapshot.Folders.Single();
        File.WriteAllBytes(Path.Combine(original.ActualPath, "字节.bin"), [1, 2, 255]);
        var target = Path.Combine(fixture.Home, "失败目标");
        transfer.ThrowAfterMove = true;
        var result = await workspace.MigrateRootAsync(target);
        Check(result.Outcome == Outcome.Failed && !workspace.Snapshot.RecoveryRequired && workspace.Snapshot.Root == fixture.Content,
            "执行后抛异常仍恢复真实移动，返回失败：" + result.Message);
        Check(File.ReadAllBytes(Path.Combine(original.ActualPath, "字节.bin")).SequenceEqual(new byte[] { 1, 2, 255 }), "失败恢复原字节");
        transfer.FailRestore = true;
        result = await workspace.MigrateRootAsync(target);
        Check(result.Outcome == Outcome.RecoveryRequired && workspace.Snapshot.Root == fixture.Content && workspace.Snapshot.RecoveryRequired,
            "恢复失败保留旧配置及明确恢复状态");
        Check(workspace.Snapshot.Folders.Single().ActualPath == Path.Combine(target, "真实目录")
            && workspace.Snapshot.RootMigration?.Items.Single().Phase == MigrationPhase.RecoveryRequired, "恢复快照定位实际目标及逐项状态");
        Check(!(await workspace.CreateFolderAsync("不应创建")).Succeeded && !Directory.Exists(Path.Combine(fixture.Content, "不应创建")), "恢复未完成暂停修改");
        workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
        Check((await workspace.InitializeAsync([new(0, 0, 1500, 1000)])).Outcome == Outcome.RecoveryRequired
            && workspace.Snapshot.RootMigration?.Items.Single().DestinationPath == Path.Combine(target, "真实目录"), "重启保留可供任务 10 消费的完整记录");
    }

    sealed class ObserveProgress(Action<RootMigrationProgress> observe) : IProgress<RootMigrationProgress>
    {
        public void Report(RootMigrationProgress value) => observe(value);
    }

    public static async Task ValidationAndSave()
    {
        using var fixture = new Fixture();
        var store = new FailingStore(fixture.Store);
        IDesktopWorkspace workspace = new DesktopWorkspace(store, new TestStartup());
        await workspace.InitializeAsync([new(0, 0, 1500, 1000)]);
        await workspace.SelectRootAsync(fixture.Content);
        await workspace.CreateFolderAsync("文件");
        var source = workspace.Snapshot.Folders.Single().ActualPath;
        File.WriteAllText(Path.Combine(source, "内容.txt"), "保持字节");
        Check((await workspace.MigrateRootAsync(fixture.Content + Path.DirectorySeparatorChar)).Succeeded, "相同根目录无变化");
        foreach (var target in new[] { Path.Combine(source, "子目录"), Path.Combine(fixture.Content, "子根"), source, "相对路径" })
            Check(!(await workspace.MigrateRootAsync(target)).Succeeded && Directory.Exists(source), "拒绝自身／子目录及非法路径");
        var root = Path.Combine(fixture.Home, "新根");
        Directory.CreateDirectory(Path.Combine(root, "文件"));
        File.WriteAllText(Path.Combine(root, "文件", "已有.txt"), "不可覆盖");
        var conflict = await workspace.MigrateRootAsync(root);
        Check(conflict.Outcome == Outcome.Conflict && File.ReadAllText(Path.Combine(root, "文件", "已有.txt")) == "不可覆盖", "预检同名明确返回冲突，不移动或覆盖");
        root = Path.Combine(fixture.Home, "可用目标");
        store.FailOnSave = store.Saves + 1;
        Check((await workspace.MigrateRootAsync(root)).Outcome == Outcome.Failed && Directory.Exists(source), "意图保存失败不产生文件效果");
        store.FailOnSave = -1;
        store.AfterSave = saved =>
        {
            if (saved.PendingRootMigration?.Items.All(item => item.Phase == MigrationPhase.Completed) == true)
                store.FailOnSave = store.Saves + 1;
        };
        var commit = await workspace.MigrateRootAsync(root);
        Check(commit.Outcome == Outcome.Failed && workspace.Snapshot.Root == fixture.Content && !workspace.Snapshot.RecoveryRequired
            && File.ReadAllText(Path.Combine(source, "内容.txt")) == "保持字节", "新根最终提交失败当场恢复原内容及旧配置：" + commit.Message);
        store.AfterSave = null;
        var valid = fixture.Store.Read().State;
        try
        {
            fixture.Store.Save(valid with { PendingRootMigration = new(Guid.NewGuid(), valid.Root, root,
                [new(Guid.NewGuid(), source, Path.Combine(root, "文件"), null)]) });
            throw new Exception("应拒绝没有对应稳定记录的迁移日志");
        }
        catch (InvalidDataException) { }
    }

    public static async Task ShortcutRollback()
    {
        using var fixture = new Fixture();
        var shell = new MigrationShell(new WindowsFolderShell(Path.Combine(fixture.Home, "桌面")));
        Directory.CreateDirectory(shell.DesktopDirectory);
        IDesktopWorkspace workspace = new DesktopWorkspace(fixture.Store, new TestStartup(), shell);
        await workspace.InitializeAsync([new(0, 0, 1500, 1000)]);
        await workspace.SelectRootAsync(fixture.Content);
        await workspace.CreateFolderAsync("保留");
        var folder = workspace.Snapshot.Folders.Single();
        File.WriteAllText(Path.Combine(folder.ActualPath, "内容.txt"), "链接恢复内容");
        Check((await workspace.DeleteFolderAsync(folder.Folder.Id, FolderDeleteChoice.KeepContents)).Succeeded, "保留入口转换为实际工具链接");
        var link = Path.Combine(shell.DesktopDirectory, "保留.lnk");
        var root = Path.Combine(fixture.Home, "新根");
        using var cancellation = new CancellationTokenSource();
        var result = await workspace.MigrateRootAsync(root, new ObserveProgress(update =>
        {
            if (update.Completed == 1) cancellation.Cancel();
        }), cancellation.Token);
        Check(result.Outcome == Outcome.Cancelled && shell.ShortcutTargets(link, folder.ActualPath)
            && workspace.Snapshot.Folders.Count == 0, "取消已更新的链接恢复旧目标，不复活入口");
        shell.ThrowAfterRetarget = true;
        result = await workspace.MigrateRootAsync(root);
        Check(result.Outcome == Outcome.Failed && shell.ShortcutTargets(link, folder.ActualPath) && !workspace.Snapshot.RecoveryRequired,
            "链接更新后抛异常仍按实际效果恢复");
        shell.ThrowAfterRetarget = false;
        shell.FailRetarget = true;
        result = await workspace.MigrateRootAsync(root);
        Check(result.Outcome == Outcome.Failed && shell.ShortcutTargets(link, folder.ActualPath), "链接更新前失败恢复目录及旧目标");
    }

    public static async Task CrossVolume()
    {
        using var fixture = new Fixture();
        var verification = Path.Combine(Environment.CurrentDirectory, ".scratch", "desktop-folder", "verification");
        Directory.CreateDirectory(verification);
        if (string.Equals(Path.GetPathRoot(fixture.Home), Path.GetPathRoot(verification), StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("未验证：没有可用的第二块本地磁盘供根迁移检查。");
            return;
        }
        using var target = new Fixture(verification);
        IDesktopWorkspace workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
        await workspace.InitializeAsync([new(0, 0, 1500, 1000)]);
        await workspace.SelectRootAsync(fixture.Content);
        await workspace.CreateFolderAsync("跨盘");
        var source = workspace.Snapshot.Folders.Single().ActualPath;
        Directory.CreateDirectory(Path.Combine(source, "子目录"));
        File.WriteAllBytes(Path.Combine(source, "子目录", "字节.bin"), [0, 255, 3, 17]);
        File.WriteAllText(Path.Combine(source, "字节.txt") + ":备注", "NTFS 文件命名流");
        File.WriteAllText(source + ":目录备注", "NTFS 目录命名流");
        var result = await workspace.MigrateRootAsync(target.Content);
        Check(result.Succeeded && !Directory.Exists(source), "真实跨盘复制、核对及源移除：" + result.Message);
        var migrated = workspace.Snapshot.Folders.Single().ActualPath;
        Check(File.ReadAllBytes(Path.Combine(migrated, "子目录", "字节.bin")).SequenceEqual(new byte[] { 0, 255, 3, 17 })
            && File.ReadAllText(Path.Combine(migrated, "字节.txt") + ":备注") == "NTFS 文件命名流"
            && File.ReadAllText(migrated + ":目录备注") == "NTFS 目录命名流", "跨盘嵌套字节及命名流保持");
        var busy = Path.Combine(migrated, "占用.txt");
        File.WriteAllText(busy, "指定文件不可读");
        using (var handle = new FileStream(busy, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            result = await workspace.MigrateRootAsync(fixture.Content);
            Check(result.Outcome == Outcome.Failed && workspace.Snapshot.Root == target.Content
                && !workspace.Snapshot.RecoveryRequired && !Directory.Exists(source), "跨盘复制部分失败恢复，未切换正常根目录：" + result.Message);
        }
        Check(File.ReadAllText(busy) == "指定文件不可读", "部分复制失败保留被占用源文件");
    }

    sealed class MigrationShell(IFolderShell real) : IFolderShell
    {
        public bool ThrowAfterRetarget { get; set; }
        public bool FailRetarget { get; set; }
        public string DesktopDirectory => real.DesktopDirectory;
        public void CreateShortcut(string path, string target) => real.CreateShortcut(path, target);
        public bool ShortcutTargets(string path, string target) => real.ShortcutTargets(path, target);
        public OperationResult Recycle(string path, Guid id) => real.Recycle(path, id);
        public string? FindRecycledFolder(string path, Guid id) => real.FindRecycledFolder(path, id);
        public void RetargetShortcut(string path, string previous, string target, Guid id)
        {
            if (FailRetarget) throw new IOException("指定链接更新前失败");
            real.RetargetShortcut(path, previous, target, id);
            if (ThrowAfterRetarget) { ThrowAfterRetarget = false; throw new IOException("指定链接实际更新后失败"); }
        }
    }

    public static async Task ReadOnlyContents()
    {
        using var fixture = new Fixture();
        IDesktopWorkspace workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
        await workspace.InitializeAsync([new(0, 0, 1500, 1000)]);
        await workspace.SelectRootAsync(fixture.Content);
        await workspace.CreateFolderAsync("只读内容");
        var source = workspace.Snapshot.Folders.Single().ActualPath;
        var file = Path.Combine(source, "只读.bin");
        File.WriteAllBytes(file, [77, 255, 0]);
        File.SetAttributes(file, FileAttributes.ReadOnly);
        try
        {
            var target = Path.Combine(fixture.Home, "新根");
            var result = await workspace.MigrateRootAsync(target);
            Check(result.Succeeded && !Directory.Exists(source), "普通只读文件也应完整迁移：" + result.Message);
            var copied = Path.Combine(target, "只读内容", "只读.bin");
            Check(File.ReadAllBytes(copied).SequenceEqual(new byte[] { 77, 255, 0 }) && (File.GetAttributes(copied) & FileAttributes.ReadOnly) != 0,
                "迁移字节及只读属性保持");
            using var cancellation = new CancellationTokenSource();
            result = await workspace.MigrateRootAsync(fixture.Content, new ObserveProgress(update =>
            {
                if (update.Completed == 1) cancellation.Cancel();
            }), cancellation.Token);
            Check(result.Outcome == Outcome.Cancelled && !workspace.Snapshot.RecoveryRequired && workspace.Snapshot.Root == target,
                "取消只读内容迁移可当场恢复：" + result.Message);
            Check(File.ReadAllBytes(copied).SequenceEqual(new byte[] { 77, 255, 0 }) && (File.GetAttributes(copied) & FileAttributes.ReadOnly) != 0,
                "取消恢复字节及只读属性保持");
        }
        finally
        {
            foreach (var item in Directory.EnumerateFiles(fixture.Home, "*", SearchOption.AllDirectories))
                if ((File.GetAttributes(item) & FileAttributes.ReadOnly) != 0) File.SetAttributes(item, File.GetAttributes(item) & ~FileAttributes.ReadOnly);
        }
    }

    sealed class FailingTransfer : IRootDirectoryTransfer
    {
        private readonly WindowsDirectoryTransfer real = new();
        public bool ThrowAfterMove { get; set; }
        public bool FailRestore { get; set; }
        public void Move(string source, string destination, CancellationToken cancellation)
        {
            real.Move(source, destination, cancellation);
            if (ThrowAfterMove) throw new IOException("指定目录实际移动后注入失败");
        }
        public void Restore(string source, string destination, CancellationToken cancellation)
        {
            if (FailRestore) throw new IOException("指定目录注入恢复失败");
            real.Restore(source, destination, cancellation);
        }
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
