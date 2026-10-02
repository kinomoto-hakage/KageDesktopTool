using Kage.Workspace;

var tests = new (string Name, Func<Task> Run)[]
{
    ("真实创建与稳定标识重启", CreateAndRestart),
    ("保存中断不丢失目录关联", InterruptedCreate),
    ("损坏状态只读加载并显式恢复", DamagedState),
    ("Windows 名称及同名三种选择", NamesAndConflicts),
    ("初始布局、空间不足及恢复", LayoutAndCapacity),
    ("自启偏好与平台双向提交及失败", StartupTransactions),
    ("根目录失败及真实文件系统失败", RootAndFileFailures),
    ("已有根目录离线时保留关联并明确另选", OfflineRootSelection),
    ("抢先创建目录与清理失败不误关联", CompetingDirectoryRecovery),
    ("中断自启操作按实际配置恢复", InterruptedStartup),
    ("不完整归属标识可核对恢复", InterruptedIdentity),
    ("真实内容、外部变化及失联目录", ContentsAndChanges),
    ("多 Folder 展开及空间不足回滚", LayoutChecks.Expansion),
    ("屏幕四边受阻反向一像素及滑动", LayoutChecks.ScreenEdges),
    ("Folder 四向接触、不穿越及尺寸重启恢复", LayoutChecks.ContactAndPersistence),
    ("对角轨迹沿实际路径不穿越 Folder", LayoutChecks.DiagonalPath),
    ("暂未展示 Folder 不挤动展开头部", LayoutChecks.HiddenAndExpansion),
    ("布局保存失败保留原状态与内容", LayoutChecks.SaveFailure)
};
var failures = 0;
var selected = tests.Where(t => args.Length == 0 || t.Name.Contains(args[0], StringComparison.Ordinal)).ToArray();
if (selected.Length == 0) { Console.Error.WriteLine("没有匹配的检查名称。"); return 1; }
foreach (var test in selected)
{
    try { await test.Run(); Console.WriteLine($"通过：{test.Name}"); }
    catch (Exception e) { failures++; Console.Error.WriteLine($"失败：{test.Name}\n{e}"); }
}
return failures == 0 ? 0 : 1;

static async Task CreateAndRestart()
{
    using var fixture = new Fixture();
    IDesktopWorkspace workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
    await workspace.InitializeAsync([new(0, 0, 1000, 800)]);
    Check(workspace.Snapshot.Folders.Count == 0, "首次运行不生成示例入口");
    Check((await workspace.SelectRootAsync(fixture.Content)).Succeeded, "选择真实隔离根目录");
    var result = await workspace.CreateFolderAsync("工作");
    Check(result.Succeeded && Directory.Exists(Path.Combine(fixture.Content, "工作")), "真实目录与成功结果一致");
    var folder = workspace.Snapshot.Folders.Single();
    File.WriteAllText(Path.Combine(folder.ActualPath, "保留.txt"), "隔离内容");
    workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
    Check((await workspace.InitializeAsync([new(0, 0, 1000, 800)])).Succeeded, "重启成功");
    var restored = workspace.Snapshot.Folders.Single();
    Check(restored.Folder == folder.Folder, "稳定标识、名称及头部位置完整恢复");
    Check(File.ReadAllText(Path.Combine(restored.ActualPath, "保留.txt")) == "隔离内容", "重启保留真实内容");
}

static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

static async Task ContentsAndChanges()
{
    using var fixture = new Fixture();
    IDesktopWorkspace workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
    DisplayArea[] areas = [new(0, 0, 1000, 800)];
    await workspace.InitializeAsync(areas);
    await workspace.SelectRootAsync(fixture.Content);
    await workspace.CreateFolderAsync("真实内容");
    var path = workspace.Snapshot.Folders.Single().ActualPath;
    File.WriteAllText(Path.Combine(path, "短.txt"), "真实文件");
    File.WriteAllText(Path.Combine(path, "快捷方式.lnk"), "文件计数不解析目标");
    Directory.CreateDirectory(Path.Combine(path, "普通子文件夹"));
    await workspace.RefreshAsync(areas);
    var folder = workspace.Snapshot.Folders.Single();
    Check(folder.FileCount == 2 && folder.Entries!.Count == 3, "直接文件含快捷方式，子目录仍展示，隐藏内部标识");
    Check(folder.Entries.Single(e => e.Name == "普通子文件夹").IsDirectory, "实际子目录类型");
    File.Move(Path.Combine(path, "短.txt"), Path.Combine(path, "重命名.txt"));
    File.Delete(Path.Combine(path, "快捷方式.lnk"));
    await workspace.RefreshAsync(areas);
    folder = workspace.Snapshot.Folders.Single();
    Check(folder.FileCount == 1 && folder.Entries!.Any(e => e.Name == "重命名.txt") && !folder.Entries.Any(e => e.Name == "短.txt"), "外部删除和重命名更新实际内容");
    Directory.Move(path, path + "-离线");
    await workspace.RefreshAsync(areas);
    folder = workspace.Snapshot.Folders.Single();
    Check(folder.FileCount == null && folder.Notice != null && folder.Entries!.Count == 0 && !Directory.Exists(path), "失联明确说明，保留记录，不生成空目录");
}

static async Task InterruptedCreate()
{
    using var fixture = new Fixture();
    var failing = new FailingStore(fixture.Store);
    IDesktopWorkspace workspace = new DesktopWorkspace(failing, new TestStartup());
    await workspace.InitializeAsync([new(0, 0, 1000, 800)]);
    await workspace.SelectRootAsync(fixture.Content);
    failing.FailOnSave = failing.Saves + 2;
    var result = await workspace.CreateFolderAsync("中断");
    Check(result.Outcome == Outcome.RecoveryRequired && Directory.Exists(result.ActualPath), "最终保存失败返回待恢复和实际目录");
    Check(!(await workspace.CreateFolderAsync("后续")).Succeeded, "待恢复期间不允许继续修改");
    File.WriteAllText(Path.Combine(result.ActualPath!, "内容.txt"), "仍然存在");
    var pendingId = fixture.Store.Read().State.PendingCreate!.Id;
    workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
    Check((await workspace.InitializeAsync([new(0, 0, 1000, 800)])).Succeeded, "重启核对真实目录完成恢复");
    Check(workspace.Snapshot.Folders.Single().Folder.Id == pendingId, "恢复沿用创建前持久化的稳定标识");
    Check(File.ReadAllText(Path.Combine(workspace.Snapshot.Folders.Single().ActualPath, "内容.txt")) == "仍然存在", "中断恢复不损坏内容");
    failing = new FailingStore(fixture.Store) { FailOnSave = 1 };
    workspace = new DesktopWorkspace(failing, new TestStartup());
    await workspace.InitializeAsync([new(0, 0, 1000, 800)]);
    Check(!(await workspace.CreateFolderAsync("无记录")).Succeeded && !Directory.Exists(Path.Combine(fixture.Content, "无记录")), "创建前保存失败不产生无关联目录");
}

static async Task DamagedState()
{
    using var fixture = new Fixture();
    var workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
    await workspace.InitializeAsync([new(0, 0, 1000, 800)]);
    await workspace.SelectRootAsync(fixture.Content);
    await workspace.CreateFolderAsync("备份");
    var primary = Path.Combine(fixture.Home, "状态", "workspace.json");
    File.WriteAllText(primary, "损坏的状态");
    workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
    Check((await workspace.InitializeAsync([new(0, 0, 1000, 800)])).Outcome == Outcome.RecoveryRequired, "损坏状态不可当作空工作区");
    Check(!(await workspace.CreateFolderAsync("禁止覆盖")).Succeeded && File.ReadAllText(primary) == "损坏的状态", "未确认前禁止覆盖损坏证据");
    Check((await workspace.RestoreBackupAsync()).Succeeded, "明确恢复最近有效备份");
    Check(workspace.Snapshot.Folders.Single().Folder.Name == "备份", "备份或未完成记录恢复现有目录关联");
    Check(!workspace.Snapshot.Notices.Any(notice => notice.Contains("需明确恢复", StringComparison.Ordinal)), "恢复完成清除旧的未恢复提示");
    Check(Directory.GetFiles(Path.GetDirectoryName(primary)!, "workspace.json.damaged-*").Length == 1, "保留损坏文件证据");
    var valid = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(primary))!;
    File.Delete(primary + ".bak");
    File.WriteAllText(primary, "{}");
    // 缺失必要格式字段同样不是合法的空工作区。
    workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
    Check((await workspace.InitializeAsync([new(0, 0, 1000, 800)])).Outcome == Outcome.RecoveryRequired, "结构不完整的状态必须阻止写入");
    valid["Folders"]![0]!.AsObject().Remove("X");
    File.WriteAllText(primary, valid.ToJsonString());
    workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
    Check((await workspace.InitializeAsync([new(0, 0, 1000, 800)])).Outcome == Outcome.RecoveryRequired, "Folder 缺失位置不能通过默认值掩盖损坏");
}

static async Task NamesAndConflicts()
{
    using var fixture = new Fixture();
    var workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
    await workspace.InitializeAsync([new(0, 0, 1000, 800)]);
    await workspace.SelectRootAsync(fixture.Content);
    foreach (var invalid in new[] { "CON", "NUL.txt", "COM¹", "LPT9.log", "..", "结尾.", "结尾 ", "a/b", "a\\b", "x:y", "", "  ", new string('字', 256) })
        Check(!(await workspace.CreateFolderAsync(invalid)).Succeeded, $"拒绝非法名称：{invalid}");
    Directory.CreateDirectory(Path.Combine(fixture.Content, "工作"));
    File.WriteAllText(Path.Combine(fixture.Content, "工作", "原内容.txt"), "不可覆盖");
    File.WriteAllText(Path.Combine(fixture.Content, "工作 (2)"), "同名文件也占位");
    Check((await workspace.CreateFolderAsync("工作")).Outcome == Outcome.Conflict, "询问冲突选择");
    Check((await workspace.CreateFolderAsync("工作", ConflictChoice.Skip)).Outcome == Outcome.Skipped, "明确跳过");
    Check((await workspace.CreateFolderAsync("工作", ConflictChoice.Cancel)).Outcome == Outcome.Cancelled, "明确取消");
    Check((await workspace.CreateFolderAsync("工作", ConflictChoice.KeepBoth)).Succeeded, "保留两份成功");
    Check(workspace.Snapshot.Folders.Single().Folder.Name == "工作 (3)", "编号避开同名目录与文件");
    Check(File.ReadAllText(Path.Combine(fixture.Content, "工作", "原内容.txt")) == "不可覆盖", "原内容未改变");
    await workspace.CreateFolderAsync("WORK");
    Check((await workspace.CreateFolderAsync("work")).Outcome == Outcome.Conflict, "Windows 不区分大小写同名");
}

static async Task LayoutAndCapacity()
{
    using var fixture = new Fixture();
    var workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
    await workspace.InitializeAsync([new(0, 0, 320, 75)]);
    await workspace.SelectRootAsync(fixture.Content);
    await workspace.CreateFolderAsync("一");
    await workspace.CreateFolderAsync("二");
    Check(workspace.Snapshot.Folders.Count == 2 && workspace.Snapshot.Folders.Count(f => f.Visible) == 1, "空间不足保留两个关联且仅显示容纳的头部");
    Check(workspace.Snapshot.Folders.Single(f => !f.Visible).Notice != null, "隐藏状态有具体说明");
    await workspace.RefreshAsync([new(-1000, -200, 1800, 800, 1.5)]);
    var records = workspace.Snapshot.Folders;
    Check(records.All(f => f.Visible && Directory.Exists(f.ActualPath)), "释放空间后恢复全部真实入口");
    var first = records[0].Folder;
    var second = records[1].Folder;
    Check(first.X + 450 <= second.X || second.X + 450 <= first.X || first.Y + 72 <= second.Y || second.Y + 72 <= first.Y, "真实缩放下头部不重叠");
    var restarted = new DesktopWorkspace(fixture.Store, new TestStartup());
    await restarted.InitializeAsync([new(-1000, -200, 1800, 800, 1.5)]);
    Check(restarted.Snapshot.Folders.Select(f => f.Folder).SequenceEqual(records.Select(f => f.Folder)), "不重叠位置持久恢复");
}

static async Task StartupTransactions()
{
    using var fixture = new Fixture();
    var platform = new TestStartup();
    var failing = new FailingStore(fixture.Store);
    var workspace = new DesktopWorkspace(failing, platform);
    await workspace.InitializeAsync([new(0, 0, 1000, 800)]);
    Check(platform.Command == null, "没有保存的开启选择时不注册自启");
    Check((await workspace.SetStartupAsync(true)).Succeeded, "保存并注册自启");
    Check(platform.Command == platform.LaunchCommand && workspace.Snapshot.StartupEnabled, "真实配置与偏好一致");
    Check((await workspace.SetStartupAsync(true)).Succeeded && platform.Command == platform.LaunchCommand, "重复开启保持单一配置");
    workspace = new DesktopWorkspace(failing, platform);
    await workspace.InitializeAsync([new(0, 0, 1000, 800)]);
    Check(workspace.Snapshot.StartupEnabled, "重启读取开启选择");
    platform.FailNextWrite = true;
    Check(!(await workspace.SetStartupAsync(false)).Succeeded, "平台更新失败不能伪装成功");
    Check(workspace.Snapshot.StartupEnabled && platform.Command == platform.LaunchCommand, "失败保持原偏好与实际配置");
    failing.FailOnSave = failing.Saves + 2;
    Check(!(await workspace.SetStartupAsync(false)).Succeeded, "平台成功后状态失败也报告失败");
    Check(platform.Command == platform.LaunchCommand && workspace.Snapshot.StartupEnabled, "保存失败恢复先前实际注册");
    Check((await workspace.SetStartupAsync(false)).Succeeded && platform.Command == null, "关闭移除真实注册");
    Check(!fixture.Store.Read().State.StartupEnabled, "关闭偏好持久化");
    platform.Command = "\"C:\\旧包\\KageDesktopTool.exe\"";
    workspace = new DesktopWorkspace(fixture.Store, platform);
    await workspace.InitializeAsync([new(0, 0, 1000, 800)]);
    Check(platform.Command.Contains("旧包", StringComparison.Ordinal) && workspace.Snapshot.Notices.Any(n => n.Contains("不一致", StringComparison.Ordinal)), "启动只说明配置差异，不自动更改注册");
}

static async Task RootAndFileFailures()
{
    using var fixture = new Fixture();
    var unavailable = Path.Combine(fixture.Home, "作为文件的根目录");
    File.WriteAllText(unavailable, "原内容");
    fixture.Store.Save(new WorkspaceState { Root = unavailable });
    var workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
    await workspace.InitializeAsync([new(0, 0, 1000, 800)]);
    Check(workspace.Snapshot.Root == unavailable && workspace.Snapshot.Notices.Count > 0, "不可用的保存根目录明确说明且不静默替换");
    Check(!(await workspace.CreateFolderAsync("失败")).Succeeded && !Directory.Exists(fixture.Content), "失败不会静默创建到别的根目录");
    Check((await workspace.SelectRootAsync(fixture.Content)).Succeeded, "用户明确选择可用目录后可创建");
    Check((await workspace.CreateFolderAsync("已选择")).Succeeded, "采用选择的真实路径");
    var root = Path.Combine(fixture.Home, "只读故障");
    Directory.CreateDirectory(root);
    fixture.Store.Save(new WorkspaceState { Root = root });
    workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
    await workspace.InitializeAsync([new(0, 0, 1000, 800)]);
    // 不可共享打开同名文件，验证真实冲突不会覆盖或伪造创建。
    var lockFile = Path.Combine(root, "锁定");
    using (var locked = new FileStream(lockFile, FileMode.CreateNew, FileAccess.Write, FileShare.None))
    {
        Check((await workspace.CreateFolderAsync("锁定")).Outcome == Outcome.Conflict, "占用的同名文件不被覆盖");
        Check(!(await workspace.CreateFolderAsync(new string('长', 255) + " (2)", ConflictChoice.KeepBoth)).Succeeded, "完整 Windows 名称长度检查");
    }
    Check(workspace.Snapshot.Folders.Count == 0, "失败与冲突不伪造 Folder 记录");
    var racing = new FailingStore(fixture.Store)
    {
        AfterSave = state =>
        {
            if (state.PendingCreate is { } pending) File.WriteAllText(Path.Combine(state.Root, pending.Name), "并发写入，不可覆盖");
        }
    };
    workspace = new DesktopWorkspace(racing, new TestStartup());
    await workspace.InitializeAsync([new(0, 0, 1000, 800)]);
    Check((await workspace.CreateFolderAsync("竞争")).Outcome == Outcome.Failed, "检查同名后出现真实文件仍返回创建失败");
    Check(File.ReadAllText(Path.Combine(root, "竞争")) == "并发写入，不可覆盖" && workspace.Snapshot.Folders.Count == 0, "排他创建不会覆盖竞争文件或伪造关联");
}

static async Task OfflineRootSelection()
{
    using var fixture = new Fixture();
    var workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
    await workspace.InitializeAsync([new(0, 0, 1000, 800)]);
    await workspace.SelectRootAsync(fixture.Content);
    await workspace.CreateFolderAsync("旧关联");
    var original = workspace.Snapshot.Folders.Single();
    var unavailable = Path.Combine(fixture.Home, "暂时离线的内容");
    Directory.Move(fixture.Content, unavailable);
    var selected = Path.Combine(fixture.Home, "用户另选");
    Check((await workspace.SelectRootAsync(selected)).Succeeded, "已有根目录不可用时允许明确选择其他目录供新建");
    Check((await workspace.CreateFolderAsync("新关联")).Succeeded, "新建采用用户明确选择的根目录");
    Check(workspace.Snapshot.Folders.Single(f => f.Folder.Id == original.Folder.Id).ActualPath == original.ActualPath, "不把旧关联伪装成新根目录中的内容");
    Directory.Move(unavailable, fixture.Content);
    var restarted = new DesktopWorkspace(fixture.Store, new TestStartup());
    await restarted.InitializeAsync([new(0, 0, 1000, 800)]);
    Check(restarted.Snapshot.Folders.Single(f => f.Folder.Id == original.Folder.Id).ActualPath == original.ActualPath, "原目录回来后沿用原标识及实际路径");
}

static async Task CompetingDirectoryRecovery()
{
    using var fixture = new Fixture();
    var failing = new FailingStore(fixture.Store);
    var workspace = new DesktopWorkspace(failing, new TestStartup());
    await workspace.InitializeAsync([new(0, 0, 1000, 800)]);
    await workspace.SelectRootAsync(fixture.Content);
    failing.FailOnSave = failing.Saves + 2;
    failing.AfterSave = state =>
    {
        if (state.PendingCreate is { } folder)
        {
            var path = Path.Combine(state.Root, folder.Name);
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "外部内容.txt"), "不属于本次创建");
        }
    };
    Check((await workspace.CreateFolderAsync("竞争目录")).Outcome == Outcome.RecoveryRequired, "竞争创建与清理记录失败被明确报告");
    var restarted = new DesktopWorkspace(fixture.Store, new TestStartup());
    Check((await restarted.InitializeAsync([new(0, 0, 1000, 800)])).Outcome == Outcome.RecoveryRequired, "重启不能把仅存在的外部目录当作创建完成");
    Check(restarted.Snapshot.Folders.Count == 0, "外部目录不会自动成为托管 Folder");
    Check(File.ReadAllText(Path.Combine(fixture.Content, "竞争目录", "外部内容.txt")) == "不属于本次创建", "保留竞争目录实际内容");
    Check((await restarted.ConfirmPendingCreateAsync()).Succeeded, "核对并明确确认后可以关联现有目录");
    Check(restarted.Snapshot.Folders.Single().FileCount == 1, "内部标识不计入用户内容文件数");
}

static async Task InterruptedStartup()
{
    using var fixture = new Fixture();
    var startup = new TestStartup { Command = new TestStartup().LaunchCommand };
    fixture.Store.Save(new WorkspaceState { Root = fixture.Content,
        PendingStartup = new(true, null, startup.LaunchCommand) });
    var workspace = new DesktopWorkspace(fixture.Store, startup);
    Check((await workspace.InitializeAsync([new(0, 0, 1000, 800)])).Succeeded && workspace.Snapshot.StartupEnabled, "平台已更新但最终保存中断时沿用实际开启结果");
    Check(fixture.Store.Read().State.PendingStartup == null, "完成恢复后清除中断记录");
    startup.Command = null;
    fixture.Store.Save(new WorkspaceState { Root = fixture.Content,
        PendingStartup = new(true, null, startup.LaunchCommand) });
    workspace = new DesktopWorkspace(fixture.Store, startup);
    Check((await workspace.InitializeAsync([new(0, 0, 1000, 800)])).Succeeded && !workspace.Snapshot.StartupEnabled && startup.Command == null, "尚未执行的平台意图不会被启动过程擅自注册");
    startup.Command = "外部修改的启动命令";
    fixture.Store.Save(new WorkspaceState { Root = fixture.Content,
        PendingStartup = new(true, null, startup.LaunchCommand) });
    workspace = new DesktopWorkspace(fixture.Store, startup);
    Check((await workspace.InitializeAsync([new(0, 0, 1000, 800)])).Outcome == Outcome.RecoveryRequired, "未知实际配置保留恢复状态");
    Check(startup.Command == "外部修改的启动命令" && fixture.Store.Read().State.PendingStartup != null, "恢复不会盲目覆盖外部实际配置");
}

static async Task InterruptedIdentity()
{
    using var fixture = new Fixture();
    var folder = new FolderRecord(Guid.NewGuid(), "标识中断");
    var path = Path.Combine(fixture.Content, folder.Name);
    Directory.CreateDirectory(path);
    File.WriteAllText(Path.Combine(path, "用户内容.txt"), "保留真实内容");
    File.WriteAllText(Path.Combine(path, ".kage-folder-id"), "截断");
    fixture.Store.Save(new WorkspaceState { Root = fixture.Content, PendingCreate = folder });
    var workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
    Check((await workspace.InitializeAsync([new(0, 0, 1000, 800)])).Outcome == Outcome.RecoveryRequired, "不完整标识不自动认领");
    Check((await workspace.ConfirmPendingCreateAsync()).Succeeded, "明确核对后修复中断标识");
    Check(workspace.Snapshot.Folders.Single().Folder.Id == folder.Id && workspace.Snapshot.Folders.Single().FileCount == 1, "恢复标识与真实用户文件数");
    var archived = Directory.GetFiles(path, ".kage-folder-id.invalid-*").Single();
    Check(File.ReadAllText(archived) == "截断", "保留中断标识证据");
    Check(File.ReadAllText(Path.Combine(path, "用户内容.txt")) == "保留真实内容", "修复不改用户内容");
    var other = new FolderRecord(Guid.NewGuid(), "不同归属");
    var otherPath = Path.Combine(fixture.Content, other.Name);
    Directory.CreateDirectory(otherPath);
    var differentId = Guid.NewGuid().ToString("N");
    File.WriteAllText(Path.Combine(otherPath, ".kage-folder-id"), differentId);
    fixture.Store.Save(new WorkspaceState { Root = fixture.Content, PendingCreate = other });
    workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
    await workspace.InitializeAsync([new(0, 0, 1000, 800)]);
    Check(!(await workspace.ConfirmPendingCreateAsync()).Succeeded && File.ReadAllText(Path.Combine(otherPath, ".kage-folder-id")) == differentId,
        "不同有效标识仍然不覆盖或自动认领");
}

sealed class TestStartup : IStartupRegistration
{
    public string LaunchCommand => "\"C:\\测试包\\KageDesktopTool.exe\"";
    public string? Command { get; set; }
    public bool FailNextWrite { get; set; }
    public string? ReadCommand() => Command;
    public void WriteCommand(string? command)
    {
        if (FailNextWrite) { FailNextWrite = false; throw new IOException("注入平台自启更新失败"); }
        Command = command;
    }
}

sealed class FailingStore(IWorkspaceStore real) : IWorkspaceStore
{
    public int Saves { get; private set; }
    public int FailOnSave { get; set; } = -1;
    public Action<WorkspaceState>? AfterSave { get; set; }
    public StateRead Read() => real.Read();
    public void Save(WorkspaceState state)
    {
        if (++Saves == FailOnSave) throw new IOException("注入状态提交失败");
        real.Save(state);
        AfterSave?.Invoke(state);
    }
    public void RestoreBackup() => real.RestoreBackup();
}

sealed class Fixture : IDisposable
{
    public string Home { get; } = Path.Combine(Path.GetTempPath(), "Kage-check-" + Guid.NewGuid().ToString("N"));
    public string Content => Path.Combine(Home, "内容");
    public IWorkspaceStore Store { get; }
    public Fixture()
    {
        Directory.CreateDirectory(Home);
        Store = new JsonWorkspaceStore(Path.Combine(Home, "状态"));
    }
    public void Dispose()
    {
        var full = Path.GetFullPath(Home);
        var temporary = Path.GetFullPath(Path.GetTempPath());
        if (!full.StartsWith(temporary, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("Kage-check-", StringComparison.Ordinal))
            throw new Exception("拒绝清理隔离目录范围之外的路径");
        Directory.Delete(full, true);
    }
}
