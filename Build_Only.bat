@echo off
setlocal EnableExtensions
cd /d "%~dp0"
REM Validate the actual minimum; do not force 4.8 on older supported computers.
if not exist "%~dp0Check_Runtime.bat" (
 echo [ERROR] Missing Check_Runtime.bat. Extract the complete package.
 if /I not "%~1"=="--no-pause" pause
 exit /b 5
)
call "%~dp0Check_Runtime.bat"
if errorlevel 1 (
 if /I not "%~1"=="--no-pause" pause
 exit /b 5
)
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
echo ===== WinForms BAT Automation Standalone v0.6.6-BAT =====
echo Compiler: "%CSC%"
pushd "src"
"%CSC%" /nologo /langversion:5 /win32manifest:"%~dp0app.manifest" /target:winexe /platform:anycpu /optimize+ /codepage:65001 /out:"%~dp0build\WinForms-BAT.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:Microsoft.CSharp.dll /reference:System.Xml.dll *.cs
set "RESULT=%ERRORLEVEL%"
popd
if not "%RESULT%"=="0" (
 echo.
 echo [FAILED] C# compiler returned error %RESULT%.
 echo Please copy or screenshot the COMPLETE error messages above.
 if /I not "%~1"=="--no-pause" pause
 exit /b %RESULT%
)
if not exist "build\WinForms-BAT.exe" (
 echo [ERROR] Compiler returned success but output EXE is missing.
 if /I not "%~1"=="--no-pause" pause
 exit /b 3
)
echo [SUCCESS] build\WinForms-BAT.exe
if /I not "%~1"=="--no-pause" pause
exit /b 0
