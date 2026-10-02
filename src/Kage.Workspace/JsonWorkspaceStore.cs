using System.Text.Json;

namespace Kage.Workspace;

public sealed class JsonWorkspaceStore(string directory) : IWorkspaceStore
{
    private string Primary => Path.Combine(directory, "workspace.json");
    private string Backup => Primary + ".bak";
    private string Temporary => Primary + ".tmp";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public StateRead Read()
    {
        if (!File.Exists(Primary) && !File.Exists(Backup) && !File.Exists(Temporary)) return new(new());
        try { return new(ReadFile(Primary)); }
        catch (Exception e) when (e is IOException or InvalidDataException or JsonException or ArgumentException)
        {
            if (!File.Exists(Backup)) throw new InvalidDataException($"状态不可读，停止写入以保留证据：{Primary}。{e.Message}", e);
            return new(ReadFile(Backup), true, $"主状态损坏或缺失：{e.Message}。已只读加载最近有效备份；需明确恢复后才能继续修改。");
        }
    }

    private static WorkspaceState ReadFile(string path)
    {
        var state = JsonSerializer.Deserialize<WorkspaceState>(File.ReadAllText(path), Json)
            ?? throw new InvalidDataException("状态为空。");
        Validate(state);
        return state;
    }

    private static void Validate(WorkspaceState state)
    {
        if (state.Version != 1) throw new InvalidDataException($"不支持的状态格式版本：{state.Version}。");
        WindowsPaths.Root(state.Root);
        if (state.Folders == null || state.IconChoice is not ("a" or "b" or "c" or "d")) throw new InvalidDataException("状态字段无效。");
        if (state.PendingStartup is { } startup && startup.Enabled != (startup.TargetCommand != null))
            throw new InvalidDataException("未完成自启记录的目标配置与开关不一致。");
        if (new[] { state.PendingStartup != null, state.PendingCreate != null, state.PendingFolderChange != null }.Count(value => value) > 1)
            throw new InvalidDataException("状态存在冲突的未完成操作。");
        if (state.RetainedFolders == null) throw new InvalidDataException("保留内容关联无效。");
        var retainedIds = new HashSet<Guid>();
        var shortcuts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var retained in state.RetainedFolders)
        {
            if (retained == null || retained.FolderId == Guid.Empty || !retainedIds.Add(retained.FolderId)
                || state.Folders.Any(folder => folder.Id == retained.FolderId)) throw new InvalidDataException("保留内容标识重复或仍在活动映射中。");
            WindowsPaths.Root(retained.ContentPath);
            if (!shortcuts.Add(WindowsPaths.Root(retained.ShortcutPath)) || !retained.ShortcutPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("保留快捷方式路径无效或重复。");
        }
        if (state.PendingFolderChange is { } change)
        {
            var source = state.Folders.SingleOrDefault(folder => folder.Id == change.FolderId)
                ?? throw new InvalidDataException("未完成 Folder 操作没有对应活动记录。");
            var destination = WindowsPaths.Root(change.Destination);
            var content = Path.Combine(source.ContentRoot ?? state.Root, source.Name);
            if (!Enum.IsDefined(change.Kind)
                || (change.Kind == FolderChangeKind.Rename && !string.Equals(Path.GetDirectoryName(destination), WindowsPaths.Root(source.ContentRoot ?? state.Root), StringComparison.OrdinalIgnoreCase))
                || (change.Kind == FolderChangeKind.KeepContents && !destination.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                || (change.Kind == FolderChangeKind.Recycle && !string.Equals(destination, content, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("未完成 Folder 操作的目标路径无效。");
        }
        var ids = new HashSet<Guid>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in state.Folders.Concat(state.PendingCreate is null ? [] : new[] { state.PendingCreate }))
        {
            if (folder == null || folder.Id == Guid.Empty || !ids.Add(folder.Id) || !names.Add(folder.Name)) throw new InvalidDataException("Folder 标识或名称重复／无效。");
            WindowsPaths.Name(folder.Name);
            if (folder.ContentRoot != null) WindowsPaths.Root(folder.ContentRoot);
            if (!double.IsFinite(folder.HeaderWidth) || !double.IsFinite(folder.HeaderHeight) || !double.IsFinite(folder.BodyHeight)
                || folder.HeaderWidth is < 240 or > 760 || folder.HeaderHeight is < 42 or > 82 || folder.BodyHeight is < 160 or > 720
                || !double.IsFinite(folder.Opacity) || folder.Opacity is < 0 or > 1
                || folder.Color == null || !System.Text.RegularExpressions.Regex.IsMatch(folder.Color, "^#[0-9a-fA-F]{6}$"))
                throw new InvalidDataException("Folder 尺寸或外观无效。");
        }
    }

    public void Save(WorkspaceState state)
    {
        Validate(state);
        Directory.CreateDirectory(directory);
        // 刷新临时文件后在同目录原子替换，旧主文件成为最近有效备份。
        using (var file = new FileStream(Temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            JsonSerializer.Serialize(file, state, Json);
            file.Flush(true);
        }
        if (File.Exists(Primary)) File.Replace(Temporary, Primary, Backup);
        else File.Move(Temporary, Primary);
    }

    public void RestoreBackup()
    {
        var valid = ReadFile(Backup);
        if (File.Exists(Primary)) File.Copy(Primary, Primary + ".damaged-" + Guid.NewGuid().ToString("N"));
        // 显式恢复保留损坏证据及原备份，后续提交才轮换备份。
        using (var file = new FileStream(Temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            JsonSerializer.Serialize(file, valid, Json);
            file.Flush(true);
        }
        File.Move(Temporary, Primary, true);
    }
}
