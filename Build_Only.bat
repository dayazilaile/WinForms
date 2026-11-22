@echo off
setlocal EnableExtensions
cd /d "%~dp0"
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
echo ===== BAT Automation Standalone v0.5.18-BAT =====
echo Compiler: "%CSC%"
pushd "src"
"%CSC%" /nologo /langversion:5 /target:winexe /platform:anycpu /optimize+ /codepage:65001 /out:"%~dp0build\BatAutomation.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:Microsoft.CSharp.dll /reference:System.Xml.dll *.cs
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
