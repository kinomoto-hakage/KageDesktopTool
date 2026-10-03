using Kage.Workspace;

internal static class DisplayChecks
{
    internal static async Task AdjacentScreens()
    {
        using var fixture = new Fixture();
        var folder = new FolderRecord(Guid.NewGuid(), "跨屏", -400, 100);
        Directory.CreateDirectory(Path.Combine(fixture.Content, folder.Name));
        fixture.Store.Save(new WorkspaceState { Root = fixture.Content, Folders = [folder] });
        IDesktopWorkspace workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
        DisplayArea[] displays = [new(-1000, -200, 1000, 1000), new(0, 0, 1200, 800)];
        await workspace.InitializeAsync(displays);
        var edit = workspace.BeginLayout(folder.Id)!;
        edit.BeginDrag(0, 0);
        edit.DragTo(250, 0);
        Check(edit.Folders.Single().Folder.X == -150, "相邻屏幕的真实连续工作区允许窗口横跨接缝");
        edit.DragTo(700, 0);
        Check(edit.Folders.Single().Folder.X == 300, "相邻输入的像素差持续移动到第二屏");
        edit.DragTo(699, 0);
        Check(edit.Folders.Single().Folder.X == 299, "跨屏后立即反向一像素");
        Check((await workspace.CommitLayoutAsync(edit)).Succeeded, "跨屏位置可提交");
        workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
        await workspace.InitializeAsync(displays);
        Check(workspace.Snapshot.Folders.Single().Folder.X == 299, "重启恢复跨屏位置");
    }

    internal static async Task GapsAndWorkAreas()
    {
        using var fixture = new Fixture();
        var folder = new FolderRecord(Guid.NewGuid(), "空洞", -400, 100);
        Directory.CreateDirectory(Path.Combine(fixture.Content, folder.Name));
        fixture.Store.Save(new WorkspaceState { Root = fixture.Content, Folders = [folder] });
        IDesktopWorkspace workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
        await workspace.InitializeAsync([new(-1000, 0, 1000, 800), new(200, 0, 1000, 800)]);
        var edit = workspace.BeginLayout(folder.Id)!;
        edit.BeginDrag(0, 0);
        edit.DragTo(1200, 0);
        Check(edit.Folders.Single().Folder.X == -300, "大步输入不能穿过显示器之间的空洞");
        edit.DragTo(1199, 10);
        Check(edit.Folders.Single().Folder is { X: -301, Y: 110 }, "空洞边缘反向和沿边滑动");
        await workspace.InitializeAsync([new(-1000, 0, 1000, 800), new(0, 200, 1200, 600)]);
        edit = workspace.BeginLayout(folder.Id)!;
        edit.BeginDrag(0, 0);
        edit.DragTo(500, 0);
        Check(edit.Folders.Single().Folder.X == -300, "错位工作区不能让内容伸入接缝上方空洞");
        edit.DragTo(500, 150);
        edit.DragTo(1000, 150);
        Check(edit.Folders.Single().Folder is { X: 200, Y: 250 }, "沿边到连续工作区后可以进入相邻屏幕");
    }

    internal static async Task MixedDpi()
    {
        using var fixture = new Fixture();
        var folder = new FolderRecord(Guid.NewGuid(), "混合 DPI", -500, 100, Expanded: true);
        Directory.CreateDirectory(Path.Combine(fixture.Content, folder.Name));
        fixture.Store.Save(new WorkspaceState { Root = fixture.Content, Folders = [folder] });
        IDesktopWorkspace workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
        DisplayArea[] displays = [new(-1200, 0, 1200, 1000), new(0, 0, 1600, 1200, 1.5)];
        await workspace.InitializeAsync(displays);
        var edit = workspace.BeginLayout(folder.Id)!;
        edit.BeginDrag(0, 0);
        edit.DragTo(800, 0);
        var moved = edit.Folders.Single();
        Check(moved.Folder.X == 300 && moved.DisplayScale == 1.5 && moved.Folder.HeaderWidth == 300 && moved.Folder.BodyHeight == 260,
            "目标屏物理边界改用 150% 而保存的 DIP 尺寸不变");
        Check(edit.ResizeBy(100, 40), "高 DPI 屏可调整共享宽度和内容高度");
        edit.DragTo(5000, 0);
        Check(edit.Folders.Single().Folder.X == 1000, "150% 下 400 DIP 宽度在右边界占 600 像素");
        edit.DragTo(4999, 0);
        Check(edit.Folders.Single().Folder.X == 999, "高 DPI 边界反向保持一物理像素");
        await workspace.CommitLayoutAsync(edit);
        await workspace.RefreshAsync([new(0, 0, 1600, 1200, 2)]);
        var refreshed = workspace.Snapshot.Folders.Single();
        Check(refreshed.Visible && refreshed.DisplayScale == 2 && refreshed.Folder.HeaderWidth == 400
            && refreshed.Folder.X <= 800, "改变缩放后重新找可见位置且保留逻辑尺寸");
    }

    internal static async Task RecoverySaveFailure()
    {
        using var fixture = new Fixture();
        var folder = new FolderRecord(Guid.NewGuid(), "恢复提交失败", 1500, 100);
        var path = Path.Combine(fixture.Content, folder.Name);
        Directory.CreateDirectory(path);
        File.WriteAllBytes(Path.Combine(path, "保留.bin"), [0, 42, 255]);
        fixture.Store.Save(new WorkspaceState { Root = fixture.Content, Folders = [folder] });
        var store = new FailingStore(fixture.Store);
        IDesktopWorkspace workspace = new DesktopWorkspace(store, new TestStartup());
        await workspace.InitializeAsync([new(0, 0, 2000, 900)]);
        store.FailOnSave = store.Saves + 1;
        Check(!(await workspace.RefreshAsync([new(0, 0, 800, 600)])).Succeeded, "恢复位置保存失败明确报告");
        var recovered = workspace.Snapshot.Folders.Single();
        Check(recovered.Visible && recovered.Folder.X <= 500 && recovered.ActualPath == path,
            "提交失败仍按当前显示区发布可见位置，不继续展示屏幕外旧边界");
        Check((await workspace.RefreshAsync([new(0, 0, 800, 600)])).Succeeded, "恢复保存可以重试");
        workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
        await workspace.InitializeAsync([new(0, 0, 800, 600)]);
        Check(workspace.Snapshot.Folders.Single().Folder == recovered.Folder && File.ReadAllBytes(Path.Combine(path, "保留.bin")).SequenceEqual(new byte[] { 0, 42, 255 }),
            "重试后重启保持恢复位置与内容字节");
    }

    internal static async Task StaleDpiInput()
    {
        using var fixture = new Fixture();
        var folder = new FolderRecord(Guid.NewGuid(), "旧 DPI 输入", 12, 12);
        Directory.CreateDirectory(Path.Combine(fixture.Content, folder.Name));
        fixture.Store.Save(new WorkspaceState { Root = fixture.Content, Folders = [folder] });
        IDesktopWorkspace workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
        await workspace.InitializeAsync([new(0, 0, 1600, 1000)]);
        var old = workspace.BeginLayout(folder.Id)!;
        old.BeginDrag(0, 0);
        old.DragTo(40, 0);
        await workspace.RefreshAsync([new(0, 0, 1600, 1000, 1.5)]);
        Check(!(await workspace.CommitLayoutAsync(old)).Succeeded && workspace.Snapshot.Folders.Single().Folder.X == 12,
            "缩放变化即使位置未变也拒绝旧 DPI 输入，不覆盖新环境");
        var fresh = workspace.BeginLayout(folder.Id)!;
        fresh.BeginDrag(0, 0);
        fresh.DragTo(40, 0);
        Check((await workspace.CommitLayoutAsync(fresh)).Succeeded && workspace.Snapshot.Folders.Single().Folder.X == 52,
            "新的显示环境下输入仍可提交");
    }

    internal static async Task UnplugAndCapacity()
    {
        using var fixture = new Fixture();
        var folders = new[]
        {
            new FolderRecord(Guid.NewGuid(), "负坐标原屏", -950, -150, Expanded: true),
            new FolderRecord(Guid.NewGuid(), "原主屏", 800, 100, Expanded: true),
            new FolderRecord(Guid.NewGuid(), "第三入口", 800, 500, Expanded: true)
        };
        foreach (var folder in folders)
        {
            var path = Path.Combine(fixture.Content, folder.Name);
            Directory.CreateDirectory(path);
            File.WriteAllBytes(Path.Combine(path, "保留.bin"), [0, 42, 255]);
        }
        fixture.Store.Save(new WorkspaceState { Root = fixture.Content, Folders = folders });
        IDesktopWorkspace workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
        DisplayArea[] original = [new(-1000, -200, 1000, 1000), new(0, 0, 1600, 1000, 1.5)];
        await workspace.InitializeAsync(original);
        Check(workspace.Snapshot.Folders.All(f => f.Visible), "原两屏能容纳全部展开入口");
        await workspace.RefreshAsync([new(-800, -100, 800, 800, 1.25)]);
        Check(workspace.Snapshot.Folders.All(f => f.Visible && f.Folder.X >= -800 && f.Folder.Y >= -100
            && f.Folder.X + 375 <= 0 && f.Folder.Y + 385 <= 700), "拔出原主屏及分辨率变化后全部窗口回到剩余负坐标屏");
        var recovered = workspace.Snapshot.Folders;
        for (var i = 0; i < recovered.Count; i++)
        for (var j = i + 1; j < recovered.Count; j++)
        {
            var a = recovered[i].Folder;
            var b = recovered[j].Folder;
            Check(a.X + 387 <= b.X || b.X + 387 <= a.X || a.Y + 397 <= b.Y || b.Y + 397 <= a.Y, "125% 恢复位置保持物理间距且不重叠");
        }
        await workspace.RefreshAsync([new(0, 0, 320, 340)]);
        Check(workspace.Snapshot.Folders.Count(f => f.Visible) == 1
            && workspace.Snapshot.Folders.Where(f => !f.Visible).All(f => f.Folder.LayoutHidden && f.Notice!.Contains("设置刷新")),
            "容量不足明确暂时不可见并提供设置恢复说明");
        workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
        await workspace.InitializeAsync([]);
        Check(workspace.Snapshot.Folders.Count == 3 && workspace.Snapshot.Folders.All(f => !f.Visible), "零可用屏及重启也保留全部记录");
        await workspace.RefreshAsync(original.Reverse().ToArray());
        Check(workspace.Snapshot.Folders.All(f => f.Visible && f.Folder.Expanded && f.FileCount == 1
            && File.ReadAllBytes(Path.Combine(f.ActualPath, "保留.bin")).SequenceEqual(new byte[] { 0, 42, 255 })),
            "显示区域恢复及主屏枚举顺序变化后重现全部入口与原字节");
        Check(workspace.Snapshot.Folders.Select(f => f.Folder.Id).SequenceEqual(folders.Select(f => f.Id)), "恢复保留稳定标识及顺序");
    }

    internal static async Task PreserveSurvivingScreen()
    {
        using var fixture = new Fixture();
        var lost = new FolderRecord(Guid.NewGuid(), "断开屏幕", 1100, 100);
        var stable = new FolderRecord(Guid.NewGuid(), "保持位置", 700, 100);
        foreach (var folder in new[] { lost, stable }) Directory.CreateDirectory(Path.Combine(fixture.Content, folder.Name));
        fixture.Store.Save(new WorkspaceState { Root = fixture.Content, Folders = [lost, stable] });
        IDesktopWorkspace workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
        await workspace.InitializeAsync([new(0, 0, 1000, 800), new(1000, 0, 1000, 800)]);
        await workspace.RefreshAsync([new(0, 0, 1000, 800)]);
        Check(workspace.Snapshot.Folders.Single(f => f.Folder.Id == stable.Id).Folder == stable,
            "断开屏幕的入口恢复空位时优先保留仍可见的已有位置");
        Check(workspace.Snapshot.Folders.Single(f => f.Folder.Id == lost.Id).Visible, "断开屏幕入口找到其他空位");
    }

    internal static async Task MixedDpiBothDirections()
    {
        using var fixture = new Fixture();
        var folder = new FolderRecord(Guid.NewGuid(), "反向混合 DPI", -500, 100);
        Directory.CreateDirectory(Path.Combine(fixture.Content, folder.Name));
        foreach (var (leftScale, rightScale) in new[] { (1.5, 1.0), (1.0, 1.5), (2.0, 1.25), (1.25, 2.0) })
        {
            fixture.Store.Save(new WorkspaceState { Root = fixture.Content, Folders = [folder] });
            IDesktopWorkspace workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
            await workspace.InitializeAsync([new(-1200, 0, 1200, 1000, leftScale), new(0, 0, 1600, 1000, rightScale)]);
            var edit = workspace.BeginLayout(folder.Id)!;
            edit.BeginDrag(0, 0);
            edit.DragTo(800, 0);
            Check(edit.Folders.Single().Folder.X == 300 && edit.Folders.Single().DisplayScale == rightScale,
                $"{leftScale:P0} 到 {rightScale:P0} 可完整穿过连续接缝");
            edit.DragTo(0, 0);
            Check(edit.Folders.Single().Folder.X == -500 && edit.Folders.Single().DisplayScale == leftScale,
                "同一次输入会话反向跨屏回到起点");
        }
        folder = folder with { X = 100, Y = -500 };
        fixture.Store.Save(new WorkspaceState { Root = fixture.Content, Folders = [folder] });
        IDesktopWorkspace vertical = new DesktopWorkspace(fixture.Store, new TestStartup());
        await vertical.InitializeAsync([new(0, -1000, 1200, 1000, 2), new(0, 0, 1200, 1000)]);
        var move = vertical.BeginLayout(folder.Id)!;
        move.BeginDrag(0, 0);
        move.DragTo(0, 800);
        Check(move.Folders.Single().Folder.Y == 300 && move.Folders.Single().DisplayScale == 1, "上下相邻屏幕高到低 DPI 可跨屏");
        move.DragTo(0, 0);
        Check(move.Folders.Single().Folder.Y == -500 && move.Folders.Single().DisplayScale == 2, "上下跨屏立即反向恢复");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
