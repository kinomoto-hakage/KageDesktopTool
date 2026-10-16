#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
. (Join-Path $PSScriptRoot 'CheckRun.ps1')
$fixture = Join-Path $repository ('.scratch/check-runs/workflow-fixture-' + [Guid]::NewGuid().ToString('N'))
$isolated = Join-Path $fixture '隔离仓库'
$package = Join-Path $fixture '含空格的完整包'
$tools = Join-Path $isolated 'tools/windows'
$source = Join-Path $isolated 'src/KageDesktopTool'
$child = $null
function Expect-Failure([scriptblock]$Action, [string]$Pattern) {
    try { & $Action | Out-Null } catch {
        if ($_.Exception.Message -notmatch $Pattern) { throw }
        return
    }
    throw "预期失败未发生：$Pattern"
}
try {
    New-Item -ItemType Directory -Path $tools, $source, $package -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $repository 'verify-release.ps1') -Destination $isolated
    foreach ($name in @('ReleasePackage.ps1', 'CheckRun.ps1', 'checks.json')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $tools
    }
    Copy-Item -LiteralPath (Join-Path $repository 'src/KageDesktopTool/Program.cs') -Destination $source
    $guard = Join-Path $PSScriptRoot 'Test-Repository.ps1'
    & $guard -Repository $isolated | Out-Null
    $catalogPath = Join-Path $tools 'checks.json'
    $original = Get-Content -LiteralPath $catalogPath -Raw -Encoding UTF8
    $catalog = $original | ConvertFrom-Json
    $catalog.checks = @($catalog.checks | Where-Object name -NE 'publish')
    Save-CheckJson $catalogPath $catalog
    Expect-Failure { & $guard -Repository $isolated } '入口与清单不一致'
    [IO.File]::WriteAllText($catalogPath, $original)
    $probe = Join-Path $source 'GuardProbe.cs'
    [IO.File]::WriteAllText($probe, 'class Probe { void Run() { System.IO.File.AppendAllText("临时.txt", "诊断"); } }')
    Expect-Failure { & $guard -Repository $isolated } '正式代码残留'
    [IO.File]::WriteAllText($probe, 'class Probe { string Path = ".scratch/临时"; }')
    Expect-Failure { & $guard -Repository $isolated } '正式代码残留'
    [IO.File]::WriteAllText($probe, 'class Probe { void Save() { System.IO.File.WriteAllText("状态.json", "正式状态"); } }')
    & $guard -Repository $isolated | Out-Null
    $runner = Join-Path $isolated 'verify-release.ps1'
    $plan = @(& $runner -Plan -Suite frosted -PackageDirectory $package)
    if ('frosted-capture' -notin $plan.name -or 'frosted-desktop-recovery' -notin $plan.name) { throw '毛玻璃回归计划缺项。' }
    $single = @(& $runner -Plan -FromCheck four-way-input -OnlyCheck appearance -PackageDirectory $package)
    if ($single.Count -ne 1 -or $single[0].name -ne 'appearance') { throw '旧版起点与单项组合不兼容。' }
    Expect-Failure { & (Join-Path $PSScriptRoot 'prepare-manual-review.ps1') -OutputDirectory $package } '人工检查包必须'

    $businessOutput = Join-Path $repository 'tests/Kage.Workspace.Checks/bin/Release/net10.0'
    if (-not (Test-Path -LiteralPath (Join-Path $businessOutput 'Kage.Workspace.Checks.exe'))) { throw '请先构建 Release 业务检查。' }
    Get-ChildItem -LiteralPath $businessOutput -Force | Copy-Item -Destination $package -Recurse
    Copy-Item -LiteralPath (Join-Path $package 'Kage.Workspace.Checks.exe') -Destination (Join-Path $package 'KageDesktopTool.exe')
    $probeCatalog = [pscustomobject]@{ schemaVersion = 1; checks = @([pscustomobject]@{
        name = 'probe'; arguments = @('--workflow-child'); suites = @('test'); interactive = $false; changesSystem = $false
    }) }
    Save-CheckJson (Join-Path $package 'checks.json') $probeCatalog
    [IO.File]::WriteAllText((Join-Path $fixture 'hold'), '等待超时恢复检查')
    Expect-Failure { & $runner -PackageDirectory $package -Suite test -TimeoutSeconds 1 } '检查超时'
    $runRoot = Join-Path $isolated '.scratch/check-runs'
    $latest = Get-Content -LiteralPath (Join-Path $runRoot 'latest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $record = Get-Content -LiteralPath $latest.record -Raw -Encoding UTF8 | ConvertFrom-Json
    $first = $record.checks[0].attempts[0]
    $child = Get-Process -Id $first.processId -ErrorAction SilentlyContinue
    if ($first.status -ne 'NeedsAttention' -or -not (Test-CheckProcess $first)) { throw '超时没有保留活进程与恢复记录。' }
    Expect-Failure { & $runner -PackageDirectory $package -Resume $latest.runId } '进程仍运行'
    if (-not $child.WaitForExit(10000)) { throw '契约检查子进程未按预期自行退出。' }
    $child.Dispose(); $child = $null
    # 已结束或复用的 PID 不得误阻塞；恢复保留第一次证据，并创建新的尝试目录。
    if (Test-CheckProcess $first) { throw '已退出检查仍被识别为活进程。' }
    Remove-Item -LiteralPath (Join-Path $fixture 'hold')
    & $runner -PackageDirectory $package -Resume $latest.runId | Out-Null
    $record = Get-Content -LiteralPath $latest.record -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($record.status -ne 'Passed' -or $record.checks[0].attempts.Count -ne 2) { throw '恢复没有追加第二次成功尝试。' }
    foreach ($attempt in $record.checks[0].attempts) {
        if (-not (Test-Path -LiteralPath (Join-Path $attempt.evidence 'child.txt'))) { throw '证据未按尝试隔离。' }
    }
    & $runner -PackageDirectory $package -Resume $latest.runId | Out-Null
    $again = Get-Content -LiteralPath $latest.record -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($again.checks[0].attempts.Count -ne 2) { throw '恢复重复执行已通过的检查。' }
    $dependency = Join-Path $package 'Kage.Workspace.dll'
    $originalDependency = [IO.File]::ReadAllBytes($dependency)
    [IO.File]::WriteAllBytes($dependency, [byte[]]@($originalDependency + 0))
    Expect-Failure { & $runner -PackageDirectory $package -Resume $latest.runId } '包内容或清单已变化'
    [IO.File]::WriteAllBytes($dependency, $originalDependency)
    Add-Content -LiteralPath (Join-Path $package 'checks.json') -Value ' '
    Expect-Failure { & $runner -PackageDirectory $package -Resume $latest.runId } '清单已变化'
    Remove-Item -LiteralPath (Join-Path $package 'checks.json')
    Expect-Failure { & $runner -PackageDirectory $package -Suite test } '产物缺少 checks.json'
    $self = Get-Process -Id $PID
    $identity = [pscustomobject]@{ processId = $PID; executable = $self.Path; processStarted = $self.StartTime.ToUniversalTime().ToString('o') }
    if (-not (Test-CheckProcess $identity)) { throw '进程身份正例失败。' }
    $identity.processStarted = '1900-01-01T00:00:00Z'
    if (Test-CheckProcess $identity) { throw '进程启动时刻不同仍误判为同一进程。' }
    $self.Dispose()
    Write-Output '执行器契约通过：遗漏入口、临时日志、正式状态写入、计划覆盖、超时存活、恢复去重、独立证据和进程身份。'
} finally {
    if ($child) { if (-not $child.WaitForExit(10000)) { throw '隔离子进程仍运行，保留夹具供核对。' }; $child.Dispose() }
    $resolved = [IO.Path]::GetFullPath($fixture)
    $allowed = [IO.Path]::GetFullPath((Join-Path $repository '.scratch/check-runs')) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path -Leaf $resolved) -notlike 'workflow-fixture-*') {
        throw '拒绝清理契约检查范围外的目录。'
    }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
