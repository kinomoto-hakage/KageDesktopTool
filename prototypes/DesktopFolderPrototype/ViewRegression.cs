// 检查真实 WPF 布局结果，不打开桌面窗口，不改动用户内容或布局状态。
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Kage.DesktopFolderPrototype;

internal static class ViewRegression
{
    internal static int Run()
    {
        Program.Capturing = true;
        var name = "视图回归-" + Guid.NewGuid().ToString("N");
        var directory = System.IO.Path.Combine(Program.Storage, name);
        Directory.CreateDirectory(directory);
        File.WriteAllText(System.IO.Path.Combine(directory, "短.txt"), "");
        File.WriteAllText(System.IO.Path.Combine(directory, "较长文件名.md"), "");
        var folder = new FolderWindow(new FolderState { Name = name, X = 100, Y = 100, Width = 300, Expanded = true, PanelHeight = 240, LayoutVersion = 2 });
        Program.Folders.Add(folder);
        try
        {
            var root = (FrameworkElement)folder.Content;
            Arrange(folder, root);
            var list = Find<ListBox>(root)!;
            var metrics = NativeViewMetrics.ForScale(folder.Scale);
            Console.WriteLine($"Windows 布局：网格 {metrics.GridWidth:0.##} × {metrics.GridHeight:0.##} DIP，列表行高 {metrics.ListHeight:0.##} DIP，小图标 {metrics.SmallIcon:0.##} DIP");
            var centered = list.Items.Cast<ListBoxItem>().All(item =>
            {
                var image = Find<Image>(item)!;
                var center = image.TranslatePoint(new Point(image.ActualWidth / 2, 0), item).X;
                return Math.Abs(center - item.ActualWidth / 2) < .6;
            });
            Console.WriteLine($"{(centered ? "PASS" : "FAIL")} 网格：每个图标应位于整个文件项的水平中心");
            ((ListBoxItem)list.Items[0]).IsSelected = true; root.UpdateLayout(); Render(root, folder, "网格");
            folder.State.Grid = false; folder.Refresh(); Arrange(folder, root);
            var first = (ListBoxItem)list.Items[0];
            var oldX = Find<Image>(first)!.TranslatePoint(new Point(), root).X;
            Render(root, folder, "列表");
            folder.State.Width = 460; folder.ApplyGeometry(); Arrange(folder, root);
            var newX = Find<Image>(first)!.TranslatePoint(new Point(), root).X;
            var stable = Math.Abs(newX - oldX) < .6;
            Console.WriteLine($"{(stable ? "PASS" : "FAIL")} 列表：向右扩宽后左侧图标位置应固定，实际偏移 {newX - oldX:0.##} DIP");
            Render(root, folder, "列表加宽");
            foreach (var width in new[] { 260.0, 480.0, 300.0 })
            {
                folder.State.Width = width; folder.ApplyGeometry(); Arrange(folder, root);
                stable &= Math.Abs(Find<Image>(first)!.TranslatePoint(new Point(), root).X - oldX) < .6;
            }
            Console.WriteLine($"{(stable ? "PASS" : "FAIL")} 列表：反复缩窄／加宽时左侧位置固定");
            return centered && stable ? 0 : 1;
        }
        finally
        {
            folder.Close(); Program.Folders.Clear();
            // 仅清理本次新建的、位于内容目录中的随机名称夹具。
            var resolved = System.IO.Path.GetFullPath(directory);
            var root = System.IO.Path.GetFullPath(Program.Storage) + System.IO.Path.DirectorySeparatorChar;
            if (resolved.StartsWith(root, StringComparison.OrdinalIgnoreCase)) Directory.Delete(resolved, true);
        }
    }

    private static void Arrange(FolderWindow folder, FrameworkElement root)
    {
        var title = Find<TextBlock>(root);
        if (title != null && title.Text.StartsWith("视图回归-")) title.Text = "示例 Folder";
        root.Measure(new Size(folder.State.Width, folder.TotalHeight)); root.Arrange(new Rect(0, 0, folder.State.Width, folder.TotalHeight)); root.UpdateLayout();
    }

    private static void Render(FrameworkElement root, FolderWindow folder, string name)
    {
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)folder.State.Width, (int)folder.TotalHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var output = File.Create(System.IO.Path.Combine(Program.Home, $"PROTOTYPE-preview-{name}.png")); encoder.Save(output);
    }

    internal static T? Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (Find<T>(VisualTreeHelper.GetChild(root, i)) is T found) return found;
        return null;
    }
}
