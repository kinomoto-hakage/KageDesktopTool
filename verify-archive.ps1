param([string]$PackageDirectory)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'tools/windows/ReleasePackage.ps1')
$version = Read-ReleaseVersion $PSScriptRoot
if (-not $PackageDirectory) { $PackageDirectory = "releases/KageDesktopTool-win-x64-$version" }
$package = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot $PackageDirectory))
$archive = $package + '.zip'
$expected = (Get-Content -LiteralPath ($archive + '.sha256') -Raw).Trim()
if ((Read-PackageSha256 $archive) -ne $expected) { throw 'ZIP 校验值不符。' }
# 临时解压到含空格的随机目录，验收后清理，避免留下重复开发版本。
$extraction = Join-Path $PSScriptRoot ('releases/解压验收 含空格 ' + $version + ' ' + [Guid]::NewGuid().ToString('N'))
try {
Expand-Archive -LiteralPath $archive -DestinationPath $extraction
$directory = Join-Path $extraction (Split-Path -Leaf $package)
$manifest = Join-Path $directory 'SHA256SUMS.txt'
$seen = @{}
foreach ($line in Get-Content -LiteralPath $manifest -Encoding UTF8) {
    if ($line -notmatch '^([A-Fa-f0-9]{64})  (.+)$') { throw '逐文件清单格式无效。' }
    $hash = $Matches[1]; $relative = $Matches[2]
    $file = [IO.Path]::GetFullPath((Join-Path $directory $relative))
    if (-not $file.StartsWith($directory + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or $seen.ContainsKey($file)) {
        throw '逐文件清单存在越界或重复路径。'
    }
    if ((Read-PackageSha256 $file) -ne $hash) { throw "解压文件校验失败：$relative" }
    $seen[$file] = $true
}
$files = @(Get-ChildItem -LiteralPath $directory -Recurse -File -Force | Where-Object { $_.FullName -ne $manifest })
if ($files.Count -ne $seen.Count) { throw '解压目录包含清单外文件。' }
$metadata = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $directory 'KageDesktopTool.exe'))
if ($metadata.ProductVersion.Split('+')[0] -ne $version) { throw '解压 EXE 版本不符。' }
Push-Location $PSScriptRoot
try {
    $process = Start-Process -FilePath (Join-Path $directory 'KageDesktopTool.exe') -ArgumentList '--publish-check' -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(30000)) { throw "解压包检查超时，保留 PID $($process.Id)。" }
    if ($process.ExitCode -ne 0) { throw '解压包自包含依赖或资源检查失败。' }
    @("ZIP SHA256：$expected", "通过：$($seen.Count) 个文件校验一致，解压 EXE $version 加载包内运行时。",
      "解压目录（验收后清理）：$directory", "完成：$(Get-Date -Format o)") |
        Set-Content -LiteralPath (Join-Path $PSScriptRoot '.scratch/desktop-folder/verification/upgrade-05-archive.txt') -Encoding UTF8
    Write-Output "解压验收通过：$directory"
} finally { Pop-Location }
} finally {
    $resolvedExtraction = [IO.Path]::GetFullPath($extraction)
    $releaseRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'releases')) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedExtraction.StartsWith($releaseRoot, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path -Leaf $resolvedExtraction) -notlike '解压验收 含空格 *') {
        throw '拒绝清理解压验收范围外的目录。'
    }
    if (Test-Path -LiteralPath $resolvedExtraction) {
        if ($process -and -not $process.HasExited) { Write-Warning '检查进程仍运行，保留解压目录供诊断。' }
        else { Remove-Item -LiteralPath $resolvedExtraction -Recurse -Force }
    }
}
