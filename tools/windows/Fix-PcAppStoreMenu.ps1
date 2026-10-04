param([ValidateSet('Apply','Restore','Status')][string]$Mode = 'Status')

# 故障仅来自这个现代卸载菜单稀疏包，不卸载 PCAppStore 主软件或传统菜单扩展。
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$backupRoot = Join-Path $repoRoot '.scratch/windows-shell-latency/final-backup-v2'
$statePath = Join-Path $backupRoot 'state.json'
$installerPath = Join-Path $backupRoot 'PCAppStoreExtPkg64.msix'
$externalLocation = 'C:\Program Files (x86)\PCAppStore\AppStore'
$sourceInstaller = Join-Path $externalLocation 'PCAppStoreExtPkg64.msix'
$packageFamily = 'PCAppStoreExt_4ptzsrs9z9qm8'
$packageData = Join-Path $env:LOCALAPPDATA ('Packages\' + $packageFamily)
$dataBackup = Join-Path $backupRoot 'user-data'

function FileDigest([string]$Path) {
    $digest = [System.Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($digest.ComputeHash([System.IO.File]::ReadAllBytes($Path))).Replace('-','') }
    finally { $digest.Dispose() }
}

function CheckPackage($Package) {
    if ($Package.Name -ne 'PCAppStoreExt' -or $Package.PackageFamilyName -ne $packageFamily -or $Package.IsFramework -or $Package.NonRemovable) {
        throw '目标不是已确认的单一可移除菜单扩展，停止操作'
    }
}

function InstallerIdentity {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($installerPath)
    try {
        $entry = $archive.GetEntry('AppxManifest.xml')
        if ($null -eq $entry) { throw '安装包没有清单' }
        $reader = [System.IO.StreamReader]::new($entry.Open())
        try { $manifest = [xml]$reader.ReadToEnd() }
        finally { $reader.Dispose() }
        $identity = $manifest.DocumentElement.Identity
        return [PSCustomObject]@{ Name=$identity.GetAttribute('Name'); Version=$identity.GetAttribute('Version');
            Architecture=$identity.GetAttribute('ProcessorArchitecture'); ResourceId=$identity.GetAttribute('ResourceId'); Publisher=$identity.GetAttribute('Publisher') }
    }
    finally { $archive.Dispose() }
}

function SaveState($Saved) {
    $temporaryState = $statePath + '.tmp'
    $Saved | ConvertTo-Json -Depth 8 | Out-File -LiteralPath $temporaryState -Encoding utf8
    Move-Item -LiteralPath $temporaryState -Destination $statePath -Force
}

function CopySettings([string]$Source, [string]$Destination) {
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    foreach ($sourceFile in Get-ChildItem -LiteralPath $Source -Force -Recurse -File) {
        # Windows 的零字节加密漫游锁由系统重建，不属于持久组件数据。
        if ($sourceFile.Name -eq 'roaming.lock' -and $sourceFile.Length -eq 0) { continue }
        $relativeFile = $sourceFile.FullName.Substring($Source.TrimEnd('\').Length).TrimStart('\')
        $destinationFile = Join-Path $Destination $relativeFile
        New-Item -ItemType Directory -Path (Split-Path $destinationFile -Parent) -Force | Out-Null
        Copy-Item -LiteralPath $sourceFile.FullName -Destination $destinationFile -Force
    }
}

function CheckState {
    $saved = Get-Content -LiteralPath $statePath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($saved.Name -ne 'PCAppStoreExt' -or $saved.Family -ne $packageFamily -or $saved.ExternalLocation -ne $externalLocation) {
        throw '恢复备份范围不符，停止操作'
    }
    if ((FileDigest $installerPath) -ne $saved.InstallerHash) { throw '原安装包备份校验失败' }
    $identity = InstallerIdentity
    $identityFullName = $identity.Name + '_' + $identity.Version + '_' + $identity.Architecture + '_' + $identity.ResourceId + '_4ptzsrs9z9qm8'
    if ($identityFullName -ne $saved.FullName -or $identity.Publisher -ne $saved.Publisher) { throw '安装包身份或版本与备份的注册不一致' }
    if ($saved.DataExisted) {
        if (-not (Test-Path -LiteralPath $dataBackup)) { throw '持久组件数据备份不存在，停止恢复' }
        foreach ($recordedFile in $saved.DataFiles) {
            $savedFile = Join-Path $dataBackup $recordedFile.Path
            if (-not (Test-Path -LiteralPath $savedFile) -or (FileDigest $savedFile) -ne $recordedFile.Hash) { throw '组件数据备份缺失或校验失败' }
        }
    }
    return $saved
}

$currentPackage = Get-AppxPackage -Name 'PCAppStoreExt'
if ($Mode -eq 'Status') {
    Write-Output ([PSCustomObject]@{ ModernUninstallRegistered=($null -ne $currentPackage); BackupAvailable=(Test-Path -LiteralPath $statePath) } | ConvertTo-Json)
    exit
}

if ($Mode -eq 'Apply') {
    if ($null -eq $currentPackage) { Write-Output '故障现代菜单组件已经停用。'; exit }
    CheckPackage $currentPackage
    if (-not (Test-Path -LiteralPath $sourceInstaller)) { throw '原签名安装包不存在，不能准备可靠恢复' }
    if (-not (Test-Path -LiteralPath $statePath)) {
        if (Test-Path -LiteralPath $backupRoot) { throw '备份目录未完成，保留现场并停止，不能覆盖' }
        New-Item -ItemType Directory -Path $backupRoot | Out-Null
        Copy-Item -LiteralPath $sourceInstaller -Destination $installerPath
        if ((FileDigest $sourceInstaller) -ne (FileDigest $installerPath)) { throw '安装包副本不一致' }
        if (Test-Path -LiteralPath $packageData) { CopySettings $packageData $dataBackup }
        $originalDataBackup = Join-Path $repoRoot '.scratch/windows-shell-latency/package-backup/user-data'
        if (Test-Path -LiteralPath $originalDataBackup) { CopySettings $originalDataBackup $dataBackup }
        $identity = InstallerIdentity
        if ($identity.Name -ne $currentPackage.Name -or $identity.Version -ne [string]$currentPackage.Version -or
            $identity.Architecture -ne ([string]$currentPackage.Architecture).ToLowerInvariant() -or $identity.Publisher -ne $currentPackage.Publisher) {
            throw '原安装包与当前组件身份或版本不一致，不能撤销注册'
        }
        $dataFiles = @(Get-ChildItem -LiteralPath $dataBackup -Recurse -File -ErrorAction SilentlyContinue | ForEach-Object {
            [PSCustomObject]@{ Path=$_.FullName.Substring($dataBackup.Length).TrimStart('\'); Hash=(FileDigest $_.FullName) }
        })
        SaveState ([PSCustomObject]@{ Name=$currentPackage.Name; FullName=$currentPackage.PackageFullName; Family=$packageFamily;
            Publisher=$currentPackage.Publisher; ExternalLocation=$externalLocation; InstallerHash=(FileDigest $installerPath);
            DataExisted=(Test-Path -LiteralPath $dataBackup); DataFiles=$dataFiles; RestorePhase='None' })
    }
    $saved = CheckState
    if ($currentPackage.PackageFullName -ne $saved.FullName) { throw '组件版本已改变，拒绝沿用旧备份' }
    if ($currentPackage.Publisher -ne $saved.Publisher) { throw '当前组件发布者与备份不一致' }
    if ($saved.RestorePhase -eq 'DataPending') { throw '上次组件数据恢复未完成，请先 Restore' }
    Remove-AppxPackage -Package $currentPackage.PackageFullName -ErrorAction Stop
    if (Get-AppxPackage -Name 'PCAppStoreExt') { throw '组件注册仍存在，未完成停用' }
    Write-Output '已仅撤销当前用户 PCAppStoreExt 现代卸载菜单组件注册；主软件、传统强力卸载和其他扩展保持。'
    Write-Output '恢复：rtk proxy powershell -NoProfile -File tools/windows/Fix-PcAppStoreMenu.ps1 -Mode Restore'
    exit
}

$saved = CheckState
if ($null -ne $currentPackage) {
    CheckPackage $currentPackage
    if ($currentPackage.PackageFullName -ne $saved.FullName) { throw '已注册组件版本与恢复备份不一致' }
    if ($saved.RestorePhase -ne 'DataPending') { Write-Output '组件当前已经注册，不覆盖现有组件数据。'; exit }
}
if (-not (Test-Path -LiteralPath (Join-Path $externalLocation 'PCAppStoreExt64.dll'))) { throw '主软件的原外部位置已经改变，停止恢复' }
if ($null -eq $currentPackage) {
    $saved.RestorePhase = 'DataPending'; SaveState $saved
    Add-AppxPackage -Path $installerPath -ExternalLocation $externalLocation -ErrorAction Stop
}
$restoredPackage = Get-AppxPackage -Name 'PCAppStoreExt'
CheckPackage $restoredPackage
if ($restoredPackage.PackageFullName -ne $saved.FullName) { throw '恢复后的组件版本不一致' }
if ($saved.DataExisted -and (Test-Path -LiteralPath $dataBackup)) {
    New-Item -ItemType Directory -Path $packageData -Force | Out-Null
    CopySettings $dataBackup $packageData
}
$saved.RestorePhase = 'Complete'; SaveState $saved
Write-Output '已从原始签名稀疏包及原外部位置恢复菜单组件，并恢复该组件数据。'
