using Kage.Workspace;

internal static class AppearanceChecks
{
    internal static async Task StrengthPreview()
    {
        using var fixture = new Fixture();
        IDesktopWorkspace workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
        await workspace.InitializeAsync([new(0, 0, 1200, 900)]);
        await workspace.SelectRootAsync(fixture.Content);
        await workspace.CreateFolderAsync("强度草稿");
        var original = workspace.Snapshot.Folders.Single();
        Check(original.Folder.FrostedStrength == 50 && !original.Folder.Frosted, "新建 Folder 强度默认 50，开关仍默认关闭");
        var edit = workspace.BeginAppearance(original.Folder.Id)!;
        edit.SetFrosted(true);
        Check(edit.SetFrostedStrength(0).Succeeded && edit.FrostedStrength == 0
            && edit.Preview.Folder.Frosted && edit.Preview.Folder.FrostedStrength == 0
            && workspace.Snapshot.Folders.Single() == original, "清晰零值只预览强度并保留开启意图，正式状态不变");
        Check(!edit.SetFrostedStrength(-1).Succeeded && !edit.SetFrostedStrength(101).Succeeded
            && edit.FrostedStrength == 0, "业务入口拒绝越界强度且不发布非法草稿");
        Check(edit.SetFrostedStrength(100).Succeeded && edit.Error.Length == 0, "更正有效强度清除错误并预览端点");
        edit.SetFrosted(false); edit.SetFrosted(true);
        Check(edit.FrostedStrength == 100 && edit.Color == original.Folder.Color && edit.Opacity == original.Folder.Opacity,
            "开关往返保留强度且不改变着色");
        await workspace.RefreshAsync([new(0, 0, 1200, 900)]);
        Check(edit.Preview.Folder.FrostedStrength == 100, "后台刷新后有效草稿保持强度预览");
        Check(workspace.CancelAppearance(edit).Outcome == Outcome.Cancelled && edit.FrostedStrength == 50
            && !edit.SetFrostedStrength(0).Succeeded && !(await workspace.ApplyAppearanceAsync(edit)).Succeeded,
            "取消恢复原强度，已结束会话不能更改或提交");
    }

    internal static async Task StrengthPersistence()
    {
        using var fixture = new Fixture();
        var store = new FailingStore(fixture.Store);
        IDesktopWorkspace workspace = new DesktopWorkspace(store, new TestStartup());
        DisplayArea[] displays = [new(0, 0, 1600, 1000)];
        await workspace.InitializeAsync(displays); await workspace.SelectRootAsync(fixture.Content);
        await workspace.CreateFolderAsync("强度保存"); await workspace.CreateFolderAsync("独立强度");
        var first = workspace.Snapshot.Folders.First(); var other = workspace.Snapshot.Folders.Last();
        var content = Path.Combine(first.ActualPath, "保留.bin"); File.WriteAllBytes(content, [0, 42, 255]);
        var stale = workspace.BeginAppearance(first.Folder.Id)!;
        var newer = workspace.BeginAppearance(first.Folder.Id)!; newer.SetFrostedStrength(0);
        Check((await workspace.ApplyAppearanceAsync(newer)).Succeeded, "显式零值可以正式保存");
        stale.SetFrostedStrength(100);
        Check(!(await workspace.ApplyAppearanceAsync(stale)).Succeeded && stale.Preview.Folder.FrostedStrength == 0,
            "仅强度正式变化使旧草稿过期，旧预览和提交均不能覆盖零值");
        workspace.CancelAppearance(stale);
        var edit = workspace.BeginAppearance(first.Folder.Id)!;
        edit.SetFrosted(true); edit.SetFrostedStrength(100); edit.SetHex("#123456"); edit.SetOpacity(0);
        var statePath = Path.Combine(fixture.Home, "状态", "workspace.json");
        var move = workspace.BeginLayout(first.Folder.Id)!; move.BeginDrag(0, 0); move.DragTo(50 - first.Folder.X, 300 - first.Folder.Y); move.EndDrag();
        Check((await workspace.CommitLayoutAsync(move)).Succeeded && (await workspace.ToggleFolderAsync(first.Folder.Id)).Succeeded
            && (await workspace.SetViewAsync(first.Folder.Id, false)).Succeeded, "强度草稿期间独立布局和查看修改成功");
        var latest = workspace.Snapshot.Folders.First().Folder; var beforeFailure = File.ReadAllBytes(statePath);
        store.FailOnSave = store.Saves + 1;
        Check(!(await workspace.ApplyAppearanceAsync(edit)).Succeeded && !edit.Closed
            && workspace.Snapshot.Folders.First().Folder == latest && File.ReadAllBytes(statePath).SequenceEqual(beforeFailure),
            "完整样式保存失败不提前发布强度或改写正式 JSON");
        Check((await workspace.ApplyAppearanceAsync(edit)).Succeeded && workspace.Snapshot.Folders.First().Folder
            == latest with { Frosted = true, FrostedStrength = 100, Color = "#123456", Opacity = 0 },
            "重试整体发布完整强度样式并保留独立布局与查看变化");
        workspace = new DesktopWorkspace(fixture.Store, new TestStartup()); await workspace.InitializeAsync(displays);
        Check(workspace.Snapshot.Folders.First().Folder.FrostedStrength == 100 && workspace.Snapshot.Folders.First().Folder.Frosted
            && workspace.Snapshot.Folders.Last().Folder == other.Folder && File.ReadAllBytes(content).SequenceEqual(new byte[] { 0, 42, 255 }),
            "真实 JSON 重启恢复端点并保持其他 Folder 与内容字节");
        edit = workspace.BeginAppearance(first.Folder.Id)!; edit.SetFrostedStrength(0);
        Check((await workspace.ApplyAppearanceAsync(edit)).Succeeded, "重新保存零值");
        workspace = new DesktopWorkspace(fixture.Store, new TestStartup()); await workspace.InitializeAsync(displays);
        Check(workspace.Snapshot.Folders.First().Folder.FrostedStrength == 0, "显式零值重启时不误作缺省 50");
        edit = workspace.BeginAppearance(first.Folder.Id)!; edit.SetFrostedStrength(75);
        await workspace.RefreshAsync([new(0, 0, 1600, 1000, 1.25)]);
        Check(!(await workspace.ApplyAppearanceAsync(edit)).Succeeded && workspace.Snapshot.Folders.First().Folder.FrostedStrength == 0,
            "显示环境变化拒绝旧强度草稿");
        workspace.CancelAppearance(edit);
    }

    internal static async Task StrengthConfiguration()
    {
        using var fixture = new Fixture();
        var id = Guid.NewGuid(); var path = Path.Combine(fixture.Content, "旧强度"); Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, ".kage-folder-id"), id.ToString("N"));
        var statePath = Path.Combine(fixture.Home, "状态", "workspace.json"); Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
        var legacy = $$"""
            { "Version": 1, "Root": {{System.Text.Json.JsonSerializer.Serialize(fixture.Content)}}, "StartupEnabled": false, "IconChoice": "d",
              "Folders": [{ "Id": "{{id}}", "Name": "旧强度", "X": 40, "Y": 50, "HeaderWidth": 300, "HeaderHeight": 48,
                "BodyHeight": 260, "Expanded": false, "Grid": true, "Color": "#123456", "Opacity": 0.35, "Frosted": true }] }
            """;
        File.WriteAllText(statePath, legacy);
        IDesktopWorkspace workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
        DisplayArea[] displays = [new(0, 0, 1200, 900)];
        Check((await workspace.InitializeAsync(displays)).Succeeded && workspace.Snapshot.Folders.Single().Folder.FrostedStrength == 50
            && workspace.Snapshot.Folders.Single().Folder.Frosted, "真实旧 JSON 缺字段默认 50 且保留已有开启意图");
        foreach (var invalid in new[] { "-1", "101", "1.5", "\"75\"", "null" })
        {
            var broken = legacy.Replace("\"Frosted\": true", "\"Frosted\": true, \"FrostedStrength\": " + invalid);
            File.WriteAllText(statePath, broken);
            workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
            Check(!(await workspace.InitializeAsync(displays)).Succeeded && workspace.Snapshot.RecoveryRequired
                && File.ReadAllText(statePath) == broken, "无效强度 " + invalid + " 沿用恢复锁定且保留原始证据");
        }
        File.WriteAllText(statePath, legacy); fixture.Store.Save(new WorkspaceState { Root = fixture.Content,
            Folders = [new FolderRecord(id, "旧强度", 40, 50, Frosted: true, FrostedStrength: 75)] });
        File.WriteAllText(statePath, legacy.Replace("\"Frosted\": true", "\"Frosted\": true, \"FrostedStrength\": 101"));
        workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
        Check((await workspace.InitializeAsync(displays)).Outcome == Outcome.RecoveryRequired
            && workspace.Snapshot.RecoveryRequired && workspace.BeginAppearance(id) == null,
            "主配置强度非法时只读加载有效备份并阻止样式修改");
    }

    internal static async Task FrostedPreview()
    {
        using var fixture = new Fixture();
        IDesktopWorkspace workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
        await workspace.InitializeAsync([new(0, 0, 1000, 800)]);
        await workspace.SelectRootAsync(fixture.Content);
        await workspace.CreateFolderAsync("毛玻璃草稿");
        var original = workspace.Snapshot.Folders.Single();
        Check(!original.Folder.Frosted, "新建 Folder 默认关闭毛玻璃");
        var edit = workspace.BeginAppearance(original.Folder.Id)!;
        Check(edit.SetFrosted(true).Succeeded && edit.Preview.Folder.Frosted
            && workspace.Snapshot.Folders.Single() == original, "毛玻璃即时预览但正式记录、路径与几何不变");
        Check(workspace.CancelAppearance(edit).Outcome == Outcome.Cancelled && !edit.Draft.Frosted,
            "取消恢复打开前的毛玻璃意图");
        Check(!edit.SetFrosted(true).Succeeded && !(await workspace.ApplyAppearanceAsync(edit)).Succeeded,
            "已结束毛玻璃草稿不能改变或提交");
    }

    internal static async Task FrostedPersistence()
    {
        using var fixture = new Fixture();
        var store = new FailingStore(fixture.Store);
        IDesktopWorkspace workspace = new DesktopWorkspace(store, new TestStartup());
        DisplayArea[] displays = [new(0, 0, 1200, 900)];
        await workspace.InitializeAsync(displays);
        await workspace.SelectRootAsync(fixture.Content);
        await workspace.CreateFolderAsync("毛玻璃保存");
        await workspace.CreateFolderAsync("独立设置");
        var original = workspace.Snapshot.Folders.First();
        var other = workspace.Snapshot.Folders.Last();
        var file = Path.Combine(original.ActualPath, "原内容.bin");
        File.WriteAllBytes(file, [0, 42, 255]);
        var stale = workspace.BeginAppearance(original.Folder.Id)!;
        var newer = workspace.BeginAppearance(original.Folder.Id)!;
        newer.SetFrosted(true);
        Check((await workspace.ApplyAppearanceAsync(newer)).Succeeded, "毛玻璃意图保存成功");
        stale.SetFrosted(false);
        Check(!(await workspace.ApplyAppearanceAsync(stale)).Succeeded && workspace.Snapshot.Folders.First().Folder.Frosted,
            "仅毛玻璃发生变化也使旧样式草稿过期");
        workspace.CancelAppearance(stale);
        var edit = workspace.BeginAppearance(original.Folder.Id)!;
        edit.SetHex("#123456");
        edit.SetOpacity(.25);
        edit.SetFrosted(false);
        edit.SetFrosted(true);
        await workspace.ToggleFolderAsync(original.Folder.Id);
        await workspace.SetViewAsync(original.Folder.Id, false);
        await workspace.RefreshAsync(displays);
        var latest = workspace.Snapshot.Folders.First().Folder;
        store.FailOnSave = store.Saves + 1;
        Check((await workspace.ApplyAppearanceAsync(edit)).Outcome == Outcome.Failed && !edit.Closed
            && workspace.Snapshot.Folders.First().Folder == latest, "完整样式保存失败保留正式状态与可重试草稿");
        var saves = store.Saves;
        Check((await workspace.ApplyAppearanceAsync(edit)).Succeeded && store.Saves == saves + 1,
            "一次成功保存完整颜色透明度与毛玻璃意图");
        var saved = workspace.Snapshot.Folders.First();
        Check(saved.Folder == latest with { Color = "#123456", Opacity = .25 }
            && saved.ActualPath == original.ActualPath && workspace.Snapshot.Folders.Last().Folder == other.Folder,
            "开关往返保留颜色透明度，应用保留独立布局查看变化和其他 Folder");
        workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
        await workspace.InitializeAsync(displays);
        Check(workspace.Snapshot.Folders.First().Folder == saved.Folder
            && workspace.Snapshot.Folders.Last().Folder == other.Folder && File.ReadAllBytes(file).SequenceEqual(new byte[] { 0, 42, 255 }),
            "真实状态文件重启逐 Folder 恢复完整样式且内容字节不变");
        edit = workspace.BeginAppearance(original.Folder.Id)!;
        edit.SetFrosted(false);
        await workspace.RefreshAsync([new(0, 0, 1200, 900, 1.25)]);
        Check(!(await workspace.ApplyAppearanceAsync(edit)).Succeeded && workspace.Snapshot.Folders.First().Folder.Frosted,
            "显示环境变化拒绝旧毛玻璃草稿");
        workspace.CancelAppearance(edit);
    }

    internal static async Task InputAndCancel()
    {
        using var fixture = new Fixture();
        IDesktopWorkspace workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
        await workspace.InitializeAsync([new(0, 0, 1000, 800)]);
        await workspace.SelectRootAsync(fixture.Content);
        await workspace.CreateFolderAsync("外观");
        var original = workspace.Snapshot.Folders.Single();
        var edit = workspace.BeginAppearance(original.Folder.Id)!;
        Check(edit.Color == "#666666" && edit.Opacity == .68, "默认灰色半透明");
        Check(edit.SetHex("1a2b3c").Succeeded && edit.Color == "#1A2B3C"
            && edit.Red == 26 && edit.Green == 43 && edit.Blue == 60, "HEX 同步三个 RGB 分量");
        Check(edit.SetRgb("255", "0", "128").Succeeded && edit.Color == "#FF0080", "RGB 同步 HEX");
        Check(!edit.SetHex("#GG0000").Succeeded && edit.Color == "#FF0080", "无效 HEX 不改变有效颜色");
        Check(!(await workspace.ApplyAppearanceAsync(edit)).Succeeded, "无效输入时禁止应用上次有效颜色");
        Check(!edit.SetRgb("256", "0", "128").Succeeded && edit.Color == "#FF0080", "越界 RGB 保留有效预览");
        Check(edit.SetHex("#4679AB").Succeeded, "有效调色盘颜色清除错误");
        Check(edit.SetOpacity(.25).Succeeded && edit.Opacity == .25, "实时预览透明度");
        Check(!edit.SetOpacity(double.NaN).Succeeded && edit.Opacity == .25, "无效透明度不进入预览");
        Check(workspace.Snapshot.Folders.Single() == original, "预览不更改已提交记录、内容路径和几何");
        Check(workspace.CancelAppearance(edit).Outcome == Outcome.Cancelled
            && edit.Color == "#666666" && edit.Opacity == .68, "取消恢复打开前的颜色和透明度");
        Check(!(await workspace.ApplyAppearanceAsync(edit)).Succeeded, "已取消会话不能再次提交");
        workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
        await workspace.InitializeAsync([new(0, 0, 1000, 800)]);
        Check(workspace.Snapshot.Folders.Single().Folder == original.Folder, "取消后重启保留原外观");
    }

    internal static async Task Persistence()
    {
        using var fixture = new Fixture();
        var failing = new FailingStore(fixture.Store);
        IDesktopWorkspace workspace = new DesktopWorkspace(failing, new TestStartup());
        DisplayArea[] areas = [new(0, 0, 1200, 900)];
        await workspace.InitializeAsync(areas);
        await workspace.SelectRootAsync(fixture.Content);
        await workspace.CreateFolderAsync("一");
        await workspace.CreateFolderAsync("二");
        var original = workspace.Snapshot.Folders.First();
        var file = Path.Combine(original.ActualPath, "内容.txt");
        File.WriteAllText(file, "保留实际内容");
        var edit = workspace.BeginAppearance(original.Folder.Id)!;
        edit.SetHex("#123456");
        edit.SetOpacity(.25);
        // 编辑期间布局和查看方式可独立提交，应用不能用旧记录覆盖这些字段。
        await workspace.ToggleFolderAsync(original.Folder.Id);
        await workspace.SetViewAsync(original.Folder.Id, false);
        var geometry = workspace.Snapshot.Folders.First().Folder;
        var other = workspace.Snapshot.Folders.Last().Folder;
        failing.FailOnSave = failing.Saves + 1;
        Check((await workspace.ApplyAppearanceAsync(edit)).Outcome == Outcome.Failed
            && workspace.Snapshot.Folders.First().Folder == geometry && !edit.Closed, "保存失败保留原记录，草稿可重试");
        Check((await workspace.ApplyAppearanceAsync(edit)).Succeeded, "失败后重试成功");
        var saved = workspace.Snapshot.Folders.First();
        Check(saved.Folder == geometry with { Color = "#123456", Opacity = .25 }
            && saved.ActualPath == original.ActualPath, "仅修改目标外观，保留最新布局及内容路径");
        Check(workspace.Snapshot.Folders.Last().Folder == other, "其他 Folder 独立保持默认外观");
        var cancelled = workspace.BeginAppearance(original.Folder.Id)!;
        cancelled.SetHex("#FFFFFF");
        workspace.CancelAppearance(cancelled);
        Check(cancelled.Color == "#123456" && cancelled.Opacity == .25, "取消恢复本次打开前已保存的值");
        workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
        await workspace.InitializeAsync(areas);
        Check(workspace.Snapshot.Folders.First().Folder == saved.Folder && workspace.Snapshot.Folders.Last().Folder == other,
            "每个 Folder 独立外观完整重启恢复");
        Check(File.ReadAllText(file) == "保留实际内容", "外观操作保留实际内容字节");
        var stale = workspace.BeginAppearance(original.Folder.Id)!;
        var newer = workspace.BeginAppearance(original.Folder.Id)!;
        newer.SetHex("#ABCDEF");
        await workspace.ApplyAppearanceAsync(newer);
        stale.SetHex("#000000");
        Check(!(await workspace.ApplyAppearanceAsync(stale)).Succeeded
            && workspace.Snapshot.Folders.First().Folder.Color == "#ABCDEF", "旧草稿不能覆盖新外观");
    }

    internal static async Task Icons()
    {
        using var fixture = new Fixture();
        var failing = new FailingStore(fixture.Store);
        IDesktopWorkspace workspace = new DesktopWorkspace(failing, new TestStartup());
        DisplayArea[] areas = [new(0, 0, 1000, 800)];
        await workspace.InitializeAsync(areas);
        await workspace.SelectRootAsync(fixture.Content);
        Check(workspace.Snapshot.IconChoice == "d", "默认 D K 文件夹");
        Check(!(await workspace.SetIconAsync("invalid")).Succeeded && workspace.Snapshot.IconChoice == "d", "非法图标不提交");
        foreach (var choice in new[] { "a", "b", "c", "d" })
        {
            Check((await workspace.SetIconAsync(choice)).Succeeded, "四套候选可切换");
            workspace = new DesktopWorkspace(failing, new TestStartup());
            await workspace.InitializeAsync(areas);
            Check(workspace.Snapshot.IconChoice == choice, "图标选择重启恢复");
        }
        failing.FailOnSave = failing.Saves + 1;
        Check((await workspace.SetIconAsync("a")).Outcome == Outcome.Failed && workspace.Snapshot.IconChoice == "d", "图标保存失败保留已选方案");
        workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
        await workspace.InitializeAsync(areas);
        Check(workspace.Snapshot.IconChoice == "d", "图标保存失败后重启保持 D");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
