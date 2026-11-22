@echo off
setlocal EnableExtensions
cd /d "%~dp0"
REM Check the installed runtime without starting PowerShell or downloading components.
set "NET_RELEASE="
for /f "tokens=3" %%R in ('"%SystemRoot%\System32\reg.exe" query "HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" /v Release 2^>nul') do set "NET_RELEASE=%%R"
if not defined NET_RELEASE goto missing_runtime
set /a NET_RELEASE_DEC=%NET_RELEASE% >nul 2>nul
if %NET_RELEASE_DEC% LSS 528040 goto missing_runtime
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
 echo.
 echo [ERROR] No .NET Framework C# compiler was found on this computer.
 echo [ERROR] Expected: %%WINDIR%%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
 echo Please send this window to the developer. No system repair or installation was attempted.
 echo.
 if /I not "%~1"=="--no-pause" pause
 exit /b 1
)
if not exist "src\Program.cs" (
 echo [ERROR] Missing src\Program.cs. Unzip the ENTIRE source package first.
 if /I not "%~1"=="--no-pause" pause
 exit /b 2
)
if not exist "build" mkdir "build"
echo.
echo ===== BAT Automation Standalone v0.5.20-BAT =====
echo Compiler: "%CSC%"
pushd "src"
"%CSC%" /nologo /langversion:5 /win32manifest:"%~dp0app.manifest" /target:winexe /platform:anycpu /optimize+ /codepage:65001 /out:"%~dp0build\BatAutomation.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:Microsoft.CSharp.dll /reference:System.Xml.dll *.cs
set "RESULT=%ERRORLEVEL%"
popd
if not "%RESULT%"=="0" (
 echo.
 echo [FAILED] C# compiler returned error %RESULT%.
 echo Please copy or screenshot the COMPLETE error messages above.
 if /I not "%~1"=="--no-pause" pause
 exit /b %RESULT%
)
if not exist "build\BatAutomation.exe" (
 echo [ERROR] Compiler returned success but output EXE is missing.
 if /I not "%~1"=="--no-pause" pause
 exit /b 3
)
echo [SUCCESS] build\BatAutomation.exe
if /I not "%~1"=="--no-pause" pause
exit /b 0

:missing_runtime
echo [ERROR] Requires .NET Framework 4.8 or newer 4.x. Win7/2008 R2 also require SP1.
if /I not "%~1"=="--no-pause" pause
exit /b 5
