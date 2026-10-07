using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Kage.Desktop;

// 从实际输出／发布目录读取每个 ICO 帧，检查可解码性、有效面积和安全边距。
internal static class TrayIconChecks
{
    internal static int Run()
    {
        var evidence = Path.Combine(Environment.CurrentDirectory, ".scratch", "desktop-folder", "verification");
        Directory.CreateDirectory(evidence);
        var log = Path.Combine(evidence, "tray-icon-resources.txt");
        File.WriteAllText(log, "04 托盘图标包内资源检查\n");
        try
        {
            _ = new Application();
            foreach (var choice in IconChoices.Options)
            {
                _ = IconChoices.Image(choice.Key);
                using var appIcon = new System.Drawing.Icon(Path.Combine(AppContext.BaseDirectory, "Assets", $"app-{choice.Key}.ico"));
                using var file = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Assets", $"tray-{choice.Key}.ico"));
                using var reader = new BinaryReader(file);
                Require(reader.ReadUInt16() == 0 && reader.ReadUInt16() == 1, "ICO 文件头");
                var count = reader.ReadUInt16();
                var frames = Enumerable.Range(0, count).Select(_ =>
                {
                    var width = reader.ReadByte();
                    var height = reader.ReadByte();
                    reader.ReadUInt16(); reader.ReadUInt16();
                    Require(reader.ReadUInt16() == 32, "ICO 应为 32 位透明资源");
                    return (Size: width == 0 ? 256 : width, Height: height == 0 ? 256 : height,
                        Length: reader.ReadInt32(), Offset: reader.ReadInt32());
                }).ToArray();
                Require(frames.Select(frame => frame.Size).Order().SequenceEqual(Enumerable.Range(16, 65).Concat(new[] { 96, 128, 256 })),
                    choice.Name + " 包含完整且无重复的分辨率帧");
                foreach (var frame in frames)
                {
                    Require(frame.Height == frame.Size && frame.Length > 0 && frame.Offset >= 6 + count * 16
                        && (long)frame.Offset + frame.Length <= file.Length, "ICO 帧边界");
                    file.Position = frame.Offset;
                    using var png = new MemoryStream(reader.ReadBytes(frame.Length));
                    var decoded = new PngBitmapDecoder(png, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                    Require(decoded.PixelWidth == frame.Size && decoded.PixelHeight == frame.Size, "帧目录与实际像素一致");
                    var bitmap = new FormatConvertedBitmap(decoded, PixelFormats.Bgra32, null, 0);
                    var pixels = new byte[frame.Size * frame.Size * 4];
                    bitmap.CopyPixels(pixels, frame.Size * 4, 0);
                    var visible = Enumerable.Range(0, frame.Size * frame.Size).Where(i => pixels[i * 4 + 3] >= 32).ToArray();
                    Require(visible.Length > 0, "图形非空");
                    var left = visible.Min(i => i % frame.Size); var right = visible.Max(i => i % frame.Size);
                    var top = visible.Min(i => i / frame.Size); var bottom = visible.Max(i => i / frame.Size);
                    Require(right - left + 1 >= frame.Size * .80 && bottom - top + 1 >= frame.Size * .75, "有效图形至少占宽度 80%、高度 75%");
                    Require(Enumerable.Range(0, frame.Size).All(i => pixels[i * 4 + 3] == 0
                        && pixels[((frame.Size - 1) * frame.Size + i) * 4 + 3] == 0
                        && pixels[(i * frame.Size) * 4 + 3] == 0 && pixels[(i * frame.Size + frame.Size - 1) * 4 + 3] == 0), "外缘透明，轮廓完整且不裁切");
                    // System.Drawing.Icon 的帧选择会把 256px（目录宽高为 0）回退为 128px，
                    // 这里直接核对 Windows 的文件图标加载能力，独立于运行时的小尺寸选择。
                    var native = LoadImage(IntPtr.Zero, Path.Combine(AppContext.BaseDirectory, "Assets", $"tray-{choice.Key}.ico"), 1, frame.Size, frame.Size, 0x10);
                    Require(native != IntPtr.Zero, "Windows 原生图标加载成功");
                    try
                    {
                        using var selected = System.Drawing.Icon.FromHandle(native);
                        Require(selected.Width == frame.Size && selected.Height == frame.Size, $"Windows 可读取对应分辨率：请求 {frame.Size}，实际 {selected.Size}");
                    }
                    finally { DestroyIcon(native); }
                    File.AppendAllText(log, $"{choice.Key.ToUpperInvariant()} {frame.Size}px：有效范围 {left},{top}—{right},{bottom}，Windows 读取通过。\n");
                }
            }
            using (var builtIn = System.Drawing.Icon.ExtractAssociatedIcon(Path.Combine(AppContext.BaseDirectory, "KageDesktopTool.exe")))
            using (var expected = new System.Drawing.Icon(Path.Combine(AppContext.BaseDirectory, "Assets", "app-d.ico"), builtIn!.Size))
            using (var actualPixels = builtIn.ToBitmap())
            using (var expectedPixels = expected.ToBitmap())
                Require(Enumerable.Range(0, actualPixels.Width).All(x => Enumerable.Range(0, actualPixels.Height)
                    .All(y => actualPixels.GetPixel(x, y) == expectedPixels.GetPixel(x, y))), "EXE 内置图标与应用 D 资源一致");
            File.AppendAllText(log, "全部资源检查通过。\n");
            return 0;
        }
        catch (Exception e) { File.AppendAllText(log, "失败：" + e + "\n"); return 1; }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr LoadImage(IntPtr instance, string name, uint type, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
}
