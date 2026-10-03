using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

namespace Kage.Desktop;

internal static class PackageIcons
{
    internal static BitmapSource? Read(IntPtr target, int pixels)
    {
        if (SHGetNameFromIDList(target, 0x80028000, out var pointer) < 0 || pointer == IntPtr.Zero) return null;
        string name;
        try { name = Marshal.PtrToStringUni(pointer) ?? ""; }
        finally { Marshal.FreeCoTaskMem(pointer); }
        var appId = name[(name.LastIndexOf('\\') + 1)..];
        var separator = appId.LastIndexOf('!');
        if (separator < 0) return null;
        var family = appId[..separator];
        var application = appId[(separator + 1)..];
        uint count = 0, length = 0;
        _ = GetPackagesByPackageFamily(family, ref count, IntPtr.Zero, ref length, IntPtr.Zero);
        if (count is 0 or > 32 || length is 0 or > 65536) return null;
        var names = Marshal.AllocHGlobal(checked((int)count * IntPtr.Size));
        var buffer = Marshal.AllocHGlobal(checked((int)length * 2));
        try
        {
            if (GetPackagesByPackageFamily(family, ref count, names, ref length, buffer) != 0) return null;
            var packageNames = Enumerable.Range(0, (int)count).Select(index => Marshal.PtrToStringUni(Marshal.ReadIntPtr(names, index * IntPtr.Size))!)
                .OrderByDescending(name => Version.TryParse(name.Split('_').ElementAtOrDefault(1), out var version) ? version : new Version()).ToArray();
            foreach (var fullName in packageNames)
            {
                uint pathLength = 0;
                _ = GetPackagePathByFullName(fullName, ref pathLength, null);
                if (pathLength is 0 or > 32768) continue;
                var path = new StringBuilder((int)pathLength);
                if (GetPackagePathByFullName(fullName, ref pathLength, path) != 0) continue;
                var root = Path.GetFullPath(path.ToString());
                var manifest = XDocument.Load(Path.Combine(root, "AppxManifest.xml"));
                var app = manifest.Descendants().FirstOrDefault(element => element.Name.LocalName == "Application" && (string?)element.Attribute("Id") == application);
                var visual = app?.Elements().FirstOrDefault(element => element.Name.LocalName == "VisualElements");
                var logo = (string?)visual?.Attribute("Square44x44Logo") ?? (string?)visual?.Attribute("Logo");
                if (logo == null) continue;
                var imagePath = Path.GetFullPath(Path.Combine(root, logo.Replace('/', Path.DirectorySeparatorChar)));
                if (!imagePath.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                var folder = Path.GetDirectoryName(imagePath)!;
                if (!Directory.Exists(folder)) continue;
                var stem = Path.GetFileNameWithoutExtension(imagePath);
                var candidates = Directory.GetFiles(folder, stem + ".targetsize-*" + Path.GetExtension(imagePath))
                    .Select(file => new { File = file, Match = Regex.Match(Path.GetFileName(file), "targetsize-([0-9]+)") })
                    .Where(item => item.Match.Success).Select(item => new { item.File, Size = int.Parse(item.Match.Groups[1].Value) })
                    .OrderBy(item => item.Size < pixels).ThenBy(item => item.Size >= pixels ? item.Size : -item.Size)
                    .ThenBy(item => !item.File.Contains("_altform-unplated", StringComparison.OrdinalIgnoreCase)).ToArray();
                var selected = candidates.FirstOrDefault()?.File ?? imagePath;
                if (!File.Exists(selected)) continue;
                using var file = File.OpenRead(selected);
                var bitmap = BitmapDecoder.Create(file, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                bitmap.Freeze(); return bitmap;
            }
        }
        finally { Marshal.FreeHGlobal(names); Marshal.FreeHGlobal(buffer); }
        return null;
    }
    [DllImport("shell32.dll")] private static extern int SHGetNameFromIDList(IntPtr pidl, uint format, out IntPtr name);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetPackagesByPackageFamily(string family, ref uint count, IntPtr names, ref uint length, IntPtr buffer);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetPackagePathByFullName(string fullName, ref uint length, StringBuilder? path);
}
