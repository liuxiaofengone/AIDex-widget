#Requires -Version 5.1
<#
.SYNOPSIS
  注册 / 注销 AIDexWidget 用量提醒计划任务：每 N 分钟（默认 2 分钟）隐藏运行
  usage-alert.ps1，检查 Nowdex 用量快照，剩余低于阈值（默认 10%）时弹
  Windows 通知；每个（服务商，窗口，重置周期）只报一次。

  用法：
    powershell -ExecutionPolicy Bypass -File scripts\06-register-usage-alert.ps1
    powershell -ExecutionPolicy Bypass -File scripts\06-register-usage-alert.ps1 -Threshold 15 -IntervalMinutes 2
    powershell -ExecutionPolicy Bypass -File scripts\06-register-usage-alert.ps1 -Unregister

  注册后先跑一次测试通知，确认提醒通道能弹出来：
    powershell -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\AIDexWidget\usage-alert.ps1" -TestToast
#>
param(
    [switch]$Unregister,
    [int]$Threshold = 10,
    [int]$IntervalMinutes = 2
)

$ErrorActionPreference = 'Stop'
$taskName = 'AIDexWidget Usage Alert'
$appDir = Join-Path $env:LOCALAPPDATA 'AIDexWidget'
$destScript = Join-Path $appDir 'usage-alert.ps1'

if ($Unregister) {
    if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) {
        Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
        Write-Host "已注销计划任务：$taskName"
    } else {
        Write-Host "计划任务不存在：$taskName"
    }
    Write-Host "注意：状态文件（alert-state.json）未删除，重装后同周期内已报过的不重复报。"
    exit 0
}

if (-not (Test-Path -LiteralPath $appDir)) {
    New-Item -ItemType Directory -Path $appDir -Force | Out-Null
}
# 任务指向 LocalAppData 下的副本，仓库移动 / 删除后任务仍可用；
# 脚本更新后重跑本文件即可同步副本（并更新间隔与阈值）。
$srcScript = Join-Path $PSScriptRoot 'usage-alert.ps1'
Copy-Item -LiteralPath $srcScript -Destination $destScript -Force
$srcLauncher = Join-Path $PSScriptRoot 'usage-alert-launcher.vbs'
$destLauncher = Join-Path $appDir 'usage-alert-launcher.vbs'
Copy-Item -LiteralPath $srcLauncher -Destination $destLauncher -Force

# 任务动作走 wscript 启动器：直接跑 powershell.exe 时，-WindowStyle Hidden 是
# 窗口建出来之后再隐藏，有些系统上每几分钟会闪一下控制台；wscript 以窗口
# 样式 0 启动，进程从创建起就是隐藏的，不会闪。
$action = New-ScheduledTaskAction -Execute 'wscript.exe' `
    -Argument "`"$destLauncher`" $Threshold"
# RepetitionDuration 必须是一个任务计划能接受的有限值：[TimeSpan]::MaxValue
# 会被序列化成 P999999999DT23H59M59S，注册时被计划程序以“超出范围”拒绝
#（HRESULT 0x80041318）。365 天足够长，重跑本脚本会重新注册、顺带续期。
$trigger = New-ScheduledTaskTrigger -Once -At (Get-Date) `
    -RepetitionInterval (New-TimeSpan -Minutes $IntervalMinutes) `
    -RepetitionDuration (New-TimeSpan -Days 365)
# 电池相关参数用开关形式：New-ScheduledTaskSettingsSet 在 Windows PowerShell 5.1
# 里的正式参数名是 AllowStartIfOnBatteries / DontStopIfGoingOnBatteries（语义与
# 任务计划 CIM 属性 DisallowStartIfOnBatteries / StopIfGoingOnBatteries 正好反过来）。
try {
    $settings = New-ScheduledTaskSettingsSet `
        -ExecutionTimeLimit (New-TimeSpan -Minutes 2) `
        -MultipleInstances IgnoreNew `
        -AllowStartIfOnBatteries `
        -DontStopIfGoingOnBatteries `
        -StartWhenAvailable
} catch {
    Write-Host "注意：电池相关参数在本机不受支持，改用默认电池策略（仅交流电源时运行）。"
    $settings = New-ScheduledTaskSettingsSet `
        -ExecutionTimeLimit (New-TimeSpan -Minutes 2) `
        -MultipleInstances IgnoreNew `
        -StartWhenAvailable
}
# 仅登录后运行（Interactive），不需要管理员密码。
$principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive
Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger `
    -Settings $settings -Principal $principal `
    -Description "AIDexWidget 用量提醒：每 $IntervalMinutes 分钟检查 Nowdex 用量快照，剩余低于 $Threshold% 时弹 Windows 通知（每个重置周期只报一次）。" -Force | Out-Null
Start-ScheduledTask -TaskName $taskName

Write-Host "已注册计划任务：$taskName（每 $IntervalMinutes 分钟，阈值 $Threshold%）"
Write-Host "提醒脚本：$destScript"
Write-Host "先跑一次测试通知，确认通道能弹出来："
Write-Host "powershell -ExecutionPolicy Bypass -File `"$destScript`" -TestToast"
