using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Kage.Workspace;

internal static class WindowsPaths
{
    internal const string IdentityFile = ".kage-folder-id";
    internal static void Name(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 255 || name is "." or ".." || name.EndsWith(' ') || name.EndsWith('.')
            || name.Any(c => c < 32 || "<>:\"/\\|?*".Contains(c))) throw new ArgumentException("名称不符合 Windows 文件夹规则。");
        var device = name.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
        if (device is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$"
            || System.Text.RegularExpressions.Regex.IsMatch(device, "^(COM|LPT)[1-9¹²³]$")) throw new ArgumentException("不能使用 Windows 保留设备名称。");
    }

    internal static string Root(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root)) throw new ArgumentException("请选择完整的 Windows 目录路径。");
        var full = Path.GetFullPath(root);
        foreach (var component in full[Path.GetPathRoot(full)!.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)) Name(component);
        return Path.TrimEndingDirectorySeparator(full);
    }

    internal static void CheckRoot(string root, bool create)
    {
        Root(root);
        if (create) Directory.CreateDirectory(root);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"存储根目录不存在或无法访问：{root}");
        // 探针只属于本次操作，不接触个人内容。
        using (var entries = Directory.EnumerateFileSystemEntries(root).GetEnumerator()) _ = entries.MoveNext();
        if (create)
        {
            var probe = Path.Combine(root, ".kage-probe-" + Guid.NewGuid().ToString("N"));
            using var file = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
            file.WriteByte(0);
            file.Flush(true);
        }
    }

    internal static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);
    internal static void CreateExclusive(string path)
    {
        if (!CreateDirectory(path, IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error(), $"创建内容文件夹失败：{path}");
    }

    internal static bool HasIdentity(string path, Guid id)
    {
        var marker = Path.Combine(path, IdentityFile);
        return File.Exists(marker) && Guid.TryParseExact(File.ReadAllText(marker), "N", out var actual) && actual == id;
    }

    internal static void WriteIdentity(string path, Guid id)
    {
        if (HasIdentity(path, id)) return;
        var marker = Path.Combine(path, IdentityFile);
        using (var stream = new FileStream(marker, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(id.ToString("N"));
            stream.Write(bytes);
            stream.Flush(true);
        }
        File.SetAttributes(marker, File.GetAttributes(marker) | FileAttributes.Hidden);
    }
    [DllImport("kernel32.dll", EntryPoint = "CreateDirectoryW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateDirectory(string path, IntPtr security);
}
