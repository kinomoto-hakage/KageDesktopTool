using Kage.Workspace;

internal static class ResizeBoundaryChecks
{
    internal static async Task ScreenAndFolders()
    {
        foreach (var direction in Enum.GetValues<HeaderPosition>())
        {
            using var fixture = new Fixture();
            var target = new FolderRecord(Guid.NewGuid(), "尺寸受阻", 500, 400,
                HeaderWidth: 300, BodyHeight: 200, Expanded: true, HeaderPosition: direction);
            Directory.CreateDirectory(Path.Combine(fixture.Content, target.Name));
            fixture.Store.Save(new WorkspaceState { Root = fixture.Content, Folders = [target] });
            IDesktopWorkspace workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
            DisplayArea[] displays = [new(0, 0, 1000, 800)];
            await workspace.InitializeAsync(displays);
            var resize = workspace.BeginLayout(target.Id)!;
            Require(!resize.ResizeBy(460, 520) && resize.Folders.Single().Folder == target, $"碰到屏幕边界保留尺寸和位置：{direction}");
            Require(resize.ResizeBy(-1, -1), "受阻后立即响应反向缩小");
            Require((await workspace.CommitLayoutAsync(resize)).Succeeded, "受阻后尺寸会话仍可提交");
            var saved = workspace.Snapshot.Folders.Single().Folder;
            Require(saved.X == target.X && saved.Y == target.Y, "提交不寻找附近位置");
            var bounds = FolderGeometry.For(target, 1);
            var obstacle = new FolderRecord(Guid.NewGuid(), "固定邻居", bounds.X + bounds.Width + 12, bounds.Y);
            if (direction == HeaderPosition.Right) obstacle = obstacle with { X = bounds.X - 312 };
            Directory.CreateDirectory(Path.Combine(fixture.Content, obstacle.Name));
            fixture.Store.Save(new WorkspaceState { Root = fixture.Content, Folders = [target, obstacle] });
            workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
            await workspace.InitializeAsync([new(-1000, -800, 3000, 2400)]);
            resize = workspace.BeginLayout(target.Id)!;
            Require(!resize.ResizeBy(20, 0) && resize.Folders.First().Folder == target, $"碰到邻居保留尺寸和位置：{direction}");
            Require(resize.Folders.Last().Folder == obstacle, "受阻不移动邻居");
            Require((await workspace.CommitLayoutAsync(resize)).Succeeded && workspace.Snapshot.Folders.First().Folder == target,
                "释放受阻尺寸会话不挪位");
        }
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new Exception(message); }

    internal static async Task DividerEdges()
    {
        foreach (var scale in new[] { 1.0, 1.25, 1.5, 2.0 })
        foreach (var direction in Enum.GetValues<HeaderPosition>())
        {
            using var fixture = new Fixture();
            var target = new FolderRecord(Guid.NewGuid(), "分隔线边界", 500, 400,
                HeaderWidth: 300, BodyHeight: 200, Expanded: true, HeaderPosition: direction);
            var bounds = FolderGeometry.For(target, scale);
            target = direction switch
            {
                HeaderPosition.Top => target with { Y = 1000 - bounds.Height },
                HeaderPosition.Bottom => target with { Y = target.Y - bounds.Y },
                HeaderPosition.Left => target with { X = 1200 - bounds.Width },
                _ => target with { X = target.X - bounds.X }
            };
            Directory.CreateDirectory(Path.Combine(fixture.Content, target.Name));
            fixture.Store.Save(new WorkspaceState { Root = fixture.Content, Folders = [target] });
            IDesktopWorkspace workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
            await workspace.InitializeAsync([new(0, 0, 1200, 1000, scale)]);
            var resize = workspace.BeginLayout(target.Id)!;
            Require(!resize.ResizeHeaderBy(8) && resize.Folders.Single().Folder == target, $"分隔线碰到四边不挪位：{direction} {scale}");
            Require(resize.ResizeHeaderBy(-1), "分隔线受阻后立即缩小");
            var changed = resize.Folders.Single().Folder;
            var before = FolderGeometry.For(target, scale); var after = FolderGeometry.For(changed, scale);
            Require(direction switch
            {
                HeaderPosition.Bottom => before.Y + before.Height == after.Y + after.Height,
                HeaderPosition.Right => before.X + before.Width == after.X + after.Width,
                _ => target.X == changed.X && target.Y == changed.Y
            }, "分隔线固定外缘，像素取整不造成外缘漂移");
            Require((await workspace.CommitLayoutAsync(resize)).Succeeded, "分隔线边界输入可正常保存");
            var restarted = new DesktopWorkspace(fixture.Store, new TestStartup());
            await restarted.InitializeAsync([new(0, 0, 1200, 1000, scale)]);
            Require(restarted.Snapshot.Folders.Single().Folder == changed, "边界调整重启不重新挪位");
        }
    }
}
