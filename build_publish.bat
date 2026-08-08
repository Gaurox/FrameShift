@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "PS_SCRIPT=%SCRIPT_DIR%build_installer.ps1"

if not exist "%PS_SCRIPT%" (
    echo build_installer.ps1 introuvable: "%PS_SCRIPT%"
    exit /b 1
)

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%PS_SCRIPT%" %*
exit /b %ERRORLEVEL%
