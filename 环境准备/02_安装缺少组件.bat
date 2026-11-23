@echo off
setlocal EnableExtensions DisableDelayedExpansion
set "PREP_PS=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
if not exist "%PREP_PS%" (
 echo [ERROR] Windows PowerShell is missing. See README.md in this folder.
 pause
 exit /b 1
)
"%PREP_PS%" -NoLogo -NoProfile -File "%~dp0Environment.ps1" -Install
set "PREP_RESULT=%ERRORLEVEL%"
echo.
echo Exit code: %PREP_RESULT%. 3010 means restart required. No automatic restart.
echo If scripts are blocked by policy, consult your administrator. No policy was changed.
pause
exit /b %PREP_RESULT%
