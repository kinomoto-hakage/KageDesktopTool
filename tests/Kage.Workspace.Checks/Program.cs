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
    ("已有根目录离线时保留关联并明确另选", OfflineRootSelection)
};
var failures = 0;
foreach (var test in tests.Where(t => args.Length == 0 || t.Name.Contains(args[0], StringComparison.Ordinal)))
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
    Check(Directory.GetFiles(Path.GetDirectoryName(primary)!, "workspace.json.damaged-*").Length == 1, "保留损坏文件证据");
    File.Delete(primary + ".bak");
    File.WriteAllText(primary, "{}");
    // 缺失必要格式字段同样不是合法的空工作区。
    workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
    Check((await workspace.InitializeAsync([new(0, 0, 1000, 800)])).Outcome == Outcome.RecoveryRequired, "结构不完整的状态必须阻止写入");
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
