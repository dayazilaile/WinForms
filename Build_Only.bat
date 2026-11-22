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
if not exist "Scripts\SoftwareCleanup.ps1" (
 echo [ERROR] Missing Scripts\SoftwareCleanup.ps1. Unzip the ENTIRE source package first.
 if /I not "%~1"=="--no-pause" pause
 exit /b 4
)
if not exist "build" mkdir "build"
if not exist "build\Database" mkdir "build\Database"
echo.
echo ===== IT Desktop Toolkit V0.5.20 =====
echo Compiler: "%CSC%"
pushd "src"
"%CSC%" /nologo /win32manifest:"%~dp0app.manifest" /target:winexe /platform:anycpu /optimize+ /codepage:65001 /out:"%~dp0build\ITDesktopToolkit.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Management.dll /reference:Microsoft.CSharp.dll /reference:System.Xml.dll *.cs
set "RESULT=%ERRORLEVEL%"
popd
if not "%RESULT%"=="0" (
 echo.
 echo [FAILED] C# compiler returned error %RESULT%.
 echo Please copy or screenshot the COMPLETE error messages above.
 if /I not "%~1"=="--no-pause" pause
 exit /b %RESULT%
)
if not exist "build\ITDesktopToolkit.exe" (
 echo [ERROR] Compiler returned success but output EXE is missing.
 if /I not "%~1"=="--no-pause" pause
 exit /b 3
)
REM Deploy the reviewable template; its version/content is checked before script export.
if not exist "build\Scripts" mkdir "build\Scripts"
copy /Y "Scripts\SoftwareCleanup.ps1" "build\Scripts\SoftwareCleanup.ps1" >nul
if errorlevel 1 (
 echo [ERROR] Could not deploy Scripts\SoftwareCleanup.ps1. Build package is incomplete.
 if /I not "%~1"=="--no-pause" pause
 exit /b 5
)
REM Source Database is the distribution baseline; never overwrite user edits in build\Database.
if not exist "build\Database\hardware_models.tsv" copy /Y "Database\hardware_models.tsv" "build\Database\hardware_models.tsv" >nul
if not exist "build\Database\hardware_models_v039.tsv" copy /Y "Database\hardware_models_v039.tsv" "build\Database\hardware_models_v039.tsv" >nul
if not exist "build\Database\bugcheck_codes.tsv" copy /Y "Database\bugcheck_codes.tsv" "build\Database\bugcheck_codes.tsv" >nul
if not exist "build\Database\pci.ids" copy /Y "Database\pci.ids" "build\Database\pci.ids" >nul
if not exist "build\Database\generic_codes.tsv" copy /Y "Database\generic_codes.tsv" "build\Database\generic_codes.tsv" >nul
if not exist "build\Database\fault_codes.tsv" copy /Y "Database\fault_codes.tsv" "build\Database\fault_codes.tsv" >nul
if not exist "build\Database\usb.ids" copy /Y "Database\usb.ids" "build\Database\usb.ids" >nul
if not exist "build\Database\offline_database_README.txt" copy /Y "Database\offline_database_README.txt" "build\Database\offline_database_README.txt" >nul
echo.
REM Versioned supplements are additive; never overwrite user database files.
for %%F in (pci.v052.ids usb.v052.ids board_models_v052.tsv component_models_v052.tsv SOURCES-v052.md) do if not exist "build\Database\%%F" copy /Y "Database\%%F" "build\Database\%%F" >nul
if not exist "build\Database\Licenses" mkdir "build\Database\Licenses"
if not exist "build\Database\Sources" mkdir "build\Database\Sources"
for %%F in ("Database\Licenses\*") do if not exist "build\Database\Licenses\%%~nxF" copy /Y "%%~fF" "build\Database\Licenses\%%~nxF" >nul
for %%F in ("Database\Sources\*") do if not exist "build\Database\Sources\%%~nxF" copy /Y "%%~fF" "build\Database\Sources\%%~nxF" >nul
echo [SUCCESS] build\ITDesktopToolkit.exe
 echo [NOTE] Keep the complete build folder including Database when moving to other computers.
if /I not "%~1"=="--no-pause" pause
exit /b 0

:missing_runtime
echo [ERROR] Requires .NET Framework 4.8 or a later 4.x runtime.
echo Windows 7 and Server 2008 R2 require SP1. Install official offline prerequisites, then retry.
echo No installation, PowerShell or security-policy change was attempted.
if /I not "%~1"=="--no-pause" pause
exit /b 5
