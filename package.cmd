@echo off
setlocal

where pwsh.exe >nul 2>nul
if %errorlevel% equ 0 (
    set "POWERSHELL_EXE=pwsh.exe"
) else (
    set "POWERSHELL_EXE=powershell.exe"
)

"%POWERSHELL_EXE%" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-release.ps1" -OpenOutput %*
set "PACKAGE_EXIT_CODE=%errorlevel%"

if not "%PACKAGE_EXIT_CODE%"=="0" (
    echo.
    echo Packaging failed. Review the error above.
    pause
)

exit /b %PACKAGE_EXIT_CODE%
