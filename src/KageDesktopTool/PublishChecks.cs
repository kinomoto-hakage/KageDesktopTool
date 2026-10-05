using System;
using System.IO;
using System.Text.Json;

namespace Kage.Desktop;

// 发布检查直接由包内 EXE 启动，不能借用开发机全局运行时掩盖缺失依赖。
internal static class PublishChecks
{
    internal static int Run()
    {
        var evidence = Path.Combine(Environment.CurrentDirectory, ".scratch", "desktop-folder", "verification");
        Directory.CreateDirectory(evidence);
        var log = Path.Combine(evidence, "publish-package.txt");
        try
        {
            foreach (var file in new[] { "KageDesktopTool.exe", "coreclr.dll", "hostfxr.dll", "hostpolicy.dll",
                "System.Private.CoreLib.dll", "PresentationFramework.dll", "使用说明.md", "Assets/app-d.ico",
                "Microsoft.WindowsAppRuntime.dll", "Microsoft.Windows.AppNotifications.Projection.dll" })
                if (!File.Exists(Path.Combine(AppContext.BaseDirectory, file))) throw new IOException("发布包缺少：" + file);
            using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "KageDesktopTool.runtimeconfig.json")));
            var options = config.RootElement.GetProperty("runtimeOptions");
            if (options.TryGetProperty("framework", out _) || options.TryGetProperty("frameworks", out _))
                throw new IOException("发布包仍依赖全局 .NET 运行时。");
            if (!Path.GetFullPath(typeof(object).Assembly.Location).StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase))
                throw new IOException("实际执行未加载包内运行时。");
            File.WriteAllText(log, $"通过：包内 EXE、运行时、WPF、图标及说明完整。\n实际 CoreLib：{typeof(object).Assembly.Location}\n程序位置：{Environment.ProcessPath}\n完成：{DateTimeOffset.Now:O}\n");
            return 0;
        }
        catch (Exception e) { File.WriteAllText(log, "失败：" + e + "\n"); return 1; }
    }
}
