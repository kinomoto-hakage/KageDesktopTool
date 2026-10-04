using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Kage.Workspace;

namespace Kage.Desktop;

// 只读检查实际快捷方式和系统 Shell；菜单仅取消，不执行用户文件命令。
internal static class ShellDiagnosticsChecks
{
    internal static int Run(bool iconsOnly, bool cancelOnly = false)
    {
        var evidence = Path.Combine(Environment.CurrentDirectory, ".scratch", "desktop-folder", "verification");
        Directory.CreateDirectory(evidence);
        var log = Path.Combine(evidence, cancelOnly ? "shell-menu-cancel-check.txt" : "shell-response-diagnostics.txt");
        File.WriteAllText(log, "右键响应、窗口样式与真实图标诊断\n");
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var exit = 1;
        app.Startup += async (_, _) =>
        {
            try
            {
                var statePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KageDesktopTool");
                var state = new JsonWorkspaceStore(statePath).Read().State;
                var paths = state.Folders.SelectMany(folder => Directory.GetFileSystemEntries(Path.Combine(folder.ContentRoot ?? state.Root, folder.Name)))
                    .Where(path => !Path.GetFileName(path).StartsWith(".kage-folder-id", StringComparison.OrdinalIgnoreCase)).ToArray();
                var scale = WindowsDesktop.Displays().First().Scale;
                var rows = await Task.Run(() => paths.SelectMany(path => new[] { 16, 32, 48, 96 }.Select(size =>
                {
                    var watch = Stopwatch.StartNew();
                    try
                    {
                        var image = ShellIcons.ForFile(path, false, (int)Math.Ceiling(size * scale)) as BitmapSource;
                        if (image != null && (Path.GetFileName(path).Contains("ChatGPT", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(path).Contains("Wallpaper", StringComparison.OrdinalIgnoreCase)))
                        {
                            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                            using var file = File.Create(Path.Combine(evidence, Path.GetFileName(path).Split('.')[0] + "-grid-" + size + ".png")); encoder.Save(file);
                        }
                        return new { path, size, milliseconds = watch.ElapsedMilliseconds, pixels = image?.PixelWidth ?? 0, visible = VisiblePixels(image), error = (string?)null };
                    }
                    catch (Exception e) { return new { path, size, milliseconds = watch.ElapsedMilliseconds, pixels = 0, visible = 0, error = (string?)e.Message }; }
                })).ToArray());
                File.WriteAllText(Path.Combine(evidence, "actual-shell-icons.json"), JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
                foreach (var row in rows.Where(row => row.visible == 0 || row.milliseconds > 250))
                    File.AppendAllText(log, $"图标 {row.size} DIP：{row.path}，可见像素 {row.visible}，{row.milliseconds} ms，{row.error}\n");
                File.AppendAllText(log, $"扫描 {paths.Length} 个实际项目，四档共 {rows.Length} 张；空白 {rows.Count(row => row.visible == 0)}。\n");
                if (iconsOnly) { exit = rows.Any(row => row.visible == 0) ? 1 : 0; return; }
                var target = paths.FirstOrDefault(path => Path.GetFileName(path).Contains("Afterburner", StringComparison.OrdinalIgnoreCase))
                    ?? paths.First(path => path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase));
                if (cancelOnly)
                {
                    var pending = ShellContextMenu.ShowAsync([target]);
                    ShellContextMenu.CancelPending();
                    try { await pending; throw new IOException("准备阶段取消未终止请求。"); }
                    catch (OperationCanceledException)
                    {
                        if (ShellContextMenu.ActiveMenuWindow != IntPtr.Zero || ShellContextMenu.LastMenuLatency != 0)
                            throw new IOException("取消后仍产生系统菜单。");
                        File.AppendAllText(log, "通过：取消准备请求后不再发送右键，不产生系统菜单。\n"); exit = 0; return;
                    }
                }
                var owner = new Window { Width = 1, Height = 1, ShowInTaskbar = false, WindowStyle = WindowStyle.None, Opacity = 0 };
                new WindowInteropHelper(owner).EnsureHandle();
                var elapsed = Stopwatch.StartNew();
                long longestTick = 0, previousTick = 0;
                var helperSeen = false;
                var menuSeen = false;
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
                timer.Tick += (_, _) =>
                {
                    var current = elapsed.ElapsedMilliseconds;
                    longestTick = Math.Max(longestTick, current - previousTick); previousTick = current;
                    foreach (Window window in app.Windows)
                    {
                        if (window == owner || !window.IsVisible) continue;
                        var handle = new WindowInteropHelper(window).Handle;
                        helperSeen |= (GetWindowLong(handle, -16) & 0x00C00000) != 0;
                    }
                    if (ShellContextMenu.OwnerWindow != IntPtr.Zero)
                        helperSeen |= (GetWindowLong(ShellContextMenu.OwnerWindow, -16) & 0x00C00000) != 0;
                    if (ShellContextMenu.ActiveMenuWindow != IntPtr.Zero)
                    { menuSeen = true; ShellContextMenu.CancelPending(); }
                };
                timer.Start();
                for (var sample = 0; sample < 2; sample++)
                {
                    if (sample == 1)
                    {
                        ShellContextMenu.Warm([target], false);
                        await Task.Delay(5500);
                    }
                    menuSeen = false;
                    try { await ShellContextMenu.ShowAsync([target]); }
                    catch (OperationCanceledException) when (menuSeen) { }
                    File.AppendAllText(log, $"第 {sample + 1} 次（{(sample == 0 ? "直接右键" : "提前准备后")}）：菜单准备 {ShellContextMenu.LastMenuLatency} ms；取得对象 {ShellContextMenu.LastObjectLatency} ms；构建命令 {ShellContextMenu.LastBuildLatency} ms；原生窗口 {menuSeen}。\n");
                    if (!menuSeen) break;
                }
                timer.Stop(); owner.Close();
                File.AppendAllText(log, $"实际对象：{target}\n菜单准备 {ShellContextMenu.LastMenuLatency} ms，到关闭 {elapsed.ElapsedMilliseconds} ms；UI 最大停顿 {longestTick} ms；带标题栏辅助窗口 {helperSeen}；系统菜单窗口 {ShellContextMenu.LastMenuClass}。\n");
                exit = !menuSeen || helperSeen || longestTick > 250 ? 1 : 0;
            }
            catch (Exception e) { File.AppendAllText(log, "失败：" + e + "\n"); }
            finally { ShellContextMenu.InvalidateWarm(); app.Shutdown(); }
        };
        app.Run();
        return exit;
    }

    internal static int VisiblePixels(BitmapSource? source)
    {
        if (source == null) return 0;
        var bitmap = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var data = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(data, bitmap.PixelWidth * 4, 0);
        var visible = 0;
        for (var i = 3; i < data.Length; i += 4) if (data[i] != 0) visible++;
        return visible;
    }
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr handle, int index);
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
}
