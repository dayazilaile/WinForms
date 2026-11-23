@echo off
setlocal EnableExtensions
cd /d "%~dp0"
echo ===== Read-only runtime diagnostic =====
ver
call "%~dp0Check_Runtime.bat"
echo.
echo Compiler locations:
if exist "%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" echo Found: %WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if exist "%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe" echo Found: %WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
echo No compilation, installation, system setting changes or log files were requested.
pause
