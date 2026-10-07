# 发布及验收共用版本来源和 SHA256 算法，避免目录与 EXE 版本各自漂移。
function Read-ReleaseVersion([string]$Repository) {
    [xml]$project = Get-Content -LiteralPath (Join-Path $Repository 'src/KageDesktopTool/KageDesktopTool.csproj') -Encoding UTF8 -Raw
    $version = [string]$project.Project.PropertyGroup.Version
    if ($version -notmatch '^\d+\.\d+\.\d+$') { throw '项目版本必须为三段数字。' }
    return $version
}

function Read-PackageSha256([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    $hash = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($hash.ComputeHash($stream)).Replace('-', '') }
    finally { $stream.Dispose(); $hash.Dispose() }
}
