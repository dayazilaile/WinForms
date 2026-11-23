@echo off
setlocal EnableExtensions DisableDelayedExpansion
REM Read-only detection. CALL avoids cmd FOR /F stripping a quoted executable path.
set "NET_RELEASE=0"
set "NET_RELEASE_VIEW=not-detected"
for %%V in (64 32) do (
 for /f "tokens=1,2,*" %%A in ('call "%SystemRoot%\System32\reg.exe" query "HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" /v Release /reg:%%V 2^>nul') do if /I "%%A"=="Release" if /I "%%B"=="REG_DWORD" call :consider "%%C" "%%V-bit"
)
REM Native view is a final fallback; do not confuse query failure with an old version.
for /f "tokens=1,2,*" %%A in ('call "%SystemRoot%\System32\reg.exe" query "HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" /v Release 2^>nul') do if /I "%%A"=="Release" if /I "%%B"=="REG_DWORD" call :consider "%%C" "native"
echo [INFO] Detected .NET Framework Release=%NET_RELEASE% view=%NET_RELEASE_VIEW%.
if "%NET_RELEASE%"=="0" goto not_detected
if %NET_RELEASE% LSS 378389 goto too_old
set "NET_VERSION=4.5"
if %NET_RELEASE% GEQ 378675 set "NET_VERSION=4.5.1"
if %NET_RELEASE% GEQ 379893 set "NET_VERSION=4.5.2"
if %NET_RELEASE% GEQ 393295 set "NET_VERSION=4.6"
if %NET_RELEASE% GEQ 394254 set "NET_VERSION=4.6.1"
if %NET_RELEASE% GEQ 394802 set "NET_VERSION=4.6.2"
if %NET_RELEASE% GEQ 460798 set "NET_VERSION=4.7"
if %NET_RELEASE% GEQ 461308 set "NET_VERSION=4.7.1"
if %NET_RELEASE% GEQ 461808 set "NET_VERSION=4.7.2"
if %NET_RELEASE% GEQ 528040 set "NET_VERSION=4.8"
if %NET_RELEASE% GEQ 533320 set "NET_VERSION=4.8.1 or newer"
echo [OK] Detected .NET Framework %NET_VERSION%. Minimum 4.5; 4.8 recommended, not mandatory.
exit /b 0
:not_detected
echo [ERROR] Could not read a .NET Framework 4.5+ Release value from either registry view.
echo It may be missing, or the registry query may have been denied. This does not identify an installed version.
echo Read-only diagnostic: reg query "HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" /v Release
exit /b 5
:too_old
echo [ERROR] The detected Release value is below the .NET Framework 4.5 minimum 378389.
echo Install Microsoft's official offline .NET Framework runtime. 4.8 is recommended when supported.
echo Windows 7 requires SP1. No component installation or security-policy change was attempted.
exit /b 5
:consider
set "NET_CANDIDATE=0"
set /a "NET_CANDIDATE=%~1" >nul 2>nul
if errorlevel 1 exit /b 0
if %NET_CANDIDATE% GTR %NET_RELEASE% (
 set "NET_RELEASE=%NET_CANDIDATE%"
 set "NET_RELEASE_VIEW=%~2"
)
exit /b 0
