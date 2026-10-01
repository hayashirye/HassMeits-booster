# ---------------------------------------------------------------------------
#  游戏 CPU 高频优化器 —— 绿色版清理脚本
# ---------------------------------------------------------------------------
#  用途：你在 portable 文件夹里点了「开启守护」之后，程序会往系统里放两样东西
#        —— 一个开机自启的计划任务（ValorantBoostWatcher）、以及一条 Windows
#        Defender 排除项。这个脚本把这两样收回去，让系统恢复原样。
#
#  它**不会**删掉这个文件夹本身 —— 绿色版的意思就是「我不想装东西」，
#  所以文件夹归你自己处置：想留就留，想删直接删。
#
#  用 dist\install.exe 装的那一份不归这个脚本管 —— 那种情况请用安装目录里的
#  uninstall.exe，或者在「设置 → 应用」里点卸载。
#
#  跑法：双击同目录的 cleanup.cmd（它会自动请求管理员权限）。
#        也可以直接：
#            powershell -NoProfile -ExecutionPolicy Bypass -File cleanup.ps1
#
#  ★ 这份文件必须存成 **UTF-8 带 BOM**。Windows PowerShell 5.1 对没有 BOM 的
#    .ps1 会按系统 ANSI 代码页（中文机器上是 GBK）解码，中文全变乱码。
#    同目录的 cleanup.cmd 是纯 ASCII，不受这条影响。
# ---------------------------------------------------------------------------

# -NoPause: never wait for Enter. A bare Read-Host blocks forever when the script
# is driven from a pipe or a non-interactive host; Pause-IfInteractive below also
# auto-detects that, but the switch makes the intent explicit.
#
# -Force: skip the "an installed copy was found" guard below and clean up anyway.
# Only for the case where the installed copy is already half-removed.
#
# NOTE: param() must be the first STATEMENT in a script -- keep it above
# $ErrorActionPreference, or PowerShell rejects the file outright.
param([switch]$NoPause, [switch]$Force)

$ErrorActionPreference = 'Continue'

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$eng  = Join-Path $here 'ValorantBoost.exe'
$ui   = Join-Path $here 'valorant_boost.exe'

function Say([string]$m)  { Write-Host ('      ' + $m) }
function Head([string]$m) { Write-Host ''; Write-Host ('[' + $m + ']') -ForegroundColor Cyan }
function Ok([string]$m)   { Write-Host ('      ' + $m) -ForegroundColor Green }
function Warn2([string]$m){ Write-Host ('      ' + $m) -ForegroundColor Yellow }

# Only pause when a human is actually there. IsInputRedirected is true when
# output is piped or the host has no console -- exactly the cases where
# Read-Host hangs instead of waiting for a keypress.
function Pause-IfInteractive {
    if ($NoPause) { return }
    if (-not [Environment]::UserInteractive) { return }
    try { if ([Console]::IsInputRedirected) { return } } catch { return }
    Write-Host ''
    Write-Host '按回车退出...'
    [void](Read-Host)
}

Write-Host ''
Write-Host '  游戏 CPU 高频优化器 —— 绿色版清理' -ForegroundColor White
Write-Host '  ──────────────────────────────────────────────'
Write-Host ('  文件夹：' + $here)
Write-Host ''

# ── 0. 管理员检查 ──────────────────────────────────────────────────────────
# 删计划任务、改 Defender 排除项都要管理员。cleanup.cmd 会负责提权；
# 如果有人直接跑 .ps1 而没提权，这里要拦住并说清楚，不能装作跑过了。
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Warn2 '这个脚本需要管理员权限（要删计划任务、改 Defender 设置）。'
    Warn2 '请改用同目录的 cleanup.cmd —— 它会自己请求提权。'
    Warn2 ''
    Warn2 '或者手动：右键 PowerShell → 以管理员身份运行，然后执行'
    Warn2 ('    powershell -ExecutionPolicy Bypass -File "' + (Join-Path $here 'cleanup.ps1') + '"')
    Write-Host ''
    Pause-IfInteractive
    exit 1
}

# ── 0.5 系统里是不是装过一份正式版？ ───────────────────────────────────────
# 绿色版和安装版共用同一套名字（ValorantBoostUI / ValorantBoostWatcher），
# 所以在这里清理会把装好的那份一起拆掉 —— 这是实测踩到过的：跑完这个脚本，
# 安装版的开机自启和提权启动项就都没了。检测到就停下，指人去用 uninstall.exe。
# 确实想强拆（比如安装版已经删了一半）就加 -Force。
$installedDir = Join-Path $env:LOCALAPPDATA 'ValorantCpuBoost'
if ((-not $Force) -and (Test-Path -LiteralPath (Join-Path $installedDir 'valorant_boost.exe'))) {
    Warn2 '这台电脑上装过一份正式版：'
    Warn2 ('    ' + $installedDir)
    Warn2 ''
    Warn2 '绿色版和正式版共用同一套计划任务名，在这里清理会把正式版也一起拆掉。'
    Warn2 '要卸载正式版，请用：'
    Warn2 ('    ' + (Join-Path $installedDir 'uninstall.exe'))
    Warn2 '或者：「设置 → 应用」里找到「游戏 CPU 高频优化器」点卸载。'
    Warn2 ''
    Warn2 '如果你确实只是想把这个绿色文件夹留下的痕迹清掉，加 -Force 重跑：'
    Warn2 ('    powershell -ExecutionPolicy Bypass -File "' + (Join-Path $here 'cleanup.ps1') + '" -Force')
    Write-Host ''
    Pause-IfInteractive
    exit 2
}

# ── 1. 关掉正在跑的界面和守护 ──────────────────────────────────────────────
Head '1/5 关闭正在运行的程序'
foreach ($n in @('valorant_boost', 'ValorantBoost')) {
    $ps = @(Get-Process -Name $n -ErrorAction SilentlyContinue)
    if ($ps.Count -gt 0) {
        $ps | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 300
        Say ('已关闭 ' + $n + ' 的 ' + $ps.Count + ' 个进程。')
    } else {
        Say ($n + ' 没在跑。')
    }
}

# ── 2. 删掉界面提权任务（只有用 install.exe 装过才会有）────────────────────
# 上面 0.5 已经把「装过正式版」的情况拦掉了，能走到这里说明要么没装过、
# 要么是 -Force。所以这里即便删掉了什么，也是残留。
Head '2/5 删除界面提权启动项'
$t = & schtasks.exe /query /tn 'ValorantBoostUI' 2>&1
if ($LASTEXITCODE -eq 0) {
    & schtasks.exe /delete /tn 'ValorantBoostUI' /f | Out-Null
    Ok '已删除遗留的 ValorantBoostUI。'
} else {
    Say '没有这个任务（绿色版本来就不会有），跳过。'
}

# ── 3. 让引擎自己卸掉守护任务 ─────────────────────────────────────────────
# 这一步交给引擎做，因为「守护任务叫什么、怎么建的」是它自己的知识，
# 重写一遍迟早会和它对不上。
Head '3/5 卸载后台守护（开机自启）'
if (Test-Path -LiteralPath $eng) {
    $out = & $eng uninstall 2>&1
    foreach ($line in $out) { if ("$line".Trim().Length -gt 0) { Say ('引擎：' + "$line".Trim()) } }
    Ok '守护已卸载。'
} else {
    Warn2 ('找不到 ' + $eng + '，没法让引擎自己卸。')
    Warn2 '如果「任务计划程序」里还留着 ValorantBoostWatcher，手动删掉即可。'
}

# ── 4. 收回 Defender 排除项 ───────────────────────────────────────────────
# ★ 这一步很重要：排除项是往系统安全设置里加的东西，清理不留尾巴。
#   只删「本项目录」这一条，不碰用户自己加的其它排除项。
Head '4/5 收回 Windows Defender 排除项'
try {
    $existing = @((Get-MpPreference).ExclusionPath)
    $hit = $existing | Where-Object { $_ -and ($_.TrimEnd('\') -ieq $here.TrimEnd('\')) }
    if ($hit) {
        Remove-MpPreference -ExclusionPath $here -ErrorAction Stop
        Ok ('已收回 ' + $here)
    } else {
        Say '没有针对这个文件夹的排除项，跳过。'
    }
} catch {
    Warn2 ('收回失败：' + $_.Exception.Message)
    Warn2 '可以手动在「Windows 安全中心 → 病毒和威胁防护 → 排除项」里删掉。'
}

# ── 5. 残留提示 ───────────────────────────────────────────────────────────
Head '5/5 剩下的东西（脚本刻意不动）'
$log    = Join-Path $here 'boost-log.txt'
$backup = Join-Path $here 'boost-backup.txt'
$cfg    = Join-Path $env:LOCALAPPDATA 'valorant_boost_flutter'
if (Test-Path -LiteralPath $log)    { Say ('运行日志：' + $log) }
if (Test-Path -LiteralPath $backup) { Say ('还原点：  ' + $backup + '   ← 「还原初始设置」要用它，删了就还原不回去了') }
if (Test-Path -LiteralPath $cfg)    { Say ('界面配色设置：' + $cfg) }
Say '这个文件夹本身也没动 —— 想删就直接删掉整个文件夹。'
Say ''
Say '注意：清理只收回了「开机自启」和「Defender 排除项」，'
Say '      电源方案里已经改过的项不会自动还原。'
Say '      想还原电源方案，请在关掉守护之前先在界面里点「还原初始设置」。'

Write-Host ''
Write-Host '  清理完成。' -ForegroundColor Green
Write-Host ''
Pause-IfInteractive
