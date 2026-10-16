#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
& (Join-Path $PSScriptRoot 'Test-Repository.ps1') -Repository $repository
Push-Location $repository
try {
    & dotnet run --project tests/Kage.Workspace.Checks -c Release -- --suite ci
    if ($LASTEXITCODE -ne 0) { throw 'CI 业务检查失败。' }
    $buildDirectory = Join-Path $repository ('.scratch/check-runs/build-' + [Guid]::NewGuid().ToString('N') + '/')
    & dotnet build src/KageDesktopTool/KageDesktopTool.csproj -c Release -t:Rebuild --disable-build-servers "-p:BaseOutputPath=$buildDirectory"
    if ($LASTEXITCODE -ne 0) { throw 'Windows 构建失败。' }
    & (Join-Path $PSScriptRoot 'Test-VerificationWorkflow.ps1')
} finally { Pop-Location }
