#Requires -Version 5.1
<#
.SYNOPSIS
  Win-CodexBar 用量预取：由计划任务每 3 分钟运行一次，把 codexbar-cli 的
  最新 JSON 写到 %LOCALAPPDATA%\CodexBarWidget\latest.json，供小组件打开时
  直接读取（毫秒级出画面）。

  失败时绝不覆盖旧文件：读不到就是上一次缓存，读到就是最新。
  语义就是"跟着 CodexBar 的数据走"，不追求秒级实时。

  由 scripts\05-register-prefetch-task.ps1 注册为计划任务
  （任务指向 %LOCALAPPDATA%\CodexBarWidget\prefetch-usage.ps1 的副本）。
#>
param(
    [string]$CliPath = "",
    [string]$OutDir = ""
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($OutDir)) {
    $OutDir = Join-Path $env:LOCALAPPDATA 'CodexBarWidget'
}
if (-not (Test-Path -LiteralPath $OutDir)) {
    New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
}
$outFile = Join-Path $OutDir 'latest.json'
$tmpFile = Join-Path $OutDir 'latest.json.tmp'
$logFile = Join-Path $OutDir 'prefetch.log'

function Write-PrefetchLog([string]$message) {
    try {
        if ((Test-Path -LiteralPath $logFile) -and
            ((Get-Item -LiteralPath $logFile).Length -gt 200KB)) {
            Remove-Item -LiteralPath $logFile -Force -ErrorAction SilentlyContinue
        }
        $line = "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') $message"
        [System.IO.File]::AppendAllText(
            $logFile, $line + [Environment]::NewLine,
            (New-Object System.Text.UTF8Encoding($false)))
    } catch { }
}

function Resolve-CliPath {
    # 1) Explicit config file (same contract as the widget's CodexBarClient).
    try {
        $cfg = Join-Path $env:APPDATA 'CodexBarWidget\cli-path.txt'
        if (Test-Path -LiteralPath $cfg) {
            $p = ([System.IO.File]::ReadAllText($cfg)).Trim()
            if ($p -and (Test-Path -LiteralPath $p)) { return $p }
        }
    } catch { }
    # 2) Environment override.
    if ($env:CODEXBAR_CLI_PATH -and (Test-Path -LiteralPath $env:CODEXBAR_CLI_PATH)) {
        return $env:CODEXBAR_CLI_PATH
    }
    # 3) PATH.
    foreach ($dir in ($env:PATH -split ';')) {
        try {
            $candidate = Join-Path $dir.Trim() 'codexbar-cli.exe'
            if (Test-Path -LiteralPath $candidate) { return $candidate }
        } catch { }
    }
    # 4) Common install locations.
    foreach ($g in @(
        (Join-Path $env:LOCALAPPDATA 'Programs\CodexBar\codexbar-cli.exe'),
        (Join-Path $env:ProgramFiles 'CodexBar\codexbar-cli.exe'))) {
        if (Test-Path -LiteralPath $g) { return $g }
    }
    return $null
}

try {
    if ([string]::IsNullOrWhiteSpace($CliPath)) { $CliPath = Resolve-CliPath }
    if (-not $CliPath) { Write-PrefetchLog 'FAIL: codexbar-cli.exe not found'; exit 2 }

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $CliPath
    $psi.Arguments = 'usage -f json --web-timeout 25'
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    # The CLI writes UTF-8; decode it as UTF-8, not the system code page,
    # or currency symbols (¥) turn to mush on Chinese Windows.
    $psi.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $psi.StandardErrorEncoding = [System.Text.Encoding]::UTF8
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $proc = [System.Diagnostics.Process]::Start($psi)
    $outTask = $proc.StandardOutput.ReadToEndAsync()
    $errTask = $proc.StandardError.ReadToEndAsync()
    if (-not $proc.WaitForExit(90000)) {
        try { $proc.Kill() } catch { }
        Write-PrefetchLog 'FAIL: CLI timed out after 90s'
        exit 3
    }
    $json = $outTask.Result
    $stderr = $errTask.Result
    if ($proc.ExitCode -ne 0) {
        Write-PrefetchLog "FAIL: CLI exit $($proc.ExitCode): $($stderr.Trim())"
        exit 4
    }
    if ([string]::IsNullOrWhiteSpace($json)) {
        Write-PrefetchLog 'FAIL: CLI returned empty output'
        exit 5
    }
    # Validate JSON before touching the good file (PS 5.1 needs -Depth:
    # its default of 2 is shallower than the CLI's nested usage objects).
    try { $null = $json | ConvertFrom-Json -Depth 20 -ErrorAction Stop }
    catch {
        Write-PrefetchLog "FAIL: CLI output is not valid JSON: $($_.Exception.Message)"
        exit 6
    }

    # Write temp file (UTF-8, no BOM) then atomic replace in the same dir,
    # so a reader never sees a half-written file.
    [System.IO.File]::WriteAllText(
        $tmpFile, $json, (New-Object System.Text.UTF8Encoding($false)))
    Move-Item -LiteralPath $tmpFile -Destination $outFile -Force
    exit 0
} catch {
    Write-PrefetchLog "FAIL: $($_.Exception.Message)"
    exit 1
}
