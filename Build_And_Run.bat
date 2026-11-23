@echo off
call "%~dp0Build_Only.bat" --no-pause
if errorlevel 1 (
 echo.
 echo Build failed. Please share the entire error message.
 pause
 exit /b 1
)
start "" "%~dp0build\WinForms.exe"
exit /b 0
