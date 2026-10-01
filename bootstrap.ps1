<#
    bootstrap.ps1 - repair file encodings, then hand over to build.ps1
    ------------------------------------------------------------------------
    Why this file exists:
      cmd.exe decodes a .bat file using the console code page that is active
      while it reads the file line by line. A .bat that contains UTF-8 Chinese
      text therefore gets mangled even when it calls "chcp 65001" on an early
      line: the text after the switch is decoded with the file offset still
      measured in the old encoding, so the parser loses sync and starts
      executing fragments of sentences. Typical damage looks like:

          'puBoost.exe' is not recognized as an internal or external command
          '?' is not recognized as an internal or external command

      (the 'puBoost.exe' fragment is the tail of "ValorantCpuBoost.exe").
      Adding a BOM does not help: cmd does not honor one.

      The only reliable rule is: a .bat must be pure ASCII.

    This script is that hand-over point.
      * fix-encoding.ps1 is ASCII-only, so it can be parsed even when every
        other file on disk is mis-encoded. It repairs them in place.
      * build.ps1 contains the Chinese messages and carries a UTF-8 BOM, so
        PowerShell 5.1 decodes it correctly.
      * Encoding: this file has a UTF-8 BOM and Chinese text. If you hand-edit
        it, keep it as "UTF-8 with BOM".

    Usage (called by the one-line ASCII launcher):
        powershell -NoProfile -ExecutionPolicy Bypass -File .\bootstrap.ps1
#>
[CmdletBinding()]
param(
    [string]$OutputName = 'ValorantCpuBoost.exe'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

# The child process writes UTF-8 bytes. Tell the console to interpret them as
# UTF-8, otherwise Chinese shows up as mojibake even when the text is correct.
try { [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false) } catch { }

$fixer = Join-Path $root 'fix-encoding.ps1'
$build = Join-Path $root 'build.ps1'

if (Test-Path -LiteralPath $fixer) {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $fixer
} else {
    Write-Host '[bootstrap] fix-encoding.ps1 not found, skipping encoding repair.' -ForegroundColor Yellow
}

if (-not (Test-Path -LiteralPath $build)) {
    Write-Host '[bootstrap] build.ps1 not found next to this script.' -ForegroundColor Red
    exit 1
}

& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $build -OutputName $OutputName
exit $LASTEXITCODE
