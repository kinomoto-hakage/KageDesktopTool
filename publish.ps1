param([string]$OutputDirectory)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'tools/windows/ReleasePackage.ps1')
$version = Read-ReleaseVersion $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = "releases/KageDesktopTool-win-x64-$version" }
$repoDirectory = $PSScriptRoot
$releaseDirectory = [IO.Path]::GetFullPath((Join-Path $repoDirectory $OutputDirectory))
$allowedDirectory = [IO.Path]::GetFullPath((Join-Path $repoDirectory 'releases')) + [IO.Path]::DirectorySeparatorChar
if (-not $releaseDirectory.StartsWith($allowedDirectory, [StringComparison]::OrdinalIgnoreCase)) {
    throw '发布位置必须位于本仓库 releases 目录内。'
}
if ((Test-Path -LiteralPath $releaseDirectory) -or (Test-Path -LiteralPath ($releaseDirectory + '.zip')) -or (Test-Path -LiteralPath ($releaseDirectory + '.zip.sha256'))) {
    throw '发布位置已存在，请指定新的 releases 子目录，避免覆盖正在运行的包。'
}

Push-Location $repoDirectory
try {
    # 允许旧实例仍使用开发构建目录；本次发布使用独立输出，避免覆盖已加载 DLL。
    $buildDirectory = Join-Path $repoDirectory ('.scratch/desktop-folder/verification/build-' + [Guid]::NewGuid().ToString('N') + '/')
    # 只有维护者构建需要 SDK 和下载运行时；日常使用只需解压、双击。
    & rtk proxy dotnet publish src/KageDesktopTool/KageDesktopTool.csproj `
        -p:PublishProfile=WindowsX64 "-p:BaseOutputPath=$buildDirectory" --source https://api.nuget.org/v3/index.json -o $releaseDirectory
    if ($LASTEXITCODE -ne 0) { throw '发布失败，未生成压缩包。' }
    $metadata = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $releaseDirectory 'KageDesktopTool.exe'))
    if ($metadata.ProductVersion.Split('+')[0] -ne $version) { throw '发布 EXE 版本与项目版本不一致。' }
    $process = Start-Process -FilePath (Join-Path $releaseDirectory 'KageDesktopTool.exe') `
        -ArgumentList '--publish-check' -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(30000)) { $process.Kill(); throw '发布产物检查超时。' }
    if ($process.ExitCode -ne 0) { throw '发布产物检查失败，查看 verification/publish-package.txt。' }

    $files = Get-ChildItem -LiteralPath $releaseDirectory -Recurse -File -Force | Sort-Object FullName
    $manifest = foreach ($file in $files) {
        $relative = $file.FullName.Substring($releaseDirectory.Length + 1)
        '{0}  {1}' -f (Read-PackageSha256 $file.FullName), $relative
    }
    $manifest | Set-Content -LiteralPath (Join-Path $releaseDirectory 'SHA256SUMS.txt') -Encoding UTF8
    Compress-Archive -LiteralPath $releaseDirectory -DestinationPath ($releaseDirectory + '.zip')
    $archiveHash = Read-PackageSha256 ($releaseDirectory + '.zip')
    $archiveHash | Set-Content -LiteralPath ($releaseDirectory + '.zip.sha256') -Encoding UTF8
    Write-Output "发布完成：$releaseDirectory.zip"
    Write-Output "SHA256：$archiveHash"
} finally { Pop-Location }
