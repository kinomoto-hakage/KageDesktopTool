using Kage.Workspace;

internal static class LayoutChecks
{
    internal static async Task Expansion()
    {
        using var fixture = new Fixture();
        var a = new FolderRecord(Guid.NewGuid(), "固定头部", 12, 12);
        var b = new FolderRecord(Guid.NewGuid(), "下方", 12, 90);
        Directory.CreateDirectory(Path.Combine(fixture.Content, a.Name));
        Directory.CreateDirectory(Path.Combine(fixture.Content, b.Name));
        fixture.Store.Save(new WorkspaceState { Root = fixture.Content, Folders = [a, b] });
        IDesktopWorkspace workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
        await workspace.InitializeAsync([new(0, 0, 1000, 800)]);
        Ensure((await workspace.ToggleFolderAsync(a.Id)).Succeeded, "展开成功");
        var expanded = workspace.Snapshot.Folders.Single(f => f.Folder.Id == a.Id).Folder;
        Ensure(expanded.Expanded && expanded.X == 12 && expanded.Y == 12, "展开保持本头部位置");
        Ensure(workspace.Snapshot.Folders.Single(f => f.Folder.Id == b.Id).Folder.Y != 90, "为冲突头部寻找附近空位");
        Ensure((await workspace.ToggleFolderAsync(b.Id)).Succeeded && workspace.Snapshot.Folders.All(f => f.Folder.Expanded), "多个 Folder 可同时展开");
        Ensure((await workspace.ToggleFolderAsync(a.Id)).Succeeded && workspace.Snapshot.Folders.Single(f => f.Folder.Id == b.Id).Folder.Expanded, "折叠一个不折叠其他");

        var narrow = new DisplayArea(0, 0, 320, 180);
        fixture.Store.Save(new WorkspaceState { Root = fixture.Content, Folders = [a, b] });
        workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
        await workspace.InitializeAsync([narrow]);
        var before = workspace.Snapshot.Folders.Select(f => f.Folder).ToArray();
        Ensure(!(await workspace.ToggleFolderAsync(a.Id)).Succeeded, "空间不足拒绝展开");
        Ensure(workspace.Snapshot.Folders.Select(f => f.Folder).SequenceEqual(before), "失败恢复全部原布局");
    }

    private static void Ensure(bool condition, string message) { if (!condition) throw new Exception(message); }

    internal static async Task ScreenEdges()
    {
        using var fixture = new Fixture();
        var folder = new FolderRecord(Guid.NewGuid(), "轨迹", 100, 100);
        Directory.CreateDirectory(Path.Combine(fixture.Content, folder.Name));
        fixture.Store.Save(new WorkspaceState { Root = fixture.Content, Folders = [folder] });
        IDesktopWorkspace workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
        await workspace.InitializeAsync([new(0, 0, 1000, 800)]);
        foreach (var (dx, dy, x, y) in new[] { (-2000, 0, 0, 100), (2000, 0, 700, 100), (0, -2000, 100, 0), (0, 2000, 100, 752) })
        {
            var edit = workspace.BeginLayout(folder.Id)!;
            edit.BeginDrag(0, 0);
            edit.DragTo(dx, dy);
            Ensure(edit.Folders.Single().Folder.X == x && edit.Folders.Single().Folder.Y == y, "边界截断到屏幕四边");
            edit.DragTo(dx * 2, dy * 2);
            edit.DragTo(dx * 2 - Math.Sign(dx), dy * 2 - Math.Sign(dy));
            Ensure(edit.Folders.Single().Folder.X == x - Math.Sign(dx) && edit.Folders.Single().Folder.Y == y - Math.Sign(dy), "受阻后立即反向一像素，无积累位移");
            edit.DragTo(dx * 2 - Math.Sign(dx) + (dy == 0 ? 0 : 5), dy * 2 - Math.Sign(dy) + (dx == 0 ? 0 : 5));
            Ensure(edit.Folders.Single().Folder.X == x - Math.Sign(dx) + (dy == 0 ? 0 : 5) && edit.Folders.Single().Folder.Y == y - Math.Sign(dy) + (dx == 0 ? 0 : 5), "贴边可沿边滑动");
            edit.EndDrag();
            var final = edit.Folders.Single().Folder;
            edit.DragTo(999, 999);
            Ensure(edit.Folders.Single().Folder == final, "结束拖动后忽略输入");
        }
    }

    internal static async Task ContactAndPersistence()
    {
        using var fixture = new Fixture();
        var moving = new FolderRecord(Guid.NewGuid(), "移动", 100, 100);
        var obstacle = new FolderRecord(Guid.NewGuid(), "接触", 500, 100);
        Directory.CreateDirectory(Path.Combine(fixture.Content, moving.Name));
        Directory.CreateDirectory(Path.Combine(fixture.Content, obstacle.Name));
        foreach (var expanded in new[] { false, true })
        foreach (var (mx, my, ox, oy, dx, dy, expectedX, expectedY) in new[]
        {
            (100, 100, 500, 100, 3000, 0, 188, 100),
            (500, 100, 100, 100, -3000, 0, 412, 100),
            (100, 100, 100, 500, 0, 3000, 100, expanded ? 180 : 440),
            (100, 600, 100, 100, 0, -3000, 100, expanded ? 420 : 160)
        })
        {
            moving = moving with { X = mx, Y = my, Expanded = expanded };
            obstacle = obstacle with { X = ox, Y = oy, Expanded = expanded };
            fixture.Store.Save(new WorkspaceState { Root = fixture.Content, Folders = [moving, obstacle] });
            IDesktopWorkspace workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
            await workspace.InitializeAsync([new(0, 0, 1800, 1200)]);
            var edit = workspace.BeginLayout(moving.Id)!;
            edit.BeginDrag(0, 0);
            edit.DragTo(dx, dy);
            var actual = edit.Folders.Single(f => f.Folder.Id == moving.Id).Folder;
            Ensure(actual.X == expectedX && actual.Y == expectedY, "大步输入停在四向接触边，不穿越其他 Folder");
            edit.DragTo(dx * 2, dy * 2);
            edit.DragTo(dx * 2 - Math.Sign(dx), dy * 2 - Math.Sign(dy));
            actual = edit.Folders.Single(f => f.Folder.Id == moving.Id).Folder;
            Ensure(actual.X == expectedX - Math.Sign(dx) && actual.Y == expectedY - Math.Sign(dy), "Folder 接触后立即反向一像素");
            edit.DragTo(dx * 2 - Math.Sign(dx) + (dy == 0 ? 0 : 5), dy * 2 - Math.Sign(dy) + (dx == 0 ? 0 : 5));
            actual = edit.Folders.Single(f => f.Folder.Id == moving.Id).Folder;
            Ensure(actual.X == expectedX - Math.Sign(dx) + (dy == 0 ? 0 : 5) && actual.Y == expectedY - Math.Sign(dy) + (dx == 0 ? 0 : 5), "Folder 接触沿边滑动");
            var saved = workspace.Snapshot.Folders.Single(f => f.Folder.Id == moving.Id).Folder;
            Ensure(saved == moving, "鼠标轨迹未提交前不改变持久布局");
            Ensure((await workspace.CommitLayoutAsync(edit)).Succeeded, "输入结束保存位置");
            var restart = new DesktopWorkspace(fixture.Store, new TestStartup());
            await restart.InitializeAsync([new(0, 0, 1800, 1200)]);
            Ensure(restart.Snapshot.Folders.Single(f => f.Folder.Id == moving.Id).Folder == actual, "重启恢复拖动位置和展开状态");
        }

        moving = moving with { X = 12, Y = 20, Expanded = false };
        fixture.Store.Save(new WorkspaceState { Root = fixture.Content, Folders = [moving] });
        IDesktopWorkspace resizing = new DesktopWorkspace(fixture.Store, new TestStartup());
        await resizing.InitializeAsync([new(0, 0, 1000, 800)]);
        var resize = resizing.BeginLayout(moving.Id)!;
        Ensure(resize.ResizeBy(100, 20), "折叠时调整头部宽高");
        await resizing.CommitLayoutAsync(resize);
        await resizing.ToggleFolderAsync(moving.Id);
        resize = resizing.BeginLayout(moving.Id)!;
        Ensure(resize.ResizeBy(60, 80), "展开时调整共享宽度和内容高度");
        var changed = resize.Folders.Single().Folder;
        Ensure(changed.X == 12 && changed.HeaderWidth == 460 && changed.HeaderHeight == 68 && changed.BodyHeight == 340, "尺寸含义正确且左侧不变");
        Ensure(!resize.ResizeBy(300, 500) && resize.Folders.Single().Folder == changed, "越界尺寸不提交");
        await resizing.CommitLayoutAsync(resize);
        await resizing.SetViewAsync(moving.Id, false);
        var restored = new DesktopWorkspace(fixture.Store, new TestStartup());
        await restored.InitializeAsync([new(0, 0, 1000, 800)]);
        Ensure(restored.Snapshot.Folders.Single().Folder == changed with { Grid = false }, "重启恢复尺寸、位置、展开和查看方式");
        var stale = resizing.BeginLayout(moving.Id)!;
        await resizing.ToggleFolderAsync(moving.Id);
        Ensure(!(await resizing.CommitLayoutAsync(stale)).Succeeded, "旧输入会话不覆盖并发新布局");
    }
}
