namespace Kage.Workspace;

public sealed partial class DesktopWorkspace
{
    public Task<OperationResult> RenameFolderAsync(Guid id, string name, ConflictChoice conflict = ConflictChoice.Ask, CancellationToken cancellation = default)
        => Run(() =>
        {
            if (blocked) return Locked();
            var folder = state.Folders.FirstOrDefault(folder => folder.Id == id);
            if (folder == null) return new(Outcome.Failed, "Folder 不存在。");
            var source = ContentPath(folder);
            if (cancellation.IsCancellationRequested || conflict == ConflictChoice.Cancel) return new(Outcome.Cancelled, "已取消重命名。", source);
            try
            {
                WindowsPaths.Name(name);
                CheckFolderSource(folder);
                if (name == folder.Name) return new(Outcome.Success, "名称未改变。", source);
                var target = Path.Combine(folder.ContentRoot ?? state.Root, name);
                bool Taken(string path) => (!string.Equals(path, source, StringComparison.OrdinalIgnoreCase) && WindowsPaths.Exists(path))
                    || state.Folders.Any(other => other.Id != id && string.Equals(other.Name, Path.GetFileName(path), StringComparison.OrdinalIgnoreCase));
                if (Taken(target))
                {
                    if (conflict == ConflictChoice.Ask) return new(Outcome.Conflict, "同名目录或文件已存在，请选择保留两份、跳过或取消。", source);
                    if (conflict == ConflictChoice.Skip) return new(Outcome.Skipped, "已跳过重命名，原目录保留。", source);
                    var original = name;
                    for (var number = 2; Taken(target); number++)
                    {
                        name = $"{original} ({number})";
                        WindowsPaths.Name(name);
                        target = Path.Combine(folder.ContentRoot ?? state.Root, name);
                    }
                }
                if (cancellation.IsCancellationRequested) return new(Outcome.Cancelled, "已取消重命名。", source);
                return ChangeFolder(folder, new(id, FolderChangeKind.Rename, target), () =>
                {
                    if (!MoveFileEx(source, target, 0)) throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
                    if (!WindowsPaths.HasIdentity(target, id)) throw new IOException("改名后归属标识核对失败。");
                    return new(Outcome.Success, "真实内容文件夹已重命名。", target);
                });
            }
            catch (Exception e) { return new(Outcome.Failed, $"重命名失败，未更改展示名称：{e.Message}", source); }
        });
    public Task<OperationResult> DeleteFolderAsync(Guid id, FolderDeleteChoice choice, ConflictChoice conflict = ConflictChoice.Ask, CancellationToken cancellation = default)
        => Run(() =>
        {
            if (blocked) return Locked();
            var folder = state.Folders.FirstOrDefault(folder => folder.Id == id);
            if (folder == null) return new(Outcome.Failed, "Folder 不存在。");
            var source = ContentPath(folder);
            if (choice == FolderDeleteChoice.Cancel || conflict == ConflictChoice.Cancel || cancellation.IsCancellationRequested)
                return new(Outcome.Cancelled, "已取消删除，原入口及内容保留。", source);
            try
            {
                if (choice is not (FolderDeleteChoice.KeepContents or FolderDeleteChoice.Recycle)) return new(Outcome.Failed, "请选择保留内容或一同删除。", source);
                CheckFolderSource(folder);
                var target = source;
                if (choice == FolderDeleteChoice.KeepContents)
                {
                    var desktop = WindowsPaths.Root(Shell.DesktopDirectory);
                    WindowsPaths.CheckRoot(desktop, false);
                    target = Path.Combine(desktop, folder.Name + ".lnk");
                    if (WindowsPaths.Exists(target))
                    {
                        if (conflict == ConflictChoice.Ask) return new(Outcome.Conflict, "桌面已存在同名快捷方式，请选择保留两份、跳过或取消。", source);
                        if (conflict == ConflictChoice.Skip) return new(Outcome.Skipped, "未生成快捷方式，原入口及内容保留。", source);
                        for (var number = 2; WindowsPaths.Exists(target); number++) target = Path.Combine(desktop, $"{folder.Name} ({number}).lnk");
                    }
                    WindowsPaths.Name(Path.GetFileName(target));
                }
                if (cancellation.IsCancellationRequested) return new(Outcome.Cancelled, "已取消删除。", source);
                var kind = choice == FolderDeleteChoice.KeepContents ? FolderChangeKind.KeepContents : FolderChangeKind.Recycle;
                return ChangeFolder(folder, new(id, kind, target), () =>
                {
                    if (kind == FolderChangeKind.Recycle) return Shell.Recycle(source, id);
                    Shell.CreateShortcut(target, source);
                    if (!Shell.ShortcutTargets(target, source)) throw new IOException($"快捷方式创建后目标核对失败：{target}");
                    return new(Outcome.Success, $"内容仍在原目录：{source}；桌面快捷方式已创建：{target}", source);
                });
            }
            catch (Exception e) { return new(Outcome.Failed, $"删除 Folder 失败，原入口保留：{e.Message}", source); }
        });

    private void CheckFolderSource(FolderRecord folder)
    {
        var path = ContentPath(folder);
        CheckAncestors(path);
        if (!Directory.Exists(path) || !WindowsPaths.HasIdentity(path, folder.Id))
            throw new IOException($"内容目录不可用或归属标识不一致：{path}。请核对实际目录。");
    }

    private OperationResult ChangeFolder(FolderRecord folder, PendingFolderChange change, Func<OperationResult> effect)
    {
        var original = state;
        try { store.Save(state with { PendingFolderChange = change }); }
        catch (Exception e) { return new(Outcome.Failed, $"操作意图保存失败，未执行文件操作：{e.Message}", ContentPath(folder)); }
        state = state with { PendingFolderChange = change };
        OperationResult result;
        try { result = effect(); }
        catch (Exception e) { result = new(Outcome.Failed, $"文件操作失败：{e.Message}", ContentPath(folder)); }
        try
        {
            if (result.Succeeded)
            {
                state = CompleteFolderChange(folder, change);
                store.Save(state);
            }
            else
            {
                // 未完成时按真实效果核对，不能以失败返回值猜测目录仍在原位。
                if (change.Kind == FolderChangeKind.KeepContents && Shell.ShortcutTargets(change.Destination, ContentPath(folder)))
                    throw new IOException("快捷方式已生成，但 Shell 返回异常；保留意图并在重启时核对关联。");
                if (!WindowsPaths.HasIdentity(ContentPath(folder), folder.Id)) throw new IOException("文件操作未完成且原目录归属无法核对。");
                store.Save(original);
                state = original;
            }
            Publish();
            return result;
        }
        catch (Exception e)
        {
            state = original with { PendingFolderChange = change };
            blocked = true;
            notices.Add($"{result.Message} 状态提交／核对失败：{e.Message}。已保留操作记录，重启核对恢复。原路径：{ContentPath(folder)}；目标：{change.Destination}");
            // 成功改名后展示实际名称和路径，但磁盘上仍保留未完成意图。
            Publish();
            return new(Outcome.RecoveryRequired, notices.Last(), result.ActualPath ?? ContentPath(folder));
        }
    }

    private WorkspaceState CompleteFolderChange(FolderRecord folder, PendingFolderChange change)
    {
        var folders = change.Kind == FolderChangeKind.Rename
            ? state.Folders.Select(item => item.Id == folder.Id ? item with { Name = Path.GetFileName(change.Destination) } : item).ToArray()
            : state.Folders.Where(item => item.Id != folder.Id).ToArray();
        return state with { PendingFolderChange = null, Folders = folders,
            RetainedFolders = change.Kind == FolderChangeKind.KeepContents
                ? [.. state.RetainedFolders, new(folder.Id, ContentPath(folder), change.Destination)] : state.RetainedFolders };
    }

    private void RecoverFolderChange()
    {
        if (state.PendingFolderChange is not { } change) return;
        var folder = state.Folders.Single(item => item.Id == change.FolderId);
        var source = ContentPath(folder);
        var sourceOwned = WindowsPaths.HasIdentity(source, folder.Id);
        var completed = change.Kind switch
        {
            FolderChangeKind.Rename => RenamedDirectoryExists(change, folder.Id),
            FolderChangeKind.KeepContents => sourceOwned && Shell.ShortcutTargets(change.Destination, source),
            FolderChangeKind.Recycle => !WindowsPaths.Exists(source) && Shell.FindRecycledFolder(source, folder.Id) != null,
            _ => false
        };
        if (change.Kind == FolderChangeKind.Rename && completed && sourceOwned && !SamePath(source, change.Destination))
            throw new IOException($"原目录与改名目标同时出现相同归属，未自动选取：{source}；{change.Destination}");
        if (!completed && !sourceOwned) throw new IOException($"未完成操作的原目录、目标或回收站无法确认归属：{source}；{change.Destination}");
        var recovered = completed ? CompleteFolderChange(folder, change) : state with { PendingFolderChange = null };
        store.Save(recovered);
        state = recovered;
        notices.Add($"已核对并恢复 Folder 操作：{source}；{change.Destination}");
    }

    private static bool RenamedDirectoryExists(PendingFolderChange change, Guid id)
        => WindowsPaths.HasIdentity(change.Destination, id)
            && Directory.EnumerateDirectories(Path.GetDirectoryName(change.Destination)!)
                .Any(path => string.Equals(Path.GetFileName(path), Path.GetFileName(change.Destination), StringComparison.Ordinal));
}
