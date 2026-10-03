namespace Kage.Workspace;

public sealed partial class DesktopWorkspace
{
    public Task<OperationResult> RenameContentAsync(Guid id, string actualPath, string name) => Run(() =>
    {
        if (blocked) return Locked();
        WindowsPaths.Name(name);
        if (WindowsPaths.IsIdentityFile(name)) return new(Outcome.Failed, "不能使用内部归属标识名称。");
        var folder = state.Folders.SingleOrDefault(folder => folder.Id == id);
        if (folder == null) return new(Outcome.Failed, "Folder 不存在。");
        var source = ContentFiles.Read(ContentPath(folder)).SingleOrDefault(entry => string.Equals(entry.ActualPath, actualPath, StringComparison.OrdinalIgnoreCase));
        if (source == null || source.Identity == null) return new(Outcome.Failed, "项目不存在或无法取得可靠身份，未执行改名。");
        if (source.Name == name) return new(Outcome.Success, "名称未改变。", source.ActualPath);
        var destination = Path.Combine(ContentPath(folder), name);
        if (!string.Equals(source.ActualPath, destination, StringComparison.OrdinalIgnoreCase) && WindowsPaths.Exists(destination))
            return new(Outcome.Conflict, "目标已存在，未覆盖。", destination);
        var intent = state with { PendingContentRename = new(id, source.Name, name, source.Identity) };
        store.Save(intent);
        state = intent;
        Exception? failure = null;
        try
        {
            if (source.IsDirectory) Directory.Move(source.ActualPath, destination);
            else File.Move(source.ActualPath, destination);
        }
        catch (Exception e) { failure = e; }
        try { RecoverContentRename(); }
        catch (Exception e)
        {
            blocked = true;
            notices.Add($"文件改名尚未协调，原操作意图保留；请核对并重启：{e.Message}");
            Publish();
            return new(Outcome.RecoveryRequired, notices.Last(), destination);
        }
        Publish();
        return failure == null ? new(Outcome.Success, "项目已重命名，显示位置保留。", destination)
            : new(Outcome.Failed, "改名失败，已核对实际项目：" + failure.Message, source.ActualPath);
    });

    private void RecoverContentRename()
    {
        if (state.PendingContentRename is not { } pending) return;
        var folder = state.Folders.Single(item => item.Id == pending.FolderId);
        var entries = ContentFiles.Read(ContentPath(folder));
        var source = entries.SingleOrDefault(entry => entry.Name == pending.SourceName && entry.Identity == pending.Identity);
        var destination = entries.SingleOrDefault(entry => entry.Name == pending.DestinationName && entry.Identity == pending.Identity);
        if (source == null && destination == null || source != null && destination != null)
            throw new IOException("原位置与目标无法唯一确认本次项目身份；未认领其他文件。");
        var changed = folder;
        if (destination != null && folder.CustomOrder != null)
            changed = folder with { CustomOrder = folder.CustomOrder.Select(item => string.Equals(item.Name, pending.SourceName, StringComparison.OrdinalIgnoreCase)
                && (item.Identity == null || item.Identity == pending.Identity)
                ? item with { Name = pending.DestinationName, Identity = pending.Identity } : item).ToArray() };
        var complete = state with { PendingContentRename = null,
            Folders = state.Folders.Select(item => item.Id == changed.Id ? changed : item).ToArray() };
        store.Save(complete);
        state = complete;
    }
}
