# 原子写入运行记录；中断前一份有效 JSON 仍保留。
function Save-CheckJson([string]$Path, [object]$Value) {
    $temporary = $Path + '.tmp'
    [IO.File]::WriteAllText($temporary, ($Value | ConvertTo-Json -Depth 16), [Text.UTF8Encoding]::new($false))
    [IO.File]::Move($temporary, $Path, $true)
}

# EXE 是 .NET apphost，源码变化通常只改变 DLL；恢复必须核对完整包，而非只核对 EXE。
function Read-CheckPackageStamp([string]$Directory) {
    $lines = @(Get-ChildItem -LiteralPath $Directory -Recurse -File -Force | Sort-Object FullName | ForEach-Object {
        (Read-PackageSha256 $_.FullName) + '  ' + [IO.Path]::GetRelativePath($Directory, $_.FullName).Replace('\', '/')
    })
    $bytes = [Text.Encoding]::UTF8.GetBytes(($lines -join "`n"))
    $hash = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($hash.ComputeHash($bytes)).Replace('-', '') }
    finally { $hash.Dispose() }
}

# 同时核对启动时刻和 EXE，避免把复用的 PID 误认为旧检查。
function Test-CheckProcess([object]$Attempt) {
    if (-not $Attempt.processId) { return $false }
    $process = Get-Process -Id $Attempt.processId -ErrorAction SilentlyContinue
    if (-not $process) { return $false }
    try {
        # PowerShell 新版本反序列化 ISO 日期时会生成 DateTime，按 UTC ticks 比较兼容两种形态。
        $started = [DateTimeOffset]$Attempt.processStarted
        return $process.StartTime.ToUniversalTime().Ticks -eq $started.UtcDateTime.Ticks -and $process.Path -eq $Attempt.executable
    } catch { throw "无法核对检查进程身份：PID=$($Attempt.processId)，请先人工核对。" }
    finally { $process.Dispose() }
}
