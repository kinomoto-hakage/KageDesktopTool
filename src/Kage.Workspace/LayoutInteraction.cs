namespace Kage.Workspace;

// 一个输入会话只处理不可变快照的几何信息。每次鼠标输入不访问目录、图标或状态文件。
public sealed class LayoutInteraction
{
    internal FolderRecord[] Original { get; }
    internal IReadOnlyList<DisplayArea> Displays => displays;
    private readonly Guid target;
    private readonly DisplayArea[] displays;
    private FolderSnapshot[] folders;
    private (int X, int Y)? anchor;
    internal bool Dragged { get; private set; }
    internal Guid Target => target;
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
        Dragged = true;
        var current = folders.Single(folder => folder.Folder.Id == target).Folder;
        var area = HeaderLayout.Available(current, displays, []);
        if (area == null) return false;
        var width = HeaderLayout.Width(current, area);
        var height = HeaderLayout.Height(current, area);
        // 拖动只受工作区限制；Folder 间隙在释放时统一计算。
        var dx = (double)screenX - previous.X;
        var dy = (double)screenY - previous.Y;
        var point = displays.Length == 1
            ? (X: (int)Math.Clamp(current.X + dx, area.X, (long)area.X + area.Width - width),
                Y: (int)Math.Clamp(current.Y + dy, area.Y, (long)area.Y + area.Height - height))
            : SweepDisplays(current, dx, dy);
        return Replace(current with { X = point.X, Y = point.Y }, []);
    }

    private (int X, int Y) SweepDisplays(FolderRecord current, double dx, double dy)
    {
        // 多屏路径逐物理像素核对真实工作区并重新选择 DPI；不能跳过屏幕空洞。
        // 超大输入限制在整个桌面跨度，防止无效位移拖慢鼠标处理。
        var span = Math.Max(displays.Max(d => (long)d.X + d.Width) - displays.Min(d => d.X),
            displays.Max(d => (long)d.Y + d.Height) - displays.Min(d => d.Y));
        var distance = Math.Max(Math.Abs(dx), Math.Abs(dy));
        if (distance > span * 2) { dx *= span * 2 / distance; dy *= span * 2 / distance; }
        var steps = (int)Math.Ceiling(Math.Max(Math.Abs(dx), Math.Abs(dy)));
        var x = current.X;
        var y = current.Y;
        bool stopX = false, stopY = false;
        for (var step = 1; step <= steps; step++)
        {
            var nextX = x + (stopX ? 0 : (int)Math.Round(dx * step / steps) - (int)Math.Round(dx * (step - 1) / steps));
            var nextY = y + (stopY ? 0 : (int)Math.Round(dy * step / steps) - (int)Math.Round(dy * (step - 1) / steps));
            var candidate = current with { X = nextX, Y = nextY };
            if (HeaderLayout.Available(candidate, displays, []) != null) { x = nextX; y = nextY; continue; }
            // 接触后保持切向位移；锚点始终由 DragTo 更新，下一次反向立即响应。
            var horizontal = candidate with { Y = y };
            var vertical = candidate with { X = x };
            if (nextX != x && HeaderLayout.Available(horizontal, displays, []) != null) { x = nextX; stopY = true; }
            else if (nextY != y && HeaderLayout.Available(vertical, displays, []) != null) { y = nextY; stopX = true; }
            else { stopX = true; stopY = true; }
            if (stopX && stopY) break;
        }
        return (x, y);
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

    public bool ResizeHeaderBy(double heightDelta)
    {
        if (!double.IsFinite(heightDelta)) return false;
        var current = folders.Single(folder => folder.Folder.Id == target).Folder;
        return Replace(current with { HeaderHeight = Math.Clamp(current.HeaderHeight + heightDelta, 42, 82) }, Occupied());
    }

    private List<(FolderRecord Folder, DisplayArea Area)> Occupied() => folders
        .Where(folder => folder.Visible && folder.Folder.Id != target)
        .Select(folder => (folder.Folder, HeaderLayout.Available(folder.Folder, displays, [])!)).ToList();

    private bool Replace(FolderRecord changed, IReadOnlyList<(FolderRecord Folder, DisplayArea Area)> occupied)
    {
        var area = HeaderLayout.Available(changed, displays, occupied);
        if (area == null) return false;
        var index = Array.FindIndex(folders, folder => folder.Folder.Id == target);
        if (folders[index].Folder == changed) return false;
        folders = folders.Select(folder => folder.Folder.Id == target ? folder with { Folder = changed, DisplayScale = area.Scale } : folder).ToArray();
        return true;
    }
}
