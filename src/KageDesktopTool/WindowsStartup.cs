using System;
using System.IO;
using Kage.Workspace;
using Microsoft.Win32;

namespace Kage.Desktop;

public sealed class WindowsStartup(string executable, string valueName = "KageDesktopTool") : IStartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public string LaunchCommand
    {
        get
        {
            if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable)) throw new IOException($"自启目标不是可运行的程序：{executable}");
            return $"\"{executable}\" --background";
        }
    }

    public string? ReadCommand()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
        var value = key?.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (value != null && value is not string) throw new IOException("实际启动项的值类型不是字符串。");
        return value as string;
    }

    public void WriteCommand(string? command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, true)
            ?? throw new IOException("无法打开当前用户的启动配置。");
        if (command == null) key.DeleteValue(valueName, false);
        else key.SetValue(valueName, command, RegistryValueKind.String);
        key.Flush();
    }
}
