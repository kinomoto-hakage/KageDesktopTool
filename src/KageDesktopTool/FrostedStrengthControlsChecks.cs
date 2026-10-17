using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Kage.Workspace;

namespace Kage.Desktop;

internal static class FrostedStrengthControlsChecks
{
    private const string Hint = "单击滑块或按 Tab 将焦点移到滑块后：方向键调整 1%，Page Up／Down 调整 10%，Home 调至 0%，End 调至 100%。";

    internal static int Run()
    {
        using var session = new DesktopCheckSession("frosted-strength-controls");
        var evidence = DesktopCheckSession.EvidenceFor("folder-frosted-strength");
        var log = Path.Combine(evidence, "02-键盘和恢复默认.txt");
        File.WriteAllText(log, "正式样式窗口：真实鼠标悬停、Tab 和系统按键\n");
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Runtime? runtime = null; var exit = 1;
        session.Defer(() => runtime?.Dispose());
        session.Defer(() => mouse_event(4, 0, 0, 0, UIntPtr.Zero));
        app.Startup += async (_, _) =>
        {
            try
            {
                var displays = WindowsDesktop.Displays(); var area = displays.OrderByDescending(a => a.Width * a.Height).First();
                session.ShowDesktop();
                var root = Path.Combine(session.DirectoryPath, "内容"); Directory.CreateDirectory(root);
                var records = DesktopCheckSession.PlaceRecords([
                    new FolderRecord(Guid.NewGuid(), "键盘强度检查", Frosted: true, FrostedStrength: 75, Color: "#4679AB", Opacity: .25),
                    new FolderRecord(Guid.NewGuid(), "独立强度检查", Frosted: true, FrostedStrength: 25)
                ], area, new Rect(area.X, area.Y, 790, area.Height));
                foreach (var record in records)
                {
                    var directory = Path.Combine(root, record.Name); Directory.CreateDirectory(directory);
                    File.WriteAllText(Path.Combine(directory, ".kage-folder-id"), record.Id.ToString("N"));
                }
                var content = Path.Combine(root, records[0].Name, "保持内容.bin"); File.WriteAllBytes(content, [0, 42, 255]);
                var disk = new JsonWorkspaceStore(Path.Combine(session.DirectoryPath, "状态"));
                disk.Save(new WorkspaceState { Root = root, Folders = records });
                var store = new FaultStore(disk); IDesktopWorkspace workspace = new DesktopWorkspace(store, new NoStartup());
                Require((await workspace.InitializeAsync(displays)).Succeeded, "真实隔离工作区初始化");
                runtime = new Runtime(workspace); var id = records[0].Id; var header = runtime.Headers[id];
                var jsonPath = Path.Combine(session.DirectoryPath, "状态", "workspace.json");
                var original = workspace.Snapshot.Folders.First().Folder; var originalJson = File.ReadAllBytes(jsonPath);
                var dialog = ShowStyle(); await Task.Delay(300);
                Require(dialog.Hex.IsKeyboardFocused, "窗口打开时键盘焦点仍在颜色输入框");
                var hover = await DesktopCheckSession.InputPointAsync(runtime, dialog, dialog.FrostedStrength,
                    new Point(dialog.FrostedStrength.ActualWidth / 2, dialog.FrostedStrength.ActualHeight / 2));
                SetCursorPos((int)hover.X, (int)hover.Y);
                await DesktopCheckSession.WaitAsync(() => PresentationSource.CurrentSources.Cast<PresentationSource>()
                    .Any(source => source.RootVisual is DependencyObject rootVisual && Descendants<TextBlock>(rootVisual)
                        .Any(text => text.IsVisible && text.Text == Hint)), "真实悬停没有显示完整中文键盘用法", attempts: 50);
                Require(dialog.Hex.IsKeyboardFocused, "真实鼠标悬停显示说明但不转移键盘焦点");
                await DesktopCheckSession.CaptureAsync(Path.Combine(evidence, "02-真实悬浮说明.png"), () => [header], () => dialog.Activate());
                SetCursorPos((int)hover.X + 30, (int)hover.Y + 80);
                await Press(0x26);
                Require(header.Record.FrostedStrength == 75, "滑块没有焦点时方向键不修改强度");
                await FocusStrength();
                foreach (var step in new (byte Key, int Value)[] {
                    (0x27, 76), (0x25, 75), (0x26, 76), (0x28, 75), (0x21, 85), (0x22, 75),
                    (0x24, 0), (0x25, 0), (0x28, 0), (0x22, 0),
                    (0x23, 100), (0x27, 100), (0x26, 100), (0x21, 100) })
                {
                    await Press(step.Key); CheckPreview(step.Value, $"真实按键 0x{step.Key:X2} 得到 {step.Value}%");
                }
                await Press(0x24);
                for (var repeat = 0; repeat < 8; repeat++) await Press(0x27);
                CheckPreview(8, "重复方向键每次仅增加 1%，不提前保存");
                await DesktopCheckSession.CaptureAsync(Path.Combine(evidence, "02-Tab和键盘预览.png"), () => [header], () => dialog.Activate());
                var reset = Descendants<Button>(dialog).SingleOrDefault(button => Equals(button.Content, "恢复默认"));
                Require(reset != null, "正式样式窗口显示恢复默认按钮");
                await Click(reset!); CheckPreview(50, "真实恢复默认只预览当前 Folder 的 50%");
                await Click(reset!); CheckPreview(50, "重复恢复默认不影响其他样式");
                await Press(0x1B); await Task.Delay(200);
                Require(!dialog.IsVisible && header.Record == original, "Esc 撤销键盘和恢复默认的完整草稿");
                dialog = ShowStyle(); await Click(ResetButton()); await Press(0x73, alt: true); await Task.Delay(200);
                Require(!dialog.IsVisible && header.Record == original, "真实关闭窗口撤销恢复默认");
                dialog = ShowStyle(); await Click(ResetButton());
                await Click(Descendants<Button>(dialog).Single(button => Equals(button.Content, "取消")));
                Require(!dialog.IsVisible && header.Record == original, "真实取消按钮撤销恢复默认");
                dialog = ShowStyle(); await FocusStrength(); await Press(0x23);
                await Click(dialog.Frosted);
                Require(!dialog.FrostedStrength.IsEnabled && !ResetButton().IsEnabled && header.Record.FrostedStrength == 100 && !header.Record.Frosted,
                    "关闭开关时滑块与恢复默认统一置灰并保留 100%");
                await Click(dialog.Frosted);
                Require(dialog.FrostedStrength.IsEnabled && ResetButton().IsEnabled && header.Record.Frosted && header.Record.FrostedStrength == 100,
                    "重新勾选后恢复键盘与默认按钮并保留强度");
                await Click(ResetButton()); CheckPreview(50, "重新启用后恢复默认仍沿用完整草稿");
                store.FailNext = true; await Click(dialog.ApplyButton);
                Require(dialog.IsVisible && dialog.Error.Text.Length != 0 && !dialog.Interaction.Closed,
                    "恢复默认保存失败后保留可重试草稿并显示错误");
                CheckPreview(50, "保存失败不发布正式记录或 JSON");
                await Click(dialog.ApplyButton);
                Require(!dialog.IsVisible && workspace.Snapshot.Folders.First().Folder == original with { FrostedStrength = 50 },
                    "真实应用重试只保存当前强度 50%");
                original = workspace.Snapshot.Folders.First().Folder; originalJson = File.ReadAllBytes(jsonPath);
                dialog = ShowStyle(); Require(dialog.StrengthValue.Text == "50%", "应用后重新打开显示 50%");
                await Press(0x1B); await Restart();
                Require(header.Record == original, "恢复默认应用后的完整样式重启恢复");
                dialog = ShowStyle(); await FocusStrength(); await Press(0x23); await Click(dialog.ApplyButton);
                original = workspace.Snapshot.Folders.First().Folder; originalJson = File.ReadAllBytes(jsonPath);
                await Restart(); Require(header.Record.FrostedStrength == 100, "键盘 End 的有效强度应用并重启恢复 100%");
                dialog = ShowStyle(); await FocusStrength(); await Press(0x24); await Click(ResetButton());
                CheckPreview(50, "可见效果为零时恢复默认仍可用");
                await Press(0x1B);
                var opaque = workspace.BeginAppearance(id)!; opaque.SetOpacity(1);
                Require((await workspace.ApplyAppearanceAsync(opaque)).Succeeded, "经业务入口准备完全不透明的已保存样式"); runtime.Render();
                original = workspace.Snapshot.Folders.First().Folder; originalJson = File.ReadAllBytes(jsonPath);
                dialog = ShowStyle(); await FocusStrength(); await Press(0x24); CheckPreview(0, "完全不透明时键盘仍可调整至零");
                await Click(ResetButton()); CheckPreview(50, "完全不透明时恢复默认仍可预览");
                await Click(dialog.ApplyButton); await Restart();
                Require(header.Record.FrostedStrength == 50 && header.Record.Opacity == 1 && header.Record.Frosted,
                    "完全不透明时恢复默认应用和重启保留强度与开启意图");
                Require(workspace.Snapshot.Folders.Last().Folder == records[1] && File.ReadAllBytes(content).SequenceEqual(new byte[] { 0, 42, 255 }),
                    "全部键盘和默认操作不影响其他 Folder、内容和布局");
                exit = 0;

                Button ResetButton() => Descendants<Button>(dialog).Single(button => Equals(button.Content, "恢复默认"));
                async Task FocusStrength()
                {
                    for (var tab = 0; tab < 40 && !dialog.FrostedStrength.IsKeyboardFocusWithin; tab++) await Press(0x09);
                    Require(dialog.FrostedStrength.IsKeyboardFocusWithin, "真实 Tab 可将焦点移到强度滑块");
                }
                async Task Click(FrameworkElement element)
                {
                    var point = await DesktopCheckSession.InputPointAsync(runtime, dialog, element,
                        new Point(element.ActualWidth / 2, element.ActualHeight / 2));
                    SetCursorPos((int)point.X, (int)point.Y); await Task.Delay(80);
                    mouse_event(2, 0, 0, 0, UIntPtr.Zero); mouse_event(4, 0, 0, 0, UIntPtr.Zero); await Task.Delay(220);
                }
                void CheckPreview(int strength, string message)
                {
                    Require(header.Record == original with { FrostedStrength = strength }
                        && dialog.StrengthValue.Text == strength + "%" && workspace.Snapshot.Folders.First().Folder == original
                        && File.ReadAllBytes(jsonPath).SequenceEqual(originalJson)
                        && runtime.Headers[records[1].Id].Record == records[1], message + "；只读值、有效草稿、正式 JSON 与独立 Folder 一致");
                }
                async Task Restart()
                {
                    runtime.Dispose(); runtime = null; workspace = new DesktopWorkspace(store, new NoStartup());
                    Require((await workspace.InitializeAsync(displays)).Succeeded, "重建真实磁盘工作区");
                    runtime = new Runtime(workspace); header = runtime.Headers[id];
                    await DesktopCheckSession.WaitAsync(() => header.FrostedVisible, "重启后毛玻璃未恢复");
                }

                StyleDialog ShowStyle()
                {
                    var result = runtime.CreateAppearance(id)!; result.WindowStartupLocation = WindowStartupLocation.Manual;
                    result.Left = area.X / area.Scale + 20; result.Top = area.Y / area.Scale + 20;
                    result.Show(); result.Activate(); return result;
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

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static async Task Press(byte key, bool alt = false)
    {
        if (alt) keybd_event(0x12, 0, 0, UIntPtr.Zero);
        keybd_event(key, 0, 0, UIntPtr.Zero); keybd_event(key, 0, 2, UIntPtr.Zero);
        if (alt) keybd_event(0x12, 0, 2, UIntPtr.Zero);
        await Task.Delay(90);
    }
    private sealed class NoStartup : IStartupRegistration
    { public string LaunchCommand => "隔离检查不注册自启"; public string? ReadCommand() => null; public void WriteCommand(string? command) => throw new InvalidOperationException(); }
    private sealed class FaultStore(IWorkspaceStore inner) : IWorkspaceStore
    { internal bool FailNext; public StateRead Read() => inner.Read(); public void Save(WorkspaceState state) { if (FailNext) { FailNext = false; throw new IOException("指定键盘强度保存故障"); } inner.Save(state); } public void RestoreBackup() => inner.RestoreBackup(); }
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
}
