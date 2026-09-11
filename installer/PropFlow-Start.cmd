@echo off
rem Start PropFlow and keep the window open so the sign-in credentials stay readable.
rem Launched by the Start Menu shortcut; `propflow-deploy up` from any terminal is equivalent.
setlocal
cd /d "%~dp0"
echo Starting PropFlow. The first run builds the web app and takes a few minutes.
echo.
"%~dp0propflow-deploy.exe" up
set EXITCODE=%ERRORLEVEL%
echo.
if %EXITCODE% neq 0 (
  echo PropFlow did not start. See the messages above; logs are in %%ProgramData%%\PropFlow\logs.
) else (
  echo PropFlow is running. Leave this window closed or open, it makes no difference.
  echo Stop it later with the "Stop PropFlow" shortcut.
)
echo.
pause
exit /b %EXITCODE%
