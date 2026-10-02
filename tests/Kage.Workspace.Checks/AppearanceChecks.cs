using Kage.Workspace;

internal static class AppearanceChecks
{
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
