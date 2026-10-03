namespace Kage.Workspace;

public sealed partial class DesktopWorkspace
{
    private MigrationRecoveryItem InspectMigrationItem(RootMigrationItem item)
    {
        var errors = new List<string>();
        MigrationPathStatus Inspect(string path)
        {
            try
            {
                CheckExistingAncestors(path);
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.Directory) == 0 || !WindowsPaths.HasIdentity(path, item.FolderId))
                {
                    errors.Add($"位置已存在，但内容归属无法确认，未覆盖：{path}");
                    return MigrationPathStatus.Unverified;
                }
                return MigrationPathStatus.Owned;
            }
            catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { return MigrationPathStatus.Missing; }
            catch (Exception error) { errors.Add($"位置不可核对：{path}。{error.Message}"); return MigrationPathStatus.Unavailable; }
        }
        var source = Inspect(item.SourcePath);
        var destination = Inspect(item.DestinationPath);
        string? shortcut = null;
        if (item.ShortcutPath != null)
        {
            try
            {
                CheckExistingAncestors(item.ShortcutPath);
                shortcut = Shell.ReadShortcutTarget(item.ShortcutPath, item.FolderId);
                if (shortcut == null || (!SamePath(shortcut, item.SourcePath) && !SamePath(shortcut, item.DestinationPath)))
                    errors.Add($"快捷方式缺失或目标已变化，未覆盖：{item.ShortcutPath}；实际目标：{shortcut ?? "缺失"}");
            }
            catch (Exception error) { errors.Add($"快捷方式不可核对：{item.ShortcutPath}。{error.Message}"); }
        }
        if (source == MigrationPathStatus.Missing && destination == MigrationPathStatus.Missing)
            errors.Add("两边均未发现内容目录；保留记录，请核对磁盘连接及实际位置。");
        var restored = source == MigrationPathStatus.Owned && destination == MigrationPathStatus.Missing
            && (item.ShortcutPath == null || shortcut != null && SamePath(shortcut, item.SourcePath));
        return new(item, source, destination, shortcut, restored, errors.Count == 0 ? null : string.Join("\n", errors));
    }

    private static void CheckExistingAncestors(string path)
    {
        for (var current = path; current != null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException($"重解析路径无法确认，保留内容：{current}");
            }
            catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { }
        }
    }
}
