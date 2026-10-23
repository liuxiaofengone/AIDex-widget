# 03-install.ps1 - install/register the widget package. Normal (non-elevated) shell.
#
# Default: developer-mode loose registration of build\layout (fast dev loop,
# no repack needed after rebuilds - just re-run this script).
# -UseMsix: trust the dev cert and install the signed MSIX instead
#           (use this if loose registration fails with a signature error,
#           or to test the real packaged form).
param([switch]$UseMsix)
$ErrorActionPreference = 'Stop'

$root   = Split-Path $PSScriptRoot -Parent
$layout = Join-Path $root 'build\layout'
$msix   = Join-Path $root 'build\CodexBarWidget.msix'
$cer    = Join-Path $root 'build\devcert.cer'

# Record the CLI's absolute path so the packaged provider process can find it
# even when its inherited PATH differs from this shell's PATH.
& (Join-Path $PSScriptRoot '04-capture-usage-sample.ps1') -OnlyResolveCli

# Remove any older registration of the same package first (keeps pins clean).
Get-AppxPackage -Name CodexBarWidget | Remove-AppxPackage -ErrorAction SilentlyContinue

if (-not $UseMsix) {
    Write-Host "== loose registration (developer mode) =="
    Add-AppxPackage -Register (Join-Path $layout 'AppxManifest.xml') -ForceApplicationShutdown
    Write-Host "Registered OK."
} else {
    Write-Host "== install signed MSIX =="
    Import-Certificate -FilePath $cer -CertStoreLocation Cert:\CurrentUser\Root | Out-Null
    Import-Certificate -FilePath $cer -CertStoreLocation Cert:\CurrentUser\TrustedPublisher | Out-Null
    Add-AppxPackage -Path $msix -ForceApplicationShutdown
    Write-Host "Installed OK."
}

Get-AppxPackage -Name CodexBarWidget | Format-List Name, PackageFullName, Status
Write-Host "Now open the Widgets board (Win+W) -> Add widgets -> find 'AI Usage' and pin it."
Write-Host "Provider log: $env:TEMP\CodexBarWidget.log"
Write-Host "Headless check: & '...\CodexBarWidget.exe' --selftest  (exe is in build\layout)"
