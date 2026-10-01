<#
    fix-encoding.ps1 - ensure script/source files carry a UTF-8 BOM
    ------------------------------------------------------------------------
    Why this exists (two separate bugs, same root cause):

    1) C# sources (compiled by csc.exe)
       src\ValorantCpuBoost.cs and src\MainForm.new.cs contain Chinese string
       literals. The in-box C# 5 compiler decides how to decode a source file
       by sniffing the first bytes: with a UTF-8 BOM it reads UTF-8, without
       one it falls back to the system ANSI code page (GBK on Chinese Windows)
       and produces garbled text or a compile error.

    2) PowerShell scripts (run by powershell.exe 5.1)
       valorant-cpu-boost.ps1 contains Chinese UI text. Windows PowerShell 5.1
       reads a BOM-less .ps1 as ANSI/GBK. The UTF-8 bytes then decode wrongly,
       and - this is the nasty part - a trailing GBK lead byte can swallow the
       NEXT ASCII character. A closing brace or a quote gets eaten, the parser
       loses sync, and you get "phantom" syntax errors that do not correspond
       to anything in the file, for example:

           Unexpected token '}' in expression or statement.
           You must provide a value expression following the '-' operator.
           Missing file specification after redirection operator.
           At ...\valorant-cpu-boost.ps1:362 char:20
           +     Write-Host '--- <garbled> ---'
                                 ~

       The garbled run is the fingerprint that identifies this: it is the
       UTF-8 bytes of the Chinese word for "power scheme", decoded as GBK.
       (Deliberately spelled out in ASCII here. Printing the real mojibake
       would make THIS file non-ASCII - which is exactly the bug it exists
       to warn about - and the self check at the bottom would flag it.)
       Editing the file cannot fix it; only adding the BOM can.

    This script is ASCII-only on purpose: it must itself parse correctly on a
    GBK console in order to repair the other files.
    It is idempotent: running it twice changes nothing.

    Usage:
        powershell -NoProfile -ExecutionPolicy Bypass -File .\fix-encoding.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$utf8Bom = New-Object System.Text.UTF8Encoding($true)

# Every file that must carry a UTF-8 BOM before it is consumed.
# Only non-ASCII files are listed here. The .bat launchers are deliberately
# ASCII-only and need no BOM (and cmd.exe ignores BOMs anyway).
$targets = @(
    (Join-Path $root 'src\ValorantCpuBoost.cs'),      # C# engine
    (Join-Path $root 'src\MainForm.new.cs'),          # C# visual UI
    (Join-Path $root 'src\app.manifest'),             # manifest
    (Join-Path $root 'build.ps1'),                    # build driver (Chinese output)
    (Join-Path $root 'bootstrap.ps1'),                # build hand-over (Chinese output)
    (Join-Path $root 'show-menu.ps1'),                # Chinese menu for the PowerShell version
    (Join-Path $root 'valorant-cpu-boost.ps1'),       # v1 PowerShell fallback / Electron engine
    (Join-Path $root 'electron-app\launch.ps1'),      # Electron launcher (Chinese output)
    (Join-Path $root 'electron-app\ps\bridge.ps1'),   # Electron <-> engine bridge (Chinese errors)
    (Join-Path $root 'electron-app\ps\sample.ps1'),   # Electron sampler (Chinese errors)
    (Join-Path $root 'electron-app\diagnose.ps1'),    # Electron startup diagnostic (Chinese output)
    (Join-Path $root 'verify-cpu.ps1'),               # Frequency verification (Chinese output + Chinese regex)
    (Join-Path $root 'game-monitor.ps1')              # Game-time GPU/CPU monitor (Chinese output)
)

$fixed = 0
$skipped = 0
foreach ($f in $targets) {
    if (-not (Test-Path -LiteralPath $f)) {
        Write-Host ("  [missing] {0}" -f $f) -ForegroundColor DarkGray
        $skipped++
        continue
    }

    $bytes = [System.IO.File]::ReadAllBytes($f)
    $hasBom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
    if ($hasBom) {
        Write-Host ("  [ok]      {0}" -f (Split-Path $f -Leaf)) -ForegroundColor DarkGray
        continue
    }

    # No BOM: read the raw bytes as UTF-8 and write them back with a BOM.
    $text = [System.IO.File]::ReadAllText($f, $utf8NoBom)
    # Guard: if the file already starts with a stray U+FEFF, do not double it.
    $text = $text.TrimStart([char]0xFEFF)
    [System.IO.File]::WriteAllText($f, $text, $utf8Bom)
    $fixed++
    Write-Host ("  [fixed]   UTF-8 BOM added: {0}" -f (Split-Path $f -Leaf)) -ForegroundColor Green
}

Write-Host ''
if ($fixed -eq 0) {
    Write-Host 'All files already carry a UTF-8 BOM. Nothing to do.' -ForegroundColor Green
} else {
    Write-Host ("Fixed {0} file(s)." -f $fixed) -ForegroundColor Green
}
if ($skipped -gt 0) {
    Write-Host ("{0} file(s) were not found (that is fine if you deleted them)." -f $skipped) -ForegroundColor DarkGray
}
Write-Host ''

# --- self check --------------------------------------------------------------
# A BOM-less .ps1 is only safe while it stays pure ASCII. This script is
# deliberately ASCII-only so it can repair the others even on a GBK console; if
# a future edit sneaks non-ASCII text in, it would break in exactly the way this
# file exists to prevent. Warn early. (The .bat launchers are ASCII-only too,
# but cmd.exe ignores BOMs, so they are not in the list above.)
foreach ($name in @('fix-encoding.ps1')) {
    $p = Join-Path $root $name
    if (-not (Test-Path -LiteralPath $p)) { continue }
    $t = [System.IO.File]::ReadAllText($p, $utf8NoBom)
    # Strip the BOM character if present, then look for anything above ASCII.
    $t = $t.TrimStart([char]0xFEFF)
    if ($t -match '[^\x00-\x7F]') {
        Write-Host ("  [WARN] {0} contains non-ASCII characters." -f $name) -ForegroundColor Yellow
        Write-Host '         This script must stay pure ASCII or it will fail to parse' -ForegroundColor Yellow
        Write-Host '         on a non-UTF-8 console. Move the text into the .bat wrapper.' -ForegroundColor Yellow
        Write-Host ''
    }
}

exit 0
