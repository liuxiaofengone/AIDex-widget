#Requires -Version 5.1
<#
.SYNOPSIS
  注册 / 注销 CodexBarWidget 用量预取计划任务：每 3 分钟运行一次
  prefetch-usage.ps1，把 codexbar-cli 的用量 JSON 写到
  %LOCALAPPDATA%\CodexBarWidget\latest.json，供小组件打开时直接读取。

  用法：
    powershell -ExecutionPolicy Bypass -File scripts\05-register-prefetch-task.ps1
    powershell -ExecutionPolicy Bypass -File scripts\05-register-prefetch-task.ps1 -Unregister
#>
param([switch]$Unregister)

$ErrorActionPreference = 'Stop'
$taskName = 'CodexBarWidget Prefetch'
$appDir = Join-Path $env:LOCALAPPDATA 'CodexBarWidget'
$destScript = Join-Path $appDir 'prefetch-usage.ps1'

if ($Unregister) {
    if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) {
        Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
        Write-Host "已注销计划任务：$taskName"
    } else {
        Write-Host "计划任务不存在：$taskName"
    }
    exit 0
}

if (-not (Test-Path -LiteralPath $appDir)) {
    New-Item -ItemType Directory -Path $appDir -Force | Out-Null
}
# 任务指向 LocalAppData 下的副本，仓库移动 / 删除后任务仍可用；
# 脚本更新后重跑本文件即可同步副本。
$srcScript = Join-Path $PSScriptRoot 'prefetch-usage.ps1'
Copy-Item -LiteralPath $srcScript -Destination $destScript -Force

$action = New-ScheduledTaskAction -Execute 'powershell.exe' `
    -Argument "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$destScript`""
try {
    $trigger = New-ScheduledTaskTrigger -Once -At (Get-Date) `
        -RepetitionInterval (New-TimeSpan -Minutes 3) `
        -RepetitionDuration ([TimeSpan]::MaxValue)
} catch {
    # 极少数系统上 MaxValue 不被接受时退到 10 年，效果等同。
    $trigger = New-ScheduledTaskTrigger -Once -At (Get-Date) `
        -RepetitionInterval (New-TimeSpan -Minutes 3) `
        -RepetitionDuration (New-TimeSpan -Days 3650)
}
# 电池相关参数用开关形式：New-ScheduledTaskSettingsSet 在 Windows PowerShell 5.1
# 里的正式参数名是 AllowStartIfOnBatteries / DontStopIfGoingOnBatteries（语义与
# 任务计划 CIM 属性 DisallowStartIfOnBatteries / StopIfGoingOnBatteries 正好反过来）。
# 之前误用了 CIM 属性名，部分系统上不认。如果开关也不被支持，退回默认电池策略
#（仅交流电源时运行），任务本身仍可注册，不至于整个脚本失败。
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
    -Description 'CodexBarWidget 用量预取：每 3 分钟把 codexbar-cli 的用量 JSON 写到 latest.json，供小组件打开时直接读取。' -Force | Out-Null
Start-ScheduledTask -TaskName $taskName

Write-Host "已注册计划任务：$taskName（每 3 分钟）"
Write-Host "预取脚本：$destScript"
Write-Host "数据文件：$(Join-Path $appDir 'latest.json')（首次运行约 1 分钟内生成）"
Write-Host "注销：powershell -ExecutionPolicy Bypass -File scripts\05-register-prefetch-task.ps1 -Unregister"
