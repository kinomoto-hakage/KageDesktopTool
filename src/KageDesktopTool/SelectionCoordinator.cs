using System;
using System.Collections.Generic;
using System.Linq;

namespace Kage.Desktop;

// 选择只属于当前交互，不进入工作区配置；注册对象随 Folder 窗口关闭释放。
internal sealed class SelectionCoordinator : IDisposable
{
    private readonly HashSet<FolderContents> contents = new();
    private FolderContents? owner;
    internal long Revision { get; private set; }

    internal void Register(FolderContents view) => contents.Add(view);
    internal void Unregister(FolderContents view)
    {
        Revision++;
        contents.Remove(view);
        if (owner == view) owner = null;
    }
    internal void Claim(FolderContents view)
    {
        Revision++;
        if (!contents.Contains(view) || owner == view) return;
        owner = view;
        foreach (var other in contents.Where(other => other != view)) other.ClearSelection();
    }
    internal void Clear()
    {
        Revision++;
        owner = null;
        foreach (var view in contents) view.ClearSelection();
    }
    internal bool OwnsCommandWindow(IntPtr window) => contents.Any(view => view.OwnsCommandWindow(window));
    public void Dispose()
    {
        Clear();
        contents.Clear();
    }
}
