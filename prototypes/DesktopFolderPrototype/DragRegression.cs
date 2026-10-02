// 重放真实拖动入口的屏幕像素轨迹；不打开桌面窗口，不修改用户文件或状态。
using System;
using System.Collections.Generic;
using System.Windows;
using Forms = System.Windows.Forms;

namespace Kage.DesktopFolderPrototype;

internal static class DragRegression
{
    internal static int Run(bool minimal = false)
    {
        Program.Capturing = true;
        var failures = new List<string>();
        var area = Forms.Screen.PrimaryScreen!.WorkingArea;
        if (minimal)
        {
            var folder = NewFolder(area.Left, area.Top + 160);
            var pointer = new Point(folder.State.X + 25, folder.State.Y + 20);
            folder.BeginHeaderDrag(pointer);
            folder.DragHeaderTo(pointer + new Vector(-80, 0));
            folder.DragHeaderTo(pointer + new Vector(-79, 0));
            var moved = folder.State.X - area.Left;
            CloseAll();
            Console.WriteLine($"{(moved == 1 ? "PASS" : "FAIL")} 最小轨迹：在左边向外拖 80 像素，再反向 1 像素，Folder 应移动 1 像素；实际移动 {moved}");
            return moved == 1 ? 0 : 1;
        }
        foreach (var direction in new[] { "左", "右", "上", "下" })
        {
            var folder = NewFolder(area.Left + 140, area.Top + 160);
            var size = DesktopLayout.Bounds(folder).Size;
            var pointer = new Point(folder.State.X + 25, folder.State.Y + 20);
            var origin = new Point(folder.State.X, folder.State.Y);
            folder.BeginHeaderDrag(pointer);
            var horizontal = direction is "左" or "右";
            var positive = direction is "右" or "下";
            var edge = direction switch { "左" => area.Left, "右" => area.Right - size.Width, "上" => area.Top, _ => area.Bottom - size.Height };
            var distance = Math.Abs(edge - (horizontal ? origin.X : origin.Y));
            var sign = positive ? 1 : -1;
            var beyond = distance + 80;
            // 小步接触边界，继续向外拖，再反向一像素。不能积累必须先偿还的鼠标距离。
            for (var step = 4.0; step < beyond; step += 4)
                folder.DragHeaderTo(pointer + new Vector(horizontal ? step * sign : 0, horizontal ? 0 : step * sign));
            var last = pointer + new Vector(horizontal ? beyond * sign : 0, horizontal ? 0 : beyond * sign);
            folder.DragHeaderTo(last);
            var held = horizontal ? folder.State.X : folder.State.Y;
            Check(held == edge, $"屏幕{direction}边：应贴住边界，不停在最后一个合法采样点", failures);
            folder.DragHeaderTo(last + new Vector(horizontal ? -sign : 0, horizontal ? 0 : -sign));
            var released = horizontal ? folder.State.X : folder.State.Y;
            Check(released == held - sign, $"屏幕{direction}边：反向 1 像素应立即跟随，实际从 {held} 到 {released}", failures);
            var before = new Point(folder.State.X, folder.State.Y);
            folder.DragHeaderTo(last + new Vector(horizontal ? sign * 20 : 7, horizontal ? 7 : sign * 20));
            Check(horizontal ? folder.State.Y == before.Y + 7 : folder.State.X == before.X + 7,
                $"屏幕{direction}边：受阻方向之外仍应沿边移动 7 像素", failures);
            CloseAll();
        }
        foreach (var expanded in new[] { false, true })
        foreach (var direction in new[] { "右", "左", "下", "上" })
        {
            var folder = NewFolder(area.Left + 20, area.Top + 20, expanded);
            var blocker = NewFolder(area.Left + 20, area.Top + 20, expanded);
            var bounds = DesktopLayout.Bounds(folder);
            var horizontal = direction is "左" or "右";
            var positive = direction is "右" or "下";
            var low = horizontal ? area.Left + 20 : area.Top + 20;
            var high = horizontal ? area.Right - (int)bounds.Width - 20 : area.Bottom - (int)bounds.Height - 20;
            if (horizontal) { folder.State.X = positive ? low : high; blocker.State.X = positive ? high : low; }
            else { folder.State.Y = positive ? low : high; blocker.State.Y = positive ? high : low; }
            var pointer = new Point(folder.State.X + 25, folder.State.Y + 20);
            var distance = horizontal ? Math.Abs(blocker.State.X - folder.State.X) - bounds.Width - 12 : Math.Abs(blocker.State.Y - folder.State.Y) - bounds.Height - 12;
            var sign = positive ? 1 : -1;
            folder.BeginHeaderDrag(pointer);
            var beyond = distance + 70;
            for (var step = 4.0; step < beyond; step += 4) folder.DragHeaderTo(pointer + new Vector(horizontal ? step * sign : 0, horizontal ? 0 : step * sign));
            var last = pointer + new Vector(horizontal ? beyond * sign : 0, horizontal ? 0 : beyond * sign);
            folder.DragHeaderTo(last);
            var held = horizontal ? folder.State.X : folder.State.Y;
            folder.DragHeaderTo(last + new Vector(horizontal ? -sign : 0, horizontal ? 0 : -sign));
            Check((horizontal ? folder.State.X : folder.State.Y) == held - sign, $"其他 Folder（{(expanded ? "展开" : "折叠")}，向{direction}接触）：反向 1 像素应立即脱离", failures);
            var before = new Point(folder.State.X, folder.State.Y);
            folder.DragHeaderTo(last + new Vector(horizontal ? sign * 20 : 7, horizontal ? 7 : sign * 20));
            Check(horizontal ? folder.State.Y == before.Y + 7 : folder.State.X == before.X + 7, $"其他 Folder（向{direction}接触）：碰撞时仍应沿接触面滑动", failures);
            Check(!DesktopLayout.Bounds(folder).IntersectsWith(DesktopLayout.Bounds(blocker)), "拖动期间不得重叠", failures);
            CloseAll();
        }
        foreach (var failure in failures) Console.WriteLine("FAIL " + failure);
        Console.WriteLine(failures.Count == 0 ? "PASS 拖动回归：屏幕四边贴边／即时反向／沿边滑动，折叠及展开 Folder 的四向碰撞与不重叠" : $"FAIL {failures.Count} 项拖动跟随检查未通过");
        return failures.Count == 0 ? 0 : 1;
    }

    private static FolderWindow NewFolder(int x, int y, bool expanded = false)
    {
        var folder = new FolderWindow(new FolderState { Name = "拖动回归-" + Guid.NewGuid().ToString("N"), X = x, Y = y, Width = 240, Height = 48, PanelHeight = 160, Expanded = expanded, LayoutVersion = 2 });
        Program.Folders.Add(folder); return folder;
    }
    private static void Check(bool passed, string failure, List<string> failures) { if (!passed) failures.Add(failure); }
    private static void CloseAll() { foreach (var folder in Program.Folders) folder.Close(); Program.Folders.Clear(); }
}
