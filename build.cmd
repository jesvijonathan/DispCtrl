:<<"::END-OF-CMD"
@echo off
rem One entry point for every system. Windows: build.cmd build ^| test ^| run engine ^| release ^| doctor ...
rem (double-click for the menu). Linux, macOS, WSL: ./build.cmd build ^| test ^| setup ^| doctor, which runs build/build.sh.
rem cmd skips the first line as a label; a POSIX shell reads it as a no-op whose heredoc swallows this cmd part.
rem LF line endings on purpose (.gitattributes): the shell needs them, and cmd runs this part without labels or goto.
setlocal
set "PS=powershell.exe"
where pwsh.exe >nul 2>nul && set "PS=pwsh.exe"
"%PS%" -NoProfile -ExecutionPolicy Bypass -File "%~dp0build\dev.ps1" %*
set "CODE=%ERRORLEVEL%"
rem Opened from Explorer the window would close before the result could be read.
if "%~1"=="" pause
exit /b %CODE%
::END-OF-CMD
exec bash "$(dirname "$0")/build/build.sh" "$@"
