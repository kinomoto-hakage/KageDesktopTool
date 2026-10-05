using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Kage.Workspace;

namespace Kage.Desktop;

// 实际 OLE、桌面及 Explorer 检查；只创建并清理随机命名的隔离项目。
internal static class FileMoveChecks
{
    internal static int Run()
    {
        var fixture = Path.Combine(Path.GetTempPath(), "Kage-file-move-" + Guid.NewGuid().ToString("N"));
        var prefix = "Kage-move-" + Guid.NewGuid().ToString("N");
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string[] desktopNames = [prefix + "-文件.bin", prefix + "-快捷方式.lnk", prefix + "-目录"];
        var evidence = Path.Combine(Environment.CurrentDirectory, ".scratch", "desktop-folder", "verification");
        Directory.CreateDirectory(fixture);
        Directory.CreateDirectory(evidence);
        var log = Path.Combine(evidence, "file-move-session.txt");
        File.WriteAllText(log, "07 实际 OLE 与文件移动检查\n");
        if (string.IsNullOrEmpty(desktop)) { File.AppendAllText(log, "失败：当前进程未获得实际用户桌面目录。\n"); return 1; }
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Runtime? runtime = null;
        IntPtr explorer = IntPtr.Zero;
        var exit = 1;
        var explorerPath = Path.Combine(fixture, "Explorer 目标");
        app.Startup += async (_, _) =>
        {
            try
            {
                DesktopVisibility(true);
                await Task.Delay(500);
                IDesktopWorkspace workspace = new DesktopWorkspace(new JsonWorkspaceStore(Path.Combine(fixture, "状态")), new NoStartup());
                await workspace.InitializeAsync(WindowsDesktop.Displays());
                await workspace.SelectRootAsync(Path.Combine(fixture, "内容"));
                await workspace.CreateFolderAsync("接收");
                await workspace.CreateFolderAsync("第二个");
                runtime = new Runtime(workspace);
                var first = runtime.Headers.Values.First();
                var second = runtime.Headers.Values.Last();
                await first.ToggleAsync();
                await second.ToggleAsync();
                var firstPath = workspace.Snapshot.Folders.First().ActualPath;
                var secondPath = workspace.Snapshot.Folders.Last().ActualPath;
                Check(runtime.DesktopAvailable, "真实桌面宿主可用");
                Directory.CreateDirectory(explorerPath);
                var incomingNames = new[] { "任意类型.xyz", "真实快捷方式.lnk", "普通子文件夹" };
                File.WriteAllBytes(Path.Combine(explorerPath, incomingNames[0]), [0, 42, 255]);
                MakeShortcut(Path.Combine(explorerPath, incomingNames[1]), fixture);
                Directory.CreateDirectory(Path.Combine(explorerPath, incomingNames[2]));
                File.WriteAllText(Path.Combine(explorerPath, incomingNames[2], "内容.txt"), "普通子目录内容");
                using var process = Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { explorerPath }, UseShellExecute = false });
                await WaitUntil(() => (explorer = ExplorerWindow(explorerPath)) != IntPtr.Zero);
                var screen = WindowsDesktop.Displays().First();
                WindowsDesktop.SetWindowPos(explorer, IntPtr.Zero, screen.X + screen.Width / 2, screen.Y + 80, screen.Width / 2 - 30, screen.Height - 150, 0x40);
                SetForegroundWindow(explorer);
                await Task.Delay(900);
                SelectShellItems(explorerPath, incomingNames);
                var incoming = ShellItem(explorer, incomingNames[0]);
                Check(incoming != null, "资源管理器实际多选项目可定位");
                var dropPoint = HeaderCenter(first);
                Check(WindowsDesktop.WindowFromPoint(new WindowsDesktop.POINT { X = (int)dropPoint.X, Y = (int)dropPoint.Y }) == first.Handle, "拖入目标未被其他窗口遮挡");
                await PhysicalDrag(Center(incoming!.Current.BoundingRectangle), HeaderCenter(first));
                await WaitUntil(() => Directory.Exists(Path.Combine(firstPath, incomingNames[2])) && runtime.ActiveMove == null);
                CloseResults();
                Check(!File.Exists(Path.Combine(explorerPath, incomingNames[0])) && !File.Exists(Path.Combine(explorerPath, incomingNames[1])) && !Directory.Exists(Path.Combine(explorerPath, incomingNames[2])), "Explorer 多选拖入后全部源消失");
                Check(File.ReadAllBytes(Path.Combine(firstPath, incomingNames[0])).SequenceEqual(new byte[] { 0, 42, 255 }) && File.ReadAllText(Path.Combine(firstPath, incomingNames[2], "内容.txt")) == "普通子目录内容", "Explorer 拖入文件及子目录字节一致");
                Check(File.Exists(Path.Combine(fixture, "快捷方式目标.txt")), "只移动真实 .lnk，指向目标未移动");
                Check(workspace.Snapshot.Folders.First().FileCount == 2 && workspace.Snapshot.Folders.Count == 2, "拖入更新计数，子文件夹不转换为 Folder");

                await DragFolderItems(first, incomingNames, HeaderCenter(second));
                await WaitUntil(() => workspace.Snapshot.Folders.Last().FileCount == 2 && runtime.ActiveMove == null);
                CloseResults();
                Check(!File.Exists(Path.Combine(firstPath, incomingNames[0])) && File.Exists(Path.Combine(secondPath, incomingNames[0])), "Folder 间真实多选仅移动一次");
                SetForegroundWindow(explorer);
                var explorerBlank = ExplorerBackground(explorer);
                // 前台切换异步完成；先确认实际命中 Explorer，再核对目标解析。
                await WaitUntil(() =>
                {
                    var hit = WindowsDesktop.WindowFromPoint(new WindowsDesktop.POINT { X = (int)explorerBlank.X, Y = (int)explorerBlank.Y });
                    return hit == explorer || WindowsDesktop.IsChild(explorer, hit);
                });
                SetCursorPos((int)explorerBlank.X, (int)explorerBlank.Y);
                var detected = FileDrag.TargetAtCursor();
                Check(detected?.Path == explorerPath, "鼠标所指 Explorer 内容区识别实际目标目录");
                await DragFolderItems(second, incomingNames, explorerBlank);
                await WaitUntil(() => File.Exists(Path.Combine(explorerPath, incomingNames[0])) && runtime.ActiveMove == null);
                CloseResults();
                Check(workspace.Snapshot.Folders.Last().Entries.Count == 0 && File.ReadAllBytes(Path.Combine(explorerPath, incomingNames[0])).SequenceEqual(new byte[] { 0, 42, 255 }), "拖出 Explorer 明确目标，真实字节与源消失一致");

                // 桌面夹具均有随机前缀，不选择或改动其他桌面内容。
                File.WriteAllBytes(Path.Combine(desktop, desktopNames[0]), [7, 0, 255]);
                MakeShortcut(Path.Combine(desktop, desktopNames[1]), fixture);
                Directory.CreateDirectory(Path.Combine(desktop, desktopNames[2]));
                File.WriteAllText(Path.Combine(desktop, desktopNames[2], "内容.txt"), "桌面子目录");
                SHChangeNotify(0x1000, 5, desktop, IntPtr.Zero);
                ShowWindow(explorer, 6);
                await Task.Delay(1200);
                // 把夹具 Folder 放到右侧，避免遮挡 Windows 自动安排在左侧的桌面源图标。
                await PositionFolder(first, screen.X + screen.Width - 320, screen.Y + screen.Height - 340);
                await PositionFolder(second, screen.X + screen.Width - 320, screen.Y + 20);
                var desktopView = AutomationElement.FromHandle(WindowsDesktop.Host());
                AutomationElement? desktopItem = null;
                foreach (var name in desktopNames)
                {
                    AutomationElement? item = null;
                    await WaitUntil(() => (item = FindItem(desktopView, name, Path.GetFileNameWithoutExtension(name))) != null);
                    Check(item != null, "实际桌面夹具可定位：" + name);
                    var selection = (SelectionItemPattern)item!.GetCurrentPattern(SelectionItemPattern.Pattern);
                    if (desktopItem == null) { selection.Select(); desktopItem = item; } else selection.AddToSelection();
                }
                var desktopStart = Center(desktopItem!.Current.BoundingRectangle);
                var sourceWindow = WindowsDesktop.WindowFromPoint(new WindowsDesktop.POINT { X = (int)desktopStart.X, Y = (int)desktopStart.Y });
                Check(sourceWindow != first.Handle && sourceWindow != second.Handle, "真实桌面源图标未被 Folder 遮挡");
                await PhysicalDrag(desktopStart, HeaderCenter(first));
                await WaitUntil(() => workspace.Snapshot.Folders.First().FileCount == 2 && runtime.ActiveMove == null);
                CloseResults();
                Check(desktopNames.All(name => !File.Exists(Path.Combine(desktop, name)) && !Directory.Exists(Path.Combine(desktop, name))), "实际桌面文件快捷方式子目录多选拖入");
                var blank = DesktopBackground();
                await DragFolderItems(first, desktopNames, blank);
                await WaitUntil(() => File.Exists(Path.Combine(desktop, desktopNames[0])) && runtime.ActiveMove == null);
                CloseResults();
                Check(File.ReadAllBytes(Path.Combine(desktop, desktopNames[0])).SequenceEqual(new byte[] { 7, 0, 255 }) && File.ReadAllText(Path.Combine(desktop, desktopNames[2], "内容.txt")) == "桌面子目录", "多选拖出至 Windows 实际桌面，字节一致");
                Check(workspace.Snapshot.Folders.First().Entries.Count == 0, "桌面移出同步源展示与计数");
                Check(desktop == Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "桌面目录由 Windows 获取，兼容重定向");

                var conflictNames = new[] { "先完成.txt", "保留两份.txt", "跳过.lnk", "取消目录", "后续.txt" };
                foreach (var name in conflictNames.Where(name => name != "取消目录")) File.WriteAllText(Path.Combine(firstPath, name), "源内容");
                Directory.CreateDirectory(Path.Combine(firstPath, "取消目录"));
                File.WriteAllText(Path.Combine(secondPath, "保留两份.txt"), "目标原内容");
                File.WriteAllText(Path.Combine(secondPath, "跳过.lnk"), "目标原快捷方式内容");
                Directory.CreateDirectory(Path.Combine(secondPath, "取消目录"));
                await workspace.RefreshAsync(WindowsDesktop.Displays());
                // 新版按可见顺序操作多选，先设置确定的手动顺序以验收完成后再取消。
                foreach (var name in conflictNames)
                    Check((await workspace.ReorderContentsAsync(first.FolderId, [Path.Combine(firstPath, name)], null)).Succeeded, "冲突夹具设置明确手动顺序");
                Check(workspace.Snapshot.Folders.First().Entries.Select(entry => entry.Name).SequenceEqual(conflictNames), "先完成、保留两份、跳过、取消与后续的可见顺序");
                runtime.Render();
                var commands = new System.Collections.Generic.Queue<int>([6, 7, 2]);
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
                var clicked = 0;
                timer.Tick += (_, _) =>
                {
                    var conflictWindow = FindWindow(null, "同名冲突");
                    if (conflictWindow == IntPtr.Zero || commands.Count == 0) return;
                    var command = commands.Dequeue();
                    WindowsDesktop.PostMessage(conflictWindow, 0x111, new IntPtr(command), IntPtr.Zero);
                    clicked++;
                };
                timer.Start();
                try
                {
                    // 当前可见顺序是批量操作次序，先完成项须先于冲突项。
                    await DragFolderItems(first, conflictNames, HeaderCenter(second));
                    await WaitUntil(() => runtime.ActiveMove == null && runtime.LastMoveResult?.Items.Count == 5);
                }
                finally { timer.Stop(); }
                var conflictResult = runtime.LastMoveResult!;
                Check(!app.Windows.OfType<MoveDialog>().Any(), "批次完成后进度自动关闭，详情在设置保留");
                Check(clicked == 3 && conflictResult.Items.Select(item => item.Outcome).SequenceEqual(new[] { Outcome.Success, Outcome.Success, Outcome.Skipped, Outcome.Cancelled, Outcome.Cancelled }), "真实冲突对话框保留两份跳过取消及逐项结果");
                Check(File.ReadAllText(Path.Combine(secondPath, "保留两份.txt")) == "目标原内容" && File.ReadAllText(Path.Combine(secondPath, "保留两份 (2).txt")) == "源内容", "真实拖放编号不覆盖原内容");
                Check(File.Exists(Path.Combine(firstPath, "跳过.lnk")) && File.Exists(Path.Combine(firstPath, "后续.txt")) && !File.Exists(Path.Combine(firstPath, "先完成.txt")), "取消保留已完成及后续未移动项目");
                CloseResults();
                await DragFolderItems(first, ["后续.txt"], HeaderCenter(second), escape: true);
                await WaitUntil(() => runtime.ActiveMove == null && runtime.LastMoveResult?.Items.Count == 1);
                Check(runtime.LastMoveResult!.Items.Single().Outcome == Outcome.Cancelled && File.Exists(Path.Combine(firstPath, "后续.txt")), "原生拖出 Esc 取消不凭效果标志删除源");
                CloseResults();

                var denied = new DirectoryInfo(Path.Combine(fixture, "拒绝写入"));
                denied.Create();
                var permissions = denied.GetAccessControl();
                var originalPermissions = denied.GetAccessControl();
                var rule = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.CreateFiles | FileSystemRights.CreateDirectories, AccessControlType.Deny);
                permissions.AddAccessRule(rule);
                denied.SetAccessControl(permissions);
                try
                {
                    var deniedResult = await workspace.MoveAsync([Path.Combine(firstPath, "后续.txt")], MoveTarget.Directory(denied.FullName));
                    Check(deniedResult.Items.Single().Outcome == Outcome.Failed && File.ReadAllText(Path.Combine(firstPath, "后续.txt")) == "源内容", "真实 ACL 权限不足逐项失败并保留源字节");
                }
                finally { permissions.SetSecurityDescriptorBinaryForm(originalPermissions.GetSecurityDescriptorBinaryForm(), AccessControlSections.Access); denied.SetAccessControl(permissions); }
                exit = 0;
            }
            catch (Exception e)
            {
                File.AppendAllText(log, "失败：" + e + "\n");
                foreach (var window in app.Windows.OfType<MoveDialog>())
                    if (window.Result != null) foreach (var item in window.Result.Items) File.AppendAllText(log, $"实际移动结果：{item.SourcePath} {item.Outcome} {item.Message}\n");
                var bounds = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
                using var bitmap = new System.Drawing.Bitmap(bounds.Width, bounds.Height);
                using var graphics = System.Drawing.Graphics.FromImage(bitmap);
                graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);
                bitmap.Save(Path.Combine(evidence, "07-会话失败.png"));
            }
            finally
            {
                if (explorer != IntPtr.Zero) WindowsDesktop.PostMessage(explorer, 0x10, IntPtr.Zero, IntPtr.Zero);
                CloseResults();
                runtime?.Dispose();
                DesktopVisibility(false);
                app.Shutdown();
            }
        };
        try { app.Run(); }
        finally
        {
            foreach (var name in desktopNames)
            {
                var path = Path.GetFullPath(Path.Combine(desktop, name));
                if (!string.Equals(Path.GetDirectoryName(path), Path.TrimEndingDirectorySeparator(Path.GetFullPath(desktop)), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(path).StartsWith(prefix, StringComparison.Ordinal)) throw new IOException("拒绝清理非夹具桌面内容。");
                if (File.Exists(path)) File.Delete(path);
                if (Directory.Exists(path)) Directory.Delete(path, true);
            }
            var full = Path.GetFullPath(fixture);
            if (!full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("Kage-file-move-", StringComparison.Ordinal)) throw new IOException("拒绝清理隔离目录外内容。");
            Directory.Delete(full, true);
        }
        return exit;

        void Check(bool condition, string description) { if (!condition) throw new Exception(description); File.AppendAllText(log, "通过：" + description + "\n"); }
        void CloseResults() { foreach (var window in app.Windows.OfType<MoveDialog>().ToArray()) if (window.Result != null) window.Close(); }
        async Task DragFolderItems(FolderHeader header, string[] names, Point destination, bool escape = false)
        {
            await header.Contents.IconsLoaded;
            header.UpdateLayout();
            var items = header.Contents.Items;
            items.SelectedItems.Clear();
            foreach (var name in names)
                items.Items.Cast<ListBoxItem>().Single(item => Path.GetFileName((string)item.Tag) == name).IsSelected = true;
            Check(items.SelectedItems.Count == names.Length, "正式展示部分实际多选集合");
            var selected = (ListBoxItem)items.SelectedItems[0]!;
            var start = selected.PointToScreen(new Point(selected.ActualWidth / 2, selected.ActualHeight / 2));
            await PhysicalDrag(start, destination, escape);
        }
        async Task PositionFolder(FolderHeader header, int x, int y)
        {
            var edit = runtime!.BeginInteraction(header.FolderId)!;
            edit.BeginDrag(header.Record.X, header.Record.Y);
            edit.DragTo(header.Record.X, y);
            edit.DragTo(x, y);
            runtime.Preview(edit);
            await runtime.CommitInteractionAsync(edit);
        }
    }

    private static Point HeaderCenter(FolderHeader header) { WindowsDesktop.GetWindowRect(header.Handle, out var rectangle); return new(rectangle.Left + 70, rectangle.Top + 24); }
    private static Point Center(Rect rectangle) => new(rectangle.Left + rectangle.Width / 2, rectangle.Top + rectangle.Height / 2);
    private static AutomationElement? FindItem(AutomationElement root, params string[] names) => root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem)).Cast<AutomationElement>().FirstOrDefault(item => names.Contains(item.Current.Name));
    private static AutomationElement? ShellItem(IntPtr window, string name) => FindItem(AutomationElement.FromHandle(window), name, Path.GetFileNameWithoutExtension(name));
    internal static Point ExplorerBackground(IntPtr window)
    {
        var list = AutomationElement.FromHandle(window).FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.List));
        if (list == null) throw new Exception("Explorer 内容视图未找到。");
        var rectangle = list.Current.BoundingRectangle;
        return new(rectangle.Right - 40, rectangle.Bottom - 40);
    }
    private static Point DesktopBackground()
    {
        var area = WindowsDesktop.Displays().First();
        for (var y = area.Y + area.Height / 2; y < area.Y + area.Height - 150; y += 100)
        for (var x = area.X + area.Width / 2; x < area.X + area.Width - 300; x += 100)
        {
            SetCursorPos(x, y);
            if (FileDrag.TargetAtCursor()?.IsDesktop == true) return new(x, y);
        }
        throw new Exception("未找到桌面空白位置。");
    }
    internal static async Task PhysicalDrag(Point source, Point destination, bool escape = false)
    {
        SetCursorPos((int)source.X, (int)source.Y);
        mouse_event(2, 0, 0, 0, UIntPtr.Zero);
        var input = Task.Run(async () =>
        {
            await Task.Delay(250);
            for (var step = 1; step <= 18; step++)
            {
                SetCursorPos((int)(source.X + (destination.X - source.X) * step / 18), (int)(source.Y + (destination.Y - source.Y) * step / 18));
                await Task.Delay(45);
            }
            if (escape)
            {
                WindowsDesktop.keybd_event(0x1B, 0, 0, UIntPtr.Zero);
                await Task.Delay(80);
                WindowsDesktop.keybd_event(0x1B, 0, 2, UIntPtr.Zero);
            }
            mouse_event(4, 0, 0, 0, UIntPtr.Zero);
        });
        await input;
        await Task.Delay(250);
    }
    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(18);
        while (DateTime.UtcNow < deadline) { if (condition()) return; await Task.Delay(100); }
        throw new TimeoutException("真实拖放未在时限内完成。");
    }
    internal static IntPtr ExplorerWindow(string path) => WithExplorer(path, browser => new IntPtr((long)browser.HWND));
    internal static void SelectShellItems(string path, string[] names) => WithExplorer(path, browser =>
    {
        dynamic document = browser.Document;
        dynamic folder = document.Folder;
        try
        {
            for (var i = 0; i < names.Length; i++)
            {
                dynamic item = folder.ParseName(names[i]);
                try { document.SelectItem(item, i == 0 ? 29 : 9); }
                finally { Marshal.FinalReleaseComObject(item); }
            }
        }
        finally { Marshal.FinalReleaseComObject(folder); Marshal.FinalReleaseComObject(document); }
        return IntPtr.Zero;
    });
    private static IntPtr WithExplorer(string path, Func<dynamic, IntPtr> action)
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
        dynamic windows = shell.Windows();
        try
        {
            for (var i = 0; i < (int)windows.Count; i++)
            {
                dynamic browser = windows.Item(i);
                if (browser == null) continue;
                try { string location = browser.LocationURL; if (Uri.TryCreate(location, UriKind.Absolute, out var uri) && uri.IsFile && string.Equals(Path.GetFullPath(uri.LocalPath), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)) return action(browser); }
                finally { Marshal.FinalReleaseComObject(browser); }
            }
        }
        finally { Marshal.FinalReleaseComObject(windows); Marshal.FinalReleaseComObject(shell); }
        return IntPtr.Zero;
    }
    private static void MakeShortcut(string path, string fixture)
    {
        var target = Path.Combine(fixture, "快捷方式目标.txt");
        File.WriteAllText(target, "指向文件保持原位");
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        dynamic shortcut = shell.CreateShortcut(path);
        try { shortcut.TargetPath = target; shortcut.Save(); }
        finally { Marshal.FinalReleaseComObject(shortcut); Marshal.FinalReleaseComObject(shell); }
    }
    internal static void DesktopVisibility(bool minimize)
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
        try { if (minimize) shell.MinimizeAll(); else shell.UndoMinimizeAll(); }
        finally { Marshal.FinalReleaseComObject(shell); }
    }
    private sealed class NoStartup : IStartupRegistration
    {
        public string LaunchCommand => "隔离移动检查不注册自启";
        public string? ReadCommand() => null;
        public void WriteCommand(string? command) => throw new InvalidOperationException("不允许检查修改自启。");
    }
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string? className, string? title);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern void SHChangeNotify(uint eventId, uint flags, string path, IntPtr other);
}
