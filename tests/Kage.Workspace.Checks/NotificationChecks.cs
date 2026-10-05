using System.Text.Json.Nodes;
using Kage.Workspace;

internal static class NotificationChecks
{
    internal static async Task Preference()
    {
        using var fixture = new Fixture();
        var legacy = new WorkspaceState { Root = fixture.Content };
        fixture.Store.Save(legacy);
        var statePath = Path.Combine(fixture.Home, "状态", "workspace.json");
        var json = JsonNode.Parse(File.ReadAllText(statePath))!.AsObject();
        json.Remove("NotificationsEnabled");
        File.WriteAllText(statePath, json.ToJsonString());
        var failing = new FailingStore(fixture.Store);
        IDesktopWorkspace workspace = new DesktopWorkspace(failing, new TestStartup());
        DisplayArea[] displays = [new(0, 0, 1200, 900)];
        await workspace.InitializeAsync(displays);
        Check(workspace.Snapshot.NotificationsEnabled, "1.0.0 缺少字段时默认开启");
        await workspace.SelectRootAsync(fixture.Content);
        await workspace.CreateFolderAsync("通知夹具");
        var folder = workspace.Snapshot.Folders.Single();
        File.WriteAllText(Path.Combine(folder.ActualPath, "内容.txt"), "保留");
        Check((await workspace.SetNotificationsAsync(false)).Succeeded && !workspace.Snapshot.NotificationsEnabled, "关闭成功才发布偏好");
        workspace = new DesktopWorkspace(failing, new TestStartup());
        await workspace.InitializeAsync(displays);
        Check(!workspace.Snapshot.NotificationsEnabled, "关闭偏好重启恢复");
        failing.FailOnSave = failing.Saves + 1;
        var failure = await workspace.SetNotificationsAsync(true);
        Check(failure.Outcome == Outcome.Failed && failure.Message.Contains("注入状态提交失败") && !workspace.Snapshot.NotificationsEnabled, "失败保留原值并说明原因");
        workspace = new DesktopWorkspace(failing, new TestStartup());
        await workspace.InitializeAsync(displays);
        Check(!workspace.Snapshot.NotificationsEnabled, "保存失败后磁盘原值保持");
        Check((await workspace.SetNotificationsAsync(true)).Succeeded, "可重试开启");
        Check(workspace.Snapshot.Folders.Single().Folder == folder.Folder && File.ReadAllText(Path.Combine(folder.ActualPath, "内容.txt")) == "保留", "偏好不改变布局或真实内容");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
