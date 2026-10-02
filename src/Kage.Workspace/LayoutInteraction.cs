namespace Kage.Workspace;

// 一个输入会话只处理不可变快照的几何信息。每次鼠标输入不访问目录、图标或状态文件。
public sealed class LayoutInteraction
{
    internal FolderRecord[] Original { get; }
    private readonly Guid target;
    private readonly DisplayArea[] displays;
    private FolderSnapshot[] folders;
    private (int X, int Y)? anchor;
    public IReadOnlyList<FolderSnapshot> Folders => Array.AsReadOnly(folders);

    internal LayoutInteraction(Guid id, WorkspaceSnapshot snapshot, DisplayArea[] areas)
    {
        target = id;
        folders = snapshot.Folders.ToArray();
        Original = folders.Select(folder => folder.Folder).ToArray();
        displays = areas.ToArray();
    }

    public void BeginDrag(int screenX, int screenY) => anchor = (screenX, screenY);
    public void EndDrag() => anchor = null;

    public bool DragTo(int screenX, int screenY)
    {
        if (anchor is not { } previous) return false;
        anchor = (screenX, screenY);
        var current = folders.Single(folder => folder.Folder.Id == target).Folder;
        var area = HeaderLayout.Available(current, displays, []);
        if (area == null) return false;
        var width = HeaderLayout.Width(current, area);
        var height = HeaderLayout.Height(current, area);
        var x = (int)Math.Clamp((long)current.X + screenX - previous.X, area.X, (long)area.X + area.Width - width);
        var y = (int)Math.Clamp((long)current.Y + screenY - previous.Y, area.Y, (long)area.Y + area.Height - height);
        var occupied = Occupied();
        // 先扫过水平路径，再扫过垂直路径；法向受阻仍保留切向移动。
        foreach (var (other, display) in occupied)
        {
            if (current.Y >= other.Y + HeaderLayout.Height(other, display) + HeaderLayout.Gap
                || current.Y + height + HeaderLayout.Gap <= other.Y) continue;
            if (x > current.X && current.X + width + HeaderLayout.Gap <= other.X)
                x = Math.Min(x, other.X - width - HeaderLayout.Gap);
            if (x < current.X && current.X >= other.X + HeaderLayout.Width(other, display) + HeaderLayout.Gap)
                x = Math.Max(x, other.X + HeaderLayout.Width(other, display) + HeaderLayout.Gap);
        }
        foreach (var (other, display) in occupied)
        {
            if (x >= other.X + HeaderLayout.Width(other, display) + HeaderLayout.Gap
                || x + width + HeaderLayout.Gap <= other.X) continue;
            if (y > current.Y && current.Y + height + HeaderLayout.Gap <= other.Y)
                y = Math.Min(y, other.Y - height - HeaderLayout.Gap);
            if (y < current.Y && current.Y >= other.Y + HeaderLayout.Height(other, display) + HeaderLayout.Gap)
                y = Math.Max(y, other.Y + HeaderLayout.Height(other, display) + HeaderLayout.Gap);
        }
        return Replace(current with { X = x, Y = y }, occupied);
    }

    public bool ResizeBy(double widthDelta, double heightDelta)
    {
        if (!double.IsFinite(widthDelta) || !double.IsFinite(heightDelta)) return false;
        var current = folders.Single(folder => folder.Folder.Id == target).Folder;
        var changed = current with
        {
            HeaderWidth = Math.Clamp(current.HeaderWidth + widthDelta, 240, 760),
            HeaderHeight = current.Expanded ? current.HeaderHeight : Math.Clamp(current.HeaderHeight + heightDelta, 42, 82),
            BodyHeight = current.Expanded ? Math.Clamp(current.BodyHeight + heightDelta, 160, 720) : current.BodyHeight
        };
        return Replace(changed, Occupied());
    }

    private List<(FolderRecord Folder, DisplayArea Area)> Occupied() => folders
        .Where(folder => folder.Visible && folder.Folder.Id != target)
        .Select(folder => (folder.Folder, HeaderLayout.Available(folder.Folder, displays, [])!)).ToList();

    private bool Replace(FolderRecord changed, IReadOnlyList<(FolderRecord Folder, DisplayArea Area)> occupied)
    {
        if (HeaderLayout.Available(changed, displays, occupied) == null) return false;
        var index = Array.FindIndex(folders, folder => folder.Folder.Id == target);
        if (folders[index].Folder == changed) return false;
        folders = folders.Select(folder => folder.Folder.Id == target ? folder with { Folder = changed } : folder).ToArray();
        return true;
    }
}
