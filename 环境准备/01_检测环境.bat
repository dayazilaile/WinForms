@echo off
setlocal EnableExtensions DisableDelayedExpansion
set "PREP_PS=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
if not exist "%PREP_PS%" (
 echo [ERROR] Windows PowerShell is missing. See README.md in this folder.
 pause
 exit /b 1
)
"%PREP_PS%" -NoLogo -NoProfile -File "%~dp0Environment.ps1"
set "PREP_RESULT=%ERRORLEVEL%"
echo.
echo Exit code: %PREP_RESULT%. If scripts are blocked by policy, consult your administrator.
pause
exit /b %PREP_RESULT%
