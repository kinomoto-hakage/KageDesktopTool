using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Kage.Workspace;
using Microsoft.Win32;
using Windows.UI.ViewManagement;

namespace Kage.Desktop;

// 真实系统限制、四向多实例与 Explorer 重启的组合，始终恢复原系统设置。
internal static class FrostedCombinedChecks
{
    internal static int Run()
    {
        using var session = new DesktopCheckSession("frosted-combined");
        var fixture = session.DirectoryPath;
        var evidence = DesktopCheckSession.EvidenceFor("folder-frosted-background");
        Directory.CreateDirectory(fixture); Directory.CreateDirectory(evidence);
        var log = Path.Combine(evidence, "02-03-combined.txt"); File.WriteAllText(log, "四向＋真实系统限制＋Explorer 生命周期组合\n");
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Runtime? runtime = null; StyleDialog? dialog = null; Window? patternWindow = null; var exit = 1;
        session.Defer(() => patternWindow?.Close());
        session.Defer(() => runtime?.Dispose());
        session.Defer(() => dialog?.Close());
        app.Startup += async (_, _) =>
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", true)!;
            var transparency = key.GetValue("EnableTransparency");
            var contrast = new HighContrast { Size = (uint)Marshal.SizeOf<HighContrast>() };
            var contrastChanged = false;
            session.Defer(async () =>
            {
                if (contrastChanged && !await Task.Run(() => SystemParametersInfo(0x43, contrast.Size, ref contrast, 3)))
                    throw new InvalidOperationException("恢复原高对比度设置失败。");
            });
            session.Defer(async () =>
            {
                if (transparency == null) key.DeleteValue("EnableTransparency", false); else key.SetValue("EnableTransparency", transparency, RegistryValueKind.DWord);
                await Task.Run(Broadcast);
                await Task.Delay(1000);
            });
            try
            {
                Require(SystemParametersInfo(0x42, contrast.Size, ref contrast, 0), "读取高对比度原设置");
                var ui = new UISettings();
                Require(ui.AdvancedEffectsEnabled && !SystemParameters.HighContrast, "组合验收从原无限制系统状态开始");
                var displays = WindowsDesktop.Displays(); var area = displays.First(); var scale = area.Scale;
                var root = Path.Combine(fixture, "内容");
                var records = Enum.GetValues<HeaderPosition>().Select((direction, i) => new FolderRecord(Guid.NewGuid(), $"组合 {direction}",
                    area.X + 280 + i % 2 * 650 + (direction == HeaderPosition.Right ? 360 : 0),
                    area.Y + 150 + i / 2 * 520 + (direction == HeaderPosition.Bottom ? 270 : 0),
                    HeaderWidth: 240, BodyHeight: 180, Expanded: true, HeaderPosition: direction,
                    NameOrientation: i % 2 == 0 ? NameOrientation.Horizontal : NameOrientation.Vertical, Color: "#426A7F", Opacity: .4, Frosted: i != 3)).ToArray();
                records = DesktopCheckSession.PlaceRecords(records, area, new Rect(area.X + 1580, area.Y + 60, 800, 1320));
                File.AppendAllText(log, "避开已有实例后的夹具锚点：" + string.Join("；", records.Select(r => $"{r.Name}={r.X},{r.Y}")) + "\n");
                foreach (var record in records)
                { Directory.CreateDirectory(Path.Combine(root, record.Name)); File.WriteAllBytes(Path.Combine(root, record.Name, "原内容.bin"), [0, 42, 255]); }
                var store = new JsonWorkspaceStore(Path.Combine(fixture, "状态")); store.Save(new WorkspaceState { Root = root, Folders = records });
                IDesktopWorkspace workspace = new DesktopWorkspace(store, new NoStartup());
                Require((await workspace.InitializeAsync(displays)).Succeeded, "真实四向组合状态加载");
                session.ShowDesktop();
                runtime = new Runtime(workspace);
                await Wait(() => runtime.Headers.Values.Count(h => h.FrostedVisible) == 3, "三个正式开启实例显示毛玻璃，正式关闭实例保持关闭");
                var previewId = records[3].Id;
                dialog = runtime.CreateAppearance(previewId)!; dialog.WindowStartupLocation = WindowStartupLocation.Manual;
                dialog.Left = (area.X + 1600) / scale; dialog.Top = (area.Y + 80) / scale;
                dialog.Show(); await Task.Delay(250);
                var point = dialog.Frosted.PointToScreen(new Point(15, dialog.Frosted.ActualHeight / 2));
                SetCursorPos((int)point.X, (int)point.Y); mouse_event(2, 0, 0, 0, UIntPtr.Zero); mouse_event(4, 0, 0, 0, UIntPtr.Zero);
                await Wait(() => runtime.Headers.Values.All(h => h.FrostedVisible), "真实开关使第四个实例即时预览，四向同时显示");
                Require(!workspace.Snapshot.Folders.Single(f => f.Folder.Id == previewId).Folder.Frosted, "有效开启草稿没有提前保存");
                await Screenshot("02-03-四向预览开启.png");
                var oldBackdrops = runtime.Headers.Values.Select(h => h.Host).ToArray();
                key.SetValue("EnableTransparency", 0, RegistryValueKind.DWord); await Task.Run(Broadcast);
                await Wait(() => !ui.AdvancedEffectsEnabled && runtime.Headers.Values.All(h => !h.FrostedVisible), "真实关闭透明效果后四向全部暂停");
                Require(oldBackdrops.All(h => !WindowsDesktop.IsWindow(h)) && runtime.Headers.Values.All(h => WindowsDesktop.IsWindowVisible(h.Handle))
                    && dialog.FrostedStatus.Text.Contains("透明效果"), "暂停销毁四个旧背景，前景可见并实时解释原因");
                await Task.Delay(1000); await Screenshot("02-03-四向真实透明暂停.png");
                await DesktopRecoveryChecks.RestartExplorerAsync(async () =>
                {
                    await runtime.RefreshDesktopSessionAsync();
                    Require(runtime.Headers.Values.All(h => !h.FrostedVisible), "Explorer 退出期间停止旧效果且不绕过系统限制");
                }, async () =>
                {
                    await Wait(() => runtime.DesktopAvailable && workspace.Snapshot.DesktopAvailable, "Explorer 新宿主恢复四个前景");
                    runtime.Render();
                    Require(runtime.Headers.Count == 4 && runtime.Headers.Values.All(h => !h.FrostedVisible && WindowsDesktop.IsWindowVisible(h.Handle))
                        && runtime.Headers[previewId].Record.Frosted && !workspace.Snapshot.Folders.Single(f => f.Folder.Id == previewId).Folder.Frosted,
                        "真实 Explorer 重启后保持暂停与有效开启草稿，正式记录不变");
                    Require(workspace.Snapshot.Folders.Select(f => f.Folder).SequenceEqual(records), "Explorer 恢复不改变任何正式布局和样式");
                await Task.Delay(800); await Screenshot("02-03-Explorer恢复仍暂停.png");
                key.SetValue("EnableTransparency", 1, RegistryValueKind.DWord); await Task.Run(Broadcast);
                await Wait(() => ui.AdvancedEffectsEnabled && runtime.Headers.Values.All(h => h.FrostedVisible), "真实限制解除后按有效草稿自动恢复四向效果");
                await Screenshot("02-03-Explorer后自动恢复.png");
                dialog.Close(); dialog = null;
                Require(!runtime.Headers[previewId].Record.Frosted && !runtime.Headers[previewId].FrostedVisible
                    && runtime.Headers.Values.Count(h => h.FrostedVisible) == 3, "Explorer 重建后取消恢复正式关闭，其他三个实例不受影响");
                var apply = workspace.BeginAppearance(previewId)!; apply.SetFrosted(true);
                Require((await workspace.ApplyAppearanceAsync(apply)).Succeeded, "完整样式正式应用第四个实例"); runtime.Render();
                var pattern = new Canvas { Background = Brushes.DarkBlue };
                for (var x = 0; x < 1500; x += 12)
                { var stripe = new Border { Width = 6, Height = 1000, Background = Brushes.White }; Canvas.SetLeft(stripe, x); pattern.Children.Add(stripe); }
                patternWindow = new Window { WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false,
                    Width = 1500, Height = 1000, Content = pattern }; patternWindow.Show();
                var patternHandle = new WindowInteropHelper(patternWindow).Handle;
                var patternHost = WindowsDesktop.Attach(patternHandle);
                Require(WindowsDesktop.Position(patternHandle, patternHost, area.X + 180, area.Y + 100, 1800, 1300), "真实动态背景图案置于四向实例后方");
                var high = contrast; high.Flags |= 1; contrastChanged = true;
                Require(await Task.Run(() => SystemParametersInfo(0x43, high.Size, ref high, 3)), "Windows 接受真实高对比度开启");
                await Wait(() => SystemParameters.HighContrast && runtime.Headers.Values.All(h => !h.FrostedVisible), "真实高对比度暂停全部四向实例");
                await Task.Delay(4000); await Screenshot("02-03-四向高对比稳定暂停.png");
                Require(await Task.Run(() => SystemParametersInfo(0x43, contrast.Size, ref contrast, 3)), "恢复原高对比度设置"); contrastChanged = false;
                await Wait(() => !SystemParameters.HighContrast && runtime.Headers.Values.All(h => h.FrostedVisible), "真实高对比度解除自动恢复四向效果");
                await Task.Delay(3000); await Screenshot("02-03-四向高对比稳定恢复.png");
                async Task<bool> LivePixels(string stage)
                {
                    var points = workspace.Snapshot.Folders.Select(f => FolderGeometry.For(f.Folder, f.DisplayScale))
                        .Select(b => new Point(b.X + b.Width * .65, b.Y + b.Height * .65)).ToArray();
                    pattern.Background = Brushes.DarkBlue; await Task.Delay(400);
                    var blue = points.Select(DesktopCheckSession.Pixel).ToArray(); await Screenshot("02-03-" + stage + "蓝.png");
                    pattern.Background = Brushes.DarkGreen; await Task.Delay(400);
                    var green = points.Select(DesktopCheckSession.Pixel).ToArray(); await Screenshot("02-03-" + stage + "绿.png");
                    File.AppendAllText(log, $"{stage}实际像素：{string.Join("；", blue.Zip(green, (b, g) => $"{b.R},{b.G},{b.B}->{g.R},{g.G},{g.B}"))}\n");
                    return DesktopCheckSession.FollowsBlueToGreen(blue, green);
                }
                var live = await LivePixels("高对比恢复");
                Require(live, "高对比解除后四向实际模糊继续跟随真实动态背景，不能仅以HWND挂接判定恢复");
                Require(workspace.Snapshot.Folders.Select(f => f.Folder).SequenceEqual(records.Select(f => f with { Frosted = true }))
                    && workspace.Snapshot.Folders.All(f => File.ReadAllBytes(Path.Combine(f.ActualPath, "原内容.bin")).SequenceEqual(new byte[] { 0, 42, 255 })),
                    "组合系统与宿主变化不改变布局、其他样式和真实内容字节");
                exit = 0;
                });
            }
            catch (Exception error) { File.AppendAllText(log, "失败：" + error + "\n"); await Screenshot("02-03-combined-failure.png", false); }
            finally
            {
                try
                {
                    await session.RestoreAsync();
                    File.AppendAllText(log, $"最终恢复：透明注册值 {key.GetValue("EnableTransparency")}，实际透明 {new UISettings().AdvancedEffectsEnabled}，高对比 {SystemParameters.HighContrast}，现代节能 {runtime?.FrostedEnvironment.EnergySaver}。\n");
                }
                catch (Exception error) { exit = 1; File.AppendAllText(log, "恢复失败：" + error + "\n"); }
                finally { app.Shutdown(); }
            }
        };
        app.Run();
        return exit;
        void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); File.AppendAllText(log, "通过：" + message + "\n"); }
        async Task Wait(Func<bool> condition, string message)
        { await DesktopCheckSession.WaitAsync(condition, message); Require(true, message); }
        async Task Screenshot(string name, bool requireVisible = true)
        {
            if (requireVisible && runtime != null)
            {
                await DesktopCheckSession.CaptureAsync(Path.Combine(evidence, name), () => runtime.Headers.Values, () =>
                {
                    session.ShowDesktop();
                    if (dialog != null) { dialog.WindowState = WindowState.Normal; dialog.Show(); }
                });
                Require(true, "截图前四向实际前景均无遮挡：" + name);
            }
            else DesktopCheckSession.SaveScreenshot(Path.Combine(evidence, name));
        }
    }
    private static void Broadcast() => SendMessageTimeout(new IntPtr(0xffff), 0x1A, IntPtr.Zero, "ImmersiveColorSet", 2, 2000, out _);
    [StructLayout(LayoutKind.Sequential)] private struct HighContrast { internal uint Size, Flags; internal IntPtr Scheme; }
    private sealed class NoStartup : IStartupRegistration
    { public string LaunchCommand => "隔离检查不注册自启"; public string? ReadCommand() => null; public void WriteCommand(string? command) => throw new InvalidOperationException(); }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool SystemParametersInfo(uint action, uint size, ref HighContrast value, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint message, IntPtr wp, string parameter, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
}
