# 02-build.ps1 - build the provider, assemble the package layout, pack + sign an MSIX.
# Produces:
#   build\layout\            loose layout (used by 03-install.ps1 default dev registration)
#   build\AIDexWidget.msix  signed package (used by 03-install.ps1 -UseMsix)
#   build\devcert.cer        dev certificate (publisher CN=AIDexWidgetDev)
param(
    # Build target. Use win-arm64 on ARM64 machines (e.g. Surface / Snapdragon
    # laptops): publish RID and the package architecture follow this choice.
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Rid = 'win-x64'
)

$ErrorActionPreference = 'Stop'

$root     = Split-Path $PSScriptRoot -Parent
$proj     = Join-Path $root 'src\AIDexWidget\AIDexWidget.csproj'
$srcDir   = Split-Path $proj -Parent
$buildDir = Join-Path $root 'build'
$pubDir   = Join-Path $buildDir 'publish'
$layout   = Join-Path $buildDir 'layout'

if (Test-Path $buildDir) { Remove-Item $buildDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $pubDir, $layout | Out-Null

Write-Host "== dotnet publish ($Rid, self-contained Windows App SDK) =="
dotnet publish $proj -c Release -r $Rid --self-contained true -o $pubDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Write-Host "`n== assemble layout =="
Copy-Item (Join-Path $pubDir '*') $layout -Recurse -Force
Copy-Item (Join-Path $srcDir 'Package.appxmanifest') (Join-Path $layout 'AppxManifest.xml') -Force
if ($Rid -eq 'win-arm64') {
    # The source manifest declares x64; the layout copy must match the build.
    # Use .NET IO with explicit UTF-8: a Get-Content/Set-Content round-trip in
    # Windows PowerShell 5.1 reads the BOM-less UTF-8 manifest as ANSI and
    # mangles its Chinese strings, and MakeAppx then fails with C00CEE21.
    $mf = Join-Path $layout 'AppxManifest.xml'
    $xml = [IO.File]::ReadAllText($mf, [Text.Encoding]::UTF8)
    $xml = $xml.Replace('ProcessorArchitecture="x64"', 'ProcessorArchitecture="arm64"')
    [IO.File]::WriteAllText($mf, $xml, (New-Object Text.UTF8Encoding $false))
}
Copy-Item (Join-Path $srcDir 'Assets') (Join-Path $layout 'Assets') -Recurse -Force

Write-Host "`n== locate makeappx / signtool =="
$sdkBin = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' -Directory |
          Where-Object { $_.Name -match '^10\.' } | Sort-Object Name -Descending | Select-Object -First 1
if (-not $sdkBin) { throw "Windows SDK bin folder not found. Run 01-setup-dev.ps1 first." }
$makeappx = Join-Path $sdkBin.FullName 'x64\makeappx.exe'
$signtool = Join-Path $sdkBin.FullName 'x64\signtool.exe'
if (-not (Test-Path $makeappx)) { throw "makeappx.exe not found: $makeappx" }

Write-Host "`n== dev signing certificate =="
$subject = 'CN=AIDexWidgetDev'   # must equal the manifest Publisher
$cert = Get-ChildItem Cert:\CurrentUser\My |
        Where-Object { $_.Subject -eq $subject -and $_.HasPrivateKey } |
        Sort-Object NotAfter -Descending | Select-Object -First 1
if (-not $cert) {
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $subject `
            -CertStoreLocation Cert:\CurrentUser\My -NotAfter (Get-Date).AddYears(5)
    Write-Host "Created cert $($cert.Thumbprint)"
} else {
    Write-Host "Reusing cert $($cert.Thumbprint)"
}
$pwd = ConvertTo-SecureString 'aidex-dev' -Force -AsPlainText
$pfx = Join-Path $buildDir 'devcert.pfx'
$cer = Join-Path $buildDir 'devcert.cer'
Export-PfxCertificate -Cert $cert -FilePath $pfx -Password $pwd | Out-Null
Export-Certificate -Cert $cert -FilePath $cer | Out-Null

Write-Host "`n== makeappx pack + sign =="
$msix = Join-Path $buildDir 'AIDexWidget.msix'
& $makeappx pack /d $layout /p $msix /o
if ($LASTEXITCODE -ne 0) { throw "makeappx pack failed" }
& $signtool sign /fd SHA256 /f $pfx /p 'aidex-dev' $msix
if ($LASTEXITCODE -ne 0) { throw "signtool failed" }

Write-Host "`n== build done =="
Write-Host "Layout : $layout"
Write-Host "MSIX   : $msix"
Write-Host "Cert   : $cer"
Write-Host "Next   : 03-install.ps1"
