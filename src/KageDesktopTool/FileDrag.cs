using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
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

    internal static async Task DragAsync(FrameworkElement source, string[] paths, FolderContents? contents = null)
    {
        MoveTarget? target = null;
        string? error = null;
        var released = false;
        var escaped = false;
        string? before = null;
        var reorder = false;
        void Feedback(object sender, GiveFeedbackEventArgs e)
        {
            if (contents == null) return;
            GetCursorPos(out var cursor);
            var screen = new Point(cursor.X, cursor.Y);
            if (contents.ContainsScreenPoint(screen)) contents.InsertionAt(screen, true);
            else contents.Feedback.Children.Clear();
        }
        void Continue(object sender, QueryContinueDragEventArgs e)
        {
            if (e.EscapePressed) { escaped = true; e.Action = DragAction.Cancel; e.Handled = true; return; }
            if ((e.KeyStates & DragDropKeyStates.LeftMouseButton) != 0) return;
            released = true;
            try
            {
                target = TargetAtCursor();
                GetCursorPos(out var cursor);
                var screen = new Point(cursor.X, cursor.Y);
                reorder = contents != null && target?.FolderId == contents.FolderId && contents.ContainsScreenPoint(screen);
                if (reorder) before = contents!.InsertionAt(screen, false);
            }
            catch (Exception exception) { error = exception.Message; }
            // 结束 OLE 预览而不交付原生 Drop，随后通过共用 interface 只执行一次移动。
            e.Action = DragAction.Cancel;
            e.Handled = true;
        }
        source.QueryContinueDrag += Continue;
        source.GiveFeedback += Feedback;
        Runtime.Current.DraggingFiles = true;
        try
        {
            var data = new DataObject(DataFormats.FileDrop, paths);
            data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(2)));
            _ = DragDrop.DoDragDrop(source, data, DragDropEffects.Move);
        }
        finally
        {
            source.QueryContinueDrag -= Continue; source.GiveFeedback -= Feedback;
            contents?.Feedback.Children.Clear();
            Runtime.Current.DraggingFiles = false;
        }
        if (reorder && !escaped && released)
        {
            var result = await Runtime.Current.Workspace.ReorderContentsAsync(contents!.FolderId, paths, before);
            Runtime.Current.Render();
            Runtime.Current.Complete("自定义排序", result);
            return;
        }
        if (!escaped && target?.FolderId == contents?.FolderId && contents != null) return;
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
        return WindowsDesktop.ContentTargetAt(point);
    }

    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out WindowsDesktop.POINT point);
}
