using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kage.Workspace;
using Forms = System.Windows.Forms;

namespace Kage.Desktop;

// 真实外观窗口、调色盘和托盘使用隔离工作区，不读写用户的正式设置。
internal static class AppearanceChecks
{
    internal static int Run()
    {
        var fixture = Path.Combine(Path.GetTempPath(), "Kage-appearance-" + Guid.NewGuid().ToString("N"));
        var evidence = Path.Combine(Environment.CurrentDirectory, ".scratch", "desktop-folder", "verification");
        Directory.CreateDirectory(fixture);
        Directory.CreateDirectory(evidence);
        var log = Path.Combine(evidence, "appearance-session.txt");
        File.WriteAllText(log, "06 外观、图标及调色盘 Windows 会话检查\n");
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Runtime? runtime = null;
        var result = 1;
        app.Startup += async (_, _) =>
        {
            try
            {
                using (var builtIn = System.Drawing.Icon.ExtractAssociatedIcon(Path.Combine(AppContext.BaseDirectory, "KageDesktopTool.exe")))
                using (var expected = new System.Drawing.Icon(Path.Combine(AppContext.BaseDirectory, "Assets", "app-d.ico"), builtIn!.Size))
                using (var actualPixels = builtIn.ToBitmap())
                using (var expectedPixels = expected.ToBitmap())
                    Check(Enumerable.Range(0, actualPixels.Width).All(x => Enumerable.Range(0, actualPixels.Height)
                        .All(y => actualPixels.GetPixel(x, y) == expectedPixels.GetPixel(x, y))), "发布 EXE 内置图标与已选 D 资源一致");
                var store = new JsonWorkspaceStore(Path.Combine(fixture, "状态"));
                IDesktopWorkspace workspace = new DesktopWorkspace(store, new NoStartup());
                var displays = WindowsDesktop.Displays();
                Check((await workspace.InitializeAsync(displays)).Succeeded, "隔离工作区初始化");
                await workspace.SelectRootAsync(Path.Combine(fixture, "内容"));
                await workspace.CreateFolderAsync("外观预览");
                await workspace.CreateFolderAsync("独立外观");
                var first = workspace.Snapshot.Folders.First();
                File.WriteAllText(Path.Combine(first.ActualPath, "Windows 原生文件图标.txt"), "保留实际内容");
                await workspace.ToggleFolderAsync(first.Folder.Id);
                runtime = new Runtime(workspace);
                var header = runtime.Headers[first.Folder.Id];
                Check(header.Host != IntPtr.Zero && WindowsDesktop.IsWindowVisible(header.Handle), "真实桌面透明窗口可见");
                await header.Contents.IconsLoaded.WaitAsync(TimeSpan.FromSeconds(15));
                WindowsDesktop.GetWindowRect(header.Handle, out var originalBounds);
                var original = header.Record;
                var dialog = runtime.CreateAppearance(header.FolderId)!;
                dialog.Show();
                dialog.Activate();
                await Task.Delay(250);
                Check(dialog.Hex.IsKeyboardFocused, "外观窗口 HEX 输入取得焦点");
                dialog.Hex.Text = "#1A2B3C";
                Check(dialog.Rgb.Select(field => field.Text).SequenceEqual(new[] { "26", "43", "60" }), "真实 HEX 输入同步 RGB");
                dialog.Rgb[0].Text = "255";
                Check(dialog.Hex.Text == "#FF2B3C", "真实 RGB 输入同步 HEX");
                dialog.Hex.Text = "#GG0000";
                Check(!dialog.ApplyButton.IsEnabled && dialog.Error.Text.Length != 0, "无效颜色清楚提示并禁用应用");
                dialog.SelectColor("#4679AB");
                Check(dialog.ApplyButton.IsEnabled && dialog.Rgb[0].Text == "70", "调色盘颜色同步并清除输入错误");
                dialog.Transparency.Value = 100;
                Check(((SolidColorBrush)header.Surface.Background).Color.A == 0, "滑块即时完全透明背景");
                Check(header.Opacity == 1 && header.Surface.Opacity == 1 && header.Contents.Opacity == 1, "透明度只作用于背景画刷");
                header.UpdateLayout();
                var item = (ListBoxItem)header.Contents.Items.Items[0];
                var content = (Grid)item.Content;
                var image = (Image)content.Children[0];
                var text = (TextBlock)content.Children[1];
                Check(image.Source != null && image.Opacity == 1 && text.Opacity == 1
                    && ((SolidColorBrush)text.Foreground).Color.A == 255, "Windows 图标及文件名保持不透明");
                Render(header.Surface, "06-完全透明背景.png");
                dialog.Transparency.Value = 75;
                Check(((SolidColorBrush)header.Surface.Background).Color.A == 64, "75% 透明度实时改变背景");
                await workspace.RefreshAsync(displays);
                runtime.Render();
                Check(((SolidColorBrush)header.Surface.Background).Color.A == 64, "后台刷新保留尚未应用的实时预览");
                WindowsDesktop.GetWindowRect(header.Handle, out var previewBounds);
                Check(originalBounds.Equals(previewBounds) && header.Record == original, "预览不改变实际 HWND 边界及内容布局记录");
                Render(header.Surface, "06-半透明预览.png");
                Render(dialog, "06-外观设置.png");

                var owner = new WindowInteropHelper(dialog).Handle;
                var nativeFocused = false;
                var owned = false;
                var ticks = 0;
                using (var timer = new Forms.Timer { Interval = 400 })
                {
                    timer.Tick += (_, _) =>
                    {
                        var popup = GetWindow(owner, 6);
                        if (popup == IntPtr.Zero || popup == owner) return;
                        owned = GetWindow(popup, 4) == owner && !IsWindowEnabled(owner);
                        nativeFocused = GetForegroundWindow() == popup;
                        if ((!owned || !nativeFocused) && ++ticks < 10) return;
                        File.AppendAllText(log, $"调色盘 HWND={popup}，owner={owner}，实际 owner={GetWindow(popup, 4)}，前台={GetForegroundWindow()}\n");
                        timer.Stop();
                        PostMessage(popup, 0x111, new IntPtr(2), IntPtr.Zero);
                    };
                    timer.Start();
                    dialog.ChooseColor();
                    timer.Stop();
                }
                Check(owned && nativeFocused, "原生调色盘正确拥有 owner 并取得前台焦点");
                Check(dialog.Interaction.Color == "#4679AB", "取消原生调色盘保留草稿颜色");
                dialog.Close();
                Check(((SolidColorBrush)header.Surface.Background).Color.A == 173 && header.Record == original, "关闭外观窗口恢复打开前外观");

                dialog = runtime.CreateAppearance(header.FolderId)!;
                dialog.Show();
                dialog.Hex.Text = "#123456";
                dialog.Transparency.Value = 75;
                await dialog.ApplyAsync();
                Check(!dialog.IsVisible && header.Record.Color == "#123456" && header.Record.Opacity == .25, "真实应用入口保存外观并关闭窗口");
                runtime.ShowSettings();
                foreach (var option in IconChoices.Options)
                {
                    var selection = await runtime.SelectIconAsync(option.Key);
                    Check(workspace.Snapshot.IconChoice == option.Key && runtime.Tray.Icon != null, "托盘候选可切换 " + option.Name + "：" + selection.Message);
                    Check(app.Windows.Cast<Window>().All(window => ReferenceEquals(window.Icon, Runtime.ApplicationIcon)), "所有已打开窗口采用所选图标");
                    var icons = (Forms.ToolStripMenuItem)runtime.Tray.ContextMenuStrip!.Items[2];
                    Check(icons.DropDownItems.Cast<Forms.ToolStripMenuItem>().Single(menu => menu.Checked).Text == option.Name, "托盘选中标记正确");
                }
                await runtime.SelectIconAsync("b");
                runtime.Dispose();
                workspace = new DesktopWorkspace(store, new NoStartup());
                await workspace.InitializeAsync(displays);
                runtime = new Runtime(workspace);
                Check(workspace.Snapshot.IconChoice == "b" && ReferenceEquals(runtime.Controller.Icon, Runtime.ApplicationIcon), "重启加载保存的 B 图标及窗口图标");
                Check(runtime.Headers[first.Folder.Id].Record.Color == "#123456"
                    && runtime.Headers.Values.Single(h => h.FolderId != first.Folder.Id).Record.Color == "#666666", "真实重启恢复各 Folder 独立外观");
                Check(File.ReadAllText(Path.Combine(first.ActualPath, "Windows 原生文件图标.txt")) == "保留实际内容", "内容字节保留");
                result = 0;
                File.AppendAllText(log, "全部检查通过。\n");
            }
            catch (Exception e) { File.AppendAllText(log, "失败：" + e + "\n"); }
            finally
            {
                runtime?.Dispose();
                var full = Path.GetFullPath(fixture);
                if (full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
                    && Path.GetFileName(full).StartsWith("Kage-appearance-", StringComparison.Ordinal)) Directory.Delete(full, true);
                app.Shutdown();
            }
        };
        app.Run();
        return result;

        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            File.AppendAllText(log, "通过：" + message + "\n");
        }

        void Render(FrameworkElement visual, string name)
        {
            visual.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth), (int)Math.Ceiling(visual.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(evidence, name));
            png.Save(file);
        }

    }

    private sealed class NoStartup : IStartupRegistration
    {
        public string LaunchCommand => "隔离检查，不注册自启";
        public string? ReadCommand() => null;
        public void WriteCommand(string? command) => throw new InvalidOperationException("外观检查不得修改自启。");
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr window);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wp, IntPtr lp);
}
