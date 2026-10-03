using Kage.Workspace;
using System.Security.AccessControl;
using System.Security.Principal;

static class FolderChangeChecks
{
    private static readonly DisplayArea[] Displays = [new(0, 0, 1600, 1000)];
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

    public static async Task Rename()
    {
        using var fixture = new Fixture();
        IDesktopWorkspace workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
        await workspace.InitializeAsync(Displays);
        await workspace.SelectRootAsync(fixture.Content);
        await workspace.CreateFolderAsync("改名前");
        var original = workspace.Snapshot.Folders.Single();
        var style = workspace.BeginAppearance(original.Folder.Id)!;
        style.SetHex("#123456");
        await workspace.ApplyAppearanceAsync(style);
        original = workspace.Snapshot.Folders.Single();
        File.WriteAllBytes(Path.Combine(original.ActualPath, "内容.bin"), [0, 1, 255]);
        var result = await workspace.RenameFolderAsync(original.Folder.Id, "改名后");
        Check(result.Succeeded, "改名成功");
        var changed = workspace.Snapshot.Folders.Single();
        Check(changed.Folder == original.Folder with { Name = "改名后" }, "只改变名称，保留标识、位置和外观");
        Check(changed.ActualPath == Path.Combine(fixture.Content, "改名后") && !Directory.Exists(original.ActualPath), "实际目录同步改名");
        Check(File.ReadAllBytes(Path.Combine(changed.ActualPath, "内容.bin")).SequenceEqual(new byte[] { 0, 1, 255 }), "内容字节保留");
        File.WriteAllText(Path.Combine(changed.ActualPath, "外部新增.txt"), "实际新增");
        await workspace.RefreshAsync(Displays);
        Check(workspace.Snapshot.Folders.Single().FileCount == 2, "改名后刷新使用更新路径");
        workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
        Check((await workspace.InitializeAsync(Displays)).Succeeded && workspace.Snapshot.Folders.Single().Folder == changed.Folder, "重启恢复稳定映射及外观");
    }
    public static async Task RenameFailures()
    {
        using var fixture = new Fixture();
        var store = new FailingStore(fixture.Store);
        IDesktopWorkspace workspace = new DesktopWorkspace(store, new TestStartup());
        await workspace.InitializeAsync(Displays);
        await workspace.SelectRootAsync(fixture.Content);
        await workspace.CreateFolderAsync("原名");
        var folder = workspace.Snapshot.Folders.Single();
        var collision = Path.Combine(fixture.Content, "已存在");
        Directory.CreateDirectory(collision);
        File.WriteAllText(Path.Combine(collision, "外部.txt"), "外部内容");
        Check((await workspace.RenameFolderAsync(folder.Folder.Id, "已存在")).Outcome == Outcome.Conflict, "同名待选择");
        Check((await workspace.RenameFolderAsync(folder.Folder.Id, "已存在", ConflictChoice.Skip)).Outcome == Outcome.Skipped, "跳过保留原名");
        Check((await workspace.RenameFolderAsync(folder.Folder.Id, "已存在", ConflictChoice.Cancel)).Outcome == Outcome.Cancelled, "取消保留原名");
        Check((await workspace.RenameFolderAsync(folder.Folder.Id, "已存在", ConflictChoice.KeepBoth)).Succeeded, "编号成功");
        folder = workspace.Snapshot.Folders.Single();
        Check(folder.Folder.Name == "已存在 (2)" && File.ReadAllText(Path.Combine(collision, "外部.txt")) == "外部内容", "目录展示编号一致且不覆盖");
        Check((await workspace.RenameFolderAsync(folder.Folder.Id, "NUL")).Outcome == Outcome.Failed, "无效名称失败");
        if (OperatingSystem.IsWindows())
        {
            var sourceInfo = new DirectoryInfo(folder.ActualPath);
            var parentInfo = new DirectoryInfo(fixture.Content);
            var originalSource = sourceInfo.GetAccessControl(AccessControlSections.Access);
            var originalParent = parentInfo.GetAccessControl(AccessControlSections.Access);
            using var identity = WindowsIdentity.GetCurrent();
            var deniedSource = new DirectorySecurity();
            deniedSource.SetSecurityDescriptorBinaryForm(originalSource.GetSecurityDescriptorBinaryForm(), AccessControlSections.Access);
            deniedSource.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.Delete, AccessControlType.Deny));
            var deniedParent = new DirectorySecurity();
            deniedParent.SetSecurityDescriptorBinaryForm(originalParent.GetSecurityDescriptorBinaryForm(), AccessControlSections.Access);
            deniedParent.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.DeleteSubdirectoriesAndFiles, AccessControlType.Deny));
            try
            {
                sourceInfo.SetAccessControl(deniedSource);
                parentInfo.SetAccessControl(deniedParent);
                Check((await workspace.RenameFolderAsync(folder.Folder.Id, "权限改名")).Outcome == Outcome.Failed
                    && workspace.Snapshot.Folders.Single().Folder == folder.Folder, "真实 ACL 拒绝改名时不改变展示和映射");
            }
            finally
            {
                deniedSource.SetSecurityDescriptorBinaryForm(originalSource.GetSecurityDescriptorBinaryForm(), AccessControlSections.Access);
                sourceInfo.SetAccessControl(deniedSource);
                deniedParent.SetSecurityDescriptorBinaryForm(originalParent.GetSecurityDescriptorBinaryForm(), AccessControlSections.Access);
                parentInfo.SetAccessControl(deniedParent);
            }
        }
        var locked = Path.Combine(folder.ActualPath, "占用.txt");
        using (var handle = new FileStream(locked, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            Check((await workspace.RenameFolderAsync(folder.Folder.Id, "占用改名")).Outcome == Outcome.Failed && Directory.Exists(folder.ActualPath), "真实占用不伪造改名");
        store.FailOnSave = store.Saves + 1;
        Check((await workspace.RenameFolderAsync(folder.Folder.Id, "意图失败")).Outcome == Outcome.Failed && Directory.Exists(folder.ActualPath), "意图保存失败没有文件副作用");
        store.FailOnSave = store.Saves + 2;
        var result = await workspace.RenameFolderAsync(folder.Folder.Id, "提交中断");
        Check(result.Outcome == Outcome.RecoveryRequired && Directory.Exists(result.ActualPath), "最终提交失败返回真实路径和恢复状态");
        Check(workspace.Snapshot.Folders.Single().Folder.Name == "提交中断", "提交失败仍展示实际名称");
        workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
        Check((await workspace.InitializeAsync(Displays)).Succeeded && workspace.Snapshot.Folders.Single().Folder.Name == "提交中断", "从意图和归属恢复真实改名");
        var current = workspace.Snapshot.Folders.Single();
        Check((await workspace.RenameFolderAsync(current.Folder.Id, "CaseName")).Succeeded, "首次英文改名");
        Check((await workspace.RenameFolderAsync(current.Folder.Id, "casename")).Succeeded && Directory.EnumerateDirectories(fixture.Content).Any(path => Path.GetFileName(path) == "casename"), "仅大小写改名实际生效");
        // 意图保存后另一程序抢占目标；实际排他改名不得覆盖它。
        store = new FailingStore(fixture.Store);
        workspace = new DesktopWorkspace(store, new TestStartup());
        await workspace.InitializeAsync(Displays);
        store.AfterSave = saved =>
        {
            if (saved.PendingFolderChange is not { Kind: FolderChangeKind.Rename } change) return;
            Directory.CreateDirectory(change.Destination);
            File.WriteAllText(Path.Combine(change.Destination, "竞争.txt"), "外部抢占");
        };
        var competed = await workspace.RenameFolderAsync(current.Folder.Id, "抢占目标");
        Check(competed.Outcome == Outcome.Failed && workspace.Snapshot.Folders.Single().Folder.Name == "casename"
            && File.ReadAllText(Path.Combine(fixture.Content, "抢占目标", "竞争.txt")) == "外部抢占", "并发目标抢占没有覆盖和误关联");
    }

    public static async Task KeepContents()
    {
        using var fixture = new Fixture();
        var desktop = Path.Combine(fixture.Home, "替换桌面");
        Directory.CreateDirectory(desktop);
        IFolderShell shell = new WindowsFolderShell(desktop);
        IDesktopWorkspace workspace = new DesktopWorkspace(fixture.Store, new TestStartup(), shell);
        await workspace.InitializeAsync(Displays);
        await workspace.SelectRootAsync(fixture.Content);
        await workspace.CreateFolderAsync("保留内容");
        var folder = workspace.Snapshot.Folders.Single();
        File.WriteAllText(Path.Combine(folder.ActualPath, "保留.txt"), "原地保留字节");
        var collision = Path.Combine(desktop, "保留内容.lnk");
        File.WriteAllText(collision, "不得覆盖的原文件");
        Check((await workspace.DeleteFolderAsync(folder.Folder.Id, FolderDeleteChoice.Cancel)).Outcome == Outcome.Cancelled, "明确取消无副作用");
        Check((await workspace.DeleteFolderAsync(folder.Folder.Id, FolderDeleteChoice.KeepContents)).Outcome == Outcome.Conflict, "快捷方式同名待选择");
        Check((await workspace.DeleteFolderAsync(folder.Folder.Id, FolderDeleteChoice.KeepContents, ConflictChoice.Skip)).Outcome == Outcome.Skipped && workspace.Snapshot.Folders.Count == 1, "跳过仍保留可操作入口");
        var result = await workspace.DeleteFolderAsync(folder.Folder.Id, FolderDeleteChoice.KeepContents, ConflictChoice.KeepBoth);
        Check(result.Succeeded && workspace.Snapshot.Folders.Count == 0, "快捷方式成功后移除工具展示");
        var link = Path.Combine(desktop, "保留内容 (2).lnk");
        Check(shell.ShortcutTargets(link, folder.ActualPath) && File.ReadAllText(collision) == "不得覆盖的原文件", "实际快捷方式指向原目录且不覆盖");
        Check(File.ReadAllText(Path.Combine(folder.ActualPath, "保留.txt")) == "原地保留字节", "内容原地保留");
        var retained = fixture.Store.Read().State.RetainedFolders.Single();
        Check(retained == new RetainedFolder(folder.Folder.Id, folder.ActualPath, link), "保存迁移所需关联");
        workspace = new DesktopWorkspace(fixture.Store, new TestStartup(), shell);
        Check((await workspace.InitializeAsync(Displays)).Succeeded && workspace.Snapshot.Folders.Count == 0, "重启不复活已移除入口");
    }
    public static async Task DeleteFailures()
    {
        using var fixture = new Fixture();
        var desktop = Path.Combine(fixture.Home, "替换桌面");
        Directory.CreateDirectory(desktop);
        var store = new FailingStore(fixture.Store);
        var shell = new FailingShell(new WindowsFolderShell(desktop));
        IDesktopWorkspace workspace = new DesktopWorkspace(store, new TestStartup(), shell);
        await workspace.InitializeAsync(Displays);
        await workspace.SelectRootAsync(fixture.Content);
        await workspace.CreateFolderAsync("失败入口");
        var folder = workspace.Snapshot.Folders.Single();
        File.WriteAllText(Path.Combine(folder.ActualPath, "保留.txt"), "不丢失");
        shell.FailShortcut = true;
        var result = await workspace.DeleteFolderAsync(folder.Folder.Id, FolderDeleteChoice.KeepContents);
        Check(result.Outcome == Outcome.Failed && result.Message.Contains("快捷方式") && workspace.Snapshot.Folders.Count == 1, "创建失败保留可操作入口并区分原因");
        Check((await workspace.DeleteFolderAsync(folder.Folder.Id, FolderDeleteChoice.Recycle)).Outcome == Outcome.Cancelled
            && Directory.Exists(folder.ActualPath), "Windows 回收取消不永久删除");
        shell.FailRecycle = true;
        Check((await workspace.DeleteFolderAsync(folder.Folder.Id, FolderDeleteChoice.Recycle)).Outcome == Outcome.Failed
            && File.ReadAllText(Path.Combine(folder.ActualPath, "保留.txt")) == "不丢失", "回收失败仍保留入口和内容");
        shell.FailShortcut = false;
        store.FailOnSave = store.Saves + 2;
        result = await workspace.DeleteFolderAsync(folder.Folder.Id, FolderDeleteChoice.KeepContents);
        Check(result.Outcome == Outcome.RecoveryRequired && workspace.Snapshot.Folders.Count == 1 && Directory.Exists(folder.ActualPath), "快捷方式成功但提交失败保留待恢复入口");
        workspace = new DesktopWorkspace(fixture.Store, new TestStartup(), shell);
        Check((await workspace.InitializeAsync(Displays)).Succeeded && workspace.Snapshot.Folders.Count == 0, "重启核对真实快捷方式后完成移除");
        Check(fixture.Store.Read().State.RetainedFolders.Count() == 1, "恢复只保存一次保留关联");

        await workspace.CreateFolderAsync("操作前中断");
        folder = workspace.Snapshot.Folders.Single();
        var intent = new PendingFolderChange(folder.Folder.Id, FolderChangeKind.KeepContents, Path.Combine(desktop, "未生成.lnk"));
        fixture.Store.Save(fixture.Store.Read().State with { PendingFolderChange = intent });
        workspace = new DesktopWorkspace(fixture.Store, new TestStartup(), shell);
        Check((await workspace.InitializeAsync(Displays)).Succeeded && workspace.Snapshot.Folders.Count == 1, "操作前中断未生成快捷方式则保留活动入口");
        shell.ThrowAfterShortcut = true;
        result = await workspace.DeleteFolderAsync(folder.Folder.Id, FolderDeleteChoice.KeepContents);
        Check(result.Outcome == Outcome.RecoveryRequired && workspace.Snapshot.Folders.Count == 1, "快捷方式已生成但 Shell 返回异常时保留意图及入口");
        shell.ThrowAfterShortcut = false;
        workspace = new DesktopWorkspace(fixture.Store, new TestStartup(), shell);
        Check((await workspace.InitializeAsync(Displays)).Succeeded && workspace.Snapshot.Folders.Count == 0
            && fixture.Store.Read().State.RetainedFolders.Length == 2, "重新核对成功文件效果并保存迁移关联");
    }

    public static async Task Recycle()
    {
        using var fixture = new Fixture(Path.Combine(Environment.CurrentDirectory, ".scratch", "desktop-folder", "verification"));
        var store = new FailingStore(fixture.Store);
        IFolderShell shell = new WindowsFolderShell(fixture.Home);
        IDesktopWorkspace workspace = new DesktopWorkspace(store, new TestStartup(), shell);
        await workspace.InitializeAsync(Displays);
        await workspace.SelectRootAsync(fixture.Content);
        await workspace.CreateFolderAsync("Kage 临时回收夹具");
        var folder = workspace.Snapshot.Folders.Single();
        Directory.CreateDirectory(Path.Combine(folder.ActualPath, "子目录"));
        File.WriteAllBytes(Path.Combine(folder.ActualPath, "子目录", "字节.bin"), [3, 5, 8, 255]);
        string? recycled = null;
        try
        {
            store.FailOnSave = store.Saves + 2;
            var result = await workspace.DeleteFolderAsync(folder.Folder.Id, FolderDeleteChoice.Recycle);
            recycled = shell.FindRecycledFolder(folder.ActualPath, folder.Folder.Id);
            Check(result.Outcome == Outcome.RecoveryRequired && recycled != null && !Directory.Exists(folder.ActualPath), $"真实回收后提交失败可恢复：{result.Message}");
            Check(File.ReadAllBytes(Path.Combine(recycled!, "子目录", "字节.bin")).SequenceEqual(new byte[] { 3, 5, 8, 255 }), "回收站保留完整子目录和字节");
            workspace = new DesktopWorkspace(fixture.Store, new TestStartup(), shell);
            Check((await workspace.InitializeAsync(Displays)).Succeeded && workspace.Snapshot.Folders.Count == 0, "重启核对回收站归属后移除活动映射");
        }
        finally
        {
            recycled ??= shell.FindRecycledFolder(folder.ActualPath, folder.Folder.Id);
            RestoreFixture(recycled, folder.ActualPath, folder.Folder.Id);
        }
        await workspace.CreateFolderAsync("正常回收夹具");
        folder = workspace.Snapshot.Folders.Single();
        try
        {
            var result = await workspace.DeleteFolderAsync(folder.Folder.Id, FolderDeleteChoice.Recycle);
            Check(result.Succeeded && workspace.Snapshot.Folders.Count == 0, $"正常回收确认后移除：{result.Message}");
        }
        finally { RestoreFixture(shell.FindRecycledFolder(folder.ActualPath, folder.Folder.Id), folder.ActualPath, folder.Folder.Id); }
    }

    private static void RestoreFixture(string? recycled, string original, Guid id)
    {
        if (recycled == null) return;
        var full = Path.GetFullPath(recycled);
        var originalFull = Path.GetFullPath(original);
        var verification = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, ".scratch", "desktop-folder", "verification")) + Path.DirectorySeparatorChar;
        Check((originalFull.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) || originalFull.StartsWith(verification, StringComparison.OrdinalIgnoreCase))
            && originalFull.Contains("Kage-check-", StringComparison.Ordinal), "恢复目标仅为随机临时夹具");
        Check(full.Contains("\\$Recycle.Bin\\", StringComparison.OrdinalIgnoreCase)
            && Path.GetFileName(full).StartsWith("$R", StringComparison.Ordinal)
            && File.ReadAllText(Path.Combine(full, ".kage-folder-id")) == id.ToString("N"), "仅恢复本夹具的回收项目");
        var metadata = Path.Combine(Path.GetDirectoryName(full)!, "$I" + Path.GetFileName(full)[2..]);
        Directory.Move(full, originalFull);
        if (File.Exists(metadata)) File.Delete(metadata);
    }
}

sealed class FailingShell(IFolderShell real) : IFolderShell
{
    public bool FailShortcut { get; set; }
    public bool FailRecycle { get; set; }
    public bool ThrowAfterShortcut { get; set; }
    public string DesktopDirectory => real.DesktopDirectory;
    public void CreateShortcut(string path, string target)
    {
        if (FailShortcut) throw new IOException("注入快捷方式创建失败");
        real.CreateShortcut(path, target);
        if (ThrowAfterShortcut) throw new IOException("注入快捷方式生成后的 Shell 异常");
    }
    public bool ShortcutTargets(string path, string target) => real.ShortcutTargets(path, target);
    public OperationResult Recycle(string path, Guid id) => new(FailRecycle ? Outcome.Failed : Outcome.Cancelled, FailRecycle ? "注入回收失败" : "注入 Windows 回收取消", path);
    public string? FindRecycledFolder(string path, Guid id) => real.FindRecycledFolder(path, id);
}
