using Kage.Workspace;

internal static class ContentViewChecks
{
    internal static async Task NaturalOrder()
    {
        using var fixture = new Fixture();
        IDesktopWorkspace workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
        await workspace.InitializeAsync([new(0, 0, 1200, 900)]);
        await workspace.SelectRootAsync(fixture.Content);
        await workspace.CreateFolderAsync("排序");
        var folder = workspace.Snapshot.Folders.Single();
        foreach (var name in new[] { "file10.txt", "file2.txt", "file1.txt" })
            File.WriteAllText(Path.Combine(folder.ActualPath, name), name);
        Directory.CreateDirectory(Path.Combine(folder.ActualPath, "z目录"));
        Directory.CreateDirectory(Path.Combine(folder.ActualPath, "中文2"));
        Directory.CreateDirectory(Path.Combine(folder.ActualPath, "中文10"));
        await workspace.RefreshAsync([new(0, 0, 1200, 900)]);
        Check(workspace.Snapshot.Folders.Single().Entries.Select(e => e.Name).SequenceEqual(
            new[] { "z目录", "中文2", "中文10", "file1.txt", "file2.txt", "file10.txt" }), "目录优先且中文数字名称按自然顺序排列");
    }

    private static void Check(bool passed, string message)
    { if (!passed) throw new Exception(message); }

    internal static async Task ViewPersistence()
    {
        using var fixture = new Fixture();
        var failing = new FailingStore(fixture.Store);
        IDesktopWorkspace workspace = new DesktopWorkspace(failing, new TestStartup());
        DisplayArea[] areas = [new(0, 0, 1200, 900)];
        await workspace.InitializeAsync(areas);
        await workspace.SelectRootAsync(fixture.Content);
        await workspace.CreateFolderAsync("视图");
        await workspace.CreateFolderAsync("独立");
        var folder = workspace.Snapshot.Folders.First();
        foreach (var pair in new[] { ("file1.txt", 10), ("file2.txt", 30), ("file10.txt", 20), ("快捷.lnk", 40) })
            File.WriteAllBytes(Path.Combine(folder.ActualPath, pair.Item1), new byte[pair.Item2]);
        Directory.CreateDirectory(Path.Combine(folder.ActualPath, "目录"));
        await workspace.RefreshAsync(areas);
        Check((await workspace.SetContentViewAsync(folder.Folder.Id, false, 96, ContentSortKey.Size, true)).Succeeded, "列表超大图标及大小降序可提交");
        Check(workspace.Snapshot.Folders.First().Entries.Select(e => e.Name).SequenceEqual(
            new[] { "目录", "快捷.lnk", "file2.txt", "file10.txt", "file1.txt" }), "目录不递归计大小，快捷方式使用自身字节数");
        Check(workspace.Snapshot.Folders.First().Entries.Single(e => e.Name == "快捷.lnk").DisplayName == "快捷", "仅隐藏显示标签后缀");
        var saved = workspace.Snapshot.Folders.First().Folder;
        failing.FailOnSave = failing.Saves + 1;
        Check(!(await workspace.SetContentViewAsync(saved.Id, true, 48, ContentSortKey.Modified, false)).Succeeded
            && workspace.Snapshot.Folders.First().Folder == saved, "保存失败保留完整已提交视图");
        Check(!(await workspace.SetContentViewAsync(saved.Id, false, 17, ContentSortKey.Name, false)).Succeeded, "只接受四档图标尺寸");
        workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
        await workspace.InitializeAsync(areas);
        Check(workspace.Snapshot.Folders.First().Folder == saved, "重启恢复查看方式、两种尺寸、排序键和方向");
        var other = workspace.Snapshot.Folders.Last().Folder;
        Check(other.ListIconSize == 16 && other.GridIconSize == 32 && other.SortKey == ContentSortKey.Name
            && other.CustomOrder == null, "其他 Folder 与旧字段缺省值独立");
    }

    internal static async Task CustomOrder()
    {
        using var fixture = new Fixture();
        var failing = new FailingStore(fixture.Store);
        IDesktopWorkspace workspace = new DesktopWorkspace(failing, new TestStartup());
        DisplayArea[] areas = [new(0, 0, 1200, 900)];
        await workspace.InitializeAsync(areas);
        await workspace.SelectRootAsync(fixture.Content);
        await workspace.CreateFolderAsync("手动");
        var folder = workspace.Snapshot.Folders.Single();
        string PathOf(string name) => Path.Combine(folder.ActualPath, name);
        foreach (var name in new[] { "1.txt", "2.txt", "3.txt", "4.txt" }) File.WriteAllText(PathOf(name), name);
        await workspace.RefreshAsync(areas);
        var first = workspace.Snapshot.Folders.Single().Folder;
        Check((await workspace.ReorderContentsAsync(first.Id, new[] { PathOf("1.txt") }, PathOf("2.txt"))).Succeeded
            && workspace.Snapshot.Folders.Single().Folder == first, "原位释放不切换排序且不提交新状态");
        Check(!(await workspace.ReorderContentsAsync(first.Id, new[] { Path.Combine(fixture.Home, "外部.txt") }, null)).Succeeded,
            "过期或外部项目不能参与内部重排");
        Check((await workspace.ReorderContentsAsync(folder.Folder.Id, new[] { PathOf("4.txt"), PathOf("2.txt") }, PathOf("1.txt"))).Succeeded,
            "多选重排以当前可见顺序为准");
        var ordered = workspace.Snapshot.Folders.Single();
        Check(ordered.Entries.Select(e => e.Name).SequenceEqual(new[] { "2.txt", "4.txt", "1.txt", "3.txt" })
            && ordered.Folder.SortKey == ContentSortKey.Custom, "新位置自动切换手动并保留相对顺序");
        failing.FailOnSave = failing.Saves + 1;
        Check(!(await workspace.ReorderContentsAsync(folder.Folder.Id, new[] { PathOf("3.txt") }, PathOf("2.txt"))).Succeeded
            && workspace.Snapshot.Folders.Single().Folder == ordered.Folder, "顺序保存失败恢复原模式和顺序");
        Check((await workspace.SetContentViewAsync(folder.Folder.Id, true, 48, ContentSortKey.Name, true)).Succeeded, "暂时切换自动排序");
        Check((await workspace.SetContentViewAsync(folder.Folder.Id, true, 48, ContentSortKey.Custom, false)).Succeeded, "可恢复最近手动顺序");
        File.Move(PathOf("4.txt"), PathOf("改名.txt"));
        File.Delete(PathOf("1.txt"));
        File.WriteAllText(PathOf("新增.txt"), "新增");
        await workspace.RefreshAsync(areas);
        Check(workspace.Snapshot.Folders.Single().Entries.Select(e => e.Name).SequenceEqual(
            new[] { "2.txt", "改名.txt", "3.txt", "新增.txt" }), "可靠文件身份保留改名位置，新增追加并清理删除");
        workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
        await workspace.InitializeAsync(areas);
        Check(workspace.Snapshot.Folders.Single().Entries.Select(e => e.Name).SequenceEqual(
            new[] { "2.txt", "改名.txt", "3.txt", "新增.txt" }), "重启恢复协调后的紧凑顺序");
        Check(Directory.GetFiles(folder.ActualPath, "*.txt").Length == 4
            && File.ReadAllText(PathOf("改名.txt")) == "4.txt", "重排不改名、移动或写入内容文件");
    }

    internal static async Task RenameRecovery()
    {
        using var fixture = new Fixture();
        var failing = new FailingStore(fixture.Store);
        IDesktopWorkspace workspace = new DesktopWorkspace(failing, new TestStartup());
        DisplayArea[] areas = [new(0, 0, 1200, 900)];
        await workspace.InitializeAsync(areas);
        await workspace.SelectRootAsync(fixture.Content);
        await workspace.CreateFolderAsync("文件改名");
        var folder = workspace.Snapshot.Folders.Single();
        var source = Path.Combine(folder.ActualPath, "1.txt");
        var second = Path.Combine(folder.ActualPath, "2.txt");
        File.WriteAllText(source, "真实内容"); File.WriteAllText(second, "第二项");
        await workspace.RefreshAsync(areas);
        await workspace.ReorderContentsAsync(folder.Folder.Id, new[] { second }, source);
        failing.FailOnSave = failing.Saves + 2;
        var renamed = await workspace.RenameContentAsync(folder.Folder.Id, second, "改名.txt");
        Check(renamed.Outcome == Outcome.RecoveryRequired && workspace.Snapshot.RecoveryRequired,
            "真实改名后最终保存失败保留意图并等待协调");
        workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
        Check((await workspace.InitializeAsync(areas)).Succeeded, "重启按可靠文件身份协调已执行改名");
        Check(workspace.Snapshot.Folders.Single().Entries.Select(entry => entry.Name).SequenceEqual(new[] { "改名.txt", "1.txt" })
            && File.ReadAllText(Path.Combine(folder.ActualPath, "改名.txt")) == "第二项", "保留手动位置、内容与新实际路径");
        Check(!(await workspace.RenameContentAsync(folder.Folder.Id, source, "改名.txt")).Succeeded
            && File.ReadAllText(source) == "真实内容", "同名改名不覆盖内容");
        Check(!(await workspace.RenameContentAsync(folder.Folder.Id, source, ".kage-folder-id")).Succeeded,
            "不能通过内容改名覆盖内部标识");
    }

    internal static async Task DatesAndLegacy()
    {
        using var fixture = new Fixture();
        IDesktopWorkspace workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
        DisplayArea[] areas = [new(0, 0, 1200, 900)];
        await workspace.InitializeAsync(areas);
        await workspace.SelectRootAsync(fixture.Content);
        await workspace.CreateFolderAsync("日期");
        var folder = workspace.Snapshot.Folders.Single();
        foreach (var (name, day) in new[] { ("2.txt", 1), ("10.txt", 1), ("1.txt", 2) })
        {
            var path = Path.Combine(folder.ActualPath, name); File.WriteAllText(path, name);
            File.SetLastWriteTimeUtc(path, new DateTime(2026, 1, day, 0, 0, 0, DateTimeKind.Utc));
        }
        await workspace.SetContentViewAsync(folder.Folder.Id, true, 32, ContentSortKey.Modified, false);
        Check(workspace.Snapshot.Folders.Single().Entries.Select(entry => entry.Name).SequenceEqual(new[] { "2.txt", "10.txt", "1.txt" }),
            "日期升序同键以自然名称稳定排序");
        await workspace.SetContentViewAsync(folder.Folder.Id, true, 32, ContentSortKey.Modified, true);
        Check(workspace.Snapshot.Folders.Single().Entries.Select(entry => entry.Name).SequenceEqual(new[] { "1.txt", "2.txt", "10.txt" }),
            "日期降序不反转同键名称顺序");
        var primary = Path.Combine(fixture.Home, "状态", "workspace.json");
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(primary))!;
        var record = (System.Text.Json.Nodes.JsonObject)json["Folders"]![0]!;
        foreach (var key in new[] { "ListIconSize", "GridIconSize", "SortKey", "SortDescending", "CustomOrder" }) record.Remove(key);
        File.WriteAllText(primary, json.ToJsonString());
        workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
        Check((await workspace.InitializeAsync(areas)).Succeeded, "有效旧版状态缺少新字段仍直接加载");
        var legacy = workspace.Snapshot.Folders.Single().Folder;
        Check(legacy.ListIconSize == 16 && legacy.GridIconSize == 32 && legacy.SortKey == ContentSortKey.Name
            && !legacy.SortDescending && legacy.CustomOrder == null && legacy.Id == folder.Folder.Id && legacy.Grid,
            "旧状态恢复默认值并保留原查看方式及稳定 Folder 标识");
    }
}
