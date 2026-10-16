param([string]$Repository = (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent))

$ErrorActionPreference = 'Stop'
$catalog = Get-Content -LiteralPath (Join-Path $Repository 'tools/windows/checks.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if ($catalog.schemaVersion -ne 1) { throw '不支持的检查清单版本。' }
$names = @{}
$routes = @{}
foreach ($check in $catalog.checks) {
    if ($check.name -notmatch '^[a-z][a-z0-9-]*$' -or $names.ContainsKey($check.name)) { throw "重复或非法检查名称：$($check.name)" }
    $names[$check.name] = $true
    if (@($check.arguments).Count -eq 0 -or $check.arguments[0] -notmatch '^--([a-z0-9-]+)-check$') { throw "检查入口缺失：$($check.name)" }
    $routes[$Matches[1]] = $true
    if (@($check.suites).Count -eq 0 -or $check.interactive -isnot [bool] -or $check.changesSystem -isnot [bool]) { throw "检查必须声明分组和系统影响：$($check.name)" }
    foreach ($argument in $check.arguments) { if ($argument -notmatch '^--[a-z0-9=-]+$') { throw "非法检查参数：$argument" } }
}
$program = Get-Content -LiteralPath (Join-Path $Repository 'src/KageDesktopTool/Program.cs') -Raw -Encoding UTF8
$entryPoints = @([regex]::Matches($program, '"--([a-z0-9-]+)-check"') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
$expected = @(@($routes.Keys) + @($catalog.helpers) | Sort-Object -Unique)
$difference = @(Compare-Object $expected $entryPoints)
if ($difference.Count) { throw "检查入口与清单不一致（=> 为未登记入口）：$($difference | Out-String)" }

# 检查类和专用证据模块可以写诊断；正式交互路径不能残留临时追加日志。
$sources = @(Get-ChildItem -LiteralPath (Join-Path $Repository 'src') -Recurse -Filter '*.cs' -File |
    Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' })
foreach ($file in $sources) {
    if ($file.Name -like '*Checks.cs' -or $file.Name -eq 'DesktopCheckSession.cs') { continue }
    $source = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8
    if ($source -match '\.scratch' -or $source -match '\bFile\s*\.\s*(AppendAllText(?:Async)?|AppendAllLines(?:Async)?|AppendText)\s*\(') {
        throw "正式代码残留临时诊断路径或直接追加日志：$($file.FullName)。请移入检查类或专用诊断模块。"
    }
}
Get-ChildItem -LiteralPath $Repository -Recurse -Filter '*.ps1' -File |
    Where-Object { $_.FullName -notmatch '[\\/](\.git|\.scratch|releases|bin|obj)[\\/]' } |
    ForEach-Object {
        $parseErrors = $null; $tokens = $null
        $null = [Management.Automation.Language.Parser]::ParseFile($_.FullName, [ref]$tokens, [ref]$parseErrors)
        if ($parseErrors.Count) { throw "PowerShell 语法错误：$($_.FullName)：$parseErrors" }
    }
Write-Output "仓库约束通过：$($catalog.checks.Count) 项检查、$($entryPoints.Count) 个入口；正式代码诊断与 PowerShell 语法正常。"
