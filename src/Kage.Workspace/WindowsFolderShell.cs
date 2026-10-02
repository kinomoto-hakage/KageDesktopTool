using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Kage.Workspace;

// COM 对象只在专用 STA 内创建和释放，不跨线程缓存 RCW。
public sealed class WindowsFolderShell(string? desktopDirectory = null) : IFolderShell
{
    internal static IFolderShell Default { get; } = new WindowsFolderShell();
    public string DesktopDirectory => desktopDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    private static T Sta<T>(Func<T> action)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("需要 Windows Shell。");
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() => { try { result = action(); } catch (Exception e) { error = e; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        return result;
    }

    public void CreateShortcut(string shortcutPath, string targetPath) => Sta(() =>
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var temporary = Path.Combine(Path.GetDirectoryName(shortcutPath)!, ".kage-link-" + Guid.NewGuid().ToString("N") + ".lnk");
        object? shell = null;
        object? link = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true)!);
            dynamic automation = shell!;
            link = automation.CreateShortcut(temporary);
            dynamic shortcut = link;
            shortcut.TargetPath = targetPath;
            shortcut.WorkingDirectory = targetPath;
            shortcut.Description = "Kage 保留的内容文件夹 " + File.ReadAllText(Path.Combine(targetPath, WindowsPaths.IdentityFile));
            shortcut.Save();
            File.Move(temporary, shortcutPath, false);
            return true;
        }
        finally
        {
            Release(link);
            Release(shell);
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    });

    public bool ShortcutTargets(string shortcutPath, string targetPath) => Sta(() =>
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (!File.Exists(shortcutPath)) return false;
        object? shell = null;
        object? link = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true)!);
            dynamic automation = shell!;
            link = automation.CreateShortcut(shortcutPath);
            dynamic shortcut = link;
            string target = shortcut.TargetPath;
            string description = shortcut.Description;
            return string.Equals(Path.GetFullPath(target), Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase)
                && description == "Kage 保留的内容文件夹 " + File.ReadAllText(Path.Combine(targetPath, WindowsPaths.IdentityFile));
        }
        finally { Release(link); Release(shell); }
    });

    public OperationResult Recycle(string contentPath, Guid folderId) => Sta(() =>
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        // 仅本地固定盘；不可回收的网络／可移动目的地直接失败。
        if (new DriveInfo(Path.GetPathRoot(contentPath)!).DriveType != DriveType.Fixed)
            return new OperationResult(Outcome.Failed, "该目录不在支持回收的本地固定磁盘，未删除。", contentPath);
        IFileOperation? operation = null;
        IShellItem? item = null;
        var sink = new RecycleSink();
        try
        {
            operation = (IFileOperation)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("3AD05575-8857-4850-9277-11B85BDB8E09"), true)!)!;
            var iid = typeof(IShellItem).GUID;
            SHCreateItemFromParsingName(contentPath, IntPtr.Zero, ref iid, out item);
            // RECYCLEONDELETE 强制回收，EARLYFAILURE 在错误时终止；不提供永久删除路径。
            operation.SetOperationFlags(0x00080000 | 0x00100000 | 0x0400 | 0x0004 | 0x0010 | 0x0040 | 0x0200);
            operation.DeleteItem(item, sink);
            operation.PerformOperations();
            operation.GetAnyOperationsAborted(out var aborted);
            var recycled = FindRecycledOnSta(contentPath, folderId);
            if (sink.Result >= 0 && sink.Recycled && recycled != null && !Directory.Exists(contentPath))
                return new(Outcome.Success, $"整个内容文件夹已进入回收站：{recycled}", recycled);
            return new(aborted ? Outcome.Cancelled : Outcome.Failed,
                $"回收未确认完成，保留入口。Shell 结果：0x{sink.Result:X8}", contentPath);
        }
        catch (COMException e) { return new(Outcome.Failed, $"Windows 回收失败：{e.Message} (0x{e.HResult:X8})，回调标志 0x{sink.DeleteFlags:X8}", contentPath); }
        finally { Release(item); Release(operation); }
    });

    public string? FindRecycledFolder(string contentPath, Guid folderId) => Sta(() =>
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        return FindRecycledOnSta(contentPath, folderId);
    });

    [SupportedOSPlatform("windows")]
    private static string? FindRecycledOnSta(string contentPath, Guid folderId)
    {
        object? shell = null;
        object? bin = null;
        object? items = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application", true)!);
            dynamic automation = shell!;
            bin = automation.NameSpace(10);
            dynamic recycle = bin!;
            items = recycle.Items();
            dynamic entries = items;
            for (var i = 0; i < (int)entries.Count; i++)
            {
                object? entry = null;
                try
                {
                    entry = entries.Item(i);
                    dynamic recycled = entry;
                    string path = recycled.Path;
                    // 限于源所在盘；隐藏归属标识可区分同名目录，避免按名称猜测完成。
                    if (string.Equals(Path.GetPathRoot(path), Path.GetPathRoot(contentPath), StringComparison.OrdinalIgnoreCase)
                        && Directory.Exists(path) && WindowsPaths.HasIdentity(path, folderId)) return path;
                }
                finally { Release(entry); }
            }
            return null;
        }
        finally { Release(items); Release(bin); Release(shell); }
    }

    [SupportedOSPlatform("windows")]
    private static void Release(object? value) { if (value != null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, IntPtr context, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr context, ref Guid handler, ref Guid iid, out IntPtr result);
        void GetParent(out IShellItem parent);
        void GetDisplayName(uint name, out IntPtr result);
        void GetAttributes(uint mask, out uint attributes);
        void Compare(IShellItem other, uint hint, out int order);
    }

    [ComImport, Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperation
    {
        void Advise(IFileOperationProgressSink sink, out uint cookie);
        void Unadvise(uint cookie);
        void SetOperationFlags(uint flags);
        void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
        void SetProgressDialog(IntPtr dialog);
        void SetProperties(IntPtr properties);
        void SetOwnerWindow(IntPtr owner);
        void ApplyPropertiesToItem(IShellItem item);
        void ApplyPropertiesToItems(IntPtr items);
        void RenameItem(IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name, IFileOperationProgressSink sink);
        void RenameItems(IntPtr items, [MarshalAs(UnmanagedType.LPWStr)] string name);
        void MoveItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string? name, IFileOperationProgressSink sink);
        void MoveItems(IntPtr items, IShellItem destination);
        void CopyItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string? name, IFileOperationProgressSink sink);
        void CopyItems(IntPtr items, IShellItem destination);
        void DeleteItem(IShellItem item, IFileOperationProgressSink sink);
        void DeleteItems(IntPtr items);
        void NewItem(IShellItem destination, uint attributes, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string? template, IFileOperationProgressSink sink);
        void PerformOperations();
        void GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
    }

    [ComVisible(true), Guid("04B0F1A7-9490-44BC-96E1-4296A31252E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperationProgressSink
    {
        [PreserveSig] int StartOperations();
        [PreserveSig] int FinishOperations(int result);
        [PreserveSig] int PreRenameItem(uint flags, IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int PostRenameItem(uint flags, IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name, int result, IShellItem? created);
        [PreserveSig] int PreMoveItem(uint flags, IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int PostMoveItem(uint flags, IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, int result, IShellItem? created);
        [PreserveSig] int PreCopyItem(uint flags, IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int PostCopyItem(uint flags, IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, int result, IShellItem? created);
        [PreserveSig] int PreDeleteItem(uint flags, IShellItem item);
        [PreserveSig] int PostDeleteItem(uint flags, IShellItem item, int result, IShellItem? created);
        [PreserveSig] int PreNewItem(uint flags, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int PostNewItem(uint flags, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string template, uint attributes, int result, IShellItem? created);
        [PreserveSig] int UpdateProgress(uint total, uint completed);
        [PreserveSig] int ResetTimer();
        [PreserveSig] int PauseTimer();
        [PreserveSig] int ResumeTimer();
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class RecycleSink : IFileOperationProgressSink
    {
        internal int Result = unchecked((int)0x80004005);
        internal bool Recycled;
        internal uint DeleteFlags;
        // 未提供可回收标志时终止，绝不允许降级为永久删除。
        public int PreDeleteItem(uint flags, IShellItem item) { DeleteFlags = flags; return (flags & 0x80) != 0 ? 0 : unchecked((int)0x80004004); }
        public int PostDeleteItem(uint flags, IShellItem item, int result, IShellItem? created) { Result = result; Recycled = created != null; return 0; }
        public int StartOperations() => 0;
        public int FinishOperations(int result) => 0;
        public int PreRenameItem(uint flags, IShellItem item, string name) => 0;
        public int PostRenameItem(uint flags, IShellItem item, string name, int result, IShellItem? created) => 0;
        public int PreMoveItem(uint flags, IShellItem item, IShellItem destination, string name) => 0;
        public int PostMoveItem(uint flags, IShellItem item, IShellItem destination, string name, int result, IShellItem? created) => 0;
        public int PreCopyItem(uint flags, IShellItem item, IShellItem destination, string name) => 0;
        public int PostCopyItem(uint flags, IShellItem item, IShellItem destination, string name, int result, IShellItem? created) => 0;
        public int PreNewItem(uint flags, IShellItem destination, string name) => 0;
        public int PostNewItem(uint flags, IShellItem destination, string name, string template, uint attributes, int result, IShellItem? created) => 0;
        public int UpdateProgress(uint total, uint completed) => 0;
        public int ResetTimer() => 0;
        public int PauseTimer() => 0;
        public int ResumeTimer() => 0;
    }
}
