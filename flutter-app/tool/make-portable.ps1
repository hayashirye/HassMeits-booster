# make-portable.ps1 -- build a self-contained folder you can hand to someone else.
#
# WHY THIS EXISTS
#   The Flutter release output is NOT self-contained. Two things are missing:
#
#     1. The MSVC runtime. valorant_boost.exe imports
#        VCRUNTIME140.dll / VCRUNTIME140_1.dll / MSVCP140.dll.
#        Flutter's CMake links with /MD (dynamic CRT), so a machine that has
#        never had Visual Studio or the "Visual C++ 2015-2022 Redistributable
#        (x64)" cannot start the exe at all -- it dies before any Dart code runs.
#        Copying those three DLLs next to the exe ("app-local deployment") is
#        supported by Microsoft and removes that dependency.
#
#     2. The engine. The Flutter UI is only a shell: every page is filled by
#        running ValorantBoost.exe. Put it in the SAME folder as the UI and
#        lib\engine.dart finds it (candidate #2), and Core.cs then writes
#        boost-log.txt right next to itself, which is where the log page looks.
#
#   Nothing here touches the C# engine -- it is copied byte for byte.
#
# NOTE: keep this file ASCII-only. PowerShell 5.1 reads a .ps1 with no BOM as
#       GBK, so Chinese characters in here turn into mojibake.

param(
    [string]$Out = '',
    [string]$Engine = '',
    [switch]$Zip,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'

$appRoot = Split-Path -Parent $PSScriptRoot          # ...\flutter-app
$release = Join-Path $appRoot 'build\windows\x64\runner\Release'
$repoRoot = Split-Path -Parent $appRoot              # ...\valorant-cpu-boost

#  The portable folder lives inside release\ alongside install.exe, so the
#  whole deliverable is one folder instead of two.
if (-not $Out) { $Out = Join-Path $repoRoot 'release\portable' }
if (-not $Engine) { $Engine = Join-Path $repoRoot 'ValorantBoost.exe' }

Write-Host '=== make-portable ===' -ForegroundColor Cyan
Write-Host ("  flutter-app : {0}" -f $appRoot)
Write-Host ("  release     : {0}" -f $release)
Write-Host ("  engine      : {0}" -f $Engine)
Write-Host ("  output      : {0}" -f $Out)

# ---------------------------------------------------------------- 1) build
if (-not $SkipBuild) {
    Write-Host "`n[1/6] flutter build windows --release" -ForegroundColor Gray
    Push-Location $appRoot
    try {
        # The mirrors matter on this network; harmless elsewhere.
        $env:FLUTTER_STORAGE_BASE_URL = 'https://storage.flutter-io.cn'
        $env:PUB_HOSTED_URL = 'https://pub.flutter-io.cn'
        & flutter --no-version-check build windows --release
        if ($LASTEXITCODE -ne 0) { throw "flutter build failed (exit $LASTEXITCODE)" }
    } finally { Pop-Location }
} else {
    Write-Host "`n[1/6] skipped (--SkipBuild)" -ForegroundColor DarkGray
}

if (-not (Test-Path (Join-Path $release 'valorant_boost.exe'))) {
    throw "no release build at $release -- run without -SkipBuild first"
}

# ---------------------------------------------------------------- 2) clean
Write-Host "`n[2/6] preparing output folder" -ForegroundColor Gray

# A running copy of the app holds flutter_windows.dll open, and Remove-Item then
# dies with "Access to the path 'flutter_windows.dll' is denied" -- which reads
# like a permissions problem but is really just a live process. Stop it first.
# (Same trap as LINK : fatal error LNK1104 on the .exe during flutter build.)
foreach ($name in @('valorant_boost', 'run-ui')) {
    $procs = @(Get-Process -Name $name -ErrorAction SilentlyContinue)
    if ($procs.Count -gt 0) {
        Write-Host ("      stopping {0} running copy/copies of {1}.exe" -f $procs.Count, $name) -ForegroundColor DarkGray
        $procs | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 400
    }
}

if (Test-Path $Out) {
    # Retry: Windows can keep a directory handle alive for a moment after the
    # process dies, so one attempt is sometimes not enough.
    $removed = $false
    for ($i = 0; $i -lt 5 -and -not $removed; $i++) {
        try {
            Remove-Item -LiteralPath $Out -Recurse -Force -ErrorAction Stop
            $removed = $true
        } catch {
            if ($i -eq 4) {
                throw ("cannot clear {0} -- something still has it open. Close the app and retry. ({1})" -f $Out, $_.Exception.Message)
            }
            Start-Sleep -Milliseconds 500
        }
    }
}
New-Item -ItemType Directory -Path $Out -Force | Out-Null

# ------------------------------------------------- 3) copy the Flutter app
Write-Host "`n[3/6] copying Flutter output" -ForegroundColor Gray
Copy-Item -Path (Join-Path $release '*') -Destination $Out -Recurse -Force

# ------------------------------------------- 4) app-local MSVC runtime
Write-Host "`n[4/6] copying the MSVC runtime (app-local)" -ForegroundColor Gray
$crt = @('vcruntime140.dll', 'vcruntime140_1.dll', 'msvcp140.dll')
foreach ($dll in $crt) {
    $src = Join-Path $env:SystemRoot "System32\$dll"
    if (Test-Path $src) {
        Copy-Item -LiteralPath $src -Destination (Join-Path $Out $dll) -Force
        Write-Host ("      ok   {0}" -f $dll) -ForegroundColor DarkGray
    } else {
        # Not fatal: if the target machine already has the redistributable,
        # everything still works. But say so loudly.
        Write-Host ("      MISS {0} -- not on this machine either" -f $dll) -ForegroundColor Yellow
    }
}

# ------------------------------------------------- 5) the engine, side by side
Write-Host "`n[5/6] copying the engine" -ForegroundColor Gray
if (Test-Path $Engine) {
    Copy-Item -LiteralPath $Engine -Destination (Join-Path $Out 'ValorantBoost.exe') -Force
    Write-Host ("      ok   ValorantBoost.exe ({0} bytes)" -f (Get-Item $Engine).Length) -ForegroundColor DarkGray
} else {
    Write-Host ("      MISS {0}" -f $Engine) -ForegroundColor Yellow
    Write-Host "      The UI will start but every page will say 'engine not found'." -ForegroundColor Yellow
    Write-Host "      Pass -Engine <path> to point at it." -ForegroundColor Yellow
}

# ------------------------------------------------- 6) the cleanup entry point
# Two files, and they need OPPOSITE encodings -- that is the whole reason this
# step exists instead of "just copy the whole folder":
#
#   cleanup.cmd  pure ASCII, NO BOM.  cmd.exe reads a .cmd with the console code
#                page (936 here), so Chinese in it is mojibake; and a UTF-8 BOM
#                would break the very first line.
#   cleanup.ps1  UTF-8 WITH BOM.      Windows PowerShell 5.1 decodes a BOM-less
#                .ps1 as GBK, which turns every Chinese message into garbage.
#
# A portable copy has no other way to un-register its own scheduled task, so
# these ship with the folder. Don't "optimise" this step away.
Write-Host "`n[6/6] copying the cleanup scripts" -ForegroundColor Gray
foreach ($f in @('cleanup.cmd', 'cleanup.ps1')) {
    $src = Join-Path $PSScriptRoot $f
    if (-not (Test-Path $src)) {
        Write-Host ("      MISS {0} -- a portable copy would have no way to un-register the task" -f $f) -ForegroundColor Yellow
        continue
    }
    Copy-Item -LiteralPath $src -Destination (Join-Path $Out $f) -Force
    $bytes = [System.IO.File]::ReadAllBytes($src)
    $bom = ($bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
    $hi = @($bytes | Where-Object { $_ -gt 127 }).Count
    if ($f -like '*.cmd') {
        if ($bom -or $hi -gt 0) {
            Write-Host ("      WARN {0} must stay ASCII with no BOM (got {1}, {2} non-ASCII bytes)" -f $f, $(if ($bom) { 'BOM' } else { 'no BOM' }), $hi) -ForegroundColor Yellow
        } else {
            Write-Host ("      ok   {0} (ASCII, no BOM)" -f $f) -ForegroundColor DarkGray
        }
    } else {
        if ($bom) {
            Write-Host ("      ok   {0} (UTF-8 BOM)" -f $f) -ForegroundColor DarkGray
        } else {
            Write-Host ("      WARN {0} must be UTF-8 WITH BOM or PowerShell 5.1 shows mojibake" -f $f) -ForegroundColor Yellow
        }
    }
}

# ---------------------------------------------------------------- summary
$files = Get-ChildItem -LiteralPath $Out -Recurse -File
$total = ($files | Measure-Object -Property Length -Sum).Sum
Write-Host "`n=== done ===" -ForegroundColor Cyan
Write-Host ("  {0} files, {1:N1} MB" -f $files.Count, ($total / 1MB))
Write-Host ("  {0}" -f $Out)

Write-Host "`nOn the target machine:" -ForegroundColor Cyan
Write-Host "  1. copy this whole folder anywhere (Desktop is fine)"
Write-Host "  2. run valorant_boost.exe -- no installer, no VC++ redist needed"
Write-Host "  3. click 'enable guard' on a game page: it asks for admin once"
Write-Host "     and registers the scheduled task with THAT folder baked in,"
Write-Host "     so boost-log.txt is written there too"
Write-Host ""
Write-Host "  to undo step 3 later, run cleanup.cmd in the same folder:"
Write-Host "     it removes the scheduled task and the Defender exclusion,"
Write-Host "     and leaves the folder itself alone"

if ($Zip) {
    # NOT $zip: PowerShell variable names are case-insensitive, so $zip IS the
    # [switch]$Zip parameter -- assigning a string to it fails with
    # "Cannot convert value "System.String" to type SwitchParameter".
    $zipPath = "$Out.zip"
    if (Test-Path $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
    Write-Host "`nzipping..." -ForegroundColor Gray
    Compress-Archive -Path (Join-Path $Out '*') -DestinationPath $zipPath -CompressionLevel Optimal
    Write-Host ("  {0}  ({1:N1} MB)" -f $zipPath, ((Get-Item $zipPath).Length / 1MB))
}
