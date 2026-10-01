# ---------------------------------------------------------------------------
#  make-installer.ps1 - build a single-file install.exe (+ standalone uninstall.exe)
# ---------------------------------------------------------------------------
#  Produces (all inside <root>\release, the one folder the user needs):
#     <Out>\install.exe     one file, ~14 MB, includes the whole portable folder
#     <Out>\uninstall.exe   standalone uninstaller, ~5 KB, needs no payload
#     <Out>\portable\       the green version, rebuilt by make-portable.ps1
#     <Out>\portable.zip    the green version zipped, for handing to someone else
#  payload.zip and run-ui.exe are pure intermediates and go to %TEMP% now --
#  keeping them in <Out> just duplicated portable.zip (11 MB) in the deliverable.
#
#  How it works:
#     1. (optional) run make-portable.ps1 to refresh the portable folder
#     2. zip the portable folder -> payload.zip  (files at the ZIP ROOT)
#     3. compile Launcher.cs              -> run-ui.exe     (/target:winexe, no manifest)
#     4. compile Vcb.cs + Uninstaller.cs  -> uninstall.exe  (requireAdministrator)
#     5. compile Vcb.cs + Installer.cs    -> install.exe    (requireAdministrator,
#                                              embeds payload.zip + run-ui.exe
#                                              + uninstall.exe as .NET resources)
#
#  Vcb.cs is the half both programs share: constants, console output, running
#  commands, killing processes, locating the install dir, the Defender exclusion
#  add/remove pair, the "Apps & Features" registry entry, and the whole uninstall
#  sequence. Installer.cs keeps thin same-signature wrappers so its 600+ call
#  sites did not have to change.
#
#  Usage:
#     powershell -NoProfile -ExecutionPolicy Bypass -File tool\make-installer.ps1
#     ... -SkipPortable          reuse the existing portable folder (fast)
#     ... -SkipBuild             pass -SkipBuild through to make-portable.ps1
#     ... -Out "D:\somewhere"
#
#  ASCII only. Windows PowerShell 5.1 decodes a BOM-less .ps1 using the system
#  ANSI code page (GBK on Chinese Windows); any non-ASCII byte here would be
#  read as mojibake and can produce bogus syntax errors. Same lesson as b143.
# ---------------------------------------------------------------------------
[CmdletBinding()]
param(
    [string]$Portable,
    [string]$Out,
    [switch]$SkipPortable,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'

$here = Split-Path -Parent $MyInvocation.MyCommand.Path          # flutter-app\tool
$flutterApp = Split-Path -Parent $here                            # flutter-app
$root = Split-Path -Parent $flutterApp                            # repo root

#  Everything the user actually needs lands in ONE folder: <root>\release.
#  The portable folder lives inside it, so the two build scripts agree on
#  where things are without either one having to be told.
if (-not $Portable) { $Portable = Join-Path $root 'release\portable' }
if (-not $Out)      { $Out      = Join-Path $root 'release' }

$installerDir = Join-Path $root 'installer'
$launcherCs   = Join-Path $installerDir 'Launcher.cs'
$vcbCs        = Join-Path $installerDir 'Vcb.cs'               # shared by both exes
$installerCs  = Join-Path $installerDir 'Installer.cs'
$uninstCs     = Join-Path $installerDir 'Uninstaller.cs'
$manifest     = Join-Path $installerDir 'installer.manifest'
$uninstMani   = Join-Path $installerDir 'uninstaller.manifest'

function Say([string]$m)  { Write-Host $m }
function Head([string]$m) { Write-Host ''; Write-Host ('=== ' + $m + ' ===') -ForegroundColor Cyan }

# ---------------------------------------------------------------------------
#  [1/6] refresh the portable folder
# ---------------------------------------------------------------------------
Head '[1/7] portable folder'
if ($SkipPortable) {
    if (-not (Test-Path -LiteralPath $Portable)) { throw "portable folder not found: $Portable" }
    Say "reusing $Portable"
} else {
    $mk = Join-Path $here 'make-portable.ps1'
    if (-not (Test-Path -LiteralPath $mk)) { throw "make-portable.ps1 not found: $mk" }
    #  -Zip so portable.zip is refreshed in the same run: handing someone the
    #  green version should not require remembering a second command.
    $a = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $mk, '-Out', $Portable, '-Zip')
    if ($SkipBuild) { $a += '-SkipBuild' }
    & powershell.exe @a
    if ($LASTEXITCODE -ne 0) { throw "make-portable.ps1 failed with exit code $LASTEXITCODE" }
}

# Runtime droppings must not be shipped: the engine writes boost-log.txt next to
# itself, and a stale log would be copied into every fresh install.
foreach ($junk in @('boost-log.txt', 'boost-backup.txt', '.wtest', 'install.exe', 'run-ui.exe', 'uninstall.exe')) {
    $p = Join-Path $Portable $junk
    if (Test-Path -LiteralPath $p) { Remove-Item -LiteralPath $p -Force -ErrorAction SilentlyContinue }
}

$ui = Join-Path $Portable 'valorant_boost.exe'
if (-not (Test-Path -LiteralPath $ui)) { throw "the portable folder has no valorant_boost.exe - run without -SkipPortable" }
$eng = Join-Path $Portable 'ValorantBoost.exe'
if (-not (Test-Path -LiteralPath $eng)) { Write-Host '  [warn] portable folder has no ValorantBoost.exe (the app will still install, but without the engine)' -ForegroundColor Yellow }

# ---------------------------------------------------------------------------
#  [2/6] find csc.exe   (same search order as build.ps1)
# ---------------------------------------------------------------------------
Head '[2/7] locating csc.exe'
function Find-Csc {
    $list = New-Object System.Collections.Generic.List[string]
    foreach ($fwRoot in @(
        (Join-Path $env:WINDIR 'Microsoft.NET\Framework64'),
        (Join-Path $env:WINDIR 'Microsoft.NET\Framework'))) {
        if (Test-Path -LiteralPath $fwRoot) {
            Get-ChildItem -LiteralPath $fwRoot -Directory -ErrorAction SilentlyContinue |
                Where-Object { $_.Name -like 'v4*' } |
                Sort-Object Name -Descending |
                ForEach-Object {
                    $p = Join-Path $_.FullName 'csc.exe'
                    if (Test-Path -LiteralPath $p) { $list.Add($p) }
                }
        }
    }
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path -LiteralPath $vswhere) {
        try {
            $vs = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath 2>$null
            if ($vs) {
                Get-ChildItem -LiteralPath (Join-Path $vs 'MSBuild') -Recurse -Filter 'csc.exe' -ErrorAction SilentlyContinue |
                    Where-Object { $_.FullName -like '*Roslyn*' } |
                    ForEach-Object { $list.Add($_.FullName) }
            }
        } catch { }
    }
    foreach ($c in $list) { if (Test-Path -LiteralPath $c) { return $c } }
    return $null
}
$csc = Find-Csc
if (-not $csc) { throw 'csc.exe not found. Install .NET Framework 4.8 or the .NET SDK.' }
Say "using $csc"

# ---------------------------------------------------------------------------
#  [3/6] UTF-8 BOM on the C# sources
# ---------------------------------------------------------------------------
# csc detects the source encoding from the BOM. Without one it falls back to the
# system ANSI code page and every Chinese string literal turns to mojibake.
Head '[3/7] source encoding'
$utf8Bom = New-Object System.Text.UTF8Encoding($true)
foreach ($cs in @($launcherCs, $vcbCs, $installerCs, $uninstCs)) {
    if (-not (Test-Path -LiteralPath $cs)) { throw "source not found: $cs" }
    $bytes = [System.IO.File]::ReadAllBytes($cs)
    $hasBom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
    if ($hasBom) { Say "BOM already present: $(Split-Path -Leaf $cs)" }
    else {
        # Read as UTF-8 (no BOM), write back with BOM. No re-encoding of the text itself.
        $text = [System.IO.File]::ReadAllText($cs, (New-Object System.Text.UTF8Encoding($false)))
        [System.IO.File]::WriteAllText($cs, $text, $utf8Bom)
        Say "BOM added: $(Split-Path -Leaf $cs)"
    }
}

# ---------------------------------------------------------------------------
#  [4/6] payload.zip
# ---------------------------------------------------------------------------
Head '[4/7] building payload.zip'
if (-not (Test-Path -LiteralPath $Out)) { New-Item -ItemType Directory -Path $Out -Force | Out-Null }
$zip = Join-Path $env:TEMP 'vcb-payload.zip'
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
# -Path "<dir>\*" puts the folder CONTENTS at the ZIP ROOT, which is what the
# installer expects (it extracts straight into the install directory).
Compress-Archive -Path (Join-Path $Portable '*') -DestinationPath $zip -CompressionLevel Optimal
$zipSize = (Get-Item -LiteralPath $zip).Length
Say ("payload.zip = {0:N0} bytes ({1:N1} MB)" -f $zipSize, ($zipSize / 1MB))

# ---------------------------------------------------------------------------
#  [5/6] run-ui.exe
# ---------------------------------------------------------------------------
# asInvoker (no manifest at all), /target:winexe so no console window flashes.
Head '[5/7] compiling run-ui.exe'
$runUi = Join-Path $env:TEMP 'vcb-run-ui.exe'
if (Test-Path -LiteralPath $runUi) { Remove-Item -LiteralPath $runUi -Force }
$refsLauncher = @()
foreach ($n in @('System.dll', 'System.Windows.Forms.dll', 'System.Drawing.dll')) {
    $p = Join-Path $env:WINDIR ('Microsoft.NET\Framework64\v4.0.30319\' + $n)
    if (-not (Test-Path -LiteralPath $p)) { $p = Join-Path $env:WINDIR ('Microsoft.NET\Framework\v4.0.30319\' + $n) }
    if (Test-Path -LiteralPath $p) { $refsLauncher += ('/reference:' + $p) }
}
$argsLauncher = @('/nologo', '/noconfig', '/target:winexe', '/platform:anycpu', '/optimize+', '/langversion:5', '/utf8output', "/out:$runUi") + $refsLauncher + @($launcherCs)
$outLauncher = & $csc $argsLauncher 2>&1
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $runUi)) {
    $outLauncher | ForEach-Object { Write-Host $_ }
    # retry without /langversion:5, same fallback build.ps1 uses
    $argsLauncher2 = @($argsLauncher | Where-Object { $_ -ne '/langversion:5' })
    $outLauncher = & $csc $argsLauncher2 2>&1
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $runUi)) {
        $outLauncher | ForEach-Object { Write-Host $_ }
        throw 'failed to compile Launcher.cs'
    }
}
Say ("run-ui.exe = {0:N0} bytes" -f (Get-Item -LiteralPath $runUi).Length)

# ---------------------------------------------------------------------------
#  [6/7] uninstall.exe   (Vcb.cs + Uninstaller.cs, no payload needed)
# ---------------------------------------------------------------------------
# A console app on purpose: the user sees what is being removed, and it asks for
# confirmation before touching anything. It is tiny because there is nothing to
# unpack - all it does is delete things.
Head '[6/7] compiling uninstall.exe'
$uninstExe = Join-Path $Out 'uninstall.exe'
if (Test-Path -LiteralPath $uninstExe) { Remove-Item -LiteralPath $uninstExe -Force }

$refsUninst = @()
foreach ($n in @('System.dll', 'System.Core.dll')) {
    $p = Join-Path $env:WINDIR ('Microsoft.NET\Framework64\v4.0.30319\' + $n)
    if (-not (Test-Path -LiteralPath $p)) { $p = Join-Path $env:WINDIR ('Microsoft.NET\Framework\v4.0.30319\' + $n) }
    if (Test-Path -LiteralPath $p) { $refsUninst += ('/reference:' + $p) }
}
if ($refsUninst.Count -lt 2) { throw 'missing .NET Framework reference assemblies for the uninstaller' }

$argsUninst = @(
    '/nologo', '/noconfig',
    '/target:exe',                       # console app: shows what it is removing
    '/platform:anycpu', '/optimize+', '/warn:4', '/langversion:5',
    '/utf8output',
    "/out:$uninstExe",
    "/win32manifest:$uninstMani"
) + $refsUninst + @(
    $vcbCs,
    $uninstCs
)

$outUninst = & $csc $argsUninst 2>&1
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $uninstExe)) {
    $outUninst | ForEach-Object { Write-Host $_ }
    $argsUninst2 = @($argsUninst | Where-Object { $_ -ne '/langversion:5' })
    $outUninst = & $csc $argsUninst2 2>&1
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $uninstExe)) {
        $outUninst | ForEach-Object { Write-Host $_ }
        throw 'failed to compile Uninstaller.cs'
    }
}
$outUninst | Where-Object { $_ -match 'warning|error' } | ForEach-Object { Write-Host $_ }
Say ("uninstall.exe = {0:N0} bytes" -f (Get-Item -LiteralPath $uninstExe).Length)

# ---------------------------------------------------------------------------
#  [7/7] install.exe   (payload + launcher + uninstaller embedded as resources)
# ---------------------------------------------------------------------------
Head '[7/7] compiling install.exe'
$installExe = Join-Path $Out 'install.exe'
if (Test-Path -LiteralPath $installExe) { Remove-Item -LiteralPath $installExe -Force }

$refsInstaller = @()
foreach ($n in @('System.dll', 'System.Core.dll', 'System.IO.Compression.dll', 'System.IO.Compression.FileSystem.dll')) {
    $p = Join-Path $env:WINDIR ('Microsoft.NET\Framework64\v4.0.30319\' + $n)
    if (-not (Test-Path -LiteralPath $p)) { $p = Join-Path $env:WINDIR ('Microsoft.NET\Framework\v4.0.30319\' + $n) }
    if (Test-Path -LiteralPath $p) { $refsInstaller += ('/reference:' + $p) }
    else { Write-Host "  [warn] missing reference assembly: $n" -ForegroundColor Yellow }
}
if ($refsInstaller.Count -lt 4) { throw 'missing .NET Framework reference assemblies for the installer' }

$argsInstaller = @(
    '/nologo', '/noconfig',
    '/target:exe',                       # console app: the user sees progress
    '/platform:anycpu', '/optimize+', '/warn:4', '/langversion:5',
    '/utf8output',                       # compiler messages in UTF-8, not OEM cp936
    "/out:$installExe",
    "/win32manifest:$manifest"
) + $refsInstaller + @(
    # /resource:<file>,<logical name>   ->   Assembly.GetManifestResourceStream("<logical name>")
    ("/resource:$zip,Payload"),
    ("/resource:$runUi,RunUi"),
    # uninstall.exe is embedded so that a successful install always leaves a
    # working uninstaller in the install directory - that is the path the
    # "Apps & Features" UninstallString points at.
    ("/resource:$uninstExe,Uninstaller"),
    $vcbCs,
    $installerCs
)

$outInstaller = & $csc $argsInstaller 2>&1
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $installExe)) {
    $outInstaller | ForEach-Object { Write-Host $_ }
    $argsInstaller2 = @($argsInstaller | Where-Object { $_ -ne '/langversion:5' })
    $outInstaller = & $csc $argsInstaller2 2>&1
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $installExe)) {
        $outInstaller | ForEach-Object { Write-Host $_ }
        throw 'failed to compile Installer.cs'
    }
}
$outInstaller | Where-Object { $_ -match 'warning|error' } | ForEach-Object { Write-Host $_ }

$exeSize = (Get-Item -LiteralPath $installExe).Length
$unSize  = (Get-Item -LiteralPath $uninstExe).Length
Write-Host ''
Write-Host '================ done ================' -ForegroundColor Green
Write-Host ("  {0}" -f $installExe) -ForegroundColor Green
Write-Host ("  {0:N0} bytes ({1:N1} MB)" -f $exeSize, ($exeSize / 1MB)) -ForegroundColor Green
Write-Host ("  {0}" -f $uninstExe) -ForegroundColor Green
Write-Host ("  {0:N0} bytes" -f $unSize) -ForegroundColor Green
Write-Host '======================================' -ForegroundColor Green
Write-Host ''
Write-Host 'On the target machine just double-click install.exe:' -ForegroundColor Cyan
Write-Host '  - one UAC prompt at install time (that is the only one, ever)' -ForegroundColor Cyan
Write-Host '  - installs to %LOCALAPPDATA%\ValorantCpuBoost' -ForegroundColor Cyan
Write-Host '  - creates Desktop + Start Menu shortcuts' -ForegroundColor Cyan
Write-Host '  - shows up in Settings > Apps, with uninstall.exe behind the Uninstall button' -ForegroundColor Cyan
Write-Host '  - from then on the UI starts elevated via a scheduled task, 0 UAC' -ForegroundColor Cyan
Write-Host ''
Write-Host 'uninstall.exe can also be handed out on its own - it finds the install' -ForegroundColor Cyan
Write-Host 'directory by itself (own folder -> registry InstallLocation -> default).' -ForegroundColor Cyan
Write-Host ''
