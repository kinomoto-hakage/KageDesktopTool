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
        var occupied = Occupied();
        var point = Sweep(current, area, width, height, (double)screenX - previous.X, (double)screenY - previous.Y, occupied);
        return Replace(current with { X = point.X, Y = point.Y }, occupied);
    }

    private static (int X, int Y) Sweep(FolderRecord current, DisplayArea area, int width, int height,
        double dx, double dy, IReadOnlyList<(FolderRecord Folder, DisplayArea Area)> occupied)
    {
        double x = current.X, y = current.Y;
        // 扫过相邻输入间的真实直线。每次接触消除法向剩余位移，继续扫过切向路径。
        for (var step = 0; step < 3 && (dx != 0 || dy != 0); step++)
        {
            double time = 1;
            bool stopX = false, stopY = false;
            void Contact(double candidate, bool horizontal, bool vertical)
            {
                if (candidate < 0 || candidate > time) return;
                if (candidate < time - 1e-10) { time = candidate; stopX = horizontal; stopY = vertical; }
                else { stopX |= horizontal; stopY |= vertical; }
            }
            if (dx < 0) Contact((area.X - x) / dx, true, false);
            if (dx > 0) Contact(((double)area.X + area.Width - width - x) / dx, true, false);
            if (dy < 0) Contact((area.Y - y) / dy, false, true);
            if (dy > 0) Contact(((double)area.Y + area.Height - height - y) / dy, false, true);
            foreach (var (other, display) in occupied)
            {
                var horizontal = AxisTimes(x, dx, (double)other.X - width - HeaderLayout.Gap,
                    (double)other.X + HeaderLayout.Width(other, display) + HeaderLayout.Gap);
                var vertical = AxisTimes(y, dy, (double)other.Y - height - HeaderLayout.Gap,
                    (double)other.Y + HeaderLayout.Height(other, display) + HeaderLayout.Gap);
                if (horizontal == null || vertical == null) continue;
                var entry = Math.Max(horizontal.Value.Entry, vertical.Value.Entry);
                var exit = Math.Min(horizontal.Value.Exit, vertical.Value.Exit);
                if (entry < 0 || entry >= exit || exit <= 0) continue;
                Contact(entry, horizontal.Value.Entry >= vertical.Value.Entry, vertical.Value.Entry >= horizontal.Value.Entry);
            }
            x += dx * time;
            y += dy * time;
            dx = stopX ? 0 : dx * (1 - time);
            dy = stopY ? 0 : dy * (1 - time);
        }
        return ((int)Math.Round(x), (int)Math.Round(y));
    }

    private static (double Entry, double Exit)? AxisTimes(double position, double delta, double minimum, double maximum)
    {
        if (delta == 0) return position <= minimum || position >= maximum ? null : (double.NegativeInfinity, double.PositiveInfinity);
        var first = (minimum - position) / delta;
        var second = (maximum - position) / delta;
        return (Math.Min(first, second), Math.Max(first, second));
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
