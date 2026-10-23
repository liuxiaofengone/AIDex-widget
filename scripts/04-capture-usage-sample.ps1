# 04-capture-usage-sample.ps1
# 1) Finds codexbar-cli.exe and writes its absolute path to
#    %APPDATA%\CodexBarWidget\cli-path.txt (the provider reads it from there).
# 2) Unless -OnlyResolveCli: captures REAL usage/dashboard JSON into ..\samples
#    so the exact success-row schema can be pinned down before shipping.
param([switch]$OnlyResolveCli)
$ErrorActionPreference = 'Continue'

function Find-Cli {
    $c = Get-Command codexbar-cli.exe -ErrorAction SilentlyContinue
    if ($c) { return $c.Source }
    $dirs = @(
        "$env:LOCALAPPDATA\Programs\CodexBar",
        "$env:ProgramFiles\CodexBar",
        "${env:ProgramFiles(x86)}\CodexBar",
        "$env:LOCALAPPDATA\Microsoft\WinGet\Links"
    )
    foreach ($d in $dirs) {
        $p = Join-Path $d 'codexbar-cli.exe'
        if (Test-Path $p) { return $p }
    }
    $hit = Get-ChildItem "$env:LOCALAPPDATA\Programs" -Filter 'codexbar-cli.exe' `
           -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($hit) { return $hit.FullName }
    return $null
}

$cli = Find-Cli
if (-not $cli) {
    Write-Host "codexbar-cli.exe NOT FOUND. Install Win-CodexBar first (01-setup-dev.ps1), then re-run."
    exit 1
}
Write-Host "CLI found: $cli"

$cfgDir = Join-Path $env:APPDATA 'CodexBarWidget'
New-Item -ItemType Directory -Force -Path $cfgDir | Out-Null
Set-Content -Path (Join-Path $cfgDir 'cli-path.txt') -Value $cli -Encoding ASCII
Write-Host "Wrote $(Join-Path $cfgDir 'cli-path.txt')"

if ($OnlyResolveCli) { return }

$sampleDir = Join-Path (Split-Path $PSScriptRoot -Parent) 'samples'
New-Item -ItemType Directory -Force -Path $sampleDir | Out-Null

Write-Host "`nCapturing usage JSON (providers enabled in CodexBar settings)..."
& $cli usage -f json --pretty | Out-File (Join-Path $sampleDir 'usage-sample.json') -Encoding utf8
Write-Host "Saved samples\usage-sample.json"

Write-Host "Capturing dashboard snapshot..."
& $cli dashboard --pretty | Out-File (Join-Path $sampleDir 'dashboard-sample.json') -Encoding utf8
Write-Host "Saved samples\dashboard-sample.json"
