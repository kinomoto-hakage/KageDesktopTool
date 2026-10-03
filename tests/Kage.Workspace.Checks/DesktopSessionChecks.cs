using Kage.Workspace;

internal static class DesktopSessionChecks
{
    internal static async Task Availability()
    {
        using var fixture = new Fixture();
        IDesktopWorkspace workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
        Check((await workspace.InitializeAsync([new(0, 0, 1200, 900)])).Succeeded, "初始化");
        var root = await workspace.SelectRootAsync(fixture.Content);
        Check(root.Succeeded, root.Message);
        var created = await workspace.CreateFolderAsync("活动内容");
        Check(created.Succeeded, created.Message);
        var id = workspace.Snapshot.Folders.Single().Folder.Id;
        await workspace.ToggleFolderAsync(id);
        File.WriteAllBytes(Path.Combine(fixture.Content, "活动内容", "保留.bin"), [0, 42, 255]);
        await workspace.RefreshAsync([new(0, 0, 1200, 900)]);
        var saved = File.ReadAllBytes(Path.Combine(fixture.Home, "状态", "workspace.json"));
        var before = workspace.Snapshot.Folders.Single();

        Check((await workspace.ReportDesktopAvailabilityAsync(false)).Succeeded, "宿主不可用可报告");
        Check(!workspace.Snapshot.DesktopAvailable && !workspace.Snapshot.RecoveryRequired,
            "展示不可用与内容恢复状态分开");
        Check(workspace.Snapshot.Folders.Single().ActualPath == before.ActualPath
            && workspace.Snapshot.Folders.Single().Folder == before.Folder
            && workspace.Snapshot.Folders.Single().Entries.SequenceEqual(before.Entries), "不可用保留实际路径、展开布局和内容");
        Check(saved.SequenceEqual(File.ReadAllBytes(Path.Combine(fixture.Home, "状态", "workspace.json"))), "会话报告不改持久配置");
        Check((await workspace.ReportDesktopAvailabilityAsync(true)).Succeeded && workspace.Snapshot.DesktopAvailable,
            "宿主恢复可报告");
        await workspace.RefreshAsync([new(0, 0, 1200, 900)]);
        Check(workspace.Snapshot.DesktopAvailable && workspace.Snapshot.Folders.Single().Folder == before.Folder,
            "刷新保持会话状态且不复制 Folder");
        var restarted = new DesktopWorkspace(fixture.Store, new TestStartup());
        await restarted.InitializeAsync([new(0, 0, 1200, 900)]);
        Check(!restarted.Snapshot.DesktopAvailable, "重启不沿用过期的宿主可用状态");
        Check(File.ReadAllBytes(Path.Combine(before.ActualPath, "保留.bin")).SequenceEqual(new byte[] { 0, 42, 255 }), "内容字节始终保留");
    }

    internal static async Task DuringMove()
    {
        using var fixture = new Fixture();
        IDesktopWorkspace workspace = new DesktopWorkspace(fixture.Store, new TestStartup());
        await workspace.InitializeAsync([new(0, 0, 1200, 900)]);
        await workspace.SelectRootAsync(fixture.Content);
        await workspace.CreateFolderAsync("接收");
        var folder = workspace.Snapshot.Folders.Single();
        var source = Path.Combine(fixture.Home, "同名.bin");
        File.WriteAllBytes(source, [1, 255]);
        File.WriteAllBytes(Path.Combine(folder.ActualPath, "同名.bin"), [42]);
        var conflict = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var choice = new TaskCompletionSource<ConflictChoice>(TaskCreationOptions.RunContinuationsAsynchronously);
        var move = workspace.MoveAsync([source], MoveTarget.Folder(folder.Folder.Id), _ =>
        { conflict.SetResult(); return choice.Task; });
        await conflict.Task.WaitAsync(TimeSpan.FromSeconds(8));
        var report = workspace.ReportDesktopAvailabilityAsync(false);
        try
        {
            Check(!report.IsCompleted && File.Exists(source), "会话报告等待文件操作，不打断待选择的移动");
        }
        finally { choice.TrySetResult(ConflictChoice.KeepBoth); }
        var result = await move.WaitAsync(TimeSpan.FromSeconds(8));
        Check((await report).Succeeded && !workspace.Snapshot.DesktopAvailable, "移动完成后报告展示不可用");
        Check(result.Items.Single().Outcome == Outcome.Success && !File.Exists(source)
            && File.ReadAllBytes(Path.Combine(folder.ActualPath, "同名 (2).bin")).SequenceEqual(new byte[] { 1, 255 })
            && File.ReadAllBytes(Path.Combine(folder.ActualPath, "同名.bin")).SequenceEqual(new byte[] { 42 }), "移动只完成一次且不覆盖已有内容");
        await workspace.ReportDesktopAvailabilityAsync(true);
        Check(workspace.Snapshot.DesktopAvailable && workspace.Snapshot.Folders.Single().FileCount == 2, "宿主恢复不重复移动且保留最新计数");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
