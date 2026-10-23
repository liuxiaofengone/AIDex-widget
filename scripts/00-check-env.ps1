# 00-check-env.ps1 - read-only environment report. Run first, paste output back.
$ErrorActionPreference = 'Continue'

Write-Host "== OS =="
$os = Get-CimInstance Win32_OperatingSystem
Write-Host ("{0} build {1} arch {2}" -f $os.Caption, $os.BuildNumber, $os.OSArchitecture)

Write-Host "`n== Widgets board (Windows Web Experience Pack) =="
$wep = Get-AppxPackage -Name MicrosoftWindows.Client.WebExperience
if ($wep) {
    Write-Host ("OK   WebExperience {0} status {1}" -f $wep.Version, $wep.Status)
} else {
    Write-Host "MISSING - the Widgets board will not work on this image."
    Write-Host "          Use a Windows 11 image with consumer features, or install"
    Write-Host "          'Windows Web Experience Pack' from the Microsoft Store."
}

Write-Host "`n== Developer mode =="
$dm = Get-ItemPropertyValue -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock' `
        -Name AllowDevelopmentWithoutDevLicense -ErrorAction SilentlyContinue
if ($dm -eq 1) { Write-Host "OK   Developer mode ON" }
else { Write-Host "OFF  - 01-setup-dev.ps1 (elevated) enables it" }

Write-Host "`n== winget =="
if (Get-Command winget -ErrorAction SilentlyContinue) { winget --version }
else { Write-Host "MISSING winget" }

Write-Host "`n== dotnet SDK =="
if (Get-Command dotnet -ErrorAction SilentlyContinue) { dotnet --list-sdks }
else { Write-Host "not installed yet (01-setup-dev.ps1 installs it)" }

Write-Host "`n== Win-CodexBar CLI =="
$cli = Get-Command codexbar-cli.exe -ErrorAction SilentlyContinue
if ($cli) { Write-Host ("OK   " + $cli.Source) }
else { Write-Host "not on PATH (04-capture-usage-sample.ps1 searches install folders too)" }

Write-Host "`n== free disk on C: =="
$d = Get-PSDrive C
Write-Host ("{0:N1} GB free" -f ($d.Free / 1GB))
