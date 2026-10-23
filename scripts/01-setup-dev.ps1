# 01-setup-dev.ps1 - run in an ELEVATED PowerShell (Run as administrator).
# Enables Developer Mode and installs: .NET 8 SDK, Windows SDK, Win-CodexBar.
#Requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'

Write-Host "== Enabling Developer Mode =="
$key = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock'
New-Item -Path $key -Force | Out-Null
New-ItemProperty -Path $key -Name AllowDevelopmentWithoutDevLicense -Value 1 -PropertyType DWord -Force | Out-Null
New-ItemProperty -Path $key -Name AllowAllTrustedApps -Value 1 -PropertyType DWord -Force | Out-Null
Write-Host "Developer Mode enabled."

function Install-Winget($id) {
    Write-Host "`n== winget install $id =="
    winget install --id $id -e --accept-source-agreements --accept-package-agreements
    if ($LASTEXITCODE -ne 0) { Write-Host "WARN: $id exited with $LASTEXITCODE (already installed is fine)" }
}

Install-Winget 'Microsoft.DotNet.SDK.8'
Install-Winget 'Microsoft.WindowsSDK.10.0.26100'
Install-Winget 'Finesssee.Win-CodexBar'

Write-Host "`nDone. Open a NEW terminal so PATH picks up dotnet, then run 02-build.ps1."
