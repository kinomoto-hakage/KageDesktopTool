#requires -Version 7.0
[CmdletBinding()]
param([string]$OutputDirectory)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ReleasePackage.ps1')
. (Join-Path $PSScriptRoot 'CheckRun.ps1')
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$commit = (& git -C $repository rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw '无法读取当前提交。' }
$commonGit = (& git -C $repository rev-parse --path-format=absolute --git-common-dir).Trim()
if ($LASTEXITCODE -ne 0) { throw '无法定位主工作区。' }
# 产物放在主工作区，删除实现用 worktree 后人工检查入口仍有效。
$stableRoot = Join-Path (Split-Path $commonGit -Parent) '.scratch/manual-review'
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $stableRoot ((Get-Date -Format 'yyyyMMddTHHmmss') + '-' + $commit.Substring(0,8) + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
}
$destination = [IO.Path]::GetFullPath($OutputDirectory, $repository)
$allowed = [IO.Path]::GetFullPath($stableRoot) + [IO.Path]::DirectorySeparatorChar
if (-not $destination.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw '人工检查包必须位于主工作区 .scratch/manual-review 子目录。' }
if (Test-Path -LiteralPath $destination) { throw '人工检查目录已存在，请使用新目录，避免覆盖正在运行的 EXE。' }
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$buildDirectory = $destination + '.build/'
Push-Location $repository
try {
    & dotnet publish src/KageDesktopTool/KageDesktopTool.csproj --disable-build-servers `
        -p:PublishProfile=WindowsX64 "-p:BaseOutputPath=$buildDirectory" -o $destination
    if ($LASTEXITCODE -ne 0) { throw '人工检查包生成失败，未更新 latest.json。' }
    $executable = Join-Path $destination 'KageDesktopTool.exe'
    $executableHash = Read-PackageSha256 $executable
    @"
双击此目录的 KageDesktopTool.exe；保留整个目录。
源码提交：$commit
EXE SHA256：$executableHash
运行使用当前用户的 KageDesktopTool 正式状态；先从托盘退出旧实例。
Folder 右键 → 样式… → 启用毛玻璃，可检查预览、应用、取消及重启恢复。
逐文件摘要和工作区修改记录见 manual-review.json。
"@ | Set-Content -LiteralPath (Join-Path $destination '人工检查说明.txt') -Encoding UTF8
    $files = @(Get-ChildItem -LiteralPath $destination -Recurse -File | Sort-Object FullName | ForEach-Object {
        [pscustomobject]@{ path = [IO.Path]::GetRelativePath($destination, $_.FullName); sha256 = Read-PackageSha256 $_.FullName }
    })
    $manifest = [pscustomobject]@{
        schemaVersion = 1; commit = $commit; workingTree = @(& git -c core.quotePath=false status --short)
        created = (Get-Date).ToUniversalTime().ToString('o'); executable = $executable
        sha256 = $executableHash; version = Read-ReleaseVersion $repository
        stateDirectory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'KageDesktopTool'
        files = $files
    }
    Save-CheckJson (Join-Path $destination 'manual-review.json') $manifest
    Save-CheckJson (Join-Path $stableRoot 'latest.json') ([pscustomobject]@{
        executable = $executable; manifest = Join-Path $destination 'manual-review.json'; commit = $commit; sha256 = $manifest.sha256
    })
    Write-Output "双击运行：$executable"
    Write-Output "SHA256：$($manifest.sha256)"
    $running = @(Get-Process -Name KageDesktopTool -ErrorAction SilentlyContinue | Select-Object Id, Path)
    if ($running.Count) { Write-Output '现有实例（请先从托盘退出）：'; $running | Format-Table }
} finally {
    Pop-Location
    $resolvedBuild = [IO.Path]::GetFullPath($buildDirectory)
    if (-not $resolvedBuild.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path -Leaf $resolvedBuild.TrimEnd('/','\')) -notlike '*.build') {
        throw '拒绝清理人工验收临时构建范围外的目录。'
    }
    if (Test-Path -LiteralPath $resolvedBuild) { Remove-Item -LiteralPath $resolvedBuild -Recurse -Force }
}
