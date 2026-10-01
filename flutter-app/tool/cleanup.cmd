@echo off
rem ---------------------------------------------------------------------------
rem  Game CPU Boost - portable cleanup (entry point)
rem
rem  This file is deliberately PURE ASCII. cmd.exe reads a .cmd using the console
rem  code page (936 on Chinese Windows), so Chinese here would be mojibake on
rem  some machines -- and a UTF-8 BOM is not an option either, because cmd.exe
rem  would choke on the very first line. All the Chinese output lives in
rem  cleanup.ps1, which IS UTF-8 WITH BOM so PowerShell 5.1 decodes it right.
rem
rem  What it does: re-launches itself elevated if needed, then hands over to
rem  cleanup.ps1. Nothing else.
rem ---------------------------------------------------------------------------

setlocal
set "HERE=%~dp0"
set "PS1=%HERE%cleanup.ps1"

if not exist "%PS1%" (
    echo.
    echo   [ERROR] cleanup.ps1 not found next to this file.
    echo   Expected: %PS1%
    echo.
    pause
    exit /b 1
)

rem -- already elevated?  ("net session" only works for administrators)
net session >nul 2>&1
if %errorlevel%==0 goto :run

echo.
echo   Requesting administrator rights (needed to remove the scheduled task
echo   and the Windows Defender exclusion)...
echo.

powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath 'powershell.exe' -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File','%PS1%') -Verb RunAs"
if %errorlevel% neq 0 (
    echo.
    echo   [ERROR] Elevation was cancelled or failed.
    echo   The cleanup needs administrator rights.
    echo.
    pause
    exit /b 1
)
exit /b 0

:run
powershell -NoProfile -ExecutionPolicy Bypass -File "%PS1%"
exit /b %errorlevel%
