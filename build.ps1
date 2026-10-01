<#
    build.ps1 - 编译 src\ValorantCpuBoost.cs 等源码，生成可直接运行的程序
    ------------------------------------------------------------------------
    只用 Windows 自带 的 C# 编译器，不需要 Visual Studio，也不需要 .NET SDK。

    编码说明（重要）：
      本文件含中文，且必须保存为「UTF-8 带 BOM」。
      Windows PowerShell 5.1 读到没有 BOM 的 .ps1 时会按系统 ANSI（中文系统
      是 GBK）解码，中文会变成乱码，还会报出与代码本身无关的「幽灵语法错误」
      （例如 Unexpected token '}' in expression or statement）。
      一旦出现这种报错，双击 编译程序.bat 或 以管理员运行.bat 即可自动修复；
      也可以手动执行 fix-encoding.ps1。
      **不要**用编辑器把它另存为 ANSI，那会重新引入这个 bug。

    用法：
        powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
    可选参数：
        -OutputName  输出文件名（默认 ValorantCpuBoost.exe）
#>
[CmdletBinding()]
param(
    [string]$OutputName = 'ValorantCpuBoost.exe'
)

$ErrorActionPreference = 'Stop'
$root     = Split-Path -Parent $MyInvocation.MyCommand.Path
$sources  = @(
    (Join-Path $root 'src\ValorantCpuBoost.cs'),   # 引擎 + 命令行 + 旧界面
    (Join-Path $root 'src\MainForm.new.cs')        # 可视化主界面
)
$manifest = Join-Path $root 'src\app.manifest'
$output   = Join-Path $root $OutputName
$exitCode = 0
$ok       = $false

# 让控制台按 UTF-8 解读本脚本输出的中文。
try { [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false) } catch { }

try {
    Write-Host ''
    Write-Host '============================================================' -ForegroundColor Cyan
    Write-Host '   无畏契约 CPU 高频优化器  -  正在生成可执行程序' -ForegroundColor Cyan
    Write-Host '============================================================' -ForegroundColor Cyan
    Write-Host ''

    foreach ($s in $sources) {
        if (-not (Test-Path -LiteralPath $s)) { throw "找不到源码文件：$s" }
    }
    if (-not (Test-Path -LiteralPath $manifest)) { throw "找不到清单文件：$manifest" }

    # ------------------------------------------------- 第 1 步：源码编码
    # 系统自带的 csc.exe 靠文件开头的 BOM 判断源码编码。没有 BOM 时它会退回
    # 系统 ANSI 代码页，把源码里的中文字符串读成乱码（或直接编译失败）。
    # fix-encoding.ps1 是幂等的，每次都跑一遍没有副作用。
    Write-Host '[1/5] 正在检查源码文件编码（UTF-8 BOM）...' -ForegroundColor Gray
    $fixer = Join-Path $root 'fix-encoding.ps1'
    if (Test-Path -LiteralPath $fixer) {
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $fixer
    } else {
        Write-Host '      fix-encoding.ps1 不存在，跳过。' -ForegroundColor DarkGray
    }
    Write-Host ''

    # ------------------------------------------------- 第 2 步：找编译器
    Write-Host '[2/5] 正在查找 C# 编译器（csc.exe）...' -ForegroundColor Gray

    function Find-Csc {
        $candidates = New-Object System.Collections.Generic.List[string]

        # .NET Framework 4.x（Windows 10 / 11 自带），先找 64 位
        foreach ($fwRoot in @(
            (Join-Path $env:WINDIR 'Microsoft.NET\Framework64'),
            (Join-Path $env:WINDIR 'Microsoft.NET\Framework'))) {
            if (Test-Path -LiteralPath $fwRoot) {
                Get-ChildItem -LiteralPath $fwRoot -Directory -ErrorAction SilentlyContinue |
                    Where-Object { $_.Name -like 'v4*' } |
                    Sort-Object Name -Descending |
                    ForEach-Object {
                        $p = Join-Path $_.FullName 'csc.exe'
                        if (Test-Path -LiteralPath $p) { $candidates.Add($p) }
                    }
            }
        }

        # Roslyn（装了 VS / Build Tools / .NET SDK 时存在，编译器更新）
        $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
        if (Test-Path -LiteralPath $vswhere) {
            try {
                $vs = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath 2>$null
                if ($vs) {
                    Get-ChildItem -LiteralPath (Join-Path $vs 'MSBuild') -Recurse -Filter 'csc.exe' -ErrorAction SilentlyContinue |
                        Where-Object { $_.FullName -like '*Roslyn*' } |
                        ForEach-Object { $candidates.Add($_.FullName) }
                }
            } catch { }
        }

        foreach ($c in $candidates) { if (Test-Path -LiteralPath $c) { return $c } }
        return $null
    }

    $csc = Find-Csc
    if (-not $csc) {
        throw @'
找不到 csc.exe（Windows 自带的 C# 编译器）。

怎么解决（任选一个）：
  1) 这台机器可能不是 Windows 10/11，或者 .NET Framework 被精简掉了。
     去微软官网装一个「.NET Framework 4.8 运行时」再重试。
  2) 或者装 .NET SDK（会带来更新的编译器）。
  3) 或者先用纯 PowerShell 版：双击 以管理员运行.bat
'@
    }
    Write-Host ("      编译器：{0}" -f $csc) -ForegroundColor DarkGray

    # csc.exe 能不能真的跑起来？这一步非常重要：如果进程启动就失败
    # （例如系统缺少它依赖的 DLL，退出码 0xC0000142 = STATUS_DLL_INIT_FAILED），
    # csc 不会输出任何 error CS，只会静默失败，让人完全无从下手。
    #
    # 注意：不要给 RedirectStandardOutput / RedirectStandardError 传同一个目标
    #       （例如都写 'NUL'），PowerShell 会直接抛
    #       "RedirectStandardOutput and RedirectStandardError are same"。
    #       这里干脆不重定向：/help 的输出留在窗口里，我们只关心退出码。
    $canRun = $false
    $probeText = ''
    $cscItem = $null
    try { $cscItem = Get-Item -LiteralPath $csc -ErrorAction Stop } catch { }

    if (-not $cscItem) {
        $probeText = "编译器文件不存在：$csc"
    }
    elseif ($cscItem.Length -lt 4096) {
        # Windows 上有个知名现象：某些"修复"工具会把丢失的 DLL 换成 0 字节占位文件，
        # 看起来存在，其实根本不能用。
        $probeText = ("编译器文件异常：只有 {0} 字节，多半是被替换成了占位文件。" -f $cscItem.Length)
    }
    else {
        Write-Host ("      编译器大小：{0:N0} 字节" -f $cscItem.Length) -ForegroundColor DarkGray
        try {
            $probe = Start-Process -FilePath $csc -ArgumentList '/help' -Wait -PassThru -NoNewWindow
            if ($probe -and $probe.ExitCode -eq 0) {
                $canRun = $true
            } else {
                $pc = if ($probe) { [int]$probe.ExitCode } else { 0 }
                $probeText = ("编译器无法正常运行，退出码 {0} (0x{1:X8})。0xC0000142 表示缺少系统 DLL。" -f $pc, $pc)
            }
        } catch {
            $probeText = '尝试启动编译器失败：' + $_.Exception.Message
        }
    }
    if (-not $canRun) {
        Write-Host ('      [警告] ' + $probeText) -ForegroundColor Yellow
        Write-Host '      这台机器上的 csc.exe 起不来，编译必然失败。' -ForegroundColor Yellow
        Write-Host ''
        Write-Host '      最可能的原因：系统缺少 .NET Framework 4.x 的组件。' -ForegroundColor Yellow
        Write-Host '      解决办法（任选一个）：' -ForegroundColor Yellow
        Write-Host '        1) 安装「.NET Framework 4.8 运行时」（微软官网免费下载），装完重跑本程序' -ForegroundColor Yellow
        Write-Host '        2) 暂时不用编译：双击 以管理员运行.bat 走纯 PowerShell 版，功能基本一样' -ForegroundColor Yellow
        Write-Host ''
        throw ($probeText + "`r`n`r`n请优先尝试上面第 2 条：双击 以管理员运行.bat（纯 PowerShell 版，不需要编译）。")
    }
    Write-Host '      编译器自检通过。' -ForegroundColor DarkGray

    # ------------------------------------------------- 第 3 步：引用程序集
    Write-Host '[3/5] 正在检查 .NET Framework 引用程序集...' -ForegroundColor Gray

    $refDirs = @(
        (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'),
        (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319')
    )
    $refNames = @('System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', 'System.Management.dll')
    $refs = @()
    foreach ($n in $refNames) {
        $hit = $null
        foreach ($d in $refDirs) {
            $p = Join-Path $d $n
            if (Test-Path -LiteralPath $p) { $hit = $p; break }
        }
        if ($hit) { $refs += $hit }
        else { Write-Host ("      [警告] 缺少引用程序集：{0}" -f $n) -ForegroundColor Yellow }
    }
    if ($refs.Count -lt 4) {
        throw '缺少必需的 .NET Framework 引用程序集。请安装「.NET Framework 4.8 运行时」后重试。'
    }

    # ------------------------------------------------- 第 4 步：编译
    Write-Host '[4/5] 正在编译（大约 5 - 20 秒，请稍候）...' -ForegroundColor Gray

    if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Force }

    $cscArgs = @(
        '/nologo',
        '/noconfig',                      # 不读 csc.rsp，避免意外引用
        '/target:winexe',                 # 不弹出控制台黑窗
        '/platform:anycpu',
        '/optimize+',
        '/warn:4',
        '/langversion:5',                 # 兼容系统自带的旧编译器
        "/out:$output",
        "/win32manifest:$manifest"        # 要求管理员权限 + 高 DPI
    )
    # 每个引用必须写成独立的 /reference:xxx 参数。
    # 不能写成 /reference:A.dll,B.dll —— csc 会把整串当成一个文件名，报：
    #   error CS2005: "/reference:"选项缺少文件规范
    #   fatal error CS2021: 文件名"A.dll,B.dll"太长或无效
    foreach ($r in $refs)    { $cscArgs += ('/reference:' + $r) }
    foreach ($s in $sources) { $cscArgs += $s }

    # 注意：这个变量绝不能叫 $args（会遮蔽 PowerShell 自动变量）。
    #
    # 关于 2>&1 与 $ErrorActionPreference：
    #   csc.exe 把错误写进 stderr。$ErrorActionPreference = 'Stop' 时，原生命令的
    #   stderr 会被包装成终止性错误并抛异常，导致「一次 error CS 都看不到」。
    #   所以这里临时改成 'Continue'，把 stderr 合并进正常输出流，最后只看退出码。
    $oldEap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'

    $result = & $csc $cscArgs 2>&1
    $code = $LASTEXITCODE
    $allOut = New-Object System.Collections.Generic.List[string]
    foreach ($line in @($result)) { $allOut.Add([string]$line) }

    $okNow = ($code -eq 0 -and (Test-Path -LiteralPath $output))
    if (-not $okNow) {
        Write-Host '      第一次编译失败，去掉 /langversion:5 再试一次...' -ForegroundColor Yellow
        $cscArgs2 = @($cscArgs | Where-Object { $_ -ne '/langversion:5' })
        $result2 = & $csc $cscArgs2 2>&1
        $code2 = $LASTEXITCODE
        $allOut.Add('--- 第二次尝试（去掉 /langversion:5）---')
        foreach ($line in @($result2)) { $allOut.Add([string]$line) }
        if ($code2 -eq 0 -and (Test-Path -LiteralPath $output)) {
            $okNow = $true
            $code = 0
        } else {
            $code = $code2
        }
    }

    # 把编译器输出落盘，窗口关了也还能翻。
    $logPath = Join-Path $root 'build.log'
    try { $allOut | Set-Content -LiteralPath $logPath -Encoding UTF8 } catch { }

    # 直接把编译器输出分两列打到控制台：csc 的每条错误都会原样出现在屏幕上。
    Write-Host ''
    Write-Host '---------- 编译器输出（原文） ----------' -ForegroundColor DarkGray
    foreach ($line in $allOut) { Write-Host $line }
    Write-Host '----------------------------------------' -ForegroundColor DarkGray
    Write-Host ''

    $ErrorActionPreference = $oldEap

    if (-not $okNow) {
        # 挑出最有用的几行，放进弹窗里，省得来回折腾。
        $errLines = @($allOut | Where-Object { $_ -match 'error CS\d+' })
        if ($errLines.Count -eq 0) {
            # 没匹配到标准错误格式（语言/格式不同），就退而取最后 15 行原文。
            $n = $allOut.Count
            if ($n -gt 15) { $errLines = @($allOut[($n - 15)..($n - 1)]) } else { $errLines = @($allOut) }
        }
        if ($errLines.Count -gt 15) { $errLines = $errLines[0..14] }

        $detail = ($errLines -join "`r`n")
        if ([string]::IsNullOrWhiteSpace($detail)) { $detail = '（编译器没有输出任何内容，退出码 ' + $code + '）' }
        $script:failDetail = "编译器退出码：$code`r`n`r`n$detail`r`n`r`n完整输出见 build.log"

        throw ("编译了两次都失败。`r`n`r`n" + $detail + "`r`n`r`n完整输出已写入 build.log，可以直接发给我。")
    }

    # ------------------------------------------------- 第 5 步：结果
    Write-Host '[5/5] 完成。' -ForegroundColor Gray
    $size = [math]::Round((Get-Item -LiteralPath $output).Length / 1KB, 1)
    Write-Host ''
    Write-Host '================ 生成成功 ================' -ForegroundColor Green
    Write-Host ("  文件：{0}" -f $output) -ForegroundColor Green
    Write-Host ("  大小：{0} KB" -f $size) -ForegroundColor Green
    Write-Host '=========================================' -ForegroundColor Green
    Write-Host ''
    Write-Host '下一步：双击目录里的 ValorantCpuBoost.exe（Windows 会弹出管理员权限请求，点「是」）' -ForegroundColor Cyan
    Write-Host '命令行模式（不弹窗口）：'
    Write-Host ('  .\{0} check     # 只读体检' -f $OutputName)
    Write-Host ('  .\{0} apply     # 均衡模式（推荐）' -f $OutputName)
    Write-Host ('  .\{0} apply max # 最高频率' -f $OutputName)
    Write-Host ('  .\{0} apply safe# 保守模式' -f $OutputName)
    Write-Host ('  .\{0} tune      # 只提升正在运行的游戏进程' -f $OutputName)
    Write-Host ('  .\{0} restore   # 撤销全部改动' -f $OutputName)
    Write-Host ''

    $ok = $true
}
catch {
    $exitCode = 1
    $raw = $script:failDetail
    if (-not $raw) { $raw = $_.Exception.Message }
    # 调试用：把原始异常的完整形状也写到日志，方便判断是不是 PowerShell 包装过的错误。
    try {
        $dbg = @(
            ('ExceptionType : ' + $_.Exception.GetType().FullName),
            ('CategoryInfo  : ' + [string]$_.CategoryInfo),
            ('FullyQualified: ' + [string]$_.FullyQualifiedErrorId),
            ('Message       : ' + [string]$_.Exception.Message),
            '',
            '--- compiler output ---',
            [string]$raw
        ) -join "`r`n"
        Set-Content -LiteralPath (Join-Path $root 'build.log') -Value $dbg -Encoding UTF8
    } catch { }

    Write-Host ''
    Write-Host '================ 生成失败 ================' -ForegroundColor Red
    Write-Host $raw -ForegroundColor Red
    Write-Host '=========================================' -ForegroundColor Red
    Write-Host '完整输出见：' -NoNewline -ForegroundColor DarkGray
    Write-Host (Join-Path $root 'build.log') -ForegroundColor DarkGray
    Write-Host ''
}

# 无论走的是哪种方式启动，最后都弹一个中文结果框，确保不会「一闪而过」看不到。
try {
    Add-Type -AssemblyName System.Windows.Forms -ErrorAction SilentlyContinue
    if ($ok) {
        $msg = "生成成功！`r`n`r`n文件：$output`r`n`r`n双击 ValorantCpuBoost.exe 即可运行，`r`nWindows 会弹出管理员权限请求，点「是」。"
        [void][System.Windows.Forms.MessageBox]::Show($msg, '无畏契约 CPU 高频优化器', 'OK', 'Information')
    } else {
        $msg = "生成失败。`r`n`r`n"
        if ($script:failDetail) { $msg += $script:failDetail + "`r`n`r`n" }
        $msg += "也可以先直接用纯 PowerShell 版：双击 以管理员运行.bat"
        [void][System.Windows.Forms.MessageBox]::Show($msg, '无畏契约 CPU 高频优化器', 'OK', 'Error')
    }
} catch { }

exit $exitCode
