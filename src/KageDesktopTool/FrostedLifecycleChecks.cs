using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Kage.Workspace;

namespace Kage.Desktop;

// 正式业务入口控制四个独立实例，实测 HWND 几何、清理及进程资源。
internal static class FrostedLifecycleChecks
{
    internal static int Run(bool draggingOnly = false)
    {
        var fixture = Path.Combine(Path.GetTempPath(), "Kage-frosted-lifecycle-" + Guid.NewGuid().ToString("N"));
        var evidence = DesktopCheckSession.EvidenceFor("folder-frosted-background");
        Directory.CreateDirectory(fixture); Directory.CreateDirectory(evidence);
        var log = Path.Combine(evidence, draggingOnly ? "02-drag-resources.txt" : "02-lifecycle.txt");
        File.WriteAllText(log, "四向毛玻璃真实窗口与资源观察\n");
        File.WriteAllText(Path.Combine(evidence, "02-process-id.txt"), Environment.ProcessId.ToString());
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Runtime? runtime = null; Window? background = null; string? retainedShortcut = null; string? retainedName = null; var exit = 1;
        WindowsDesktop.GetCursorPos(out var cursor);
        app.Startup += async (_, _) =>
        {
            try
            {
                var displays = WindowsDesktop.Displays(); var area = displays.First(); var scale = area.Scale;
                File.AppendAllText(log, $"实际环境：{Environment.OSVersion}; {string.Join("; ", displays.Select(d => d.ToString()))}\n");
                var root = Path.Combine(fixture, "内容");
                var suffix = draggingOnly ? "-" + Guid.NewGuid().ToString("N")[..8] : "";
                var records = Enum.GetValues<HeaderPosition>().Select((direction, i) => new FolderRecord(Guid.NewGuid(), $"独立背景 {direction}{suffix}",
                    area.X + (draggingOnly ? 900 : 300) + i % 2 * 650 + (direction == HeaderPosition.Right ? 360 : 0),
                    area.Y + 160 + i / 2 * 520 + (direction == HeaderPosition.Bottom ? 270 : 0),
                    HeaderWidth: 240, BodyHeight: 180, Expanded: true, HeaderPosition: direction,
                    NameOrientation: i % 2 == 0 ? NameOrientation.Horizontal : NameOrientation.Vertical, Opacity: 0, Frosted: true,
                    FrostedStrength: new[] { 0, 25, 75, 100 }[i])).ToArray();
                records = DesktopCheckSession.PlaceRecords(records, area, new Rect(area.X, area.Y, 0, 0));
                foreach (var record in records)
                { Directory.CreateDirectory(Path.Combine(root, record.Name)); File.WriteAllText(Path.Combine(root, record.Name, "清晰内容.txt"), "真实字节"); }
                var store = new JsonWorkspaceStore(Path.Combine(fixture, "状态")); store.Save(new WorkspaceState { Root = root, Folders = records });
                IDesktopWorkspace workspace = new DesktopWorkspace(store, new NoStartup());
                Check((await workspace.InitializeAsync(displays)).Succeeded, "四向隔离工作区初始化");
                FileMoveChecks.DesktopVisibility(true);
                var pattern = new Canvas { Background = Brushes.DarkBlue };
                for (var x = 0; x < 1500; x += 12)
                { var stripe = new Border { Width = 6, Height = 1000, Background = Brushes.White }; Canvas.SetLeft(stripe, x); pattern.Children.Add(stripe); }
                background = new Window { WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false,
                    Width = 1500, Height = 1000, Content = pattern }; background.Show();
                var backgroundHandle = new WindowInteropHelper(background).Handle; var host = WindowsDesktop.Attach(backgroundHandle);
                WindowsDesktop.Position(backgroundHandle, host, area.X + 200, area.Y + 100, 1800, 1300);
                runtime = new Runtime(workspace); await Task.Delay(500);
                foreach (var header in runtime.Headers.Values) await header.Contents.IconsLoaded;
                Check(runtime.Headers.Values.All(h => h.FrostedVisible), "四向多个真实毛玻璃背景同时显示");
                Check(runtime.Headers.Values.Select(h => h.Host).Distinct().Count() == 4, "逐 Folder 使用独立原生背景 HWND");
                Bounds();
                if (draggingOnly)
                {
                    await Sample("开启-拖动", false, true);
                    foreach (var record in records) await Set(record.Id, false);
                    await Sample("关闭-拖动", false, true);
                    Check(workspace.Snapshot.Folders.Select(f => f.Folder with { Frosted = true }).SequenceEqual(records), "开关对照真实拖动后全部锚点恢复且其他样式不变");
                    await Set(records[1].Id, true);
                    var deleteName = "删除资源验证-" + Guid.NewGuid().ToString("N")[..8];
                    Check((await workspace.CreateFolderAsync(deleteName)).Succeeded, "通过正式创建入口取得删除夹具的归属标识");
                    var deleteFolder = workspace.Snapshot.Folders.Single(f => f.Folder.Name == deleteName);
                    File.WriteAllText(Path.Combine(deleteFolder.ActualPath, "清晰内容.txt"), "真实字节");
                    await Set(deleteFolder.Folder.Id, true);
                    var removedHost = runtime.Headers[deleteFolder.Folder.Id].Host;
                    var survivorHost = runtime.Headers[records[1].Id].Host;
                    var deletion = await workspace.DeleteFolderAsync(deleteFolder.Folder.Id, FolderDeleteChoice.KeepContents);
                    Check(deletion.Succeeded, "正式删除开启毛玻璃的隔离 Folder 并保留真实内容：" + deletion.Message);
                    var retained = store.Read().State.RetainedFolders.Single(r => r.FolderId == deleteFolder.Folder.Id);
                    retainedShortcut = retained.ShortcutPath; retainedName = deleteName;
                    runtime.Render();
                    Check(!runtime.Headers.ContainsKey(deleteFolder.Folder.Id) && !WindowsDesktop.IsWindow(removedHost)
                        && runtime.Headers[records[1].Id].FrostedVisible && runtime.Headers[records[1].Id].Host == survivorHost
                        && File.ReadAllText(Path.Combine(retained.ContentPath, "清晰内容.txt")) == "真实字节",
                        "删除后旧背景销毁无残留、另一个开启实例不受影响且保留内容字节不变");
                    exit = 0; return;
                }
                Screenshot("02-四向蓝白背景.png");
                pattern.Background = Brushes.DarkGreen; await Task.Delay(350); Screenshot("02-四向绿白实时背景.png");
                pattern.Background = Brushes.Black; await Task.Delay(350); Screenshot("02-四向暗背景.png");
                pattern.Background = Brushes.WhiteSmoke; await Task.Delay(350); Screenshot("02-四向明背景.png");
                await Sample("开启-静置", false);
                await Sample("开启-动态背景", true);
                await Sample("开启-拖动", false, true);
                var baseline = Resources();
                for (var cycle = 0; cycle < 30; cycle++)
                {
                    foreach (var record in records) await Set(record.Id, false);
                    Check(runtime.Headers.Values.All(h => !h.FrostedVisible), $"第 {cycle + 1} 轮关闭均释放背景");
                    foreach (var record in records) await Set(record.Id, true);
                    Bounds();
                    if (cycle is 9 or 19 or 29) File.AppendAllText(log, $"资源轮次 {cycle + 1}：{Resources()}\n");
                }
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); await Task.Delay(300);
                var final = Resources(); File.AppendAllText(log, $"预热后基线 {baseline}；30轮后 {final}\n");
                Check(final.User <= baseline.User + 8 && final.Gdi <= baseline.Gdi + 8 && final.Handles <= baseline.Handles + 25,
                    "30轮逐实例开关后 USER/GDI/进程handle保持有界");
                var first = records[0]; var otherHosts = runtime.Headers.Values.Where(h => h.FolderId != first.Id).Select(h => h.Host).ToArray();
                await Set(first.Id, false);
                Check(runtime.Headers.Values.Where(h => h.FolderId != first.Id).Select(h => h.Host).SequenceEqual(otherHosts)
                    && runtime.Headers.Values.Where(h => h.FolderId != first.Id).All(h => h.FrostedVisible), "关闭一个实例不改变其余背景或资源");
                foreach (var record in records) await Set(record.Id, false);
                await Sample("关闭-静置", false); await Sample("关闭-动态背景", true);
                await Sample("关闭-拖动", false, true);
                Check(workspace.Snapshot.Folders.Select(f => f.Folder with { Frosted = true }).SequenceEqual(records), "反复背景操作不改变布局与其他样式");
                Check(workspace.Snapshot.Folders.All(f => File.ReadAllText(Path.Combine(f.ActualPath, "清晰内容.txt")) == "真实字节"), "反复背景操作不改变真实内容");
                exit = 0;

                async Task Set(Guid id, bool enabled)
                { var edit = workspace.BeginAppearance(id)!; edit.SetFrosted(enabled); Check((await workspace.ApplyAppearanceAsync(edit)).Succeeded, "完整样式保存"); runtime.Render(); }
                void Bounds()
                {
                    foreach (var snapshot in workspace.Snapshot.Folders)
                    {
                        var header = runtime.Headers[snapshot.Folder.Id]; var expected = FolderGeometry.For(header.Record, snapshot.DisplayScale);
                        WindowsDesktop.GetWindowRect(header.Handle, out var foreground);
                        WindowsDesktop.GetWindowRect(header.Host, out var backdrop);
                        Check(header.FrostedVisible && foreground.Left == expected.X && foreground.Top == expected.Y
                            && foreground.Right - foreground.Left == expected.Width && foreground.Bottom - foreground.Top == expected.Height
                            && foreground.Equals(backdrop), "四向前景和背景物理边界完全一致");
                        Check(header.Record.FrostedStrength == records.Single(r => r.Id == header.FolderId).FrostedStrength,
                            "多实例与反复重建保持各自0/25/75/100%强度");
                    }
                }
                async Task Sample(string stage, bool dynamic, bool dragging = false)
                {
                    File.WriteAllText(Path.Combine(evidence, "02-resource-stage.txt"), stage);
                    using var process = Process.GetCurrentProcess(); var cpu = process.TotalProcessorTime; var timer = Stopwatch.StartNew();
                    if (dragging)
                    {
                        var header = runtime.Headers[records[0].Id];
                        for (var i = 0; i < 4; i++)
                        {
                            await app.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                            await Task.Delay(80);
                            header.UpdateLayout();
                            var origin = header.NameDisplay.PointToScreen(new Point());
                            Point? start = null;
                            // 关闭效果且alpha=0时只通过实际名称像素命中，避免比较
                            // 一条没有真正拖动的桌面路径。
                            for (var y = 2; y < header.NameDisplay.ActualHeight * scale && start == null; y += 2)
                            for (var x = 2; x < Math.Min(140, header.NameDisplay.ActualWidth * scale) && start == null; x += 2)
                            {
                                var candidate = new Point(origin.X + x, origin.Y + y);
                                if (WindowsDesktop.WindowFromPoint(new WindowsDesktop.POINT { X = (int)candidate.X, Y = (int)candidate.Y }) == header.Handle) start = candidate;
                            }
                            Check(start != null, "资源拖动场景真实命中名称前景");
                            SetCursorPos((int)start!.Value.X, (int)start.Value.Y); await Task.Delay(80);
                            var oldX = header.Record.X; var distance = i % 2 == 0 ? 50 : -50;
                            await FileMoveChecks.PhysicalDrag(start!.Value, start.Value + new Vector(distance, 0));
                            for (var wait = 0; wait < 30 && runtime.Interacting; wait++) await Task.Delay(50);
                            Check(!runtime.Interacting && header.Record.X == oldX + distance, $"资源拖动场景实际保存物理位移：{oldX}->{header.Record.X}，期望 {distance}，交互 {runtime.Interacting}");
                        }
                    }
                    else for (var i = 0; i < 100; i++)
                        { if (dynamic) pattern.Background = i % 2 == 0 ? Brushes.DarkBlue : Brushes.DarkGreen; await Task.Delay(50); }
                    process.Refresh();
                    File.AppendAllText(log, $"实测 {stage}：墙钟 {timer.ElapsedMilliseconds}ms，进程CPU {(process.TotalProcessorTime - cpu).TotalMilliseconds:F0}ms；{Resources()}\n");
                    File.WriteAllText(Path.Combine(evidence, "02-resource-stage.txt"), "阶段间切换");
                }
            }
            catch (Exception e) { File.AppendAllText(log, "失败：" + e + "\n"); Screenshot("02-lifecycle-failure.png"); }
            finally
            { runtime?.Dispose(); background?.Close(); FileMoveChecks.DesktopVisibility(false); SetCursorPos(cursor.X, cursor.Y); app.Shutdown(); }
        };
        try { app.Run(); }
        finally
        {
            if (retainedShortcut != null)
            {
                var desktop = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)));
                var shortcut = Path.GetFullPath(retainedShortcut);
                if (!string.Equals(Path.GetDirectoryName(shortcut), desktop, StringComparison.OrdinalIgnoreCase)
                    || retainedName == null || !Path.GetFileName(shortcut).StartsWith(retainedName, StringComparison.Ordinal))
                    throw new IOException("拒绝清理非本轮隔离 Folder 创建的快捷方式。");
                File.Delete(shortcut);
            }
            var full = Path.GetFullPath(fixture);
            if (!full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(full).StartsWith("Kage-frosted-lifecycle-", StringComparison.Ordinal)) throw new IOException("拒绝清理隔离范围外目录。");
            Directory.Delete(full, true);
        }
        return exit;
        void Check(bool condition, string message)
        { if (!condition) throw new Exception(message); File.AppendAllText(log, "通过：" + message + "\n"); }
        void Screenshot(string name)
        {
            var screen = System.Windows.Forms.SystemInformation.VirtualScreen;
            using var bitmap = new System.Drawing.Bitmap(screen.Width, screen.Height); using var graphics = System.Drawing.Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(screen.Left, screen.Top, 0, 0, screen.Size); bitmap.Save(Path.Combine(evidence, name));
        }
    }
    private static (int Handles, uint Gdi, uint User, long PrivateMiB) Resources()
    { using var process = Process.GetCurrentProcess(); return (process.HandleCount, GetGuiResources(process.Handle, 0), GetGuiResources(process.Handle, 1), process.PrivateMemorySize64 / 1048576); }
    private sealed class NoStartup : IStartupRegistration
    { public string LaunchCommand => "隔离检查不注册自启"; public string? ReadCommand() => null; public void WriteCommand(string? command) => throw new InvalidOperationException(); }
    [DllImport("user32.dll")] private static extern uint GetGuiResources(IntPtr process, uint flags);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
}
