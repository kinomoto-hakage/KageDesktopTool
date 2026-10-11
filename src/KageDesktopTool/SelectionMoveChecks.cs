using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Kage.Workspace;

namespace Kage.Desktop;

internal static partial class ContentInputChecks
{
    // 从真实点击产生多选，再核对拖动开始后的清除不会改变桌面／Explorer 移动对象。
    private static async Task SelectionMoves(IDesktopWorkspace workspace, Runtime runtime, FolderHeader first,
        string fixture, Action<bool, string> require, Func<FolderHeader, int, bool, Task> click)
    {
        var folderPath = workspace.Snapshot.Folders.Single(folder => folder.Folder.Id == first.FolderId).ActualPath;
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var names = new[] { "Kage-selection-" + Guid.NewGuid().ToString("N") + "-A.bin", "Kage-selection-" + Guid.NewGuid().ToString("N") + "-B.bin" };
        var explorerPath = Path.Combine(fixture, "选择拖动的 Explorer 目标");
        Directory.CreateDirectory(explorerPath);
        IntPtr explorer = IntPtr.Zero;
        try
        {
            using var process = Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = false, ArgumentList = { explorerPath } });
            await WaitUntil(() => (explorer = FileMoveChecks.ExplorerWindow(explorerPath)) != IntPtr.Zero);
            require(explorer != IntPtr.Zero, "选择回归的随机 Explorer 真实目录窗口可定位");
            var area = WindowsDesktop.Displays().First();
            ShowSelectionWindow(explorer, 9);
            WindowsDesktop.SetWindowPos(explorer, IntPtr.Zero, area.X + area.Width / 2, area.Y + 40, area.Width / 2 - 30, area.Height - 150, 0x40);
            SetForegroundWindow(explorer); await Task.Delay(400);
            for (var scenario = 0; scenario < 2; scenario++)
            {
                if (scenario == 1)
                {
                    FileMoveChecks.DesktopVisibility(true);
                    await Task.Delay(400);
                }
                var paths = names.Select(name => Path.Combine(folderPath, name)).ToArray();
                foreach (var path in paths) File.WriteAllBytes(path, [7, 42, 0, 255]);
                await workspace.RefreshAsync(WindowsDesktop.Displays()); runtime.Render(); await first.Contents.IconsLoaded;
                for (var index = 0; index < paths.Length; index++)
                {
                    if (index > 0) keybd_event(0x11, 0, 0, UIntPtr.Zero);
                    var itemIndex = Array.FindIndex(first.Contents.Items.Items.Cast<ListBoxItem>().ToArray(), item => (string)item.Tag == paths[index]);
                    await click(first, itemIndex, false);
                    if (index > 0) keybd_event(0x11, 0, 2, UIntPtr.Zero);
                }
                require(first.Contents.SelectedPaths().ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(paths), "系统 Ctrl 点击产生正确两文件集合");
                Point destination;
                if (scenario == 0)
                {
                    ShowSelectionWindow(explorer, 4);
                    WindowsDesktop.SetWindowPos(explorer, IntPtr.Zero, 0, 0, 0, 0, 0x53);
                    await Task.Delay(200);
                    destination = FileMoveChecks.ExplorerBackground(explorer);
                    var hit = WindowsDesktop.WindowFromPoint(new WindowsDesktop.POINT { X = (int)destination.X, Y = (int)destination.Y });
                    require(hit == explorer || WindowsDesktop.IsChild(explorer, hit), "真实 Explorer 释放位置未被显示桌面隐藏");
                }
                else
                {
                    ShowSelectionWindow(explorer, 6); await Task.Delay(200);
                    destination = FileMoveChecks.DesktopBackground();
                }
                SetCursorPos((int)destination.X, (int)destination.Y);
                var detected = FileDrag.TargetAtCursor();
                require(scenario == 0 ? detected?.Path == explorerPath : detected?.IsDesktop == true,
                    $"释放前核对真实目标：场景 {scenario}；目标 {detected}；坐标 {destination}");
                var selected = first.Contents.Items.Items.Cast<ListBoxItem>().Single(item => (string)item.Tag == paths.Last());
                var icon = (Image)((Grid)selected.Content).Children[0];
                var origin = icon.PointToScreen(new Point(icon.ActualWidth / 2, icon.ActualHeight / 2));
                await Drag(origin, destination, duringDrag: async () =>
                {
                    require(runtime.DraggingFiles, "真实桌面／Explorer 文件拖动已开始");
                    if (scenario == 0)
                    {
                        keybd_event(0x12, 0, 0, UIntPtr.Zero); Key(0x09); keybd_event(0x12, 0, 2, UIntPtr.Zero);
                        await Task.Delay(300);
                        require(GetSelectionForeground() == explorer && first.Contents.SelectedPaths().Length == 0,
                            "释放前真实切换到 Explorer 清除原选择，拖动继续");
                    }
                });
                await WaitUntil(() => !runtime.Moving);
                foreach (var dialog in Application.Current.Windows.OfType<MoveDialog>().ToArray()) dialog.Close();
                var targetPath = scenario == 0 ? explorerPath : desktop;
                require(true, "实际移动结果：" + string.Join("；", runtime.LastMoveResult?.Items.Select(item => item.ToString()) ?? []));
                require(paths.All(path => !File.Exists(path)) && names.All(name => File.Exists(Path.Combine(targetPath, name))
                    && File.ReadAllBytes(Path.Combine(targetPath, name)).SequenceEqual(new byte[] { 7, 42, 0, 255 })),
                    (scenario == 0 ? "Explorer" : "桌面") + "真实多选移出仅移动开始时的两文件，源消失且字节一致");
            }
        }
        finally
        {
            keybd_event(0x11, 0, 2, UIntPtr.Zero);
            if (explorer != IntPtr.Zero) PostMessage(explorer, 0x10, IntPtr.Zero, IntPtr.Zero);
            foreach (var name in names)
            {
                var path = Path.Combine(desktop, name);
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
