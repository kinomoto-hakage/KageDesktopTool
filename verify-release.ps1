param([string]$PackageDirectory, [string]$FromCheck = 'publish', [string]$OnlyCheck)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'tools/windows/ReleasePackage.ps1')
if (-not $PackageDirectory) { $PackageDirectory = 'releases/KageDesktopTool-win-x64-' + (Read-ReleaseVersion $PSScriptRoot) }
$executable = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot (Join-Path $PackageDirectory 'KageDesktopTool.exe')))
if (-not (Test-Path -LiteralPath $executable)) { throw '发布 EXE 不存在。' }
$checks = @('publish', 'release-workflow', 'session', 'content-input', 'shell-image', 'content-layout', 'layout-input', 'appearance',
    'settings-notification', 'move-progress', 'tray-session', 'file-move', 'folder-action', 'root-migration', 'migration-recovery', 'display-dpi', 'desktop-recovery')
$first = [Array]::IndexOf($checks, $FromCheck)
if ($first -lt 0) { throw '未知的检查起点。' }
$checks = $checks[$first..($checks.Length - 1)]
if ($OnlyCheck) {
    if ($OnlyCheck -notin $checks) { throw '未知的单项检查。' }
    $checks = @($OnlyCheck)
}
$evidence = Join-Path $PSScriptRoot '.scratch/desktop-folder/verification'
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
$summary = Join-Path $evidence 'upgrade-05-release-summary.txt'
if ($first -eq 0 -and -not $OnlyCheck) {
    "发布 EXE：$executable`n开始：$(Get-Date -Format o)" | Set-Content -LiteralPath $summary -Encoding UTF8
} else {
    "继续：$FromCheck，$(Get-Date -Format o)" | Add-Content -LiteralPath $summary -Encoding UTF8
}

# 在当前用户的交互式 Windows 会话串行运行；后半段操作鼠标、成对 Win+D 并恢复 Explorer。
Push-Location $PSScriptRoot
try {
    foreach ($check in $checks) {
        Write-Output "开始检查：$check"
        $process = Start-Process -FilePath $executable -ArgumentList "--$check-check" -WindowStyle Hidden -PassThru
        if (-not $process.WaitForExit(180000)) {
            # 不强杀文件操作／恢复会话，以免中断清理；保留 PID 供检查并停止后续输入。
            throw "检查 $check 超时，进程 $($process.Id) 仍运行，请查看其日志。"
        }
        $exitCode = $process.ExitCode
        "$check：ExitCode=$exitCode" | Add-Content -LiteralPath $summary -Encoding UTF8
        if ($exitCode -ne 0) { throw "检查 $check 失败，停止后续检查。" }
        Write-Output "通过：$check"
    }
    "完成：$(Get-Date -Format o)" | Add-Content -LiteralPath $summary -Encoding UTF8
} finally { Pop-Location }
