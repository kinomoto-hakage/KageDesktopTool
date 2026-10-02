// 四种小尺寸友好的几何图标，分别导出应用版和透明托盘版。
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace Kage.DesktopFolderPrototype;

internal static class IconChoices
{
    private static readonly string[] Names = { "收纳夹", "分区格", "叠层抽屉", "K 文件夹" };
    private static readonly string[] Colors = { "#2868D8", "#188B82", "#293548", "#7954C3" };
    private static System.Drawing.Icon? trayIcon;
    private static Forms.ToolStripMenuItem? choices;
    private static int selected = 3;
    internal static ImageSource? ApplicationImage;
    private static string Assets => Path.Combine(Program.Home, "Assets");

    internal static void AddMenu(Forms.ContextMenuStrip menu)
    {
        choices = new Forms.ToolStripMenuItem("图标方案");
        for (var i = 0; i < Names.Length; i++)
        {
            var index = i;
            var item = new Forms.ToolStripMenuItem($"{(char)('A' + i)} · {Names[i]}");
            item.Click += (_, _) => Use(index); choices.DropDownItems.Add(item);
        }
        menu.Items.Insert(Math.Max(0, menu.Items.Count - 1), choices);
    }

    internal static void Load()
    {
        var settings = Path.Combine(Program.Home, "PROTOTYPE-settings.json");
        if (File.Exists(settings)) selected = Math.Clamp(JsonSerializer.Deserialize<int>(File.ReadAllText(settings)), 0, 3);
        Use(selected);
    }

    private static void Use(int index)
    {
        selected = index;
        var key = ((char)('a' + index)).ToString();
        var path = Path.Combine(Assets, $"tray-{key}.ico");
        if (!File.Exists(path)) return;
        var next = new System.Drawing.Icon(path, 24, 24);
        Program.Tray.Icon = next; trayIcon?.Dispose(); trayIcon = next;
        ApplicationImage = BitmapFrame.Create(new Uri(Path.Combine(Assets, $"app-{key}.png")));
        Program.Controller.Icon = ApplicationImage;
        foreach (var folder in Program.Folders) folder.Icon = ApplicationImage;
        if (choices != null)
            for (var i = 0; i < choices.DropDownItems.Count; i++) ((Forms.ToolStripMenuItem)choices.DropDownItems[i]).Checked = i == index;
        File.WriteAllText(Path.Combine(Program.Home, "PROTOTYPE-settings.json"), JsonSerializer.Serialize(index));
    }

    internal static void Generate()
    {
        Directory.CreateDirectory(Assets);
        for (var i = 0; i < 4; i++)
        {
            var key = ((char)('a' + i)).ToString();
            File.WriteAllBytes(Path.Combine(Assets, $"app-{key}.png"), Png(Paint(i, false, 256)));
            File.WriteAllBytes(Path.Combine(Assets, $"tray-{key}.png"), Png(Paint(i, true, 256)));
            WriteIco(Path.Combine(Assets, $"app-{key}.ico"), i, false);
            WriteIco(Path.Combine(Assets, $"tray-{key}.ico"), i, true);
        }
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brush("#F2F4F8"), null, new Rect(0, 0, 1120, 490));
            Text(dc, "Kage · 应用与托盘图标方案", 36, 28, 25, "#172337");
            Text(dc, "每款提供多尺寸 .ico；托盘菜单可直接切换比较", 36, 66, 13, "#667286");
            for (var i = 0; i < 4; i++)
            {
                var x = 30 + i * 274;
                dc.DrawRoundedRectangle(Brushes.White, null, new Rect(x, 109, 256, 348), 16, 16);
                dc.DrawImage(Paint(i, false, 256), new Rect(x + 72, 136, 112, 112));
                Text(dc, $"{(char)('A' + i)}  {Names[i]}", x + 30, 267, 18, "#172337");
                Text(dc, "应用图标", x + 30, 302, 12, "#667286");
                dc.DrawRoundedRectangle(Brush("#263142"), null, new Rect(x + 22, 334, 212, 45), 8, 8);
                for (var j = 0; j < 3; j++) dc.DrawImage(Paint(i, true, 32), new Rect(x + 39 + j * 65, 345 + (32 - (16 + j * 8)) / 2, 16 + j * 8, 16 + j * 8));
                dc.DrawRoundedRectangle(Brush("#E9EDF3"), null, new Rect(x + 22, 388, 212, 45), 8, 8);
                for (var j = 0; j < 3; j++) dc.DrawImage(Paint(i, true, 32), new Rect(x + 39 + j * 65, 399 + (32 - (16 + j * 8)) / 2, 16 + j * 8, 16 + j * 8));
            }
        }
        var sheet = new RenderTargetBitmap(1120, 490, 96, 96, PixelFormats.Pbgra32); sheet.Render(visual);
        File.WriteAllBytes(Path.Combine(Assets, "图标方案.png"), Png(sheet));
    }

    private static BitmapSource Paint(int index, bool tray, int size)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(size / 256.0, size / 256.0));
            if (!tray) dc.DrawRoundedRectangle(Brush(Colors[index]), null, new Rect(6, 6, 244, 244), 54, 54);
            var foreground = tray ? Brush(Colors[index == 2 ? 0 : index]) : Brushes.White;
            var border = tray ? new Pen(Brushes.White, 10) : null;
            if (index is 0 or 3)
            {
                var geometry = Geometry.Parse("M 42,77 L 101,77 L 120,96 L 214,96 L 214,192 Q 214,203 202,203 L 54,203 Q 42,203 42,191 Z");
                dc.DrawGeometry(foreground, border, geometry);
                if (index == 0)
                {
                    var paper = tray ? Brushes.White : Brush("#2868D8");
                    dc.DrawRoundedRectangle(paper, null, new Rect(75, 130, 42, 46), 6, 6);
                    dc.DrawRoundedRectangle(paper, null, new Rect(135, 130, 42, 46), 6, 6);
                }
                else Text(dc, "K", 101, 111, 69, tray ? "#FFFFFF" : Colors[index], true);
            }
            else if (index == 1)
            {
                foreach (var point in new[] { new Point(47, 47), new Point(142, 47), new Point(47, 142), new Point(142, 142) })
                    dc.DrawRoundedRectangle(foreground, border, new Rect(point.X, point.Y, 67, 67), 13, 13);
            }
            else
            {
                for (var row = 0; row < 3; row++)
                {
                    dc.DrawRoundedRectangle(tray ? Brush("#F7B84A") : Brush(row == 0 ? "#F7B84A" : "#FFFFFF"), border, new Rect(47, 45 + row * 61, 162, 44), 9, 9);
                    dc.DrawRoundedRectangle(tray ? Brush("#293548") : Brush(Colors[index]), null, new Rect(108, 58 + row * 61, 40, 8), 3, 3);
                }
            }
            dc.Pop();
        }
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }

    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));
    private static void Text(DrawingContext dc, string text, double x, double y, double size, string color, bool bold = false)
    {
        var face = new Typeface(new FontFamily("Microsoft YaHei UI"), FontStyles.Normal, bold ? FontWeights.Bold : FontWeights.Medium, FontStretches.Normal);
        dc.DrawText(new FormattedText(text, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight, face, size, Brush(color), 1), new Point(x, y));
    }

    private static byte[] Png(BitmapSource bitmap)
    { var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = new MemoryStream(); encoder.Save(stream); return stream.ToArray(); }

    private static void WriteIco(string path, int index, bool tray)
    {
        var sizes = new[] { 16, 20, 24, 32, 48, 64, 128, 256 };
        var images = sizes.Select(size => Png(Paint(index, tray, size))).ToArray();
        using var output = new BinaryWriter(File.Create(path));
        output.Write((ushort)0); output.Write((ushort)1); output.Write((ushort)sizes.Length);
        var offset = 6 + 16 * sizes.Length;
        for (var i = 0; i < sizes.Length; i++)
        {
            output.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); output.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
            output.Write((byte)0); output.Write((byte)0); output.Write((ushort)1); output.Write((ushort)32);
            output.Write(images[i].Length); output.Write(offset); offset += images[i].Length;
        }
        foreach (var bytes in images) output.Write(bytes);
    }
}
