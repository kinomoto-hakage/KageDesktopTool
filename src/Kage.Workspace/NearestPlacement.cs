namespace Kage.Workspace;

// 释放与展开共用的物理像素搜索；只返回目标记录，不重新排列其他 Folder。
internal static class NearestPlacement
{
    internal static FolderRecord? Find(FolderRecord target, IReadOnlyList<DisplayArea> displays,
        IReadOnlyList<(FolderRecord Folder, DisplayArea Area)> occupied)
    {
        if (HeaderLayout.Available(target, displays, occupied) != null) return target;
        var preferred = displays.Where(d => target.X >= d.X && target.X < (long)d.X + d.Width
            && target.Y >= d.Y && target.Y < (long)d.Y + d.Height).OrderBy(d => d.Y).ThenBy(d => d.X).FirstOrDefault()
            ?? HeaderLayout.DisplayFor(target, displays);
        FolderRecord? best = null;
        double bestDistance = double.PositiveInfinity;
        foreach (var screen in displays.OrderBy(d => d == preferred ? 0 : 1)
            .ThenBy(d => Distance(Math.Clamp(target.X, d.X, d.X + d.Width - 1), Math.Clamp(target.Y, d.Y, d.Y + d.Height - 1), target))
            .ThenBy(d => d.Y).ThenBy(d => d.X))
        {
            // 每个物理纵坐标中，工作区和障碍边缘将横轴切成几何条件恒定的区间。
            // 不使用粗网格，避免遗漏狭窄空位或真正最近的像素。
            foreach (var y in Enumerable.Range(screen.Y, screen.Height).OrderBy(y => Math.Abs((long)y - target.Y)).ThenBy(y => y))
            {
                if (Math.Pow((double)y - target.Y, 2) > bestDistance) break;
                foreach (var scale in displays.Select(d => d.Scale).Distinct())
                {
                    var metrics = screen with { Scale = scale };
                    var width = HeaderLayout.Width(target, metrics);
                    var height = HeaderLayout.Height(target, metrics);
                    var cuts = new List<long> { screen.X, (long)screen.X + screen.Width };
                    foreach (var display in displays)
                        cuts.AddRange([(long)display.X, (long)display.X + display.Width,
                            (long)display.X - width + 1, (long)display.X + display.Width - width + 1]);
                    foreach (var (other, area) in occupied.Where(o => (long)y + height + HeaderLayout.Gap > o.Folder.Y
                        && y < (long)o.Folder.Y + HeaderLayout.Height(o.Folder, o.Area) + HeaderLayout.Gap))
                        cuts.AddRange([(long)other.X - width - HeaderLayout.Gap + 1,
                            (long)other.X + HeaderLayout.Width(other, area) + HeaderLayout.Gap]);
                    var boundaries = cuts.Where(x => x >= screen.X && x <= (long)screen.X + screen.Width).Distinct().Order().ToArray();
                    for (var i = 1; i < boundaries.Length; i++)
                    {
                        var left = (int)boundaries[i - 1];
                        var right = (int)(boundaries[i] - 1);
                        var nearest = Math.Clamp(target.X, left, right);
                        var sample = target with { X = nearest, Y = y };
                        if (Distance(nearest, y, target) > bestDistance || !HeaderLayout.Fits(sample, metrics, displays, occupied)) continue;
                        // 混合 DPI 在接缝可能切换度量；在区间内按距离检查，直到找到自洽像素。
                        for (long offset = 0; nearest - offset >= left || nearest + offset <= right; offset++)
                        {
                            var found = false;
                            foreach (var x in offset == 0 ? new[] { (long)nearest } : new[] { nearest - offset, nearest + offset })
                            {
                                if (x < left || x > right) continue;
                                var distance = Distance((int)x, y, target);
                                if (distance > bestDistance) continue;
                                var candidate = target with { X = (int)x, Y = y };
                                var actual = HeaderLayout.Available(candidate, displays, occupied);
                                if (actual == null || actual.Scale != scale) continue;
                                found = true;
                                if (distance < bestDistance || best == null || (distance == bestDistance && (y < best.Y || (y == best.Y && x < best.X))))
                                { best = candidate; bestDistance = distance; }
                            }
                            if (found || Distance((int)Math.Clamp((long)nearest + offset, int.MinValue, int.MaxValue), y, target) > bestDistance) break;
                        }
                    }
                }
            }
            if (screen == preferred && best != null) return best;
        }
        return best;
    }

    private static double Distance(int x, int y, FolderRecord target)
        => Math.Pow((double)x - target.X, 2) + Math.Pow((double)y - target.Y, 2);
}
