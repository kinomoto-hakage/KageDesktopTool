namespace Kage.Workspace;

internal static class HeaderLayout
{
    internal const int Gap = 12;
    internal static int Width(FolderRecord folder, DisplayArea area) => (int)Math.Ceiling(folder.HeaderWidth * area.Scale);
    internal static int Height(FolderRecord folder, DisplayArea area) => (int)Math.Ceiling((folder.HeaderHeight + (folder.Expanded ? folder.BodyHeight : 0)) * area.Scale);

    internal static DisplayArea? Available(FolderRecord folder, IReadOnlyList<DisplayArea> displays, IReadOnlyList<(FolderRecord Folder, DisplayArea Area)> occupied)
        => displays.FirstOrDefault(area => folder.X >= area.X && folder.Y >= area.Y
            && (long)folder.X + Width(folder, area) <= (long)area.X + area.Width && (long)folder.Y + Height(folder, area) <= (long)area.Y + area.Height
            && occupied.All(other => folder.X + Width(folder, area) + Gap <= other.Folder.X
                || folder.X >= other.Folder.X + Width(other.Folder, other.Area) + Gap
                || folder.Y + Height(folder, area) + Gap <= other.Folder.Y
                || folder.Y >= other.Folder.Y + Height(other.Folder, other.Area) + Gap));

    internal static FolderRecord[] Place(FolderRecord[] folders, IReadOnlyList<DisplayArea> displays)
    {
        var occupied = new List<(FolderRecord Folder, DisplayArea Area)>();
        return folders.Select(folder =>
        {
            // 找不到空位仍保留原记录及内容关联。
            var candidate = Find(folder, displays, occupied) ?? folder;
            var area = Available(candidate, displays, occupied);
            if (area != null) occupied.Add((candidate, area));
            return candidate;
        }).ToArray();
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
