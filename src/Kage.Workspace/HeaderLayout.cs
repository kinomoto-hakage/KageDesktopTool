namespace Kage.Workspace;

internal static class HeaderLayout
{
    internal const int Gap = 12;
    internal static int Width(FolderRecord folder, DisplayArea area) => (int)Math.Ceiling(folder.HeaderWidth * area.Scale);
    internal static int Height(FolderRecord folder, DisplayArea area) => (int)Math.Ceiling((folder.HeaderHeight + (folder.Expanded ? folder.BodyHeight : 0)) * area.Scale);

    internal static DisplayArea? Available(FolderRecord folder, IReadOnlyList<DisplayArea> displays, IReadOnlyList<(FolderRecord Folder, DisplayArea Area)> occupied)
    {
        var area = DisplayFor(folder, displays);
        return area != null && Covered(folder, area, displays)
            && occupied.All(other => (long)folder.X + Width(folder, area) + Gap <= other.Folder.X
                || folder.X >= other.Folder.X + Width(other.Folder, other.Area) + Gap
                || (long)folder.Y + Height(folder, area) + Gap <= other.Folder.Y
                || folder.Y >= (long)other.Folder.Y + Height(other.Folder, other.Area) + Gap) ? area : null;
    }

    // 与 MonitorFromWindow 一致，使用物理窗口最大交叠面积对应的显示器。
    // 尺寸随 DPI 改变，须核对该显示器的尺寸选择仍得到同一最大交叠区域。
    internal static DisplayArea? DisplayFor(FolderRecord folder, IReadOnlyList<DisplayArea> displays)
    {
        foreach (var area in displays.OrderBy(a => a.Scale).ThenBy(a => a.X).ThenBy(a => a.Y))
        {
            var width = Width(folder, area);
            var height = Height(folder, area);
            long Intersection(DisplayArea display) => Math.Max(0, Math.Min((long)folder.X + width, (long)display.X + display.Width) - Math.Max(folder.X, display.X))
                * Math.Max(0, Math.Min((long)folder.Y + height, (long)display.Y + display.Height) - Math.Max(folder.Y, display.Y));
            var selected = displays.OrderByDescending(Intersection).ThenBy(a => a.X).ThenBy(a => a.Y).FirstOrDefault();
            if (selected == area && Intersection(area) > 0) return area;
        }
        return null;
    }

    private static bool Covered(FolderRecord folder, DisplayArea area, IReadOnlyList<DisplayArea> displays)
    {
        long left = folder.X, top = folder.Y, right = left + Width(folder, area), bottom = top + Height(folder, area);
        var cuts = displays.SelectMany(d => new[] { (long)d.X, (long)d.X + d.Width }).Append(left).Append(right)
            .Where(x => x >= left && x <= right).Distinct().Order().ToArray();
        for (var i = 1; i < cuts.Length; i++)
        {
            var coveredY = top;
            foreach (var display in displays.Where(d => d.X <= cuts[i - 1] && (long)d.X + d.Width >= cuts[i]).OrderBy(d => d.Y))
            {
                if (display.Y > coveredY) break;
                coveredY = Math.Max(coveredY, (long)display.Y + display.Height);
                if (coveredY >= bottom) break;
            }
            if (coveredY < bottom) return false;
        }
        return true;
    }

    internal static FolderRecord[] Place(FolderRecord[] folders, IReadOnlyList<DisplayArea> displays)
    {
        var occupied = new List<(FolderRecord Folder, DisplayArea Area)>();
        var placement = new Dictionary<Guid, FolderRecord>();
        // 先占住当前仍可见的位置；屏幕外和暂未展示的记录只能使用剩余空位。
        foreach (var folder in folders.Where(f => !f.LayoutHidden))
        {
            var area = Available(folder, displays, occupied);
            if (area == null) continue;
            occupied.Add((folder, area));
            placement[folder.Id] = folder;
        }
        foreach (var folder in folders.Where(f => !placement.ContainsKey(f.Id)).OrderBy(folder => folder.LayoutHidden))
        {
            // 找不到空位仍保留原记录及内容关联。
            var candidate = Find(folder, displays, occupied) ?? folder;
            var area = Available(candidate, displays, occupied);
            candidate = candidate with { LayoutHidden = area == null };
            if (area != null) occupied.Add((candidate, area));
            placement[folder.Id] = candidate;
        }
        return folders.Select(folder => placement[folder.Id]).ToArray();
    }

    private static IEnumerable<(int X, int Y)> Positions(FolderRecord folder, DisplayArea area, IReadOnlyList<(FolderRecord Folder, DisplayArea Area)> occupied)
    {
        var width = Width(folder, area);
        var height = Height(folder, area);
        var xs = new List<int> { folder.X, area.X, area.X + area.Width - width };
        var ys = new List<int> { folder.Y, area.Y, area.Y + area.Height - height };
        foreach (var (other, display) in occupied)
        {
            xs.Add(other.X + Width(other, display) + Gap);
            xs.Add(other.X - width - Gap);
            ys.Add(other.Y + Height(other, display) + Gap);
            ys.Add(other.Y - height - Gap);
        }
        foreach (var y in ys.Distinct())
            foreach (var x in xs.Distinct()) yield return (x, y);
        for (var y = area.Y + Gap; (long)y + Height(folder, area) <= (long)area.Y + area.Height; y += 24)
            for (var x = area.X + Gap; (long)x + Width(folder, area) <= (long)area.X + area.Width; x += 24)
                yield return (x, y);
    }

    internal static FolderRecord? Find(FolderRecord folder, IReadOnlyList<DisplayArea> displays, IReadOnlyList<(FolderRecord Folder, DisplayArea Area)> occupied)
    {
        if (Available(folder, displays, occupied) != null) return folder;
        foreach (var point in displays.SelectMany(display => Positions(folder, display, occupied))
            .OrderBy(p => Math.Pow((double)p.X - folder.X, 2) + Math.Pow((double)p.Y - folder.Y, 2)))
        {
            var candidate = folder with { X = point.X, Y = point.Y };
            if (Available(candidate, displays, occupied) != null) return candidate;
        }
        return null;
    }
}
