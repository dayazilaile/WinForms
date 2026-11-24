@echo off
setlocal EnableExtensions DisableDelayedExpansion
pushd "%~dp0"
if errorlevel 1 (
 echo [ERROR] Could not open the environment-helper directory.
 if not defined WINFORMS_ENV_NO_PAUSE pause
 exit /b 1
)
if not exist ".\Environment.cmd" (
 echo [ERROR] Environment.cmd is missing. Extract the complete environment-helper folder.
 popd
 if not defined WINFORMS_ENV_NO_PAUSE pause
 exit /b 1
)
call ".\Environment.cmd" --check
set "PREP_RESULT=%ERRORLEVEL%"
popd
echo.
echo Exit code: %PREP_RESULT%. 3010 means restart required. No automatic restart.
if not defined WINFORMS_ENV_NO_PAUSE pause
exit /b %PREP_RESULT%
