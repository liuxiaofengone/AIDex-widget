#Requires -Version 5.1
<#
.SYNOPSIS
  AIDexWidget 用量提醒：检查 Nowdex 用量快照，剩余低于阈值时弹 Windows 通知。
  由计划任务每 N 分钟隐藏运行（见 06-register-usage-alert.ps1）。
  每个（服务商，窗口，重置周期）只报一次；新周期（ResetsAt 变化）会重新报。
  余额型服务商没有剩余百分比，不参与提醒。

  用法：
    powershell -ExecutionPolicy Bypass -File usage-alert.ps1
    powershell -ExecutionPolicy Bypass -File usage-alert.ps1 -Threshold 10
    powershell -ExecutionPolicy Bypass -File usage-alert.ps1 -TestToast
#>
param(
    [int]$Threshold = 10,
    [string]$StatePath = (Join-Path $env:LOCALAPPDATA 'AIDexWidget\alert-state.json'),
    [switch]$TestToast
)

$ErrorActionPreference = 'Stop'

function Write-AlertLog {
    param([string]$Message)
    try {
        $logDir = Split-Path -Parent $StatePath
        if (-not (Test-Path -LiteralPath $logDir)) { New-Item -ItemType Directory -Path $logDir -Force | Out-Null }
        $log = Join-Path $logDir 'alert.log'
        Add-Content -LiteralPath $log -Value ("[{0}] {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Message) -Encoding UTF8
    } catch { }
}

function Get-ToastNotifier {
    try {
        $pkg = Get-AppxPackage -Name 'AIDexWidget' -ErrorAction Stop | Select-Object -First 1
        if (-not $pkg) { return $null }
        $aumid = $pkg.PackageFamilyName + '!App'
        [Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null
        return [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier($aumid)
    } catch {
        Write-AlertLog ("Toast sender unavailable: " + $_.Exception.Message)
        return $null
    }
}

function Show-Toast {
    param([string]$Title, [string]$Body)
    try {
        $notifier = Get-ToastNotifier
        if (-not $notifier) { return $false }
        $t = [System.Security.SecurityElement]::Escape($Title)
        $b = [System.Security.SecurityElement]::Escape($Body)
        $xmlText = "<toast><visual><binding template=`"ToastGeneric`"><text>$t</text><text>$b</text></binding></visual></toast>"
        $xml = New-Object Windows.Data.Xml.Dom.XmlDocument
        $xml.LoadXml($xmlText)
        $toast = New-Object Windows.UI.Notifications.ToastNotification $xml
        $notifier.Show($toast)
        return $true
    } catch {
        Write-AlertLog ("Toast failed: " + $_.Exception.Message)
        return $false
    }
}

function Norm-Guid {
    param([string]$Guid)
    return ($Guid -replace '-', '').ToLower()
}

function Format-Reset {
    param([string]$Iso)
    if (-not $Iso) { return '' }
    try { $reset = [DateTimeOffset]::Parse($Iso) } catch { return '' }
    $local = $reset.ToLocalTime()
    $diff = $reset - [DateTimeOffset]::Now
    if ($diff -le [TimeSpan]::Zero) { return $local.ToString('M月d日 HH:mm') + ' 重置' }
    if ($diff -lt [TimeSpan]::FromHours(1)) {
        $m = [int][Math]::Floor($diff.TotalMinutes)
        if ($m -lt 1) { return '即将重置' }
        return "$m 分钟后重置"
    }
    if ($diff -lt [TimeSpan]::FromDays(1)) {
        $h = [int][Math]::Floor($diff.TotalHours)
        $mi = $diff.Minutes
        return "$h 小时 $mi 分钟后重置"
    }
    return $local.ToString('M月d日 HH:mm') + ' 重置'
}

try {
    if ($TestToast) {
        if (Show-Toast 'AI 用量提醒测试' '提醒通道正常。真实提醒会在某家剩余低于阈值时弹出。') {
            Write-Host '测试通知已发送。'
        } else {
            Write-Host '测试通知未能发送（小组件包未找到或通知发送方不可用），详见 alert.log。'
        }
        exit 0
    }

    $reportsPath = $null
    $bestTime = [DateTime]::MinValue
    foreach ($dir in Get-ChildItem (Join-Path $env:LOCALAPPDATA 'Packages') -Directory -Filter 'RandyLu.Nowdex_*' -ErrorAction SilentlyContinue) {
        $candidate = Join-Path $dir.FullName 'LocalState\reports.json'
        if (Test-Path -LiteralPath $candidate) {
            $t = (Get-Item -LiteralPath $candidate).LastWriteTime
            if ($t -gt $bestTime) { $bestTime = $t; $reportsPath = $candidate }
        }
    }
    if (-not $reportsPath) { exit 0 }

    $localState = Split-Path -Parent $reportsPath
    $names = @{}
    try {
        $dlPath = Join-Path $localState 'reset-deadlines.json'
        if (Test-Path -LiteralPath $dlPath) {
            $dl = Get-Content -LiteralPath $dlPath -Raw -Encoding UTF8 | ConvertFrom-Json
            foreach ($prop in $dl.PSObject.Properties) {
                $parts = $prop.Name -split '\.'
                if ($parts.Count -ge 3) {
                    $g = Norm-Guid $parts[1]
                    if ($g -and -not $names.ContainsKey($g)) { $names[$g] = $parts[0] }
                }
            }
        }
    } catch { }
    try {
        $connPath = Join-Path $localState 'connections.json'
        if (Test-Path -LiteralPath $connPath) {
            $conns = Get-Content -LiteralPath $connPath -Raw -Encoding UTF8 | ConvertFrom-Json
            foreach ($c in $conns) {
                $g = Norm-Guid ([string]$c.Id)
                if ($g -and -not $names.ContainsKey($g) -and $c.Provider) { $names[$g] = [string]$c.Provider }
            }
        }
    } catch { }

    $reports = Get-Content -LiteralPath $reportsPath -Raw -Encoding UTF8 | ConvertFrom-Json

    $stateKeys = @()
    $stateSet = @{}
    if (Test-Path -LiteralPath $StatePath) {
        try {
            $raw = Get-Content -LiteralPath $StatePath -Raw -Encoding UTF8
            if ($raw -and $raw.Trim()) {
                $loaded = $raw | ConvertFrom-Json
                if ($loaded -is [System.Array]) {
                    foreach ($k in $loaded) {
                        $s = [string]$k
                        if (-not $stateSet.ContainsKey($s)) { $stateSet[$s] = $true; $stateKeys += $s }
                    }
                }
            }
        } catch { $stateKeys = @(); $stateSet = @{} }
    }

    $changed = $false
    foreach ($prov in $reports.PSObject.Properties) {
        $guid = Norm-Guid $prov.Name
        $entry = $prov.Value
        $pname = '未命名服务'
        if ($guid -and $names.ContainsKey($guid)) { $pname = $names[$guid] }
        if (-not $entry.Windows) { continue }
        foreach ($w in $entry.Windows) {
            $used = 0
            try { $used = [double]$w.UsedPercent } catch { }
            $rem = $null
            try { if ($null -ne $w.RemainingPercent) { $rem = [double]$w.RemainingPercent } } catch { }
            if ($null -eq $rem) { $rem = 100 - $used }
            if ($rem -ge $Threshold) { continue }
            $resetsAt = ''
            try { if ($null -ne $w.ResetsAt) { $resetsAt = [string]$w.ResetsAt } } catch { }
            $key = "$guid|$($w.Id)|$resetsAt"
            if ($stateSet.ContainsKey($key)) { continue }
            $resetText = Format-Reset $resetsAt
            $body = "$pname $($w.Title) 剩余 $([int][Math]::Round($rem))%（低于 ${Threshold}%）"
            if ($resetText) { $body += "，" + $resetText }
            if (Show-Toast 'AI 用量提醒' $body) {
                Write-AlertLog ("Notified: " + $body)
            }
            $stateSet[$key] = $true
            $stateKeys += $key
            $changed = $true
        }
    }

    if ($changed -or $stateKeys.Count -gt 200) {
        $stateDir = Split-Path -Parent $StatePath
        if (-not (Test-Path -LiteralPath $stateDir)) { New-Item -ItemType Directory -Path $stateDir -Force | Out-Null }
        if ($stateKeys.Count -gt 200) { $stateKeys = $stateKeys[($stateKeys.Count - 200)..($stateKeys.Count - 1)] }
        $tmp = $StatePath + '.tmp'
        $stateKeys | ConvertTo-Json | Set-Content -LiteralPath $tmp -Encoding UTF8
        Move-Item -LiteralPath $tmp -Destination $StatePath -Force
    }
    exit 0
} catch {
    Write-AlertLog ("usage-alert failed: " + $_.Exception.Message)
    exit 0
}
