// 以实际屏幕像素统一计算布局；折叠、展开、移动和缩放共享同一碰撞规则。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Forms = System.Windows.Forms;

namespace Kage.DesktopFolderPrototype;

internal static class DesktopLayout
{
    private const int Gap = 12;

    internal static Rect Bounds(FolderWindow folder, int? x = null, int? y = null)
    {
        var scale = folder.Scale;
        return new Rect(x ?? folder.State.X, y ?? folder.State.Y, Math.Ceiling(folder.State.Width * scale), Math.Ceiling(folder.TotalHeight * scale));
    }

    private static bool Free(Rect rectangle, IEnumerable<Rect> occupied)
    {
        if (!FitsDisplays(rectangle)) return false;
        var padded = rectangle;
        padded.Inflate(Gap / 2.0, Gap / 2.0);
        return occupied.All(other => { var enlarged = other; enlarged.Inflate(Gap / 2.0, Gap / 2.0); return !padded.IntersectsWith(enlarged); });
    }

    private static Point? Find(Rect preferred, IReadOnlyList<Rect> occupied)
    {
        if (Free(preferred, occupied)) return preferred.Location;
        var candidates = new List<Point>();
        foreach (var other in occupied)
        {
            candidates.Add(new Point(preferred.X, other.Bottom + Gap));
            candidates.Add(new Point(other.Right + Gap, preferred.Y));
            candidates.Add(new Point(preferred.X, other.Top - preferred.Height - Gap));
            candidates.Add(new Point(other.Left - preferred.Width - Gap, preferred.Y));
        }
        foreach (var screen in Forms.Screen.AllScreens)
        {
            var area = screen.WorkingArea;
            for (var y = area.Top + Gap; y + preferred.Height <= area.Bottom; y += 24)
                for (var x = area.Left + Gap; x + preferred.Width <= area.Right; x += 24)
                    candidates.Add(new Point(x, y));
        }
        foreach (var point in candidates.OrderBy(point => (point - preferred.Location).LengthSquared))
            if (Free(new Rect(point, preferred.Size), occupied)) return point;
        return null;
    }

    internal static bool Move(FolderWindow folder, int x, int y)
    {
        if (!Free(Bounds(folder, x, y), Program.Folders.Where(other => other != folder && !other.LayoutHidden).Select(other => Bounds(other)))) return false;
        folder.State.X = x; folder.State.Y = y; folder.ApplyGeometry(); return true;
    }

    private static Rect[] DisplayAreas() => Forms.Screen.AllScreens.Select(screen => new Rect(screen.WorkingArea.X, screen.WorkingArea.Y, screen.WorkingArea.Width, screen.WorkingArea.Height)).ToArray();

    private static bool FitsDisplays(Rect rectangle)
    {
        // 使用工作区域的并集，允许跨相邻屏幕移动；显示器之间的空洞仍然是边界。
        var uncovered = new List<Rect> { rectangle };
        foreach (var area in DisplayAreas())
        {
            var next = new List<Rect>();
            foreach (var part in uncovered)
            {
                var covered = Rect.Intersect(part, area);
                if (covered.IsEmpty || covered.Width == 0 || covered.Height == 0) { next.Add(part); continue; }
                if (part.Top < covered.Top) next.Add(new Rect(part.Left, part.Top, part.Width, covered.Top - part.Top));
                if (covered.Bottom < part.Bottom) next.Add(new Rect(part.Left, covered.Bottom, part.Width, part.Bottom - covered.Bottom));
                if (part.Left < covered.Left) next.Add(new Rect(part.Left, covered.Top, covered.Left - part.Left, covered.Height));
                if (covered.Right < part.Right) next.Add(new Rect(covered.Right, covered.Top, part.Right - covered.Right, covered.Height));
            }
            uncovered = next;
            if (uncovered.Count == 0) return true;
        }
        return false;
    }

    internal static bool DragBy(FolderWindow folder, int deltaX, int deltaY)
    {
        var current = Bounds(folder);
        var desired = new Rect(current.X + deltaX, current.Y + deltaY, current.Width, current.Height);
        if (!FitsDisplays(desired))
        {
            var candidates = DisplayAreas().Where(area => area.Width >= current.Width && area.Height >= current.Height)
                .Select(area => new Point(Math.Clamp(desired.X, area.Left, area.Right - current.Width), Math.Clamp(desired.Y, area.Top, area.Bottom - current.Height)))
                .OrderBy(point => (point - desired.Location).LengthSquared).ToArray();
            if (candidates.Length == 0) return false;
            desired.Location = candidates[0];
        }
        var x = desired.X;
        var y = desired.Y;
        var occupied = Program.Folders.Where(other => other != folder && !other.LayoutHidden).Select(other => Bounds(other)).ToArray();
        // 接触边界只限制法向移动，另一方向可以继续滑动；扫过路径也不会穿越其他 Folder。
        foreach (var other in occupied)
        {
            if (current.Top > other.Bottom + Gap || current.Bottom < other.Top - Gap) continue;
            if (x > current.X && current.Right <= other.Left - Gap) x = Math.Min(x, other.Left - current.Width - Gap - 1);
            if (x < current.X && current.Left >= other.Right + Gap) x = Math.Max(x, other.Right + Gap + 1);
        }
        var horizontal = new Rect(x, current.Y, current.Width, current.Height);
        foreach (var other in occupied)
        {
            if (horizontal.Left > other.Right + Gap || horizontal.Right < other.Left - Gap) continue;
            if (y > current.Y && current.Bottom <= other.Top - Gap) y = Math.Min(y, other.Top - current.Height - Gap - 1);
            if (y < current.Y && current.Top >= other.Bottom + Gap) y = Math.Max(y, other.Bottom + Gap + 1);
        }
        var targetX = (int)Math.Round(x);
        var targetY = (int)Math.Round(y);
        if (targetX == folder.State.X && targetY == folder.State.Y) return false;
        return Move(folder, targetX, targetY);
    }

    internal static bool Resize(FolderWindow folder, double width, double headerHeight, double bodyHeight)
    {
        var old = (folder.State.Width, folder.State.Height, folder.State.PanelHeight);
        folder.State.Width = Math.Clamp(width, 240, 760);
        folder.State.Height = Math.Clamp(headerHeight, 42, 82);
        folder.State.PanelHeight = Math.Clamp(bodyHeight, 160, 720);
        if (!Free(Bounds(folder), Program.Folders.Where(other => other != folder && !other.LayoutHidden).Select(other => Bounds(other))))
        { (folder.State.Width, folder.State.Height, folder.State.PanelHeight) = old; return false; }
        folder.ApplyGeometry(); return true;
    }

    internal static bool Toggle(FolderWindow target)
    {
        if (target.State.Expanded)
        { target.State.Expanded = false; target.ApplyGeometry(); return true; }
        var original = Program.Folders.ToDictionary(folder => folder, folder => (folder.State.X, folder.State.Y));
        target.State.Expanded = true;
        var placed = new List<Rect>();
        // 优先保留被展开的头部位置，再为冲突的其他 Folder 查找最近空位。
        foreach (var folder in new[] { target }.Concat(Program.Folders.Where(folder => folder != target && !folder.LayoutHidden)))
        {
            var preferred = Bounds(folder);
            var point = Find(preferred, placed);
            if (point == null)
            {
                target.State.Expanded = false;
                foreach (var item in original) { item.Key.State.X = item.Value.X; item.Key.State.Y = item.Value.Y; item.Key.ApplyGeometry(); }
                Program.Notice("桌面没有足够空间展开。请缩小 Folder，或先折叠其他 Folder。");
                return false;
            }
            folder.State.X = (int)point.Value.X; folder.State.Y = (int)point.Value.Y;
            placed.Add(Bounds(folder));
        }
        foreach (var folder in Program.Folders) folder.ApplyGeometry();
        return true;
    }

    internal static void Restore(FolderWindow target)
    {
        var occupied = Program.Folders.Where(other => other != target && !other.LayoutHidden).Select(other => Bounds(other)).ToArray();
        var point = Find(Bounds(target), occupied);
        if (point == null && target.State.Expanded) { target.State.Expanded = false; point = Find(Bounds(target), occupied); }
        if (point == null)
        {
            target.LayoutHidden = true; target.Hide();
            Program.Notice("桌面空间不足，暂时隐藏这个 Folder。记录和内容均保留；缩小其他 Folder 后可通过托盘“重新挂接桌面”恢复。");
            return;
        }
        target.LayoutHidden = false; target.Show();
        target.State.X = (int)point.Value.X; target.State.Y = (int)point.Value.Y; target.ApplyGeometry();
    }

    internal static bool NonOverlapping() => Program.Folders.SelectMany((a, index) => Program.Folders.Skip(index + 1).Select(b => !Bounds(a).IntersectsWith(Bounds(b)))).All(value => value);
}
