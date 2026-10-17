using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Interop;
using Kage.Workspace;

namespace Kage.Desktop;

// 仅供检查入口使用：真实命中、物理像素和成对恢复共用同一套边界。
internal sealed class DesktopCheckSession : IDisposable
{
    private readonly Stack<Func<Task>> restore = new();
    private bool desktopShown;
    internal string DirectoryPath { get; }

    internal DesktopCheckSession(string name)
    {
        if (name.Length == 0 || name.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
            throw new ArgumentException("隔离检查名称只能使用字母、数字和连字符。", nameof(name));
        DirectoryPath = Path.Combine(Path.GetTempPath(), "Kage-" + name + "-" + Guid.NewGuid().ToString("N"));
        if (!WindowsDesktop.GetCursorPos(out var cursor)) throw new InvalidOperationException("无法保存鼠标原位置。");
        Directory.CreateDirectory(DirectoryPath);
        Defer(() => { if (!SetCursorPos(cursor.X, cursor.Y)) throw new InvalidOperationException("恢复鼠标位置失败。"); });
        Defer(() => { if (desktopShown) FileMoveChecks.DesktopVisibility(false); });
    }

    internal static string EvidenceFor(string feature)
    {
        var configured = Environment.GetEnvironmentVariable("KAGE_CHECK_EVIDENCE_DIRECTORY");
        if (configured != null && !Path.IsPathFullyQualified(configured)) throw new IOException("检查证据目录必须是绝对路径。");
        var directory = configured ?? Path.Combine(Environment.CurrentDirectory, ".scratch", feature, "verification");
        Directory.CreateDirectory(directory);
        return directory;
    }

    internal void Defer(Action action) => restore.Push(() => { action(); return Task.CompletedTask; });
    internal void Defer(Func<Task> action) => restore.Push(action);

    internal void ShowDesktop()
    {
        desktopShown = true;
        FileMoveChecks.DesktopVisibility(true);
    }

    internal async Task RestoreAsync()
    {
        var errors = new List<Exception>();
        while (restore.TryPop(out var action))
        {
            try { await action(); }
            catch (Exception error) { errors.Add(error); }
        }
        if (errors.Count != 0) throw new AggregateException("检查恢复失败，已继续尝试其余恢复步骤。", errors);
    }

    internal static async Task WaitAsync(Func<bool> condition, string message, int attempts = 100, int delay = 100)
    {
        for (var i = 0; i < attempts && !condition(); i++) await Task.Delay(delay);
        if (!condition()) throw new InvalidOperationException(message);
    }

    internal static bool OwnsPoint(FolderHeader header, Point point) => header.OwnsInputWindow(
        WindowsDesktop.WindowFromPoint(new WindowsDesktop.POINT { X = (int)point.X, Y = (int)point.Y }));

    internal static async Task<Point> InputPointAsync(Runtime runtime, FolderHeader header, FrameworkElement element, Point offset)
    {
        var stable = 0; Point? previous = null; var point = new Point();
        for (var attempt = 0; attempt < 60 && stable < 3; attempt++)
        {
            await element.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            element.UpdateLayout(); point = element.PointToScreen(offset);
            var ready = !runtime.Interacting && !runtime.ContentInputActive && OwnsPoint(header, point);
            stable = ready && previous == point ? stable + 1 : 0;
            previous = point; await Task.Delay(50);
        }
        if (stable < 3) throw new InvalidOperationException("真实输入目标未稳定，或仍有活动手势／窗口遮挡。");
        return point;
    }

    internal static async Task CaptureAsync(string path, Func<IEnumerable<FolderHeader>> headers, Action expose)
    {
        bool Visible() => headers().Any() && headers().All(h =>
        {
            if (OwnsPoint(h, h.HeaderInput.PointToScreen(new Point(20, 20)))) return true;
            // 系统暂停会撤掉背景 HWND；完全透明着色的空白像素应穿透。
            // 改查实际名称的清晰前景像素，不能把正确的穿透当成窗口遮挡。
            var name = h.NameDisplay;
            for (var y = 2.0; y < name.ActualHeight; y += 3)
            for (var x = 2.0; x < name.ActualWidth; x += 3)
                if (OwnsPoint(h, name.PointToScreen(new Point(x, y)))) return true;
            return false;
        });
        var stable = 0;
        for (var attempt = 0; attempt < 30 && stable < 3; attempt++)
        {
            await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            if (Visible()) stable++;
            else { stable = 0; expose(); }
            await Task.Delay(200);
        }
        if (stable < 3 || !Visible()) throw new InvalidOperationException("截图前 Folder 实际前景未稳定或被遮挡：" + path);
        SaveScreenshot(path);
    }

    internal static async Task<Point> InputPointAsync(Runtime runtime, Window window, FrameworkElement element, Point offset)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var stable = 0; Point? previous = null; var point = new Point();
        for (var attempt = 0; attempt < 60 && stable < 3; attempt++)
        {
            await element.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            element.BringIntoView(); element.UpdateLayout(); point = element.PointToScreen(offset);
            var hit = WindowsDesktop.WindowFromPoint(new WindowsDesktop.POINT { X = (int)point.X, Y = (int)point.Y });
            var ready = !runtime.Interacting && !runtime.ContentInputActive && GetAncestor(hit, 2) == handle;
            stable = ready && previous == point ? stable + 1 : 0;
            previous = point; await Task.Delay(50);
        }
        if (stable < 3) throw new InvalidOperationException("样式窗口真实输入目标未稳定或被遮挡。");
        return point;
    }

    // 已运行的正式实例有自己的桌面子窗口；只调整检查记录，避免暂停效果时被其遮挡。
    internal static FolderRecord[] PlaceRecords(IEnumerable<FolderRecord> records, DisplayArea area, Rect reserved)
    {
        var occupied = new List<Rect> { reserved };
        bool Collect(IntPtr window, IntPtr parameter)
        {
            var title = new StringBuilder(256); GetWindowText(window, title, title.Capacity);
            if (title.ToString().StartsWith("Kage · ", StringComparison.Ordinal) && WindowsDesktop.IsWindowVisible(window)
                && WindowsDesktop.GetWindowRect(window, out var bounds) && bounds.Right > bounds.Left && bounds.Bottom > bounds.Top)
                occupied.Add(new Rect(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top));
            return true;
        }
        EnumWindows((window, parameter) => { Collect(window, parameter); EnumChildWindows(window, Collect, IntPtr.Zero); return true; }, IntPtr.Zero);
        var placed = new List<FolderRecord>();
        foreach (var record in records)
        {
            var origin = FolderGeometry.For(record with { X = 0, Y = 0 }, area.Scale);
            FolderRecord? chosen = null;
            for (var y = area.Y + 80; y + origin.Height <= area.Y + area.Height && chosen == null; y += 20)
                for (var x = area.X + 40; x + origin.Width <= area.X + area.Width; x += 20)
                {
                    var candidate = new Rect(x, y, origin.Width, origin.Height);
                    if (occupied.Any(r => r.IntersectsWith(candidate))) continue;
                    chosen = record with { X = x - origin.X, Y = y - origin.Y };
                    occupied.Add(new Rect(x - 10, y - 10, origin.Width + 20, origin.Height + 20));
                    break;
                }
            if (chosen == null) throw new InvalidOperationException("桌面没有足够空白区域放置组合夹具，请先折叠部分 Folder。");
            placed.Add(chosen);
        }
        return placed.ToArray();
    }

    internal static void SaveScreenshot(string path)
    {
        var screen = System.Windows.Forms.SystemInformation.VirtualScreen;
        using var bitmap = new System.Drawing.Bitmap(screen.Width, screen.Height);
        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(screen.Left, screen.Top, 0, 0, screen.Size); bitmap.Save(path);
    }

    internal static System.Drawing.Color Pixel(Point point)
    {
        using var bitmap = new System.Drawing.Bitmap(1, 1);
        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
        graphics.CopyFromScreen((int)point.X, (int)point.Y, 0, 0, bitmap.Size); return bitmap.GetPixel(0, 0);
    }

    internal static bool FollowsBlueToGreen(IReadOnlyList<System.Drawing.Color> blue, IReadOnlyList<System.Drawing.Color> green) =>
        blue.Count != 0 && blue.Count == green.Count && blue.Zip(green, (b, g) => g.G > b.G + 15 && g.B < b.B - 15).All(v => v);

    public void Dispose()
    {
        try
        {
            if (restore.Count != 0) RestoreAsync().GetAwaiter().GetResult();
        }
        finally
        {
            var full = Path.GetFullPath(DirectoryPath);
            var temporary = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(temporary, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("Kage-", StringComparison.Ordinal))
                throw new IOException("拒绝清理隔离范围之外的目录。");
            Directory.Delete(full, true);
        }
    }

    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    private delegate bool EnumProc(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumProc callback, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int count);
}
