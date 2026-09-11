@echo off
rem Stop PropFlow. The database volume and everything in %ProgramData%\PropFlow are kept;
rem `propflow-deploy down --reset` from a terminal is what drops the data.
setlocal
cd /d "%~dp0"
"%~dp0propflow-deploy.exe" down
echo.
pause
exit /b %ERRORLEVEL%
