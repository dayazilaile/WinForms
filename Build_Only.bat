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
if not exist "build\Database" mkdir "build\Database"
echo.
echo ===== IT Desktop Toolkit V0.4.4 =====
echo Compiler: "%CSC%"
pushd "src"
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 /out:"%~dp0build\ITDesktopToolkit.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Management.dll /reference:Microsoft.CSharp.dll *.cs
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
echo [SUCCESS] build\ITDesktopToolkit.exe
 echo [NOTE] Keep the complete build folder including Database when moving to other computers.
if /I not "%~1"=="--no-pause" pause
exit /b 0
