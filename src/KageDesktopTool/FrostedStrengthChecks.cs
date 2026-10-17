using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using Kage.Workspace;

namespace Kage.Desktop;

internal static class FrostedStrengthChecks
{
    internal static int Run()
    {
        using var session = new DesktopCheckSession("frosted-strength");
        var evidence = DesktopCheckSession.EvidenceFor("folder-frosted-strength");
        var log = Path.Combine(evidence, "01-正式强度.txt"); File.WriteAllText(log, "真实强度滑块与实时桌面背景\n");
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Runtime? runtime = null; Window? background = null; var exit = 1;
        session.Defer(() => background?.Close()); session.Defer(() => runtime?.Dispose());
        session.Defer(() => mouse_event(4, 0, 0, 0, UIntPtr.Zero));
        app.Startup += async (_, _) =>
        {
            try
            {
                var displays = WindowsDesktop.Displays(); var area = displays.OrderByDescending(a => a.Width * a.Height).First();
                File.AppendAllText(log, $"环境 {Environment.OSVersion}；{string.Join("; ", displays.Select(a => a.ToString()))}\n");
                session.ShowDesktop();
                var root = Path.Combine(session.DirectoryPath, "内容"); Directory.CreateDirectory(root);
                var records = DesktopCheckSession.PlaceRecords([
                    new FolderRecord(Guid.NewGuid(), "实时强度检查", HeaderWidth: 440, BodyHeight: 440, Expanded: true, Opacity: 0),
                    new FolderRecord(Guid.NewGuid(), "独立样式检查", Opacity: .4, Frosted: true, FrostedStrength: 25)
                ], area, new Rect(area.X, area.Y, 790, area.Height));
                foreach (var record in records)
                {
                    var path = Path.Combine(root, record.Name); Directory.CreateDirectory(path);
                    File.WriteAllText(Path.Combine(path, ".kage-folder-id"), record.Id.ToString("N"));
                }
                var file = Path.Combine(root, records[0].Name, "清晰内容.bin"); File.WriteAllBytes(file, [0, 42, 255]);
                var disk = new JsonWorkspaceStore(Path.Combine(session.DirectoryPath, "状态")); disk.Save(new WorkspaceState { Root = root, Folders = records });
                var store = new FaultStore(disk); IDesktopWorkspace workspace = new DesktopWorkspace(store, new NoStartup());
                Require((await workspace.InitializeAsync(displays)).Succeeded, "隔离真实工作区初始化");
                var id = records[0].Id; var bounds = FolderGeometry.For(workspace.Snapshot.Folders.First().Folder, area.Scale);
                var canvas = new Canvas { Background = Brushes.DarkBlue };
                var white = new Border { Background = Brushes.White, Width = (bounds.X + bounds.Width / 2.0 - area.X) / area.Scale,
                    Height = area.Height / area.Scale };
                canvas.Children.Add(white);
                background = new Window { WindowStyle = WindowStyle.None, ShowActivated = false, ShowInTaskbar = false,
                    Content = canvas, Width = area.Width / area.Scale, Height = area.Height / area.Scale };
                background.Show(); var backgroundHandle = new WindowInteropHelper(background).Handle;
                var host = WindowsDesktop.Attach(backgroundHandle); WindowsDesktop.Position(backgroundHandle, host, area.X, area.Y, area.Width, area.Height);
                runtime = new Runtime(workspace); var header = runtime.Headers[id];
                var original = workspace.Snapshot.Folders.First().Folder;
                var statePath = Path.Combine(session.DirectoryPath, "状态", "workspace.json"); var originalJson = File.ReadAllBytes(statePath);
                var dialog = ShowStyle();
                Require(!dialog.FrostedStrength.IsEnabled && dialog.FrostedStrength.Value == 50 && dialog.StrengthValue.Text == "50%",
                    "正式窗口默认强度 50，关闭开关时滑块可见且置灰");
                await Click(dialog.Frosted); await DesktopCheckSession.WaitAsync(() => header.FrostedVisible, "实时 GPU 背景未恢复");
                Require(dialog.FrostedStrength.IsEnabled, "勾选后强度可以编辑");
                var otherHeader = runtime.Headers[records[1].Id];
                await DesktopCheckSession.WaitAsync(() => otherHeader.FrostedVisible, "第二个 Folder 背景未恢复");
                var deviation = new double[5]; var values = new[] { 0, 25, 50, 75, 100 };
                for (var i = 0; i < values.Length; i++)
                {
                    var value = values[i]; await DragSlider(dialog.FrostedStrength, value);
                    Require(dialog.FrostedStrength.Value == value && dialog.StrengthValue.Text == value + "%"
                        && header.Record.FrostedStrength == value && header.Record.Frosted,
                        $"真实鼠标强度 {value}% 同步只读数值与有效预览，零值保留开启意图");
                    Require(workspace.Snapshot.Folders.First().Folder == original && File.ReadAllBytes(statePath).SequenceEqual(originalJson),
                        "鼠标预览不提前写正式记录或 JSON");
                    Require(otherHeader.Record.FrostedStrength == 25 && otherHeader.FrostedVisible, "另一已启用 Folder 保持独立 25% 强度和可见效果");
                    await workspace.RefreshAsync(displays); runtime.Render(); await Task.Delay(400);
                    Require(header.Record.FrostedStrength == value, "后台刷新保持当前有效强度草稿");
                    await DesktopCheckSession.CaptureAsync(Path.Combine(evidence, $"01-强度-{value}-蓝.png"), () => [header], () => dialog.Activate());
                    deviation[i] = Deviation(bounds.X + 30, bounds.Y + (int)(bounds.Height * .7), bounds.Width - 60);
                    var point = new Point(bounds.X + bounds.Width - 45, bounds.Y + bounds.Height * .7);
                    var blue = DesktopCheckSession.Pixel(point); canvas.Background = Brushes.DarkGreen; await Task.Delay(450);
                    var green = DesktopCheckSession.Pixel(point);
                    Require(green.G > blue.G + 15 && green.B < blue.B - 15, $"{value}% 持续采样真实蓝变绿背景");
                    await DesktopCheckSession.CaptureAsync(Path.Combine(evidence, $"01-强度-{value}-绿.png"), () => [header], () => dialog.Activate());
                    canvas.Background = Brushes.DarkBlue; await Task.Delay(300);
                }
                File.AppendAllText(log, "同一宽边缘屏幕像素红通道标准差（0/25/50/75/100%）：" + string.Join(" / ", deviation.Select(v => v.ToString("F3"))) + "\n");
                Require(deviation.Zip(deviation.Skip(1), (a, b) => a > b + 2).All(v => v), "相同图案下强度增大持续减弱边缘对比，五档效果可区分");
                await Click(dialog.Frosted);
                Require(!dialog.FrostedStrength.IsEnabled && dialog.FrostedStrength.Value == 100 && !header.Record.Frosted,
                    "关闭开关保留 100%，滑块置灰");
                await Click(dialog.Frosted); await Task.Delay(350);
                Require(header.FrostedVisible && header.Record.FrostedStrength == 100, "重新开启使用保留强度");
                Key(0x1B); await Task.Delay(350);
                Require(!dialog.IsVisible && workspace.Snapshot.Folders.First().Folder == original && !header.Record.Frosted,
                    "真实 Esc 撤销本次开关与强度预览");
                dialog = ShowStyle(); await Click(dialog.Frosted); await DragSlider(dialog.FrostedStrength, 75);
                store.FailNext = true; await Click(dialog.ApplyButton);
                Require(dialog.IsVisible && !dialog.Interaction.Closed && dialog.Error.Text.Length != 0
                    && workspace.Snapshot.Folders.First().Folder == original && File.ReadAllBytes(statePath).SequenceEqual(originalJson)
                    && header.Record.FrostedStrength == 75, "真实应用故障保持原正式 JSON 与可重试强度草稿");
                await Click(dialog.ApplyButton);
                Require(workspace.Snapshot.Folders.First().Folder.FrostedStrength == 75
                    && workspace.Snapshot.Folders.Last().Folder == records[1], "真实重试整体保存目标强度且其他 Folder 不受影响");
                var toggle = await DesktopCheckSession.InputPointAsync(runtime, header, header.ExpandButton,
                    new Point(header.ExpandButton.ActualWidth / 2, header.ExpandButton.ActualHeight / 2));
                await ClickAt(toggle);
                Require(!header.Record.Expanded && header.Record.FrostedStrength == 75 && header.FrostedVisible, "真实折叠后头部保留已保存 75% 强度");
                toggle = await DesktopCheckSession.InputPointAsync(runtime, header, header.ExpandButton,
                    new Point(header.ExpandButton.ActualWidth / 2, header.ExpandButton.ActualHeight / 2));
                await ClickAt(toggle);
                Require(header.Record.Expanded && header.Record.FrostedStrength == 75 && header.FrostedVisible, "真实展开后头部和展示部分继续统一 75% 强度");
                dialog = ShowStyle(); Require(dialog.FrostedStrength.Value == 75, "重新打开显示已保存强度");
                await DragSlider(dialog.FrostedStrength, 25); Key(0x73, alt: true); await Task.Delay(300);
                Require(!dialog.IsVisible && header.Record.FrostedStrength == 75 && workspace.Snapshot.Folders.First().Folder.FrostedStrength == 75,
                    "真实 Alt+F4 关闭窗口撤销尚未应用的修改");
                dialog = ShowStyle(); await DragSlider(dialog.FrostedStrength, 0); await Click(dialog.ApplyButton);
                runtime.Dispose(); runtime = null;
                workspace = new DesktopWorkspace(store, new NoStartup()); Require((await workspace.InitializeAsync(displays)).Succeeded, "重建真实磁盘工作区");
                runtime = new Runtime(workspace); header = runtime.Headers[id]; await DesktopCheckSession.WaitAsync(() => header.FrostedVisible, "重建后背景不可用");
                Require(header.Record.FrostedStrength == 0 && header.Record.Frosted && File.ReadAllBytes(file).SequenceEqual(new byte[] { 0, 42, 255 }),
                    "重启恢复清晰零值与开启意图，内容字节未变");
                dialog = ShowStyle(); await DragSlider(dialog.Transparency, 0); await DragSlider(dialog.FrostedStrength, 100); await Click(dialog.ApplyButton);
                Require(header.Record.FrostedStrength == 100 && header.Record.Opacity == 1, "完全不透明时仍可通过真实滑块调整并保存强度");
                runtime.Dispose(); runtime = null; workspace = new DesktopWorkspace(store, new NoStartup()); await workspace.InitializeAsync(displays);
                runtime = new Runtime(workspace); header = runtime.Headers[id];
                Require(header.Record.FrostedStrength == 100 && header.Record.Opacity == 1, "完全不透明的强度重启恢复");
                dialog = ShowStyle(); await DragSlider(dialog.Transparency, 100); await Task.Delay(450);
                Require(header.Record.FrostedStrength == 100 && ((SolidColorBrush)header.Surface.Background).Color.A == 0 && header.FrostedVisible,
                    "提高透明度显现先前强度，模糊与着色独立");
                await DesktopCheckSession.CaptureAsync(Path.Combine(evidence, "01-不透明重启后恢复显示.png"), () => [header], () => dialog.Activate());
                var process = System.Diagnostics.Process.GetCurrentProcess(); var cpu = process.TotalProcessorTime;
                var memory = process.PrivateMemorySize64; var watch = System.Diagnostics.Stopwatch.StartNew();
                for (var cycle = 0; cycle < 6; cycle++) { await DragSlider(dialog.FrostedStrength, 0); await DragSlider(dialog.FrostedStrength, 100); }
                process.Refresh(); File.AppendAllText(log, $"连续 12 次全范围拖动：{watch.Elapsed.TotalMilliseconds:F0} ms；CPU {(process.TotalProcessorTime - cpu).TotalMilliseconds:F0} ms；私有内存 {memory / 1048576.0:F1}→{process.PrivateMemorySize64 / 1048576.0:F1} MiB；GDI {GetGuiResources(process.Handle, 0)}，USER {GetGuiResources(process.Handle, 1)}。\n");
                Require(header.FrostedVisible && !runtime.Interacting && header.Record.FrostedStrength == 100,
                    "连续鼠标调整后仍可操作并显示最终强度");
                Key(0x1B); await Task.Delay(250);
                runtime.Dispose(); runtime = null;
                runtime = new Runtime(workspace, frostedCapability: () => false); header = runtime.Headers[id];
                dialog = ShowStyle();
                Require(dialog.FrostedStrength.IsEnabled && !header.FrostedVisible && dialog.FrostedStatus.Text == "当前环境无法显示毛玻璃",
                    "平台不可用时保留原因提示，已勾选强度仍可编辑");
                await DragSlider(dialog.FrostedStrength, 75); await Click(dialog.ApplyButton);
                runtime.Dispose(); runtime = null; workspace = new DesktopWorkspace(store, new NoStartup()); await workspace.InitializeAsync(displays);
                runtime = new Runtime(workspace, frostedCapability: () => false); header = runtime.Headers[id];
                Require(header.Record.Frosted && header.Record.FrostedStrength == 75 && !header.FrostedVisible && header.Record.Opacity == 1,
                    "不可用期间保存并重启保留强度、启用意图与既有着色回退");
                exit = 0;

                StyleDialog ShowStyle()
                {
                    var result = runtime.CreateAppearance(id)!; result.WindowStartupLocation = WindowStartupLocation.Manual;
                    result.Left = area.X / area.Scale + 20; result.Top = area.Y / area.Scale + 20; result.Show(); result.Activate(); return result;
                }
                async Task Click(FrameworkElement element)
                {
                    var point = await DesktopCheckSession.InputPointAsync(runtime, dialog, element,
                        new Point(element.ActualWidth / 2, element.ActualHeight / 2));
                    await ClickAt(point);
                }
                async Task DragSlider(Slider slider, int value)
                {
                    slider.UpdateLayout();
                    var track = (Track)slider.Template.FindName("PART_Track", slider); var thumb = track.Thumb;
                    var end = await DesktopCheckSession.InputPointAsync(runtime, dialog, track,
                        new Point(thumb.ActualWidth / 2 + (track.ActualWidth - thumb.ActualWidth) * value / 100.0, track.ActualHeight / 2));
                    // 最后确认当前 Thumb 的真实命中，避免布局或遮挡变化时误按其他窗口。
                    var start = await DesktopCheckSession.InputPointAsync(runtime, dialog, thumb,
                        new Point(thumb.ActualWidth / 2, thumb.ActualHeight / 2));
                    SetCursorPos((int)start.X, (int)start.Y); await Task.Delay(70); mouse_event(2, 0, 0, 0, UIntPtr.Zero);
                    for (var step = 1; step <= 8; step++) { SetCursorPos((int)Math.Round(start.X + (end.X - start.X) * step / 8), (int)Math.Round(end.Y)); await Task.Delay(25); }
                    mouse_event(4, 0, 0, 0, UIntPtr.Zero); await Task.Delay(250);
                }
            }
            catch (Exception error) { File.AppendAllText(log, "失败：" + error + "\n"); }
            finally
            {
                try { await session.RestoreAsync(); File.AppendAllText(log, "恢复完成：临时窗口、鼠标和 GPU 资源已释放。\n"); }
                catch (Exception error) { exit = 1; File.AppendAllText(log, "恢复失败：" + error + "\n"); }
                app.Shutdown();
            }
        };
        app.Run(); return exit;
        void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); File.AppendAllText(log, "通过：" + message + "\n"); }
    }

    private static async Task ClickAt(Point point)
    {
        SetCursorPos((int)point.X, (int)point.Y); await Task.Delay(80); mouse_event(2, 0, 0, 0, UIntPtr.Zero);
        mouse_event(4, 0, 0, 0, UIntPtr.Zero); await Task.Delay(200);
    }
    private static double Deviation(int x, int y, int width)
    {
        using var bitmap = new System.Drawing.Bitmap(width, 1); using (var graphics = System.Drawing.Graphics.FromImage(bitmap)) graphics.CopyFromScreen(x, y, 0, 0, bitmap.Size);
        double sum = 0, squares = 0; for (var i = 0; i < width; i++) { var value = bitmap.GetPixel(i, 0).R; sum += value; squares += value * value; }
        return Math.Sqrt(Math.Max(0, squares / width - sum / width * (sum / width)));
    }
    private static void Key(byte key, bool alt = false)
    { if (alt) keybd_event(0x12, 0, 0, UIntPtr.Zero); keybd_event(key, 0, 0, UIntPtr.Zero); keybd_event(key, 0, 2, UIntPtr.Zero); if (alt) keybd_event(0x12, 0, 2, UIntPtr.Zero); }
    private sealed class NoStartup : IStartupRegistration
    { public string LaunchCommand => "隔离检查不注册自启"; public string? ReadCommand() => null; public void WriteCommand(string? command) => throw new InvalidOperationException(); }
    private sealed class FaultStore(IWorkspaceStore inner) : IWorkspaceStore
    { internal bool FailNext; public StateRead Read() => inner.Read(); public void Save(WorkspaceState state) { if (FailNext) { FailNext = false; throw new IOException("指定强度保存故障"); } inner.Save(state); } public void RestoreBackup() => inner.RestoreBackup(); }
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] private static extern int GetGuiResources(IntPtr process, uint flags);
}
