using Kage.Workspace;

internal static class UpgradeLayoutChecks
{
    internal static async Task FreeDrag()
    {
        using var fixture = new Fixture();
        var moving = new FolderRecord(Guid.NewGuid(), "自由移动", 0, 100);
        var obstacle = new FolderRecord(Guid.NewGuid(), "保持原位", 400, 100);
        foreach (var folder in new[] { moving, obstacle }) Directory.CreateDirectory(Path.Combine(fixture.Content, folder.Name));
        fixture.Store.Save(new WorkspaceState { Root = fixture.Content, Folders = [moving, obstacle] });
        IDesktopWorkspace workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
        await workspace.InitializeAsync([new(0, 0, 1400, 800)]);
        var edit = workspace.BeginLayout(moving.Id)!;
        edit.BeginDrag(0, 0);
        edit.DragTo(450, 0);
        Ensure(edit.Folders.First().Folder is { X: 450, Y: 100 }, "预览可进入占用落点");
        Ensure(workspace.Snapshot.Folders.First().Folder == moving, "预览不发布持久布局");
        Ensure((await workspace.CommitLayoutAsync(edit)).Succeeded, "释放在落点附近找空位");
        Ensure(workspace.Snapshot.Folders.First().Folder is { X: 450, Y: 40 }, "最近位置平局先选上方，保留 12 像素间隙");
        Ensure(workspace.Snapshot.Folders.Last().Folder == obstacle, "释放不改变其他 Folder");
        edit = workspace.BeginLayout(moving.Id)!;
        edit.BeginDrag(0, 0); edit.DragTo(400, 180);
        Ensure(edit.Folders.First().Folder is { X: 850, Y: 220 }, "对角预览直接穿越 Folder");
        Ensure((await workspace.CommitLayoutAsync(edit)).Succeeded, "可用落点原位提交");
        IDesktopWorkspace restart = new DesktopWorkspace(fixture.Store, new TestStartup());
        await restart.InitializeAsync([new(0, 0, 1400, 800)]);
        Ensure(restart.Snapshot.Folders.Select(f => f.Folder).SequenceEqual(workspace.Snapshot.Folders.Select(f => f.Folder)), "重启保留最终布局");
    }

    internal static async Task ExpansionAndHeader()
    {
        using var fixture = new Fixture();
        var bottom = new FolderRecord(Guid.NewGuid(), "底部展开", 50, 740);
        var other = new FolderRecord(Guid.NewGuid(), "其他已展开", 450, 100, Expanded: true);
        foreach (var folder in new[] { bottom, other }) Directory.CreateDirectory(Path.Combine(fixture.Content, folder.Name));
        fixture.Store.Save(new WorkspaceState { Root = fixture.Content, Folders = [bottom, other] });
        IDesktopWorkspace workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
        DisplayArea[] displays = [new(0, 0, 1000, 800)];
        await workspace.InitializeAsync(displays);
        Ensure((await workspace.ToggleFolderAsync(bottom.Id)).Succeeded, "底部自动寻找完整展开位置");
        var expanded = workspace.Snapshot.Folders.First().Folder;
        Ensure(expanded is { X: 50, Y: 492, Expanded: true }, "底部展开选择最近的上方位置");
        Ensure(workspace.Snapshot.Folders.Last().Folder == other, "展开不移动其他已展开 Folder");
        var edit = workspace.BeginLayout(bottom.Id)!;
        Ensure(!edit.ResizeHeaderBy(20), "底部空间不足不改变头部高度");
        edit.BeginDrag(0, 0); edit.DragTo(0, -100);
        await workspace.CommitLayoutAsync(edit);
        var before = workspace.Snapshot.Folders.First().Folder;
        edit = workspace.BeginLayout(bottom.Id)!;
        Ensure(edit.ResizeHeaderBy(100), "分隔线可以单独调高");
        Ensure(edit.Folders.First().Folder == before with { HeaderHeight = 82 }, "头部上限 82 DIP，内容高度、宽度和位置保持不变");
        Ensure(workspace.Snapshot.Folders.First().Folder == before, "尺寸预览不提交");
        Ensure((await workspace.CommitLayoutAsync(edit)).Succeeded, "分隔线最终高度保存");
        await workspace.ToggleFolderAsync(bottom.Id);
        await workspace.ToggleFolderAsync(bottom.Id);
        Ensure(workspace.Snapshot.Folders.First().Folder.HeaderHeight == 82, "折叠展开保持头部高度");
        edit = workspace.BeginLayout(bottom.Id)!;
        Ensure(edit.ResizeHeaderBy(-100) && edit.Folders.First().Folder.HeaderHeight == 42, "头部下限 42 DIP");
        await workspace.CommitLayoutAsync(edit);
        IDesktopWorkspace restart = new DesktopWorkspace(fixture.Store, new TestStartup());
        await restart.InitializeAsync(displays);
        Ensure(restart.Snapshot.Folders.Select(f => f.Folder).SequenceEqual(workspace.Snapshot.Folders.Select(f => f.Folder)), "调高后重启恢复全部尺寸");
        var cancelled = workspace.BeginLayout(bottom.Id)!;
        cancelled.ResizeHeaderBy(10);
        Ensure(workspace.Snapshot.Folders.First().Folder.HeaderHeight == 42, "未提交会话可直接丢弃");
    }

    internal static async Task DisplayPriorityAndCapacity()
    {
        using var fixture = new Fixture();
        var moving = new FolderRecord(Guid.NewGuid(), "当前屏", -500, 100);
        var obstacle = new FolderRecord(Guid.NewGuid(), "占用落点", -300, 300);
        foreach (var folder in new[] { moving, obstacle }) Directory.CreateDirectory(Path.Combine(fixture.Content, folder.Name));
        DisplayArea[] displays = [new(-1000, 0, 1000, 800), new(0, 0, 1000, 800)];
        fixture.Store.Save(new WorkspaceState { Root = fixture.Content, Folders = [moving, obstacle] });
        IDesktopWorkspace workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
        await workspace.InitializeAsync(displays);
        var edit = workspace.BeginLayout(moving.Id)!;
        edit.BeginDrag(0, 0); edit.DragTo(400, 200);
        Ensure((await workspace.CommitLayoutAsync(edit)).Succeeded, "跨屏接缝占用落点可释放");
        Ensure(workspace.Snapshot.Folders.First().Folder is { X: -100, Y: 240 }, "优先原点所在当前屏，平局按纵坐标");
        Ensure(workspace.Snapshot.Folders.Last().Folder == obstacle, "跨屏寻找保持障碍原位");

        moving = moving with { X = -300, Y = 100, BodyHeight = 260 };
        displays = [new(-500, 0, 500, 180), new(200, -100, 1000, 900, 1.5)];
        fixture.Store.Save(new WorkspaceState { Root = fixture.Content, Folders = [moving] });
        workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
        await workspace.InitializeAsync(displays);
        Ensure((await workspace.ToggleFolderAsync(moving.Id)).Succeeded, "当前屏无法展开时考虑其他显示器");
        Ensure(workspace.Snapshot.Folders.Single() is { Folder.X: 200, Folder.Y: 100, DisplayScale: 1.5 }, "排除屏幕空洞并使用目标 DPI");
        moving = moving with { X = 0, Y = 0 };
        fixture.Store.Save(new WorkspaceState { Root = fixture.Content, Folders = [moving] });
        workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
        await workspace.InitializeAsync([new(0, 0, 300, 48)]);
        var result = await workspace.ToggleFolderAsync(moving.Id);
        Ensure(!result.Succeeded && result.Message.Contains("保持折叠") && workspace.Snapshot.Folders.Single().Folder == moving, "无空位保持折叠并返回原因");
        edit = workspace.BeginLayout(moving.Id)!;
        edit.BeginDrag(0, 0); edit.DragTo(100, 100);
        Ensure((await workspace.CommitLayoutAsync(edit)).Succeeded && workspace.Snapshot.Folders.Single().Folder == moving, "唯一空位释放恢复原位");
    }

    internal static async Task ExactNearest()
    {
        using var fixture = new Fixture();
        var moving = new FolderRecord(Guid.NewGuid(), "精确落点", 0, 0, 240, 42);
        var obstacle = new FolderRecord(Guid.NewGuid(), "固定障碍", 260, 80, 240, 42);
        foreach (var folder in new[] { moving, obstacle }) Directory.CreateDirectory(Path.Combine(fixture.Content, folder.Name));
        fixture.Store.Save(new WorkspaceState { Root = fixture.Content, Folders = [moving, obstacle] });
        IDesktopWorkspace workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
        await workspace.InitializeAsync([new(0, 0, 660, 240)]);
        foreach (var (x, y) in new[] { (260, 80), (270, 80), (250, 100), (420, 81), (13, 79), (312, 121) })
        {
            // 独立穷举整个小工作区，作为最近位置和固定平局规则的参考答案。
            var expected = (from py in Enumerable.Range(0, 199)
                from px in Enumerable.Range(0, 421)
                where px + 252 <= 260 || px >= 512 || py + 54 <= 80 || py >= 134
                orderby (px - x) * (px - x) + (py - y) * (py - y), py, px
                select (X: px, Y: py)).First();
            var current = workspace.Snapshot.Folders.First().Folder;
            var edit = workspace.BeginLayout(moving.Id)!;
            edit.BeginDrag(0, 0); edit.DragTo(x - current.X, y - current.Y);
            Ensure((await workspace.CommitLayoutAsync(edit)).Succeeded, "精确最近位置可提交");
            var placed = workspace.Snapshot.Folders.First().Folder;
            Ensure((placed.X, placed.Y) == expected, $"最近位置与独立穷举一致：落点 {x},{y}，期望 {expected}，实际 {placed.X},{placed.Y}");
        }
    }

    private static void Ensure(bool condition, string message) { if (!condition) throw new Exception(message); }
}
