using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Kage.Workspace;

public sealed class WindowsDirectoryTransfer : IRootDirectoryTransfer
{
    public void Move(string source, string destination, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        WindowsPaths.CreateExclusive(destination);
        CopyDirectoryAcrossVolumes(source, destination, cancellation);
    }

    public void Restore(string source, string destination, CancellationToken cancellation)
    {
        // 恢复跨盘部分复制／源部分移除。只补回缺失项，同名字节不同则保留两边。
        if (!Directory.Exists(destination)) return;
        if (!Directory.Exists(source)) WindowsPaths.CreateExclusive(source);
        MergeBack(destination, source, cancellation);
        RemoveCopiedContents(destination, source);
    }

    private static void MergeBack(string destination, string source, CancellationToken cancellation)
    {
        if ((File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0
            || (File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0) throw new IOException("恢复目录出现重解析路径，保留两边。");
        foreach (var stream in NamedStreams(destination).Keys)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!NamedStreams(source).ContainsKey(stream))
            {
                using var input = File.OpenRead(destination + stream);
                using var output = new FileStream(source + stream, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                input.CopyTo(output);
                output.Flush(true);
            }
        }
        foreach (var entry in new DirectoryInfo(destination).EnumerateFileSystemInfos())
        {
            cancellation.ThrowIfCancellationRequested();
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException($"恢复内容出现重解析项目：{entry.FullName}");
            var original = Path.Combine(source, entry.Name);
            if (entry is DirectoryInfo)
            {
                if (!Directory.Exists(original)) WindowsPaths.CreateExclusive(original);
                MergeBack(entry.FullName, original, cancellation);
            }
            else if (!File.Exists(original))
            {
                CopyAndFlush(entry.FullName, original);
            }
        }
        // 删除目标副本前先确认其全部内容已在旧位置，允许旧位置保留外部新增项。
        VerifySubset(destination, source);
    }
    internal static void CopyDirectoryAcrossVolumes(string source, string destination, CancellationToken cancellation)
    {
        // 跨盘目录先完整复制，任何失败保留两边。目标采用排他创建，绝不合并或覆盖。
        CopyContents(source, destination, cancellation);
        cancellation.ThrowIfCancellationRequested();
        VerifyTree(source, destination);
        // 仅删除本次已完整复制的文件；不递归删除复制期间外部新增的内容。
        RemoveCopiedContents(source, destination);
    }

    private static void VerifyTree(string source, string destination)
    {
        VerifyNamedStreams(source, destination);
        var originals = new DirectoryInfo(source).GetFileSystemInfos();
        var copies = new DirectoryInfo(destination).GetFileSystemInfos();
        if (originals.Length != copies.Length) throw new IOException($"复制后项目清单改变，保留源与目标：{source}");
        foreach (var original in originals)
        {
            var target = Path.Combine(destination, original.Name);
            if ((original.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException($"不支持重解析内容：{original.FullName}");
            if (original is DirectoryInfo) VerifyTree(original.FullName, target);
            else
            {
                using var input = File.OpenRead(original.FullName);
                using var output = File.OpenRead(target);
                if (!SameBytes(input, output))
                    throw new IOException($"复制后字节不一致，保留源与目标：{original.FullName}");
                VerifyNamedStreams(original.FullName, target);
            }
        }
    }

    private static void CopyContents(string source, string destination, CancellationToken cancellation)
    {
        CopyDirectoryStreams(source, destination, cancellation);
        // 先复制归属标识，失败恢复时能识别本次排他创建的目标。
        foreach (var entry in new DirectoryInfo(source).EnumerateFileSystemInfos().OrderBy(entry => entry.Name == WindowsPaths.IdentityFile ? 0 : 1))
        {
            cancellation.ThrowIfCancellationRequested();
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException($"跨盘目录含重解析项目，未删除源：{entry.FullName}");
            var target = Path.Combine(destination, entry.Name);
            if (entry is DirectoryInfo)
            {
                WindowsPaths.CreateExclusive(target);
                CopyContents(entry.FullName, target, cancellation);
            }
            else
            {
                // 原生复制保留 NTFS 命名流及文件属性；默认流 CopyTo 会丢失用户内容。
                CopyAndFlush(entry.FullName, target);
            }
        }
    }

    private static void VerifySubset(string source, string destination)
    {
        VerifyNamedStreams(source, destination);
        foreach (var entry in new DirectoryInfo(source).EnumerateFileSystemInfos())
        {
            var target = Path.Combine(destination, entry.Name);
            if (entry is DirectoryInfo) VerifySubset(entry.FullName, target);
            else
            {
                using var input = File.OpenRead(entry.FullName);
                using var output = File.OpenRead(target);
                if (!SameBytes(input, output))
                    throw new IOException($"恢复时同名内容不同，未覆盖：{target}");
                VerifyNamedStreams(entry.FullName, target);
            }
        }
    }

    private static void RemoveCopiedContents(string source, string destination)
    {
        // 中断时尚存内容仍需可确认归属，目录标识最后才移除。
        foreach (var entry in new DirectoryInfo(source).EnumerateFileSystemInfos().OrderBy(entry => entry.Name == WindowsPaths.IdentityFile ? 1 : 0))
        {
            var original = entry.FullName;
            var copied = Path.Combine(destination, entry.Name);
            if (entry is DirectoryInfo) RemoveCopiedContents(original, copied);
            else
            {
                // 校验与删除持有同一排他句柄，避免校验后外部替换源文件造成误删。
                using var handle = CreateFile(original, 0x80010000, 0, IntPtr.Zero, 3, 0, IntPtr.Zero);
                if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
                using (var input = new FileStream(handle, FileAccess.Read))
                using (var copy = File.OpenRead(copied))
                {
                    if (!SameBytes(input, copy))
                        throw new IOException($"复制后源内容发生变化，保留源与目标：{original}");
                    VerifyNamedStreams(original, copied);
                    if ((entry.Attributes & FileAttributes.ReadOnly) != 0)
                    {
                        // 仅对已验证副本对应的源，使用删除句柄忽略只读属性，不更改用户内容属性。
                        var disposition = new FileDispositionEx { Flags = 0x1 | 0x10 };
                        if (!SetFileInformationByHandle(handle, 21, ref disposition, Marshal.SizeOf<FileDispositionEx>()))
                            throw new Win32Exception(Marshal.GetLastWin32Error());
                    }
                    else
                    {
                        var disposition = new FileDisposition { Delete = true };
                        if (!SetFileInformationByHandle(handle, 4, ref disposition, Marshal.SizeOf<FileDisposition>()))
                            throw new Win32Exception(Marshal.GetLastWin32Error());
                    }
                }
            }
        }
        VerifyNamedStreams(source, destination);
        Directory.Delete(source, false);
    }

    private static void CopyDirectoryStreams(string source, string destination, CancellationToken cancellation)
    {
        foreach (var name in NamedStreams(source).Keys)
        {
            cancellation.ThrowIfCancellationRequested();
            using var input = File.OpenRead(source + name);
            using var output = new FileStream(destination + name, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            input.CopyTo(output);
            output.Flush(true);
        }
    }

    private static void VerifyNamedStreams(string source, string destination)
    {
        var original = NamedStreams(source);
        var copied = NamedStreams(destination);
        if (original.Count != copied.Count || original.Any(stream => !copied.TryGetValue(stream.Key, out var size) || stream.Value != size))
            throw new IOException($"命名数据流未完整复制，保留源与目标：{source}");
        foreach (var name in original.Keys)
        {
            using var input = new FileStream(source + name, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            using var output = File.OpenRead(destination + name);
            if (!SameBytes(input, output))
                throw new IOException($"命名数据流字节不一致，保留源与目标：{source}{name}");
        }
    }

    private static Dictionary<string, long> NamedStreams(string path)
    {
        var streams = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var handle = FindFirstStream(path, 0, out var data, 0);
        if (handle == new IntPtr(-1))
        {
            var error = Marshal.GetLastWin32Error();
            if (error is 38 or 87) return streams;
            throw new Win32Exception(error);
        }
        try
        {
            do { if (data.Name != "::$DATA") streams.Add(data.Name, data.Size); }
            while (FindNextStream(handle, out data));
            var error = Marshal.GetLastWin32Error();
            if (error != 38) throw new Win32Exception(error);
            return streams;
        }
        finally { FindClose(handle); }
    }

    private static bool SameBytes(Stream original, Stream copied)
        => original.Length == copied.Length && System.Security.Cryptography.SHA256.HashData(original).SequenceEqual(System.Security.Cryptography.SHA256.HashData(copied));

    private static void CopyAndFlush(string source, string destination)
    {
        if (!CopyFile(source, destination, true)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var attributes = File.GetAttributes(destination);
        var readOnly = (attributes & FileAttributes.ReadOnly) != 0;
        if (readOnly) File.SetAttributes(destination, attributes & ~FileAttributes.ReadOnly);
        try
        {
            using var output = new FileStream(destination, FileMode.Open, FileAccess.Write, FileShare.None);
            output.Flush(true);
        }
        finally { if (readOnly) File.SetAttributes(destination, attributes); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct FileDisposition { [MarshalAs(UnmanagedType.Bool)] internal bool Delete; }
    [StructLayout(LayoutKind.Sequential)] private struct FileDispositionEx { internal uint Flags; }
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int informationClass, ref FileDisposition information, int size);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int informationClass, ref FileDispositionEx information, int size);
    [DllImport("kernel32.dll", EntryPoint = "CopyFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CopyFile(string source, string destination, bool failIfExists);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StreamData { internal long Size; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 296)] internal string Name; }
    [DllImport("kernel32.dll", EntryPoint = "FindFirstStreamW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindFirstStream(string path, int level, out StreamData data, uint flags);
    [DllImport("kernel32.dll", EntryPoint = "FindNextStreamW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool FindNextStream(IntPtr handle, out StreamData data);
    [DllImport("kernel32.dll")] private static extern bool FindClose(IntPtr handle);
}
