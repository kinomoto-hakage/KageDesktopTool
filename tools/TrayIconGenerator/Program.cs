using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// 基于既有四套图形，用矢量重新绘制每个实际像素尺寸，不从大图缩放小图。
internal static class Program
{
    // 100%—500% 自定义缩放可能产生任意 16—80px 度量，逐像素提供原生帧。
    private static readonly int[] Sizes = Enumerable.Range(16, 65).Concat(new[] { 96, 128, 256 }).ToArray();

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("参数：原资源目录、输出资源目录、预览 PNG 路径。");
        Directory.CreateDirectory(args[1]);
        var old = new BitmapSource[4];
        var oldFrames = new BitmapDecoder[4];
        for (var index = 0; index < 4; index++)
        {
            var key = (char)('a' + index);
            old[index] = BitmapFrame.Create(new Uri(Path.GetFullPath(Path.Combine(args[0], $"tray-{key}.png"))), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            Report(old[index], key + " 原图");
            oldFrames[index] = BitmapDecoder.Create(new Uri(Path.GetFullPath(Path.Combine(args[0], $"tray-{key}.ico"))), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frames = Sizes.Select(size => Draw(key, size)).ToArray();
            foreach (var size in new[] { 16, 24, 36, 44, 80, 256 }) Report(frames[Array.IndexOf(Sizes, size)], $"{key} {size}px");
            SavePng(frames[^1], Path.Combine(args[1], $"tray-{key}.png"));
            WriteIco(frames, Path.Combine(args[1], $"tray-{key}.ico"));
        }
        Preview(oldFrames, args[2]);
    }

    private static BitmapSource Draw(char key, int size)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            // 四边至少保留一个实际透明像素，细节在小尺寸仍使用完整像素。
            var margin = Math.Max(1, Math.Round(size / 32d));
            var scale = (size - 2 * margin) / 14d;
            dc.PushTransform(new TranslateTransform(margin, margin));
            dc.PushTransform(new ScaleTransform(scale, scale));
            var blue = Brush("#2867E4"); var teal = Brush("#148C85");
            var purple = Brush("#7954CB"); var yellow = Brush("#FFBB45");
            var white = Brush("#F8FAFF"); var dark = Brush("#263246");
            var edge = new Pen(white, 1) { LineJoin = PenLineJoin.Round };
            if (key == 'b')
            {
                foreach (var x in new[] { .5, 8.5 })
                    foreach (var y in new[] { .5, 8.5 })
                        dc.DrawRoundedRectangle(teal, edge, new Rect(x, y, 5, 5), .8, .8);
            }
            else if (key == 'c')
            {
                foreach (var y in new[] { .5, 5.5, 10.5 })
                {
                    dc.DrawRoundedRectangle(yellow, edge, new Rect(.5, y, 13, 3), .6, .6);
                    dc.DrawRectangle(dark, null, new Rect(5, y + 1, 4, 1));
                }
            }
            else
            {
                var folder = Geometry.Parse("M .5,.5 L 5,.5 7,2.5 13.5,2.5 13.5,13.5 .5,13.5 Z");
                dc.DrawGeometry(key == 'a' ? blue : purple, edge, folder);
                if (key == 'a')
                {
                    dc.DrawRectangle(white, null, new Rect(3, 6, 3, 5));
                    dc.DrawRectangle(white, null, new Rect(8, 6, 3, 5));
                }
                else
                {
                    // 直接绘制 K，避免字体替换和小字抗锯齿导致辨认困难。
                    var k = Geometry.Parse("M 4,5 L 5.5,5 5.5,8 8.5,5 10.5,5 7,8.5 10.5,12 8.5,12 5.5,9 5.5,12 4,12 Z");
                    dc.DrawGeometry(white, null, k);
                }
            }
            dc.Pop(); dc.Pop();
        }
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual); bitmap.Freeze();
        return bitmap;
    }

    private static void Preview(BitmapDecoder[] old, string path)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brush("#F1F4F8"), null, new Rect(0, 0, 1120, 760));
            Text(dc, "Kage · 托盘图标优化", 28, 20, 25, "#18253B");
            Text(dc, "每组左侧为原图，右侧为新图；按实际像素显示。放大镜使用最近邻显示像素边缘。", 28, 60, 14, "#52637B");
            string[] names = ["A 收纳夹", "B 分区格", "C 叠层抽屉", "D K 文件夹（默认）"];
            int[] previewSizes = [16, 20, 24, 28, 32, 40, 48];
            for (var row = 0; row < 4; row++)
            {
                var y = 100 + row * 160;
                Text(dc, names[row], 28, y + 4, 17, "#18253B");
                for (var theme = 0; theme < 2; theme++)
                {
                    var top = y + 34 + theme * 58;
                    dc.DrawRoundedRectangle(Brush(theme == 0 ? "#FDFDFD" : "#202020"), null, new Rect(28, top, 1064, 52), 5, 5);
                    for (var col = 0; col < previewSizes.Length; col++)
                    {
                        var size = previewSizes[col]; var x = 44 + col * 128;
                        Text(dc, size + "px", x, top + 4, 11, theme == 0 ? "#52637B" : "#BDCBDD");
                        var original = old[row].Frames.OrderBy(frame => Math.Abs(frame.PixelWidth - size)).First();
                        dc.DrawImage(original, new Rect(x + 30, top + (52 - size) / 2, size, size));
                        dc.DrawImage(Draw((char)('a' + row), size), new Rect(x + 78, top + (52 - size) / 2, size, size));
                    }
                    var small = Draw((char)('a' + row), 16);
                    var sourcePixels = new byte[16 * 16 * 4]; small.CopyPixels(sourcePixels, 64, 0);
                    var zoomPixels = new byte[48 * 48 * 4];
                    for (var py = 0; py < 48; py++)
                        for (var px = 0; px < 48; px++)
                            Array.Copy(sourcePixels, (py / 3 * 16 + px / 3) * 4, zoomPixels, (py * 48 + px) * 4, 4);
                    var zoomBitmap = BitmapSource.Create(48, 48, 96, 96, PixelFormats.Pbgra32, null, zoomPixels, 48 * 4);
                    dc.DrawImage(zoomBitmap, new Rect(1008, top + 2, 48, 48));
                }
            }
        }
        var preview = new RenderTargetBitmap(1120, 760, 96, 96, PixelFormats.Pbgra32);
        preview.Render(visual);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        SavePng(preview, path);
    }

    private static void WriteIco(BitmapSource[] frames, string path)
    {
        var images = frames.Select(Png).ToArray();
        using var file = File.Create(path);
        using var writer = new BinaryWriter(file);
        writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)images.Length);
        var offset = 6 + 16 * images.Length;
        for (var i = 0; i < images.Length; i++)
        {
            writer.Write((byte)(Sizes[i] == 256 ? 0 : Sizes[i])); writer.Write((byte)(Sizes[i] == 256 ? 0 : Sizes[i]));
            writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)32);
            writer.Write(images[i].Length); writer.Write(offset); offset += images[i].Length;
        }
        foreach (var data in images) writer.Write(data);
    }

    private static void Report(BitmapSource source, string label)
    {
        var bitmap = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        var visible = Enumerable.Range(0, pixels.Length / 4).Where(i => pixels[i * 4 + 3] >= 32).ToArray();
        var width = visible.Max(i => i % bitmap.PixelWidth) - visible.Min(i => i % bitmap.PixelWidth) + 1;
        var height = visible.Max(i => i / bitmap.PixelWidth) - visible.Min(i => i / bitmap.PixelWidth) + 1;
        Console.WriteLine($"{label}：{bitmap.PixelWidth}×{bitmap.PixelHeight}，有效图形 {width}×{height}（{width * 100d / bitmap.PixelWidth:F1}% × {height * 100d / bitmap.PixelHeight:F1}%）");
    }

    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));
    private static void Text(DrawingContext dc, string text, double x, double y, double size, string color)
        => dc.DrawText(new FormattedText(text, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight,
            new Typeface("Microsoft YaHei"), size, Brush(color), 1), new Point(x, y));
    private static byte[] Png(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream(); encoder.Save(stream); return stream.ToArray();
    }
    private static void SavePng(BitmapSource bitmap, string path) => File.WriteAllBytes(path, Png(bitmap));
}
