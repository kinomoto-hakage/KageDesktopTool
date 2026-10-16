#requires -Version 7.0
[CmdletBinding()]
param(
    [string]$PackageDirectory,
    [string]$Suite = 'release',
    [string]$FromCheck,
    [string]$OnlyCheck,
    [string]$Resume,
    [switch]$List,
    [switch]$Plan,
    [int]$TimeoutSeconds = 300
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'tools/windows/ReleasePackage.ps1')
. (Join-Path $PSScriptRoot 'tools/windows/CheckRun.ps1')
if ($TimeoutSeconds -lt 1) { throw '超时必须为正整数秒。' }
$catalogPath = Join-Path $PSScriptRoot 'tools/windows/checks.json'
if ($List) {
    (Get-Content -LiteralPath $catalogPath -Raw -Encoding UTF8 | ConvertFrom-Json).checks |
        Select-Object name, @{n='分组';e={$_.suites -join ','}}, interactive, changesSystem
    return
}
if (-not $PackageDirectory) { $PackageDirectory = 'releases/KageDesktopTool-win-x64-' + (Read-ReleaseVersion $PSScriptRoot) }
$package = [IO.Path]::GetFullPath($PackageDirectory, $PSScriptRoot)
$executable = Join-Path $package 'KageDesktopTool.exe'
$packageCatalog = Join-Path $package 'checks.json'
if (Test-Path -LiteralPath $packageCatalog) { $catalogPath = $packageCatalog }
if (-not $Plan) {
    if (-not (Test-Path -LiteralPath $executable)) { throw '检查 EXE 不存在。' }
    $catalogPath = Join-Path $package 'checks.json'
    if (-not (Test-Path -LiteralPath $catalogPath)) { throw '产物缺少 checks.json，请重新构建；旧 EXE 不会接收未知检查参数。' }
}
$catalog = Get-Content -LiteralPath $catalogPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($catalog.schemaVersion -ne 1) { throw '不支持的检查清单版本。' }
$checks = @($catalog.checks | Where-Object { $Suite -in $_.suites })
if ($FromCheck) {
    $first = [Array]::IndexOf(@($checks.name), $FromCheck)
    if ($first -lt 0) { throw '未知的检查起点。' }
    $checks = @($checks[$first..($checks.Count - 1)])
}
if ($OnlyCheck) {
    if ($FromCheck -and $OnlyCheck -notin $checks.name) { throw '单项检查不在所选起点之后。' }
    $checks = @($catalog.checks | Where-Object name -EQ $OnlyCheck)
}
if (-not $checks.Count -and -not $Resume) { throw '没有匹配的检查，请用 -List 查看名称与分组。' }
if ($Plan -and $Resume) { throw '-Plan 与 -Resume 分开使用；恢复计划见原 run.json。' }
if ($Plan) { $checks | Select-Object name, arguments, interactive, changesSystem; return }

$runRoot = Join-Path $PSScriptRoot '.scratch/check-runs'
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
# Windows 会话内跨工作树互斥；运行记录另外核对超时或中断后仍存活的检查进程。
$mutex = [Threading.Mutex]::new($false, ('Local\KageDesktopChecks-' + (Get-Process -Id $PID).SessionId))
$ownsMutex = $false
try {
    try { $ownsMutex = $mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $ownsMutex = $true }
    if (-not $ownsMutex) { throw '本 Windows 会话已有检查在执行，请等待其结束。' }
    foreach ($file in Get-ChildItem -LiteralPath $runRoot -Filter 'run.json' -Recurse -File) {
        $previous = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
        foreach ($item in $previous.checks) {
            foreach ($attempt in $item.attempts) {
                if ($attempt.status -eq 'Starting') { throw "检查启动期间中断，需先核对进程与恢复状态：$($file.FullName)" }
                if ($attempt.status -in @('Running', 'NeedsAttention') -and (Test-CheckProcess $attempt)) {
                    throw "检查进程仍运行：PID=$($attempt.processId)，入口=$($item.name)，记录=$($file.FullName)。后续桌面输入已停止。"
                }
            }
        }
    }
    $binaryHash = Read-PackageSha256 $executable
    $packageHash = Read-CheckPackageStamp $package
    $catalogHash = Read-PackageSha256 $catalogPath
    $commit = (& git -C $PSScriptRoot rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw '无法读取当前提交。' }
    if ($Resume) {
        if ($Resume -notmatch '^\d{8}T\d{6}-[a-f0-9]{8}$') { throw '-Resume 必须为运行 ID。' }
        $runDirectory = Join-Path $runRoot $Resume
        $recordPath = Join-Path $runDirectory 'run.json'
        $record = Get-Content -LiteralPath $recordPath -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($record.executable -ne $executable -or $record.sha256 -ne $binaryHash -or $record.packageSha256 -ne $packageHash -or $record.catalogSha256 -ne $catalogHash -or $record.commit -ne $commit) {
            throw '提交、包内容或清单已变化，请创建新运行，不能将新结果拼接到旧证据。'
        }
        if ($PSBoundParameters.ContainsKey('Suite') -or $FromCheck -or $OnlyCheck) { throw '恢复运行使用原检查计划，请仅传 -Resume 和原 -PackageDirectory。' }
        $checks = @($record.checks | ForEach-Object {
            $name = $_.name
            $catalog.checks | Where-Object name -EQ $name
        })
        if ($checks.Count -eq 0 -or $checks.Count -ne $record.checks.Count) { throw '恢复记录中的检查计划与清单不一致。' }
    } else {
        $runId = (Get-Date -Format 'yyyyMMddTHHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8)
        $runDirectory = Join-Path $runRoot $runId
        New-Item -ItemType Directory -Path $runDirectory | Out-Null
        $recordPath = Join-Path $runDirectory 'run.json'
        $record = [pscustomobject]@{
            schemaVersion = 1; runId = $runId; commit = $commit
            workingTree = @(& git -C $PSScriptRoot -c core.quotePath=false status --short)
            executable = $executable; sha256 = $binaryHash; packageSha256 = $packageHash; catalogSha256 = $catalogHash
            started = (Get-Date).ToUniversalTime().ToString('o'); ended = $null; status = 'Running'
            checks = @($checks | ForEach-Object { [pscustomobject]@{ name = $_.name; attempts = @() } })
        }
    }
    if (@($checks | Where-Object interactive).Count -and (-not [Environment]::UserInteractive -or (Get-Process -Id $PID).SessionId -eq 0)) {
        throw '这些检查需要当前用户的交互式 Windows 会话。CI 请执行业务检查的 --suite ci。'
    }
    $record.status = 'Running'; $record.ended = $null
    Save-CheckJson $recordPath $record
    Save-CheckJson (Join-Path $runRoot 'latest.json') ([pscustomobject]@{ runId = $record.runId; record = $recordPath })
    Write-Output "运行 ID：$($record.runId)；记录：$recordPath"
    foreach ($check in $checks) {
        $item = $record.checks | Where-Object name -EQ $check.name
        if (@($item.attempts | Where-Object status -EQ 'Passed').Count) { Write-Output "保留已通过：$($check.name)"; continue }
        $attemptDirectory = Join-Path $runDirectory ($check.name + '/' + ($item.attempts.Count + 1))
        New-Item -ItemType Directory -Path $attemptDirectory -Force | Out-Null
        $attempt = [pscustomobject]@{
            status = 'Starting'; command = @($executable) + @($check.arguments); executable = $executable
            evidence = $attemptDirectory; processId = $null; processStarted = $null
            started = (Get-Date).ToUniversalTime().ToString('o'); ended = $null; exitCode = $null
        }
        $item.attempts = @($item.attempts) + @($attempt)
        Save-CheckJson $recordPath $record
        $process = [Diagnostics.Process]::new()
        try {
            $process.StartInfo.FileName = $executable
            $process.StartInfo.WorkingDirectory = $PSScriptRoot
            $process.StartInfo.UseShellExecute = $false
            $process.StartInfo.CreateNoWindow = $true
            foreach ($argument in $check.arguments) { $process.StartInfo.ArgumentList.Add($argument) }
            $process.StartInfo.Environment['KAGE_CHECK_EVIDENCE_DIRECTORY'] = $attemptDirectory
            Write-Output "开始：$($check.name)；系统设置／Explorer 变化：$($check.changesSystem)"
            if (-not $process.Start()) { throw '无法启动检查进程。' }
            $attempt.processId = $process.Id
            $attempt.processStarted = $process.StartTime.ToUniversalTime().ToString('o')
            $attempt.status = 'Running'; Save-CheckJson $recordPath $record
            # 只等目标进程，不能用 Start-Process -Wait 等待它启动的 Explorer 子进程。
            if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
                $attempt.status = 'NeedsAttention'
                throw "检查超时：$($check.name)，PID=$($process.Id)。保留进程执行恢复；核对其结束后用 -Resume $($record.runId) 重试。"
            }
            $attempt.exitCode = $process.ExitCode
            $attempt.ended = (Get-Date).ToUniversalTime().ToString('o')
            $attempt.status = if ($process.ExitCode -eq 0) { 'Passed' } else { 'Failed' }
            Save-CheckJson $recordPath $record
            if ($process.ExitCode -ne 0) { throw "检查失败：$($check.name)，ExitCode=$($process.ExitCode)。" }
            Write-Output "通过：$($check.name)"
        } catch {
            if ($attempt.status -eq 'Starting') { $attempt.status = if ($attempt.processId) { 'NeedsAttention' } else { 'Failed' } }
            $record.status = 'NeedsAttention'; Save-CheckJson $recordPath $record
            throw
        } finally { $process.Dispose() }
    }
    $record.status = 'Passed'; $record.ended = (Get-Date).ToUniversalTime().ToString('o')
    Save-CheckJson $recordPath $record
    Write-Output "全部通过：$recordPath"
} finally { if ($ownsMutex) { $mutex.ReleaseMutex() }; $mutex.Dispose() }
