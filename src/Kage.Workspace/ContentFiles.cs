using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Kage.Workspace;

internal static class ContentFiles
{
    internal static ContentEntry[] Read(string path) => new DirectoryInfo(path).EnumerateFileSystemInfos()
        .Where(info => !WindowsPaths.IsIdentityFile(info.FullName)).Select(info =>
        {
            long? modified = null, length = null;
            try { modified = info.LastWriteTimeUtc.Ticks; } catch (IOException) { } catch (UnauthorizedAccessException) { }
            if (info is FileInfo file)
                try { length = file.Length; } catch (IOException) { } catch (UnauthorizedAccessException) { }
            return new ContentEntry(info.FullName, info.Name, info is DirectoryInfo, modified, length, Identity(info.FullName));
        }).ToArray();

    private static string? Identity(string path)
    {
        if (!OperatingSystem.IsWindows()) return null;
        // 零访问权限及共享删除，不占用文件；目录使用 BACKUP_SEMANTICS，重解析链接读取自身身份。
        using var handle = CreateFile(path, 0, 7, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
        return !handle.IsInvalid && GetFileInformationByHandle(handle, out var info) && (info.IndexHigh != 0 || info.IndexLow != 0)
            ? $"{info.VolumeSerial:X8}:{info.IndexHigh:X8}{info.IndexLow:X8}" : null;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        internal uint Attributes;
        internal System.Runtime.InteropServices.ComTypes.FILETIME Created, Accessed, Modified;
        internal uint VolumeSerial, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateFileW")]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll")] private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation info);
}
