using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kage.Workspace;

internal static class UpgradeChecks
{
    private static readonly DisplayArea[] Displays = [new(0, 0, 1800, 1200)];
    private static readonly Guid ActiveId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid RetainedId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    // 独立记录 1.0.0 的磁盘字段，不通过新版序列化器生成旧状态。
    private static string Legacy(Fixture fixture, string intent = "") => $$"""
        {
          "Version": 1, "Root": {{JsonSerializer.Serialize(fixture.Content)}},
          "StartupEnabled": false, "IconChoice": "c",
          "Folders": [{ "Id": "{{ActiveId}}", "Name": "旧版内容", "X": 70, "Y": 90,
            "HeaderWidth": 340, "HeaderHeight": 62, "BodyHeight": 280,
            "Expanded": true, "Grid": false, "Color": "#235A81", "Opacity": 0.55,
            "ContentRoot": null, "LayoutHidden": false }],
          "RetainedFolders": [{ "FolderId": "{{RetainedId}}",
            "ContentPath": {{JsonSerializer.Serialize(Path.Combine(fixture.Content, "保留内容"))}},
            "ShortcutPath": {{JsonSerializer.Serialize(Path.Combine(fixture.Home, "保留内容.lnk"))}} }]
          {{intent}}
        }
        """;

    private static string Prepare(Fixture fixture, string intent = "", bool owned = true)
    {
        var content = Path.Combine(fixture.Content, "旧版内容");
        Directory.CreateDirectory(content);
        File.WriteAllText(Path.Combine(content, ".kage-folder-id"), (owned ? ActiveId : Guid.NewGuid()).ToString("N"));
        File.WriteAllBytes(Path.Combine(content, "2.bin"), [0, 13, 255, 72]);
        File.WriteAllText(Path.Combine(content, "10.txt"), "旧版真实内容");
        Directory.CreateDirectory(Path.Combine(fixture.Content, "保留内容"));
        File.WriteAllText(Path.Combine(fixture.Content, "保留内容", ".kage-folder-id"), RetainedId.ToString("N"));
        File.WriteAllText(Path.Combine(fixture.Content, "保留内容", "保留.txt"), "保留内容不得移动");
        var path = Path.Combine(fixture.Home, "状态", "workspace.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, Legacy(fixture, intent));
        return path;
    }

    internal static async Task ValidAndBackup()
    {
        using var fixture = new Fixture();
        var path = Prepare(fixture);
        var original = File.ReadAllBytes(path);
        IDesktopWorkspace workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
        Require((await workspace.InitializeAsync(Displays)).Succeeded, "1.0.0 磁盘状态直接加载");
        var folder = workspace.Snapshot.Folders.Single().Folder;
        Require(folder.Id == ActiveId && folder.X == 70 && folder.Y == 90 && folder.HeaderWidth == 340
            && folder.HeaderHeight == 62 && folder.BodyHeight == 280 && folder.Expanded && !folder.Grid
            && folder.Color == "#235A81" && folder.Opacity == .55, "保留旧几何、视图、外观和稳定归属");
        Require(folder.ListIconSize == 32 && folder.GridIconSize == 48 && folder.SortKey == ContentSortKey.Name
            && !folder.SortDescending && folder.CustomOrder == null && workspace.Snapshot.NotificationsEnabled
            && workspace.Snapshot.IconChoice == "c", "缺字段采用票据 01 后续确认的默认尺寸及名称升序、通知开启");
        Require(File.ReadAllBytes(path).SequenceEqual(original), "读取升级不主动重写有效旧状态");
        var retained = fixture.Store.Read().State.RetainedFolders.Single();
        File.Copy(path, path + ".bak");
        File.WriteAllText(path, "损坏的主状态");
        workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
        Require((await workspace.InitializeAsync(Displays)).Outcome == Outcome.RecoveryRequired
            && workspace.Snapshot.Folders.Single().Folder == folder
            && !(await workspace.SetNotificationsAsync(false)).Succeeded, "旧备份只读加载并暂停修改");
        Require(File.ReadAllText(path) == "损坏的主状态" && File.ReadAllBytes(path + ".bak").SequenceEqual(original), "未确认时保留主状态和有效备份");
        var restored = await workspace.RestoreBackupAsync();
        Require(restored.Succeeded, "显式恢复旧备份：" + restored.Message);
        Require(Directory.GetFiles(Path.GetDirectoryName(path)!, "workspace.json.damaged-*")
            .Any(file => File.ReadAllText(file) == "损坏的主状态"), "恢复留下损坏证据");
        Require((await workspace.SetNotificationsAsync(false)).Succeeded && (await workspace.SetIconAsync("a")).Succeeded,
            "旧状态恢复后可保存新版偏好");
        workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
        Require((await workspace.InitializeAsync(Displays)).Succeeded && !workspace.Snapshot.NotificationsEnabled
            && workspace.Snapshot.IconChoice == "a" && workspace.Snapshot.Folders.Single().Folder == folder
            && fixture.Store.Read().State.RetainedFolders.Single() == retained, "重启保留新偏好、全部旧属性及保留内容映射");
        Require(File.ReadAllBytes(Path.Combine(fixture.Content, "旧版内容", "2.bin")).SequenceEqual(new byte[] { 0, 13, 255, 72 })
            && File.ReadAllText(Path.Combine(fixture.Content, "保留内容", "保留.txt")) == "保留内容不得移动", "升级和备份恢复不改实际内容");
    }

    internal static async Task PendingIntents()
    {
        foreach (var kind in new[] { "创建", "自启", "改名", "保留", "回收", "迁移" })
        {
            using var fixture = new Fixture();
            var content = Path.Combine(fixture.Content, "旧版内容");
            string Quoted(string value) => JsonSerializer.Serialize(value);
            var intent = kind switch
            {
                "创建" => ", \"PendingCreate\": " + $$"""
                    { "Id": "33333333-3333-3333-3333-333333333333", "Name": "待创建", "X": 500, "Y": 90,
                      "HeaderWidth": 300, "HeaderHeight": 48, "BodyHeight": 260, "Expanded": false,
                      "Grid": true, "Color": "#666666", "Opacity": 0.68 }
                    """,
                "自启" => ", \"PendingStartup\": { \"Enabled\": true, \"PreviousCommand\": null, \"TargetCommand\": \"旧包命令\" }",
                "迁移" => $$"""
                    , "PendingRootMigration": { "OperationId": "44444444-4444-4444-4444-444444444444",
                      "OldRoot": {{Quoted(fixture.Content)}}, "NewRoot": {{Quoted(Path.Combine(fixture.Home, "新根"))}},
                      "Items": [ { "FolderId": "{{ActiveId}}", "SourcePath": {{Quoted(content)}},
                        "DestinationPath": {{Quoted(Path.Combine(fixture.Home, "新根", "旧版内容"))}}, "ShortcutPath": null, "Phase": 6 },
                        { "FolderId": "{{RetainedId}}", "SourcePath": {{Quoted(Path.Combine(fixture.Content, "保留内容"))}},
                          "DestinationPath": {{Quoted(Path.Combine(fixture.Home, "新根", "保留内容"))}},
                          "ShortcutPath": {{Quoted(Path.Combine(fixture.Home, "保留内容.lnk"))}}, "Phase": 0 } ] }
                    """,
                _ => $$"""
                    , "PendingFolderChange": { "FolderId": "{{ActiveId}}", "Kind": {{(kind == "改名" ? 0 : kind == "保留" ? 1 : 2)}},
                      "Destination": {{Quoted(kind == "改名" ? Path.Combine(fixture.Content, "新名称") : kind == "保留" ? Path.Combine(fixture.Home, "待保留.lnk") : content)}} }
                    """
            };
            var path = Prepare(fixture, intent, owned: false);
            if (kind == "创建")
            {
                Directory.CreateDirectory(Path.Combine(fixture.Content, "待创建"));
                File.WriteAllText(Path.Combine(fixture.Content, "待创建", "未知内容.txt"), "不得自动认领");
            }
            var bytes = File.ReadAllBytes(path);
            var shell = new WindowsFolderShell(fixture.Home);
            IDesktopWorkspace workspace = new DesktopWorkspace(fixture.Store, new TestStartup { Command = "外部实际启动项" }, shell);
            Require((await workspace.InitializeAsync(Displays)).Outcome == Outcome.RecoveryRequired
                && workspace.Snapshot.RecoveryRequired && workspace.Snapshot.Notices.Count > 0, "升级后保留待核对意图及原因：" + kind);
            Require(!(await workspace.CreateFolderAsync("禁止创建")).Succeeded
                && File.ReadAllBytes(path).SequenceEqual(bytes), "未核对前不写入、不清除旧意图：" + kind);
            Require(File.ReadAllText(Path.Combine(content, "10.txt")) == "旧版真实内容"
                && File.ReadAllText(Path.Combine(fixture.Content, "保留内容", "保留.txt")) == "保留内容不得移动",
                "未知归属升级不覆盖活动与保留内容：" + kind);
        }
    }

    internal static async Task CustomOrderMigration()
    {
        using var fixture = new Fixture();
        Prepare(fixture);
        IDesktopWorkspace workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
        Require((await workspace.InitializeAsync(Displays)).Succeeded, "加载迁移排序夹具");
        var content = workspace.Snapshot.Folders.Single().ActualPath;
        Require((await workspace.ReorderContentsAsync(ActiveId, [Path.Combine(content, "10.txt")], Path.Combine(content, "2.bin"))).Succeeded,
            "将名称自动顺序反转为手动顺序");
        var original = workspace.Snapshot.Folders.Single();
        // 保留关联不参与这个最小复现；它的真实 Shell 行为由发布组合检查覆盖。
        var saved = fixture.Store.Read().State;
        fixture.Store.Save(saved with { RetainedFolders = [] });
        workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
        await workspace.InitializeAsync(Displays);
        var target = Path.Combine(fixture.Home, "迁移后");
        Require((await workspace.MigrateRootAsync(target)).Succeeded, "真实复制迁移成功");
        var migrated = workspace.Snapshot.Folders.Single();
        Console.WriteLine("迁移前后 10.txt 身份：" + original.Entries.Single(entry => entry.Name == "10.txt").Identity
            + " → " + migrated.Entries.Single(entry => entry.Name == "10.txt").Identity
            + "；持久顺序：" + string.Join(",", fixture.Store.Read().State.Folders.Single().CustomOrder!.Select(item => item.Name)));
        Require(migrated.Entries.Select(entry => entry.Name).SequenceEqual(new[] { "10.txt", "2.bin" }),
            "迁移后保留非名称顺序，实际为：" + string.Join(",", migrated.Entries.Select(entry => entry.Name)));
        workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
        Require((await workspace.InitializeAsync(Displays)).Succeeded
            && workspace.Snapshot.Folders.Single().Entries.Select(entry => entry.Name).SequenceEqual(new[] { "10.txt", "2.bin" }), "迁移重启仍保留手动顺序");
        using var cancellation = new CancellationTokenSource();
        var cancelled = await workspace.MigrateRootAsync(fixture.Content, new CancelAfterCopy(cancellation), cancellation.Token);
        Require(cancelled.Outcome == Outcome.Cancelled && workspace.Snapshot.Root == target
            && workspace.Snapshot.Folders.Single().Entries.Select(entry => entry.Name).SequenceEqual(new[] { "10.txt", "2.bin" }), "取消复制并恢复原位置后仍保留顺序");
        workspace = new DesktopWorkspace(fixture.Store, new TestStartup(), rootTransfer: new InterruptedOrderTransfer());
        await workspace.InitializeAsync(Displays);
        Require((await workspace.MigrateRootAsync(fixture.Content)).Outcome == Outcome.RecoveryRequired, "复制后中断并保留迁移意图");
        workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
        Require((await workspace.InitializeAsync(Displays)).Outcome == Outcome.RecoveryRequired, "重启先核对中断位置");
        Require((await workspace.RecoverRootMigrationAsync()).Succeeded
            && workspace.Snapshot.Folders.Single().Entries.Select(entry => entry.Name).SequenceEqual(new[] { "10.txt", "2.bin" }), "中断后重启恢复仍保留手动顺序");
        Require((await workspace.SetContentViewAsync(ActiveId, true, 48, ContentSortKey.Name, false)).Succeeded,
            "切回自动排序仍保留最近手动顺序");
        Require((await workspace.MigrateRootAsync(fixture.Content)).Succeeded
            && (await workspace.SetContentViewAsync(ActiveId, true, 48, ContentSortKey.Custom, false)).Succeeded
            && workspace.Snapshot.Folders.Single().Entries.Select(entry => entry.Name).SequenceEqual(new[] { "10.txt", "2.bin" }), "自动模式迁移后切回自定义仍恢复最近顺序");
        Require(File.ReadAllBytes(Path.Combine(fixture.Content, "旧版内容", "2.bin")).SequenceEqual(new byte[] { 0, 13, 255, 72 }), "多次复制和恢复字节不变");
        // 不经刷新直接迁移，仍需先按源身份识别外部改名；新同名文件应追加。
        var latest = workspace.Snapshot.Folders.Single().ActualPath;
        File.Move(Path.Combine(latest, "10.txt"), Path.Combine(latest, "20.txt"));
        Require((await workspace.MigrateRootAsync(target)).Succeeded
            && workspace.Snapshot.Folders.Single().Entries.Select(entry => entry.Name).SequenceEqual(new[] { "20.txt", "2.bin" }),
            "外部可靠改名后立即迁移保留原位置");
        latest = workspace.Snapshot.Folders.Single().ActualPath;
        File.Move(Path.Combine(latest, "20.txt"), Path.Combine(fixture.Home, "移出旧文件.txt"));
        File.WriteAllText(Path.Combine(latest, "20.txt"), "同名新项目");
        Require((await workspace.MigrateRootAsync(fixture.Content)).Succeeded
            && workspace.Snapshot.Folders.Single().Entries.Select(entry => entry.Name).SequenceEqual(new[] { "2.bin", "20.txt" }),
            "同名替换后立即迁移按新增追加，不能继承旧身份的位置");
    }

    private sealed class CancelAfterCopy(CancellationTokenSource cancellation) : IProgress<RootMigrationProgress>
    {
        public void Report(RootMigrationProgress value) { if (value.Completed == 1) cancellation.Cancel(); }
    }

    private sealed class InterruptedOrderTransfer : IRootDirectoryTransfer
    {
        public void Move(string source, string destination, CancellationToken cancellation)
        {
            new WindowsDirectoryTransfer().Move(source, destination, cancellation);
            throw new IOException("顺序夹具：真实复制完成后中断");
        }
        public void Restore(string source, string destination, CancellationToken cancellation)
            => throw new IOException("顺序夹具：等待重启恢复");
    }

    // 手动传入保留的 1.0.0 包；在随机副本调用旧版公开 store，实测回写会丢哪些字段。
    internal static void Rollback(string package)
    {
        using var fixture = new Fixture();
        Prepare(fixture);
        var state = fixture.Store.Read().State;
        fixture.Store.Save(state with { NotificationsEnabled = false, PendingContentRename = new(ActiveId, "10.txt", "新名.txt", "待核对身份"),
            Folders = [state.Folders.Single() with { ListIconSize = 96, GridIconSize = 16, SortKey = ContentSortKey.Custom,
                SortDescending = true, CustomOrder = [new("10.txt"), new("2.bin")] }] });
        var context = new AssemblyLoadContext("1.0.0 回退隔离", isCollectible: true);
        try
        {
            var assembly = context.LoadFromAssemblyPath(Path.GetFullPath(Path.Combine(package, "Kage.Workspace.dll")));
            var type = assembly.GetType("Kage.Workspace.JsonWorkspaceStore", throwOnError: true)!;
            var oldStore = Activator.CreateInstance(type, Path.Combine(fixture.Home, "状态"))!;
            var read = type.GetMethod("Read")!.Invoke(oldStore, null)!;
            var oldState = read.GetType().GetProperty("State")!.GetValue(read)!;
            type.GetMethod("Save")!.Invoke(oldStore, [oldState]);
            var json = JsonNode.Parse(File.ReadAllText(Path.Combine(fixture.Home, "状态", "workspace.json")))!;
            Require(json["Version"]!.GetValue<int>() == 1 && json["Folders"]![0]!["Id"]!.GetValue<Guid>() == ActiveId
                && json["RetainedFolders"]![0]!["FolderId"]!.GetValue<Guid>() == RetainedId, "旧版能读写新版核心格式及保留关联");
            Require(json["NotificationsEnabled"] == null && json["PendingContentRename"] == null
                && json["Folders"]![0]!["CustomOrder"] == null, "确认旧版回写会丢失新偏好、顺序及新版内容改名意图");
            Require(File.ReadAllText(Path.Combine(fixture.Content, "旧版内容", "10.txt")) == "旧版真实内容", "回退探针只作用于随机副本");
            Console.WriteLine("通过：1.0.0 实际程序集回退读取及回写；需先由新版完成恢复，回写会丢失新版字段。");
        }
        finally { context.Unload(); }
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new Exception(message); }
}
