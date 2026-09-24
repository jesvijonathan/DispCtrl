@echo off
rem Double-click for the menu, or: build.cmd build ^| test ^| run engine ^| release ^| doctor ...
rem Everything is in build\dev.ps1; this only finds a PowerShell to run it.
setlocal
set "PS=powershell.exe"
where pwsh.exe >nul 2>nul && set "PS=pwsh.exe"
"%PS%" -NoProfile -ExecutionPolicy Bypass -File "%~dp0build\dev.ps1" %*
set "CODE=%ERRORLEVEL%"
rem Opened from Explorer the window would close before the result could be read.
if "%~1"=="" pause
exit /b %CODE%
