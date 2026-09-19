@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Publish-Organiza.ps1" %*
if errorlevel 1 exit /b %errorlevel%
echo.
echo Organiza publicado em: %~dp0publish\Organiza.Wpf.exe
endlocal
