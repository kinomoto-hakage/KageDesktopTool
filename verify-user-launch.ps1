param([string]$PackageDirectory)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'tools/windows/ReleasePackage.ps1')
if (-not $PackageDirectory) { $PackageDirectory = 'releases/KageDesktopTool-win-x64-' + (Read-ReleaseVersion $PSScriptRoot) }
if (Get-Process -Name KageDesktopTool -ErrorAction SilentlyContinue) { throw '请从托盘退出现有实例后验收普通启动。' }
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,System.Windows.Forms,System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class KageReleaseInput {
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
}
'@
function Wait-For([scriptblock]$Condition) {
    $deadline = (Get-Date).AddSeconds(12)
    do { $value = & $Condition; if ($value) { return $value }; Start-Sleep -Milliseconds 100 } while ((Get-Date) -lt $deadline)
    throw '真实普通启动的 UI 未在时限内就绪。'
}
function Click-Element($Element, [bool]$Right = $false) {
    $bounds = $Element.Current.BoundingRectangle
    [KageReleaseInput]::SetCursorPos([int]($bounds.Left + $bounds.Width / 2), [int]($bounds.Top + $bounds.Height / 2)) | Out-Null
    if ($Right) { $down = 8; $up = 16 } else { $down = 2; $up = 4 }
    [KageReleaseInput]::mouse_event($down, 0, 0, 0, [UIntPtr]::Zero)
    [KageReleaseInput]::mouse_event($up, 0, 0, 0, [UIntPtr]::Zero)
}
function Read-FileHash([string]$Path) {
    $hash = [Security.Cryptography.SHA256]::Create()
    $stream = [IO.File]::OpenRead($Path)
    try { [BitConverter]::ToString($hash.ComputeHash($stream)) } finally { $stream.Dispose(); $hash.Dispose() }
}
function Read-ProtectedFiles {
    # 普通打开设置会追加操作历史；只把工作区配置及备份视为必须不变的受保护状态。
    $files = @(Get-ChildItem -LiteralPath $stateDirectory -File -Force | Where-Object { $_.Name -like 'workspace.json*' })
    $files | Sort-Object FullName | ForEach-Object { $_.FullName + ' ' + (Read-FileHash $_.FullName) }
}

$executable = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot (Join-Path $PackageDirectory 'KageDesktopTool.exe')))
$stateDirectory = Join-Path $env:LOCALAPPDATA 'KageDesktopTool'
$statePath = Join-Path $stateDirectory 'workspace.json'
if (-not (Test-Path -LiteralPath $statePath)) { throw '没有实际用户状态，不能将隔离初次启动标为实际设置验收。' }
$state = Get-Content -LiteralPath $statePath -Encoding UTF8 -Raw | ConvertFrom-Json
if ($state.PendingCreate -or $state.PendingStartup -or $state.PendingFolderChange -or $state.PendingRootMigration -or $state.PendingContentRename) {
    throw '实际工作区待恢复，停止普通启动验收，避免触发恢复副作用。'
}
$before = @(Read-ProtectedFiles)
$contentBefore = @(Get-ChildItem -LiteralPath $state.Root -Recurse -Force | Sort-Object FullName | Select-Object FullName,Length,LastWriteTimeUtc | ConvertTo-Json -Compress)
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$startupBefore = (Get-ItemProperty -LiteralPath $runKey -ErrorAction SilentlyContinue).KageDesktopTool
$evidence = Join-Path $PSScriptRoot '.scratch/desktop-folder/verification'
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
$log = Join-Path $evidence 'upgrade-05-user-launch.txt'
$cursor = [System.Windows.Forms.Cursor]::Position
$process = Start-Process -FilePath $executable -WindowStyle Hidden -PassThru
try {
    Start-Sleep -Milliseconds 800
    $repeat = Start-Process -FilePath $executable -WindowStyle Hidden -PassThru
    if (-not $repeat.WaitForExit(8000) -or $repeat.ExitCode -ne 0) { throw '普通重复启动未正常结束。' }
    $settings = Wait-For {
        [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children,
            [System.Windows.Automation.AndCondition]::new(
                [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $process.Id),
                [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, 'Kage 桌面工具 · 设置')))
    }
    if ($settings.Current.Name -ne 'Kage 桌面工具 · 设置') { throw '普通重复启动未显示现有实例的设置。' }
    $navigation = $settings.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, '存储'))
    $navigation.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 200
    $edit = $settings.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit))
    if ($edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -ne $state.Root) { throw '普通启动没有读取实际根目录。' }
    $folderNavigation = $settings.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, '桌面 Folder'))
    $folderNavigation.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 200
    $texts = $settings.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text))
    foreach ($folder in $state.Folders) {
        if (-not ($texts | Where-Object { $_.Current.Name.StartsWith($folder.Name + ' · ') })) { throw '普通设置缺少已保存 Folder。' }
    }
    $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bitmap = [System.Drawing.Bitmap]::new($bounds.Width, $bounds.Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try { $graphics.CopyFromScreen($bounds.Left, $bounds.Top, 0, 0, $bounds.Size); $bitmap.Save((Join-Path $evidence 'upgrade-05-实际用户设置.png')) }
    finally { $graphics.Dispose(); $bitmap.Dispose() }
    $settings.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    Start-Sleep -Milliseconds 300
    if ($process.HasExited) { throw '关闭设置意外退出程序。' }
    $tray = Wait-For {
        $buttons = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button))
        $button = $buttons | Where-Object { $_.Current.Name.Contains('Kage 桌面整理') } | Select-Object -First 1
        if ($button) { return $button }
        $overflow = $buttons | Where-Object { $_.Current.Name -in @('显示隐藏的图标','Show hidden icons') } | Select-Object -First 1
        if ($overflow) { Click-Element $overflow }
    }
    Click-Element $tray $true
    $exit = Wait-For {
        [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, '退出')) |
            Where-Object { $_.Current.ProcessId -eq $process.Id } | Select-Object -First 1
    }
    $exit.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    if (-not $process.WaitForExit(8000) -or $process.ExitCode -ne 0) { throw '真实托盘退出没有正常结束。' }
    if (Compare-Object $before @(Read-ProtectedFiles)) { throw '实际状态或原型数据发生变化。' }
    $contentAfter = @(Get-ChildItem -LiteralPath $state.Root -Recurse -Force | Sort-Object FullName | Select-Object FullName,Length,LastWriteTimeUtc | ConvertTo-Json -Compress)
    if (Compare-Object $contentBefore $contentAfter) { throw '实际内容目录的路径或文件属性发生变化。' }
    if ((Get-ItemProperty -LiteralPath $runKey -ErrorAction SilentlyContinue).KageDesktopTool -ne $startupBefore) { throw '正式自启项发生变化。' }
    @('通过：最终 EXE 无检查参数普通启动，读取实际用户根目录和全部 Folder。',
      '通过：普通重复启动转交设置请求；关闭设置继续运行，真实托盘退出正常。',
      '通过：实际状态、备份及原型数据 SHA256 不变，内容路径／文件属性与正式自启项不变。',
      "程序位置：$executable", "完成：$(Get-Date -Format o)") | Set-Content -LiteralPath $log -Encoding UTF8
} finally {
    [KageReleaseInput]::SetCursorPos($cursor.X, $cursor.Y) | Out-Null
    if (-not $process.HasExited) { Write-Warning "验收未完成，保留普通实例 PID $($process.Id)，请从托盘退出。" }
}
