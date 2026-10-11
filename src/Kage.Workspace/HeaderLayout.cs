namespace Kage.Workspace;

internal static class HeaderLayout
{
    internal const int Gap = 12;
    internal static int Width(FolderRecord folder, DisplayArea area) => FolderGeometry.For(folder, area.Scale).Width;
    internal static int Height(FolderRecord folder, DisplayArea area) => FolderGeometry.For(folder, area.Scale).Height;

    internal static (FolderRecord Folder, DisplayArea Area)[] Occupied(IEnumerable<FolderSnapshot> folders, Guid target, IReadOnlyList<DisplayArea> displays)
        => folders.Where(folder => folder.Visible && folder.Folder.Id != target)
            .Select(folder => (folder.Folder, Available(folder.Folder, displays, [])!)).ToArray();

    internal static DisplayArea? Available(FolderRecord folder, IReadOnlyList<DisplayArea> displays, IReadOnlyList<(FolderRecord Folder, DisplayArea Area)> occupied)
    {
        var area = DisplayFor(folder, displays);
        return area != null && Fits(folder, area, displays, occupied) ? area : null;
    }

    internal static bool Fits(FolderRecord folder, DisplayArea area, IReadOnlyList<DisplayArea> displays, IReadOnlyList<(FolderRecord Folder, DisplayArea Area)> occupied)
    {
        var bounds = FolderGeometry.For(folder, area.Scale);
        return Covered(folder, area, displays) && occupied.All(other =>
        {
            var obstacle = FolderGeometry.For(other.Folder, other.Area.Scale);
            return (long)bounds.X + bounds.Width + Gap <= obstacle.X || bounds.X >= (long)obstacle.X + obstacle.Width + Gap
                || (long)bounds.Y + bounds.Height + Gap <= obstacle.Y || bounds.Y >= (long)obstacle.Y + obstacle.Height + Gap;
        });
    }

    // 优先使用物理窗口最大交叠面积对应的显示器，并核对尺寸选择。
    // 高到低 DPI 的接缝可能没有自洽解，此时稳定使用头部原点所在屏。
    internal static DisplayArea? DisplayFor(FolderRecord folder, IReadOnlyList<DisplayArea> displays)
    {
        foreach (var area in displays.OrderBy(a => a.Scale).ThenBy(a => a.X).ThenBy(a => a.Y))
        {
            var bounds = FolderGeometry.For(folder, area.Scale);
            long Intersection(DisplayArea display) => Math.Max(0, Math.Min((long)bounds.X + bounds.Width, (long)display.X + display.Width) - Math.Max(bounds.X, display.X))
                * Math.Max(0, Math.Min((long)bounds.Y + bounds.Height, (long)display.Y + display.Height) - Math.Max(bounds.Y, display.Y));
            var selected = displays.OrderByDescending(Intersection).ThenBy(a => a.X).ThenBy(a => a.Y).FirstOrDefault();
            if (selected == area && Intersection(area) > 0) return area;
        }
        return displays.Where(a => folder.X >= a.X && folder.Y >= a.Y
            && folder.X < (long)a.X + a.Width && folder.Y < (long)a.Y + a.Height)
            .OrderBy(a => a.Scale).ThenBy(a => a.X).ThenBy(a => a.Y).FirstOrDefault();
    }

    private static bool Covered(FolderRecord folder, DisplayArea area, IReadOnlyList<DisplayArea> displays)
    {
        var bounds = FolderGeometry.For(folder, area.Scale);
        long left = bounds.X, top = bounds.Y, right = left + bounds.Width, bottom = top + bounds.Height;
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
        var bounds = FolderGeometry.For(folder, area.Scale);
        var offsetX = folder.X - bounds.X;
        var offsetY = folder.Y - bounds.Y;
        var xs = new List<int> { folder.X, area.X + offsetX, area.X + area.Width - width + offsetX };
        var ys = new List<int> { folder.Y, area.Y + offsetY, area.Y + area.Height - height + offsetY };
        foreach (var (other, display) in occupied)
        {
            var obstacle = FolderGeometry.For(other, display.Scale);
            xs.Add(obstacle.X + obstacle.Width + Gap + offsetX);
            xs.Add(obstacle.X - width - Gap + offsetX);
            ys.Add(obstacle.Y + obstacle.Height + Gap + offsetY);
            ys.Add(obstacle.Y - height - Gap + offsetY);
        }
        foreach (var y in ys.Distinct())
            foreach (var x in xs.Distinct()) yield return (x, y);
        for (var y = area.Y + Gap; (long)y + Height(folder, area) <= (long)area.Y + area.Height; y += 24)
            for (var x = area.X + Gap; (long)x + Width(folder, area) <= (long)area.X + area.Width; x += 24)
                yield return (x + offsetX, y + offsetY);
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
