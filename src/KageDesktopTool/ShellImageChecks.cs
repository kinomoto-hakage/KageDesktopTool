using System;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace Kage.Desktop;

internal static class ShellImageChecks
{
    internal static int Run()
    {
        var fixture = Path.Combine(Path.GetTempPath(), "Kage-shell-images-" + Guid.NewGuid().ToString("N"));
        var evidence = Path.Combine(Environment.CurrentDirectory, ".scratch", "desktop-folder", "verification");
        Directory.CreateDirectory(fixture); Directory.CreateDirectory(evidence);
        var log = Path.Combine(evidence, "shell-image-check.txt");
        File.WriteAllText(log, "快捷方式资源、尺寸和缓存回归\n");
        try
        {
            var url = Path.Combine(fixture, "图标.url");
            void SetIcon(string name) => File.WriteAllText(url, "[InternetShortcut]\nURL=https://example.invalid/\nIconIndex=0\nIconFile=" + Path.Combine(AppContext.BaseDirectory, "Assets", name) + "\n");
            SetIcon("app-d.ico");
            var first = (BitmapSource)ShellIcons.ForFile(url, false, 72)!;
            Check(ShellDiagnosticsChecks.VisiblePixels(first) > first.PixelWidth * first.PixelHeight / 8, "URL 显式资源拥有有效实际图案");
            var cached = ShellIcons.ForFile(url, false, 72);
            Check(ReferenceEquals(first, cached), "未变化的项目复用冻结图像，不重复请求 Shell");
            SetIcon("app-a.ico"); File.SetLastWriteTimeUtc(url, DateTime.UtcNow.AddSeconds(2));
            var updated = (BitmapSource)ShellIcons.ForFile(url, false, 72)!;
            Check(!Fingerprint(first).SequenceEqual(Fingerprint(updated)), "修改实际 URL 资源后缓存自动失效");
            var chinese = Path.Combine(fixture, "中文图标"); Directory.CreateDirectory(chinese);
            var iconPath = Path.Combine(chinese, "真实资源.ico"); File.Copy(Path.Combine(AppContext.BaseDirectory, "Assets", "app-d.ico"), iconPath);
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var ansi = Path.Combine(fixture, "系统编码.url");
            File.WriteAllText(ansi, "[InternetShortcut]\r\nURL=https://example.invalid/\r\nIconFile=" + iconPath + "\r\nIconIndex=0\r\n", Encoding.GetEncoding((int)GetACP()));
            var ansiImage = (BitmapSource)ShellIcons.ForFile(ansi, false, 72)!;
            Check(Fingerprint(ansiImage).SequenceEqual(Fingerprint(first)), "系统 ANSI 编码的中文 IconFile 正确读取，不回退通用图标");
            var ambiguousIcon = Path.Combine(fixture, "猫.ico"); File.Copy(iconPath, ambiguousIcon);
            var ambiguous = Path.Combine(fixture, "合法多编码.url");
            File.WriteAllText(ambiguous, "[InternetShortcut]\r\nURL=https://example.invalid/\r\nIconFile=" + ambiguousIcon + "\r\n", Encoding.GetEncoding((int)GetACP()));
            Check(Fingerprint((BitmapSource)ShellIcons.ForFile(ambiguous, false, 72)!).SequenceEqual(Fingerprint(first)),
                "也可合法解成 UTF-8 的 ANSI 字节优先匹配实际系统编码资源");
            var state = new Kage.Workspace.JsonWorkspaceStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KageDesktopTool")).Read().State;
            var actual = state.Folders.SelectMany(folder => Directory.GetFiles(Path.Combine(folder.ContentRoot ?? state.Root, folder.Name)))
                .Where(path => Path.GetFileName(path).Contains("ChatGPT", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(path).Contains("Wallpaper", StringComparison.OrdinalIgnoreCase)).ToArray();
            Check(actual.Length >= 2, "存在用户报告的实际包应用与 InternetShortcut");
            foreach (var path in actual)
            foreach (var size in new[] { 16, 32, 48, 96 })
            {
                var pixels = (int)Math.Ceiling(size * WindowsDesktop.Displays().First().Scale);
                var image = (BitmapSource)ShellIcons.ForFile(path, false, pixels)!;
                Check(image.PixelWidth >= pixels && ShellDiagnosticsChecks.VisiblePixels(image) > image.PixelWidth * image.PixelHeight / 8,
                    $"{Path.GetFileName(path)} {size} DIP 图案填充有效且像素充足");
                var data = Fingerprint(image);
                var center = (image.PixelHeight / 2 * image.PixelWidth + image.PixelWidth / 2) * 4;
                if (Path.GetFileName(path).Contains("ChatGPT", StringComparison.OrdinalIgnoreCase))
                    Check(data[center + 3] < 64, "OpenAI 结形图案中央透明孔正确，不是通用白纸");
                else
                {
                    var blue = 0;
                    for (var index = 0; index < data.Length; index += 4)
                        if (data[index + 3] > 128 && data[index] > data[index + 2] + 30) blue++;
                    Check(blue > image.PixelWidth * image.PixelHeight / 3, "Wallpaper Engine 蓝色主体图案正确，不是通用白纸");
                }
            }
            return 0;
            void Check(bool valid, string description)
            { File.AppendAllText(log, (valid ? "通过：" : "失败：") + description + "\n"); if (!valid) throw new IOException(description); }
        }
        catch (Exception e) { File.AppendAllText(log, "失败：" + e + "\n"); return 1; }
        finally
        {
            var full = Path.GetFullPath(fixture);
            if (!full.StartsWith(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(full).StartsWith("Kage-shell-images-", StringComparison.Ordinal)) throw new IOException("拒绝清理隔离范围之外的目录。");
            Directory.Delete(full, true);
        }
    }
    private static byte[] Fingerprint(BitmapSource image)
    {
        var bitmap = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var bytes = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(bytes, bitmap.PixelWidth * 4, 0); return bytes;
    }
    [DllImport("kernel32.dll")] private static extern uint GetACP();
}
