namespace Kage.Workspace;

internal static class HeaderLayout
{
    private const int Gap = 12;
    private static int Width(FolderRecord folder, DisplayArea area) => (int)Math.Ceiling(folder.HeaderWidth * area.Scale);
    private static int Height(FolderRecord folder, DisplayArea area) => (int)Math.Ceiling((folder.HeaderHeight + (folder.Expanded ? folder.BodyHeight : 0)) * area.Scale);

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
            var candidate = folder;
            var area = Available(candidate, displays, occupied);
            if (area == null)
            {
                // 搜索靠近保存坐标的网格空位，无法容纳则保留原记录。
                var positions = displays.SelectMany(display => Positions(folder, display))
                    .OrderBy(point => Math.Pow((double)point.X - folder.X, 2) + Math.Pow((double)point.Y - folder.Y, 2));
                foreach (var point in positions)
                {
                    var next = folder with { X = point.X, Y = point.Y };
                    area = Available(next, displays, occupied);
                    if (area != null) { candidate = next; break; }
                }
            }
            if (area != null) occupied.Add((candidate, area));
            return candidate;
        }).ToArray();
    }

    private static IEnumerable<(int X, int Y)> Positions(FolderRecord folder, DisplayArea area)
    {
        for (var y = area.Y + Gap; (long)y + Height(folder, area) <= (long)area.Y + area.Height; y += 24)
            for (var x = area.X + Gap; (long)x + Width(folder, area) <= (long)area.X + area.Width; x += 24)
                yield return (x, y);
    }
}
