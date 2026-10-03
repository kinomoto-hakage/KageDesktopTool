param([string]$OutputDirectory = 'releases/KageDesktopTool-win-x64-1.0.0')

$ErrorActionPreference = 'Stop'
function Read-Sha256([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    $hash = [Security.Cryptography.SHA256]::Create()
    try { [BitConverter]::ToString($hash.ComputeHash($stream)).Replace('-', '') }
    finally { $stream.Dispose(); $hash.Dispose() }
}
$repoDirectory = $PSScriptRoot
$releaseDirectory = [IO.Path]::GetFullPath((Join-Path $repoDirectory $OutputDirectory))
$allowedDirectory = [IO.Path]::GetFullPath((Join-Path $repoDirectory 'releases')) + [IO.Path]::DirectorySeparatorChar
if (-not $releaseDirectory.StartsWith($allowedDirectory, [StringComparison]::OrdinalIgnoreCase)) {
    throw '发布位置必须位于本仓库 releases 目录内。'
}
if ((Test-Path -LiteralPath $releaseDirectory) -or (Test-Path -LiteralPath ($releaseDirectory + '.zip'))) {
    throw '发布位置已存在，请指定新的 releases 子目录，避免覆盖正在运行的包。'
}

Push-Location $repoDirectory
try {
    # 只有维护者构建需要 SDK 和下载运行时；日常使用只需解压、双击。
    & rtk proxy dotnet publish src/KageDesktopTool/KageDesktopTool.csproj `
        -p:PublishProfile=WindowsX64 --source https://api.nuget.org/v3/index.json -o $releaseDirectory
    if ($LASTEXITCODE -ne 0) { throw '发布失败，未生成压缩包。' }
    $process = Start-Process -FilePath (Join-Path $releaseDirectory 'KageDesktopTool.exe') `
        -ArgumentList '--publish-check' -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(30000)) { $process.Kill(); throw '发布产物检查超时。' }
    if ($process.ExitCode -ne 0) { throw '发布产物检查失败，查看 verification/publish-package.txt。' }

    $files = Get-ChildItem -LiteralPath $releaseDirectory -Recurse -File | Sort-Object FullName
    $manifest = foreach ($file in $files) {
        $relative = $file.FullName.Substring($releaseDirectory.Length + 1)
        '{0}  {1}' -f (Read-Sha256 $file.FullName), $relative
    }
    $manifest | Set-Content -LiteralPath (Join-Path $releaseDirectory 'SHA256SUMS.txt') -Encoding UTF8
    Compress-Archive -LiteralPath $releaseDirectory -DestinationPath ($releaseDirectory + '.zip')
    $archiveHash = Read-Sha256 ($releaseDirectory + '.zip')
    $archiveHash | Set-Content -LiteralPath ($releaseDirectory + '.zip.sha256') -Encoding UTF8
    Write-Output "发布完成：$releaseDirectory.zip"
    Write-Output "SHA256：$archiveHash"
} finally { Pop-Location }
