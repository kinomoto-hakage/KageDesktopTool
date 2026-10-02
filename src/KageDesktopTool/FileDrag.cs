using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Kage.Workspace;

namespace Kage.Desktop;

internal static class FileDrag
{
    internal static void Receive(FolderHeader header)
    {
        header.AllowDrop = true;
        header.PreviewDragOver += (_, e) =>
        {
            e.Effects = !Runtime.Current.Exiting && (!Runtime.Current.Moving || Runtime.Current.DraggingFiles) && !Runtime.Current.Interacting && !Runtime.Current.Workspace.Snapshot.RecoveryRequired && e.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        };
        header.PreviewDrop += async (_, e) =>
        {
            // 本程序拥有实际移动；不给外部源全局 Move 标志，避免删除跳过或失败的源。
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length != 0)
                await Runtime.Current.MoveFilesAsync(paths, MoveTarget.Folder(header.FolderId));
        };
    }

    internal static void Send(ListBox items)
    {
        Point? origin = null;
        ListBoxItem? pressed = null;
        items.PreviewMouseLeftButtonDown += (_, e) =>
        {
            pressed = FolderHeader.FindParent<ListBoxItem>(e.OriginalSource as DependencyObject);
            origin = pressed == null ? null : e.GetPosition(items);
            // 在已有多选项目上起拖时保留选择；普通点击在释放时仍可收拢为单选。
            if (pressed?.IsSelected == true && Keyboard.Modifiers == ModifierKeys.None) e.Handled = true;
        };
        items.PreviewMouseLeftButtonUp += (_, _) =>
        {
            if (origin != null && pressed != null && Keyboard.Modifiers == ModifierKeys.None)
            { items.SelectedItems.Clear(); pressed.IsSelected = true; }
            origin = null;
            pressed = null;
        };
        items.PreviewMouseMove += async (_, e) =>
        {
            if (origin == null || e.LeftButton != MouseButtonState.Pressed || Runtime.Current.Moving) return;
            var position = e.GetPosition(items);
            if (Math.Abs(position.X - origin.Value.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(position.Y - origin.Value.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            origin = null;
            var paths = items.SelectedItems.Cast<ListBoxItem>().Select(item => (string)item.Tag).ToArray();
            if (paths.Length == 0) return;
            e.Handled = true;
            await DragAsync(items, paths);
        };
    }

    internal static async Task DragAsync(FrameworkElement source, string[] paths)
    {
        MoveTarget? target = null;
        string? error = null;
        var released = false;
        var escaped = false;
        void Continue(object sender, QueryContinueDragEventArgs e)
        {
            if (e.EscapePressed) { escaped = true; e.Action = DragAction.Cancel; e.Handled = true; return; }
            if ((e.KeyStates & DragDropKeyStates.LeftMouseButton) != 0) return;
            released = true;
            try { target = TargetAtCursor(); }
            catch (Exception exception) { error = exception.Message; }
            // 结束 OLE 预览而不交付原生 Drop，随后通过共用 interface 只执行一次移动。
            e.Action = DragAction.Cancel;
            e.Handled = true;
        }
        source.QueryContinueDrag += Continue;
        Runtime.Current.DraggingFiles = true;
        try
        {
            var data = new DataObject(DataFormats.FileDrop, paths);
            data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(2)));
            _ = DragDrop.DoDragDrop(source, data, DragDropEffects.Move);
        }
        finally { source.QueryContinueDrag -= Continue; Runtime.Current.DraggingFiles = false; }
        if (escaped || !released) await Runtime.Current.MoveFilesAsync(paths, MoveTarget.Directory(""), cancelled: true);
        else if (target != null) await Runtime.Current.MoveFilesAsync(paths, target);
        else await Runtime.Current.MoveFilesAsync(paths, MoveTarget.Directory(""), targetError: error ?? "请选择桌面空白处、Folder 或资源管理器中的实际目录内容区。");
    }

    internal static MoveTarget? TargetAtCursor()
    {
        GetCursorPos(out var point);
        var window = WindowsDesktop.WindowFromPoint(point);
        foreach (var header in Runtime.Current.Headers.Values)
            if (window == header.Handle || WindowsDesktop.IsChild(header.Handle, window)) return MoveTarget.Folder(header.FolderId);
        // 只认原生内容视图，排除地址栏、导航树和其他应用窗口。
        var inView = false;
        for (var current = window; current != IntPtr.Zero; current = WindowsDesktop.GetParent(current))
        {
            var name = new StringBuilder(256);
            GetClassName(current, name, name.Capacity);
            if (name.ToString() == "SHELLDLL_DefView") { inView = true; break; }
        }
        if (!inView) return null;
        var element = AutomationElement.FromPoint(new Point(point.X, point.Y));
        string? directoryName = null;
        for (var current = element; current != null; current = TreeWalker.ControlViewWalker.GetParent(current))
        {
            if (current.Current.ControlType == ControlType.ListItem) { directoryName = current.Current.Name; break; }
            if (current.Current.ControlType == ControlType.List) break;
        }
        var host = WindowsDesktop.Host();
        if (window == host || WindowsDesktop.IsChild(host, window))
        {
            if (directoryName == null) return MoveTarget.Desktop;
            var desktopItem = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), directoryName);
            return Directory.Exists(desktopItem) ? MoveTarget.Directory(desktopItem) : null;
        }
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
        dynamic windows = shell.Windows();
        try
        {
            for (var i = 0; i < (int)windows.Count; i++)
            {
                dynamic browser = windows.Item(i);
                if (browser == null) continue;
                try
                {
                    var handle = new IntPtr((long)browser.HWND);
                    if (window != handle && !WindowsDesktop.IsChild(handle, window)) continue;
                    dynamic document = browser.Document;
                    dynamic folder = document.Folder;
                    dynamic self = folder.Self;
                    try
                    {
                        string actual = self.Path;
                        if (directoryName != null) actual = Path.Combine(actual, directoryName);
                        if (Path.IsPathFullyQualified(actual) && Directory.Exists(actual)) return MoveTarget.Directory(actual);
                        throw new IOException("资源管理器目标不是可用的实际目录；请选择明确的文件系统目录。");
                    }
                    finally { Marshal.FinalReleaseComObject(self); Marshal.FinalReleaseComObject(folder); Marshal.FinalReleaseComObject(document); }
                }
                finally { Marshal.FinalReleaseComObject(browser); }
            }
        }
        finally { Marshal.FinalReleaseComObject(windows); Marshal.FinalReleaseComObject(shell); }
        return null;
    }

    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out WindowsDesktop.POINT point);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int capacity);
}
