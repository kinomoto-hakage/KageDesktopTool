using Kage.Workspace;

internal static class FourWayChecks
{
    internal static async Task Directions()
    {
        using var fixture = new Fixture();
        IDesktopWorkspace workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
        DisplayArea[] displays = [new(0, 0, 1800, 1200)];
        await workspace.InitializeAsync(displays);
        await workspace.SelectRootAsync(fixture.Content);
        await workspace.CreateFolderAsync("四向头部");
        var id = workspace.Snapshot.Folders.Single().Folder.Id;
        var move = workspace.BeginLayout(id)!;
        move.BeginDrag(0, 0); move.DragTo(600, 400);
        Require((await workspace.CommitLayoutAsync(move)).Succeeded, "移动到可容纳四向的锚点");
        await workspace.ToggleFolderAsync(id);
        var original = workspace.Snapshot.Folders.Single().Folder;
        foreach (var direction in Enum.GetValues<HeaderPosition>())
        {
            var edit = workspace.BeginAppearance(id)!;
            Require(edit.SetHeaderPosition(direction).Succeeded, "四个位置可预览");
            Require(edit.Draft.X == original.X && edit.Draft.Y == original.Y
                && edit.Draft.HeaderWidth == original.HeaderWidth && edit.Draft.BodyHeight == original.BodyHeight,
                "切换方向保留头部锚点与展示宽高");
            Require(workspace.Snapshot.Folders.Single().Folder.HeaderPosition != direction || direction == HeaderPosition.Top,
                "预览不提交方向");
            Require((await workspace.ApplyAppearanceAsync(edit)).Succeeded, "方向可保存");
            var saved = workspace.Snapshot.Folders.Single().Folder;
            var bounds = FolderGeometry.For(saved, 1);
            Require(bounds.X == saved.X - (direction == HeaderPosition.Right ? 300 : 0)
                && bounds.Y == saved.Y - (direction == HeaderPosition.Bottom ? 260 : 0), "窗口原点按展开方向计算");
            await workspace.ToggleFolderAsync(id);
            var collapsed = workspace.Snapshot.Folders.Single().Folder;
            Require(collapsed.X == saved.X && collapsed.Y == saved.Y && FolderGeometry.For(collapsed, 1).X == saved.X
                && FolderGeometry.For(collapsed, 1).Y == saved.Y, "折叠保留头部锚点");
            await workspace.ToggleFolderAsync(id);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    internal static async Task Resizing()
    {
        using var fixture = new Fixture();
        IDesktopWorkspace workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
        DisplayArea[] displays = [new(-1000, -500, 2400, 1800, 1.5)];
        await workspace.InitializeAsync(displays); await workspace.SelectRootAsync(fixture.Content);
        await workspace.CreateFolderAsync("四向缩放");
        var id = workspace.Snapshot.Folders.Single().Folder.Id;
        var move = workspace.BeginLayout(id)!; move.BeginDrag(0, 0); move.DragTo(-300, 100);
        await workspace.CommitLayoutAsync(move);
        foreach (var direction in Enum.GetValues<HeaderPosition>())
        {
            var edit = workspace.BeginAppearance(id)!; edit.SetHeaderPosition(direction);
            Require((await workspace.ApplyAppearanceAsync(edit)).Succeeded, "保存待缩放方向");
            if (!workspace.Snapshot.Folders.Single().Folder.Expanded) await workspace.ToggleFolderAsync(id);
            var original = workspace.Snapshot.Folders.Single().Folder;
            var resize = workspace.BeginLayout(id)!;
            Require(resize.ResizeHeaderBy(10), "四向分隔线调厚度");
            var preview = resize.Folders.Single().Folder;
            Require(preview.HeaderWidth == original.HeaderWidth && preview.BodyHeight == original.BodyHeight
                && preview.X == original.X - (direction == HeaderPosition.Right ? 15 : 0)
                && preview.Y == original.Y - (direction == HeaderPosition.Bottom ? 15 : 0), $"分隔线保留展示宽高及外缘：{direction}，原 {original}，新 {preview}");
            Require(FolderGeometry.Side(preview) ? preview.SideHeaderWidth == original.SideHeaderWidth + 10
                : preview.HeaderHeight == original.HeaderHeight + 10, "侧栏宽度独立于顶部厚度");
            Require((await workspace.CommitLayoutAsync(resize)).Succeeded, "保存分隔线");
            original = workspace.Snapshot.Folders.Single().Folder;
            resize = workspace.BeginLayout(id)!;
            Require(resize.ResizeBy(20, 15), "四向远端外角调整展示尺寸");
            preview = resize.Folders.Single().Folder;
            Require(preview.HeaderWidth == original.HeaderWidth + 20 && preview.BodyHeight == original.BodyHeight + 15
                && preview.HeaderHeight == original.HeaderHeight && preview.SideHeaderWidth == original.SideHeaderWidth,
                "外角只调整展示宽高");
            await workspace.CommitLayoutAsync(resize); await workspace.ToggleFolderAsync(id);
            original = workspace.Snapshot.Folders.Single().Folder; resize = workspace.BeginLayout(id)!;
            resize.ResizeBy(10, 10); await workspace.CommitLayoutAsync(resize);
            preview = workspace.Snapshot.Folders.Single().Folder;
            Require(FolderGeometry.Side(preview) ? preview.SideHeaderWidth == original.SideHeaderWidth + 10 && preview.BodyHeight == original.BodyHeight + 10
                : preview.HeaderWidth == original.HeaderWidth + 10 && preview.HeaderHeight == Math.Min(82, original.HeaderHeight + 10),
                "折叠调整长度同步展示相接边长");
        }
    }

    internal static async Task DraftTransactions()
    {
        using var fixture = new Fixture();
        var store = new FailingStore(fixture.Store);
        IDesktopWorkspace workspace = new DesktopWorkspace(store, new TestStartup());
        DisplayArea[] displays = [new(0, 0, 900, 650)];
        await workspace.InitializeAsync(displays); await workspace.SelectRootAsync(fixture.Content);
        await workspace.CreateFolderAsync("草稿"); await workspace.CreateFolderAsync("保持不变");
        var first = workspace.Snapshot.Folders.First(); var id = first.Folder.Id;
        var other = workspace.Snapshot.Folders.Last();
        File.WriteAllText(Path.Combine(first.ActualPath, "真实内容.txt"), "保留字节");
        var top = workspace.BeginLayout(id)!; top.BeginDrag(0, 0); top.DragTo(500 - first.Folder.X, 12 - first.Folder.Y);
        Require((await workspace.CommitLayoutAsync(top)).Succeeded && (await workspace.ToggleFolderAsync(id)).Succeeded,
            "为原地草稿事务准备明确可展开且靠近顶部的夹具");
        var edge = workspace.Snapshot.Folders.First().Folder;
        var rejected = workspace.BeginAppearance(id)!;
        Require(!rejected.SetHeaderPosition(HeaderPosition.Bottom).Succeeded && rejected.Draft == edge
            && workspace.Snapshot.Folders.First().Folder == edge, "顶部方向预览受阻不寻找其他空位");
        workspace.CancelAppearance(rejected);
        var move = workspace.BeginLayout(id)!; move.BeginDrag(0, 0); move.DragTo(0, 300);
        Require((await workspace.CommitLayoutAsync(move)).Succeeded, "用户移动至四向草稿可用位置");
        var original = workspace.Snapshot.Folders.First().Folder;
        var savedState = fixture.Store.Read().State;
        var edit = workspace.BeginAppearance(id)!;
        Require(edit.SetHeaderPosition(HeaderPosition.Bottom).Succeeded && edit.Draft.X == original.X && edit.Draft.Y == original.Y,
            "方向预览保持用户头部原点");
        edit.SetNameOrientation(NameOrientation.Vertical); edit.SetHex("#2468AC"); edit.SetOpacity(.4);
        Require(edit.SetHeaderHeight(60).Succeeded && edit.Preview.Folder.HeaderHeight == 60, "头部高度进入有效草稿预览");
        Require(fixture.Store.Read().State.Folders.SequenceEqual(savedState.Folders)
            && workspace.Snapshot.Folders.First().Folder == original, "方向名称颜色透明度预览不写盘");
        var draft = edit.Draft;
        Require(workspace.CancelAppearance(edit).Outcome == Outcome.Cancelled && edit.Draft == original, "取消包含方向和高度的整个草稿");
        edit = workspace.BeginAppearance(id)!;
        edit.SetHeaderPosition(HeaderPosition.Bottom); edit.SetNameOrientation(NameOrientation.Vertical); edit.SetHex("#2468AC"); edit.SetOpacity(.4);
        edit.SetHeaderHeight(60);
        store.FailOnSave = store.Saves + 1;
        Require(!(await workspace.ApplyAppearanceAsync(edit)).Succeeded && !edit.Closed && edit.Draft == draft
            && workspace.Snapshot.Folders.First().Folder == original, "失败保持正式状态和完整可重试草稿");
        Require((await workspace.ApplyAppearanceAsync(edit)).Succeeded && workspace.Snapshot.Folders.First().Folder == draft, "一次提交整个有效草稿");
        Require(workspace.Snapshot.Folders.Last().Folder == other.Folder && workspace.Snapshot.Folders.Last().ActualPath == other.ActualPath,
            "草稿原地应用不影响其他 Folder");
        workspace = new DesktopWorkspace(store, new TestStartup()); await workspace.InitializeAsync(displays);
        Require(workspace.Snapshot.Folders.First().Folder == draft && File.ReadAllText(Path.Combine(first.ActualPath, "真实内容.txt")) == "保留字节",
            "重启恢复全部样式与几何和真实内容");
        var stale = workspace.BeginAppearance(id)!; stale.SetHeaderPosition(HeaderPosition.Left);
        var newer = workspace.BeginAppearance(id)!; newer.SetNameOrientation(NameOrientation.Horizontal);
        Require((await workspace.ApplyAppearanceAsync(newer)).Succeeded && !(await workspace.ApplyAppearanceAsync(stale)).Succeeded,
            "过期草稿不覆盖后来有效样式");
        stale = workspace.BeginAppearance(id)!;
        await workspace.RefreshAsync([new(0, 0, 900, 650, 1.25)]);
        Require(!stale.SetHeaderPosition(HeaderPosition.Right).Succeeded && !(await workspace.ApplyAppearanceAsync(stale)).Succeeded,
            "显示环境变化拒绝旧草稿预览和提交");
        stale = workspace.BeginAppearance(id)!;
        Require(stale.SetHeaderHeight(stale.Draft.HeaderHeight + 2).Succeeded, "准备确定可容纳的过期几何草稿");
        await workspace.ToggleFolderAsync(id);
        var later = workspace.Snapshot.Folders.First().Folder;
        Require(!(await workspace.ApplyAppearanceAsync(stale)).Succeeded && stale.Preview.Folder == later,
            "相关几何改变后旧方向草稿不覆盖后来布局，预览采用当前状态");
        workspace.CancelAppearance(stale);
        // 确保真实备份含当前记录，再用损坏主文件触发正式恢复锁定。
        store.Save(store.Read().State);
        stale = workspace.BeginAppearance(id)!; stale.SetHeaderPosition(HeaderPosition.Right);
        File.WriteAllText(Path.Combine(fixture.Home, "状态", "workspace.json"), "损坏主状态");
        Require((await workspace.InitializeAsync([new(0, 0, 900, 650, 1.25)])).Outcome == Outcome.RecoveryRequired
            && workspace.BeginAppearance(id) == null && stale.SetHeaderPosition(HeaderPosition.Left).Outcome == Outcome.RecoveryRequired
            && (await workspace.ApplyAppearanceAsync(stale)).Outcome == Outcome.RecoveryRequired,
            "真实恢复锁定拒绝新会话、旧方向预览和提交");
        Require(workspace.CancelAppearance(stale).Outcome == Outcome.Cancelled, "恢复锁定仍允许安全取消旧草稿");
    }

    internal static async Task Capacity()
    {
        using var fixture = new Fixture();
        IDesktopWorkspace workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
        DisplayArea[] displays = [new(0, 0, 320, 280)];
        await workspace.InitializeAsync(displays); await workspace.SelectRootAsync(fixture.Content); await workspace.CreateFolderAsync("无空间");
        var id = workspace.Snapshot.Folders.Single().Folder.Id;
        foreach (var direction in Enum.GetValues<HeaderPosition>())
        {
            var edit = workspace.BeginAppearance(id)!;
            Require(edit.SetHeaderPosition(direction).Succeeded, "四向折叠可以预览");
            var good = edit.Draft;
            if (FolderGeometry.Side(good))
            {
                Require(!edit.SetSideHeaderWidth(360).Succeeded && edit.Draft == good, "无效侧栏宽度保留最后有效草稿");
                Require(edit.SetNameOrientation(NameOrientation.Vertical).Succeeded, "无效几何后仍可继续改名称");
            }
            Require((await workspace.ApplyAppearanceAsync(edit)).Succeeded, "无效预览不阻止应用有效草稿");
            var saved = workspace.Snapshot.Folders.Single().Folder;
            Require(!(await workspace.ToggleFolderAsync(id)).Succeeded && workspace.Snapshot.Folders.Single().Folder == saved,
                "无展开空位保持折叠方向尺寸及锚点");
        }
    }

    internal static async Task Legacy()
    {
        using var fixture = new Fixture();
        var id = Guid.NewGuid(); var path = Path.Combine(fixture.Content, "旧状态");
        Directory.CreateDirectory(path); File.WriteAllText(Path.Combine(path, ".kage-folder-id"), id.ToString("N"));
        File.WriteAllText(Path.Combine(path, "保留.txt"), "真实旧内容");
        var statePath = Path.Combine(fixture.Home, "状态", "workspace.json"); Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
        File.WriteAllText(statePath, $$"""
            { "Version": 1, "Root": {{System.Text.Json.JsonSerializer.Serialize(fixture.Content)}}, "StartupEnabled": false, "IconChoice": "d",
              "Folders": [{ "Id": "{{id}}", "Name": "旧状态", "X": 77, "Y": 88, "HeaderWidth": 350, "HeaderHeight": 62,
                "BodyHeight": 290, "Expanded": true, "Grid": false, "Color": "#123456", "Opacity": 0.35,
                "ListIconSize": 16, "GridIconSize": 96, "SortKey": 2, "SortDescending": true }] }
            """);
        IDesktopWorkspace workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
        DisplayArea[] displays = [new(0, 0, 1400, 1000)];
        Require((await workspace.InitializeAsync(displays)).Succeeded, "真实旧磁盘缺省新增字段仍可初始化");
        var saved = workspace.Snapshot.Folders.Single();
        Require(saved.Folder.HeaderPosition == HeaderPosition.Top && saved.Folder.NameOrientation == NameOrientation.Horizontal
            && saved.Folder.X == 77 && saved.Folder.Y == 88 && saved.Folder.HeaderWidth == 350 && saved.Folder.HeaderHeight == 62
            && saved.Folder.BodyHeight == 290 && saved.Folder.ListIconSize == 16 && saved.Folder.GridIconSize == 96
            && saved.Folder.SortKey == ContentSortKey.Size && saved.Folder.SortDescending && !saved.Folder.Grid
            && saved.Folder.Color == "#123456" && saved.Folder.Opacity == .35 && saved.ActualPath == path, "旧几何内容视图排序外观均保留");
        var edit = workspace.BeginAppearance(id)!; Require((await workspace.ApplyAppearanceAsync(edit)).Succeeded, "旧状态可保存新字段");
        workspace = new DesktopWorkspace(fixture.Store, new TestStartup()); await workspace.InitializeAsync(displays);
        Require(workspace.Snapshot.Folders.Single().Folder == saved.Folder && File.ReadAllText(Path.Combine(path, "保留.txt")) == "真实旧内容", "旧状态保存重启不丢关联");
    }

    internal static async Task Displays()
    {
        using var fixture = new Fixture();
        var target = new FolderRecord(Guid.NewGuid(), "跨屏", -700, 100, Expanded: true);
        Directory.CreateDirectory(Path.Combine(fixture.Content, target.Name));
        foreach (var direction in Enum.GetValues<HeaderPosition>())
        {
            foreach (var scales in new[] { (1.5, 1.0), (1.0, 1.5) })
            {
                var folder = target with { HeaderPosition = direction };
                fixture.Store.Save(new WorkspaceState { Root = fixture.Content, Folders = [folder] });
                IDesktopWorkspace workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
                await workspace.InitializeAsync([new(-1800, -600, 1800, 2000, scales.Item1), new(0, -600, 1800, 2000, scales.Item2)]);
                var edit = workspace.BeginLayout(folder.Id)!; edit.BeginDrag(0, 0); edit.DragTo(1400, 0);
                Require(edit.Folders.Single().Folder.X == 700 && edit.Folders.Single().DisplayScale == scales.Item2, "四向混合 DPI 向右跨屏");
                edit.DragTo(0, 0);
                Require(edit.Folders.Single().Folder.X == -700 && edit.Folders.Single().DisplayScale == scales.Item1, "四向混合 DPI 向左跨屏并保留负坐标");
                folder = folder with { X = 100, Y = -700 };
                fixture.Store.Save(new WorkspaceState { Root = fixture.Content, Folders = [folder] });
                workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
                await workspace.InitializeAsync([new(-600, -1800, 2200, 1800, scales.Item1), new(-600, 0, 2200, 1800, scales.Item2)]);
                edit = workspace.BeginLayout(folder.Id)!; edit.BeginDrag(0, 0); edit.DragTo(0, 1400);
                Require(edit.Folders.Single().Folder.Y == 700 && edit.Folders.Single().DisplayScale == scales.Item2, "四向混合 DPI 向下跨屏");
                edit.DragTo(0, 0);
                Require(edit.Folders.Single().Folder.Y == -700 && edit.Folders.Single().DisplayScale == scales.Item1, "四向混合 DPI 向上反向跨屏");
            }
            var gapFolder = target with { X = 100, Y = 100, HeaderPosition = direction };
            fixture.Store.Save(new WorkspaceState { Root = fixture.Content, Folders = [gapFolder] });
            IDesktopWorkspace gaps = new DesktopWorkspace(fixture.Store, new TestStartup());
            await gaps.InitializeAsync([new(0, 0, 800, 800), new(900, 0, 800, 800)]);
            var start = gaps.Snapshot.Folders.Single().Folder;
            var drag = gaps.BeginLayout(gapFolder.Id)!; drag.BeginDrag(0, 0); drag.DragTo(1500, 0);
            var bounds = FolderGeometry.For(drag.Folders.Single().Folder, 1);
            Require(bounds.X + bounds.Width <= 800, "四向整个可见边界受屏幕空洞阻挡");
            var stopped = drag.Folders.Single().Folder.X; drag.DragTo(1499, 0);
            Require(drag.Folders.Single().Folder.X == stopped - 1, "四向受阻后一物理像素立即反向");
            Require(gaps.Snapshot.Folders.Single().Folder == start, "跨屏输入预览不提交");
        }
    }

    internal static async Task Nearest()
    {
        using var fixture = new Fixture();
        var target = new FolderRecord(Guid.NewGuid(), "当前", 30, 35, HeaderWidth: 240, BodyHeight: 160);
        var obstacle = new FolderRecord(Guid.NewGuid(), "固定", 290, 240, HeaderWidth: 240, BodyHeight: 160, Expanded: true, HeaderPosition: HeaderPosition.Right);
        foreach (var folder in new[] { target, obstacle }) Directory.CreateDirectory(Path.Combine(fixture.Content, folder.Name));
        DisplayArea[] displays = [new(0, 0, 600, 470)];
        foreach (var direction in Enum.GetValues<HeaderPosition>())
        {
            fixture.Store.Save(new WorkspaceState { Root = fixture.Content, Folders = [target with { HeaderPosition = direction }, obstacle] });
            IDesktopWorkspace workspace = new DesktopWorkspace(fixture.Store, new TestStartup()); await workspace.InitializeAsync(displays);
            var original = workspace.Snapshot.Folders.Single(f => f.Folder.Id == target.Id).Folder;
            var other = workspace.Snapshot.Folders.Single(f => f.Folder.Id == obstacle.Id).Folder;
            var proposed = original with { Expanded = true };
            var obstacleBounds = FolderGeometry.For(other, 1);
            FolderRecord? Nearest(FolderRecord proposal) => (from y in Enumerable.Range(0, 470) from x in Enumerable.Range(0, 600)
                let candidate = proposal with { X = x, Y = y }
                let b = FolderGeometry.For(candidate, 1)
                where b.X >= 0 && b.Y >= 0 && b.X + b.Width <= 600 && b.Y + b.Height <= 470
                    && (b.X + b.Width + 12 <= obstacleBounds.X || b.X >= obstacleBounds.X + obstacleBounds.Width + 12
                        || b.Y + b.Height + 12 <= obstacleBounds.Y || b.Y >= obstacleBounds.Y + obstacleBounds.Height + 12)
                orderby (long)(x - proposal.X) * (x - proposal.X) + (long)(y - proposal.Y) * (y - proposal.Y), y, x
                select candidate).FirstOrDefault();
            var legal = Nearest(proposed);
            if (legal == null)
            {
                Require(!(await workspace.ToggleFolderAsync(target.Id)).Succeeded, "无展开空间保留折叠");
                continue;
            }
            fixture.Store.Save(new WorkspaceState { Root = fixture.Content, Folders = [legal, other] });
            workspace = new DesktopWorkspace(fixture.Store, new TestStartup()); await workspace.InitializeAsync(displays);
            var drag = workspace.BeginLayout(target.Id)!; drag.BeginDrag(0, 0);
            drag.DragTo(original.X - legal.X, original.Y - legal.Y);
            var expected = Nearest(drag.Folders.Single(f => f.Folder.Id == target.Id).Folder);
            var result = await workspace.CommitLayoutAsync(drag);
            Require(result.Succeeded && workspace.Snapshot.Folders.Single(f => f.Folder.Id == target.Id).Folder == expected,
                "四向最近空位与独立逐像素穷举一致（含右侧障碍）");
            Require(workspace.Snapshot.Folders.Single(f => f.Folder.Id == obstacle.Id).Folder == other, "最近放置不移动右侧头部障碍");
        }
    }
}
