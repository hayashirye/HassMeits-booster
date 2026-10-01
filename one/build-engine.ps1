# build-engine.ps1
# ---------------------------------------------------------------------------
# Rebuild the "game CPU high-frequency optimizer" engine (ValorantBoost.exe)
# from its two C# sources with the command-line C# compiler.
#
# This script is intentionally PURE ASCII (no BOM, no non-ASCII bytes).
# Windows PowerShell 5.1 decodes a BOM-less .ps1 as the ANSI/GBK codepage, so
# non-ASCII comments would turn into mojibake and produce phantom syntax
# errors that have nothing to do with the code.
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File one\build-engine.ps1
#   powershell -File one\build-engine.ps1 -Out D:\some\path\ValorantBoost.exe
#   powershell -File one\build-engine.ps1 -Sources one\Core.cs,one\Ui.cs
#   powershell -File one\build-engine.ps1 -Compiler fx      # force in-box csc
#
# Compiler selection: Roslyn (Visual Studio) is preferred; if it fails to
# produce an exe, the in-box .NET Framework csc.exe is tried automatically.
#
# Output: prints the compiler exit code and the produced file size, and writes
# the full raw compiler output to <Out dir>\build-engine.log.
# ---------------------------------------------------------------------------

[CmdletBinding()]
param(
    # Where to write the compiled engine. NEVER the shipped exe.
    # Empty = <repo>\engine-build\ValorantBoost.exe. The default deliberately
    # stays INSIDE the repository: it used to point at D:\test\vcb-engine-build,
    # so every build re-created a stray directory next to the repo.
    [string]   $Out     = '',

    # Source files, resolved relative to the repository root (the parent of the
    # directory holding this script) unless an absolute path is given.
    [string[]] $Sources = @('one\Core.cs', 'one\Ui.cs'),

    # auto   = Roslyn first, in-box csc as fallback (default)
    # roslyn = only the Visual Studio Roslyn compiler
    # fx     = only the in-box .NET Framework compiler
    [ValidateSet('auto', 'roslyn', 'fx')]
    [string]   $Compiler = 'auto'
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# [1/5] Paths and safety guard
# ---------------------------------------------------------------------------
$scriptDir = $PSScriptRoot
if (-not $scriptDir) { $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path }
$repoRoot  = Split-Path -Parent $scriptDir

# The shipped engine binary must never be overwritten by a build.
$protectedExe = Join-Path $repoRoot 'ValorantBoost.exe'

if (-not $Out) { $Out = Join-Path $repoRoot 'engine-build\ValorantBoost.exe' }
$Out = [System.IO.Path]::GetFullPath($Out)

Write-Host '[1/5] Paths' -ForegroundColor Gray
Write-Host ("      repo root : {0}" -f $repoRoot)
Write-Host ("      output    : {0}" -f $Out)

if ($Out -eq [System.IO.Path]::GetFullPath($protectedExe)) {
    throw ("Refusing to write over the shipped binary: {0}" -f $protectedExe)
}

$outDir = Split-Path -Parent $Out
if (-not (Test-Path -LiteralPath $outDir)) {
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null
}

# Resolve sources.
$srcPaths = @()
foreach ($s in $Sources) {
    if ([System.IO.Path]::IsPathRooted($s)) { $p = $s } else { $p = Join-Path $repoRoot $s }
    $p = [System.IO.Path]::GetFullPath($p)
    if (-not (Test-Path -LiteralPath $p)) { throw ("Source file not found: {0}" -f $p) }
    $srcPaths += $p
}
foreach ($p in $srcPaths) {
    $len = (Get-Item -LiteralPath $p).Length
    Write-Host ("      source    : {0} ({1} bytes)" -f $p, $len)
}

# ---------------------------------------------------------------------------
# [2/5] Locate csc.exe -- Roslyn first, then the in-box .NET Framework one
# ---------------------------------------------------------------------------
Write-Host '[2/5] Locating the C# compiler' -ForegroundColor Gray

$roslynCandidates = New-Object System.Collections.Generic.List[string]

# Preferred: Roslyn shipped with Visual Studio / Build Tools.
$vsRoots = @(
    'C:\Program Files\Microsoft Visual Studio',
    'C:\Program Files (x86)\Microsoft Visual Studio'
)
foreach ($vsRoot in $vsRoots) {
    if (-not (Test-Path -LiteralPath $vsRoot)) { continue }
    $hits = @(Get-ChildItem -LiteralPath $vsRoot -Filter 'csc.exe' -Recurse -File -ErrorAction SilentlyContinue |
              Where-Object { $_.FullName -match '\\MSBuild\\Current\\Bin\\Roslyn\\csc\.exe$' })
    foreach ($h in $hits) { $roslynCandidates.Add($h.FullName) }
}
# Known-good explicit Roslyn path (checked even if the glob above missed it).
$roslynCandidates.Add('C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\Roslyn\csc.exe')

# Fallback: the compiler that ships with .NET Framework 4.x.
$fxCandidates = @(
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
)

$ordered = switch ($Compiler) {
    'roslyn' { @($roslynCandidates) }
    'fx'     { @($fxCandidates) }
    default  { @($roslynCandidates) + @($fxCandidates) }
}

$cscList = @()
foreach ($c in $ordered) {
    if ($c -and (Test-Path -LiteralPath $c) -and ($cscList -notcontains $c)) { $cscList += $c }
}
if ($cscList.Count -eq 0) { throw 'No usable csc.exe found (neither Roslyn nor the .NET Framework 4.x compiler).' }

$csc = $cscList[0]
$cscInfo = Get-Item -LiteralPath $csc
Write-Host ("      compiler  : {0}" -f $csc) -ForegroundColor Green
Write-Host ("      kind      : {0}" -f $(if ($csc -match 'Roslyn') { 'Roslyn' } else { 'in-box .NET Framework' }))
Write-Host ("      size      : {0} bytes" -f $cscInfo.Length)
try { Write-Host ("      version   : {0}" -f $cscInfo.VersionInfo.FileVersion) } catch { }

# ---------------------------------------------------------------------------
# [3/5] Reference assemblies
# ---------------------------------------------------------------------------
Write-Host '[3/5] Resolving .NET Framework reference assemblies' -ForegroundColor Gray

$refDirs = @(
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'),
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319')
)

# Final working reference set for Core.cs + Ui.cs. This exact set was verified
# to compile both sources to completion with BOTH compilers:
#   System.dll                - base BCL, DllImport / IntPtr / Console
#   System.Core.dll           - LINQ (System.Linq.Enumerable, Func<>)
#   System.Drawing.dll        - Icon / Bitmap / Graphics for the tray icon
#   System.Windows.Forms.dll  - NotifyIcon / MessageBox / Application
#   System.Management.dll     - WMI: ManagementObjectSearcher, ManagementClass
$refNames = @(
    'System.dll',
    'System.Core.dll',
    'System.Drawing.dll',
    'System.Windows.Forms.dll',
    'System.Management.dll'
)

$refs = @()
foreach ($n in $refNames) {
    $hit = $null
    foreach ($d in $refDirs) {
        $p = Join-Path $d $n
        if (Test-Path -LiteralPath $p) { $hit = $p; break }
    }
    if ($hit) { $refs += $hit; Write-Host ("      ref       : {0}" -f $hit) }
    else      { Write-Host ("      [WARN] missing reference assembly: {0}" -f $n) -ForegroundColor Yellow }
}
if ($refs.Count -lt 4) { throw 'Not enough .NET Framework reference assemblies; install the .NET Framework 4.8 runtime.' }

# ---------------------------------------------------------------------------
# [4/5] Compile
# ---------------------------------------------------------------------------
Write-Host '[4/5] Compiling' -ForegroundColor Gray

# Base switches.
$baseArgs = @(
    '/nologo',
    '/noconfig',        # do not read csc.rsp -- avoids surprise references
    '/target:winexe',   # GUI app, no console window
    '/platform:anycpu',
    '/optimize+',
    '/codepage:65001'   # sources are UTF-8 WITHOUT BOM and contain Chinese
)

# Each reference must be its own /reference:xxx argument. Writing
# /reference:A.dll,B.dll makes csc treat the whole string as one file name:
#   error CS2005: Missing file specification for '/reference:' option
#   fatal error CS2021: File name "A.dll,B.dll" is too long or invalid
$refArgs = @()
foreach ($r in $refs) { $refArgs += ('/reference:' + $r) }

# csc writes its errors to stderr. Under $ErrorActionPreference = 'Stop' a
# native command's stderr is wrapped into a terminating error, so you never see
# a single "error CS" line. Drop to 'Continue', merge 2>&1, judge by exit code.
$oldEap = $ErrorActionPreference
$ErrorActionPreference = 'Continue'

$allOut = New-Object System.Collections.Generic.List[string]
$code = -1
$usedCsc = $null
$attempt = 0

foreach ($candidate in $cscList) {
    $attempt++
    if ($attempt -gt 1) {
        Write-Host ("      first compiler failed; falling back to {0}" -f $candidate) -ForegroundColor Yellow
    }

    # The output file must not exist before the attempt, otherwise a failure
    # would leave the previous attempt's exe behind and look like a success.
    if (Test-Path -LiteralPath $Out) { Remove-Item -LiteralPath $Out -Force }

    # NOTE the argument order: /out MUST come before the source files. The
    # in-box .NET Framework csc rejects the opposite order with
    #   fatal error CS2022: Options '/out' and '/target' cannot appear before
    #   source files
    # Roslyn accepts either order, so /out goes first for both.
    $cscArgs = @($baseArgs) + @(('/out:' + $Out)) + $refArgs + $srcPaths

    $allOut.Add(('===== attempt {0}: {1}' -f $attempt, $candidate))
    $allOut.Add(('args: ' + ($cscArgs -join ' ')))

    $raw = & $candidate $cscArgs 2>&1
    $code = $LASTEXITCODE

    foreach ($line in @($raw)) { $allOut.Add([string]$line) }

    Write-Host ''
    Write-Host ('---------- raw compiler output (attempt {0}) ----------' -f $attempt) -ForegroundColor DarkGray
    foreach ($line in @($raw)) { Write-Host $line }
    Write-Host '-------------------------------------------------------' -ForegroundColor DarkGray
    Write-Host ''
    Write-Host ("      exit code : {0}" -f $code) -ForegroundColor $(if ($code -eq 0) { 'Green' } else { 'Red' })

    if ($code -eq 0 -and (Test-Path -LiteralPath $Out)) { $usedCsc = $candidate; break }
}

$ErrorActionPreference = $oldEap

$logPath = Join-Path $outDir 'build-engine.log'
try {
    $header = @(
        ('exit code  : ' + $code),
        ('used csc   : ' + $usedCsc),
        ('sources    : ' + ($srcPaths -join ' ')),
        '--- output ---'
    )
    ($header + $allOut) | Set-Content -LiteralPath $logPath -Encoding UTF8
} catch { }

# ---------------------------------------------------------------------------
# [5/5] Result
# ---------------------------------------------------------------------------
if (-not $usedCsc) {
    $errs = @($allOut | Where-Object { $_ -match 'error CS\d+' })
    if ($errs.Count -gt 0) {
        Write-Host 'First compiler errors:' -ForegroundColor Red
        foreach ($e in ($errs | Select-Object -First 15)) { Write-Host ('  ' + $e) -ForegroundColor Red }
    }
    throw ("Compilation failed with exit code {0}. Full output: {1}" -f $code, $logPath)
}

$outSize = (Get-Item -LiteralPath $Out).Length
$kind = if ($usedCsc -match 'Roslyn') { 'roslyn' } else { 'fx' }

Write-Host ''
Write-Host '================ BUILD OK ================' -ForegroundColor Green
Write-Host ("  file : {0}" -f $Out) -ForegroundColor Green
Write-Host ("  size : {0} bytes ({1} KB)" -f $outSize, [math]::Round($outSize / 1KB, 1)) -ForegroundColor Green
Write-Host ("  csc  : {0} ({1})" -f $usedCsc, $kind) -ForegroundColor Green
Write-Host ("  log  : {0}" -f $logPath) -ForegroundColor Green
Write-Host '==========================================' -ForegroundColor Green
Write-Host ''

# Emit a small machine-readable trailer so callers can grep the result.
Write-Host ("BUILD_RESULT exit={0} size={1} csc={2} attempts={3}" -f $code, $outSize, $kind, $attempt)

exit $code
