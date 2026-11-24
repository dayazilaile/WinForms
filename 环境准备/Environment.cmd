@echo off
setlocal EnableExtensions DisableDelayedExpansion
rem No PS1 files, policy changes, encoded commands or self-reading payloads.
set "PREP_ROOT=%~dp0"
set "PREP_MODE=%~1"
if /I "%PREP_MODE%"=="--check" goto mode_ok
if /I "%PREP_MODE%"=="--install" goto mode_ok
echo Usage: Environment.cmd --check or --install
exit /b 1
:mode_ok
set "PREP_PS=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
if exist "%SystemRoot%\Sysnative\WindowsPowerShell\v1.0\powershell.exe" set "PREP_PS=%SystemRoot%\Sysnative\WindowsPowerShell\v1.0\powershell.exe"
if not exist "%PREP_PS%" goto powershell_missing

rem Inline read-only metadata query; compatible with stock Windows PowerShell 2.0.
set "PREP_PROBE=$ErrorActionPreference='Stop';try{$o=Get-WmiObject -Class Win32_OperatingSystem -ErrorAction Stop;$v=[version]$o.Version;$p=$PSVersionTable.PSVersion;$a=$env:PROCESSOR_ARCHITECTURE;if($env:PROCESSOR_ARCHITEW6432){$a=$env:PROCESSOR_ARCHITEW6432};if($a -eq 'AMD64' -or $a -eq 'x64'){$a='x64'}elseif($a -eq 'x86'){$a='x86'}elseif($a -eq 'ARM64'){$a='ARM64'}else{$a='unknown'};$type=[int]$o.ProductType;$sp=[int]$o.ServicePackMajorVersion;"
set "PREP_PROBE=%PREP_PROBE% $net=0;$reg=Join-Path $env:SystemRoot 'System32\reg.exe';foreach($view in @('/reg:64','/reg:32','')){$ra=@('query','HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full','/v','Release');if($view){$ra+=$view};try{$ErrorActionPreference='Continue';$rows=@(& $reg @ra 2>$null)}finally{$ErrorActionPreference='Stop'};foreach($row in $rows){$f=$row.Trim() -split '\s+';if($f.Count -ge 3 -and $f[0] -eq 'Release' -and $f[1] -eq 'REG_DWORD'){try{if($f[2].StartsWith('0x')){$n=[Convert]::ToInt32($f[2].Substring(2),16)}else{$n=[Convert]::ToInt32($f[2],10)};if($n -gt $net){$net=$n}}catch{}}}};"
set "PREP_PROBE=%PREP_PROBE% $pending=0;try{foreach($k in @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending','HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired')){if(Test-Path -LiteralPath $k -ErrorAction Stop){$pending=1}};$s=Get-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager' -ErrorAction Stop;foreach($rename in @($s.PendingFileRenameOperations)){if($rename){$pending=1}}}catch{$pending=2};"
set "PREP_PROBE=%PREP_PROBE% $supported=0;$wmf='none';$server=$type -eq 2 -or $type -eq 3;if(($type -eq 1 -or $server) -and ($a -eq 'x86' -or $a -eq 'x64') -and (-not $server -or $a -eq 'x64')){if($v.Major -eq 6 -and $v.Minor -eq 1 -and $sp -ge 1){$supported=1;$wmf='win7-'+$a}elseif($v.Major -eq 6 -and $v.Minor -eq 2 -and $server){$supported=1;$wmf='server2012-x64'}elseif($v.Major -eq 6 -and $v.Minor -eq 3){$supported=1;$wmf='win81-'+$a}elseif($v.Major -eq 10 -and $v.Build -ge 14393){$supported=1}};$needNet=[int]($net -lt 528040);$needWmf=[int]($p -lt [version]'5.1');"
set "PREP_PROBE=%PREP_PROBE% Write-Output ((@('PREP_META',$v.Major,$v.Minor,$v.Build,$sp,$type,$a,$p.Major,$p.Minor,$pending,$net,$supported,$wmf,$needNet,$needWmf) -join '|'))}catch{Write-Output 'PREP_PROBE_ERROR'}"

set "PREP_META_OK=0"
for /f "tokens=1-15 delims=|" %%A in ('call "%PREP_PS%" -NoLogo -NoProfile -NonInteractive -Command "%PREP_PROBE%" 2^>nul') do if "%%A"=="PREP_META" (
 set "PREP_META_OK=1"
 set "PREP_OS=%%B.%%C.%%D"
 set "PREP_SP=%%E"
 set "PREP_TYPE=%%F"
 set "PREP_ARCH=%%G"
 set "PREP_PS_VERSION=%%H.%%I"
 set "PREP_PENDING=%%J"
 set "PREP_NET=%%K"
 set "PREP_SUPPORTED=%%L"
 set "PREP_WMF=%%M"
 set "PREP_NEED_NET=%%N"
 set "PREP_NEED_WMF=%%O"
)
if "%PREP_META_OK%"=="0" goto probe_failed
if not "%PREP_NEED_WMF%"=="0" if not "%PREP_NEED_WMF%"=="1" goto probe_failed
echo ===== WinForms environment preparation =====
echo OS: %PREP_OS% SP%PREP_SP% / %PREP_ARCH% / ProductType=%PREP_TYPE%
echo .NET Release: %PREP_NET%  Minimum=378389 [4.5]; recommended=528040 [4.8].
echo Windows PowerShell: %PREP_PS_VERSION%  Automation requires 5.1.
echo Pending restart: %PREP_PENDING%  [0=no, 1=yes, 2=unreadable]
if "%PREP_NET%"=="0" echo [INFO] No readable Release value. Missing runtime and denied reads cannot be distinguished.
if "%PREP_SUPPORTED%"=="0" echo [INFO] Unsupported installer platform. Win7 needs SP1; early Win10 needs updating; x86/x64 only.
if /I "%PREP_MODE%"=="--install" goto install
echo Read-only check complete. No installation, policy change or log file.
if "%PREP_SUPPORTED%"=="0" exit /b 5
if %PREP_NET% LSS 378389 exit /b 5
if "%PREP_NEED_WMF%"=="1" exit /b 5
exit /b 0

:install
if "%PREP_SUPPORTED%"=="0" exit /b 5
if "%PREP_NEED_NET%%PREP_NEED_WMF%"=="00" goto already_ready
if "%PREP_PENDING%"=="1" goto pending_restart
if "%PREP_PENDING%"=="2" goto restart_unknown
set "PREP_ADMIN=$ErrorActionPreference='Stop';try{$i=[Security.Principal.WindowsIdentity]::GetCurrent();try{$p=New-Object Security.Principal.WindowsPrincipal($i);if($p.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){exit 0}else{exit 1}}finally{$i.Dispose()}}catch{exit 13}"
"%PREP_PS%" -NoLogo -NoProfile -NonInteractive -Command "%PREP_ADMIN%"
set "PREP_ADMIN_RESULT=%ERRORLEVEL%"
if "%PREP_ADMIN_RESULT%"=="1" goto need_admin
if not "%PREP_ADMIN_RESULT%"=="0" goto admin_unknown
set "PREP_PACKAGE_NAME="
if "%PREP_NEED_NET%"=="1" set "PREP_PACKAGE_NAME=ndp48-x86-x64-allos-enu.exe"
if defined PREP_PACKAGE_NAME goto package_selected
if "%PREP_WMF%"=="win7-x86" set "PREP_PACKAGE_NAME=Win7AndW2K8R2-KB3191566-x86.msu"
if "%PREP_WMF%"=="win7-x64" set "PREP_PACKAGE_NAME=Win7AndW2K8R2-KB3191566-x64.msu"
if "%PREP_WMF%"=="server2012-x64" set "PREP_PACKAGE_NAME=W2K12-KB3191565-x64.msu"
if "%PREP_WMF%"=="win81-x86" set "PREP_PACKAGE_NAME=Win8.1AndW2K12R2-KB3191564-x86.msu"
if "%PREP_WMF%"=="win81-x64" set "PREP_PACKAGE_NAME=Win8.1AndW2K12R2-KB3191564-x64.msu"
if not defined PREP_PACKAGE_NAME goto wmf_manual
:package_selected
rem The installer's directory name is passed through the environment, never inserted into PowerShell source.
set "PREP_PACKAGE_PATH=%PREP_ROOT%Installers\%PREP_PACKAGE_NAME%"
if not exist "%PREP_PACKAGE_PATH%" goto package_missing
if "%PREP_NEED_NET%"=="0" goto verified
set "PREP_VERIFY=$ErrorActionPreference='Stop';try{$s=Get-AuthenticodeSignature -FilePath ([Management.Automation.WildcardPattern]::Escape($env:PREP_PACKAGE_PATH)) -ErrorAction Stop;if($s.Status -eq 'Valid' -and $s.SignerCertificate -and $s.SignerCertificate.Subject -match '(^|,\s*)O=Microsoft Corporation(,|$)'){exit 0};[Console]::WriteLine('Microsoft signature verification failed.');exit 12}catch{[Console]::WriteLine('Could not verify Microsoft installer signature.');exit 12}"
"%PREP_PS%" -NoLogo -NoProfile -NonInteractive -Command "%PREP_VERIFY%"
if errorlevel 1 goto signature_failed
:verified
echo.
echo This run installs one component: %PREP_PACKAGE_NAME%
echo Check Server / WMF compatibility before installing. No automatic restart.
echo Installer configuration is written to Windows. Windows may keep its own caches and logs.
"%SystemRoot%\System32\choice.exe" /c YN /n /m "Install this component? [Y/N] "
if errorlevel 2 goto cancelled
if not errorlevel 1 goto cancelled
set "PREP_LOG_PATH=%PREP_ROOT%Logs\environment-install.tsv"
set "PREP_DETAIL_LOG=%PREP_ROOT%Logs\dotnet-%RANDOM%-%RANDOM%.html"
set "PREP_STAGE=begin"
set "PREP_INSTALL_CODE=not-started"
set "PREP_LOG=$ErrorActionPreference='Stop';try{$mac=@();$ip=@();$os='unread';try{$os=(Get-WmiObject -Class Win32_OperatingSystem -ErrorAction Stop).Caption}catch{};try{foreach($n in @(Get-WmiObject -Class Win32_NetworkAdapterConfiguration -Filter 'IPEnabled=True' -ErrorAction Stop)){if($n.MACAddress){$mac+=$n.MACAddress};$ip+=@($n.IPAddress)}}catch{};if($mac.Count -eq 0){$mac=@('unread')};if($ip.Count -eq 0){$ip=@('unread')};$row=@((Get-Date).ToString('yyyy-MM-ddTHH:mm:ss.fffzzz'),[Environment]::MachineName,($mac -join '; '),($ip -join '; '),$os,([Environment]::UserDomainName+'\'+[Environment]::UserName),('install '+$env:PREP_PACKAGE_NAME),($env:PREP_STAGE+': '+$env:PREP_INSTALL_CODE));$cells=@();foreach($v in $row){$cells+=([string]$v -replace '[\t\r\n]',' ')};$p=$env:PREP_LOG_PATH;[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($p));$e=New-Object Text.UTF8Encoding($true);$tab=[string][char]9;$nl=[string][char]13+[char]10;if(-not [IO.File]::Exists($p)){[IO.File]::WriteAllText($p,(('Time','Host','MAC','IP','OS','Account','Operation','Result') -join $tab)+$nl,$e)};[IO.File]::AppendAllText($p,($cells -join $tab)+$nl,$e);exit 0}catch{[Console]::WriteLine('Portable installation log could not be written.');exit 14}"
"%PREP_PS%" -NoLogo -NoProfile -NonInteractive -Command "%PREP_LOG%"
if errorlevel 1 goto log_start_failed
if "%PREP_NEED_NET%"=="0" goto run_wmf
start "" /wait "%PREP_PACKAGE_PATH%" /passive /norestart /log "%PREP_DETAIL_LOG%"
set "PREP_INSTALL_CODE=%ERRORLEVEL%"
goto installer_returned
:run_wmf
set "PREP_WUSA=%SystemRoot%\System32\wusa.exe"
if exist "%SystemRoot%\Sysnative\wusa.exe" set "PREP_WUSA=%SystemRoot%\Sysnative\wusa.exe"
rem WUSA validates MSU signature and OS applicability.
start "" /wait "%PREP_WUSA%" "%PREP_PACKAGE_PATH%" /quiet /norestart
set "PREP_INSTALL_CODE=%ERRORLEVEL%"
:installer_returned
set "PREP_STAGE=end"
"%PREP_PS%" -NoLogo -NoProfile -NonInteractive -Command "%PREP_LOG%"
if errorlevel 1 goto log_end_failed
echo Installer exit code: %PREP_INSTALL_CODE%
if "%PREP_INSTALL_CODE%"=="0" goto installed
if "%PREP_INSTALL_CODE%"=="2359302" goto installed
if "%PREP_INSTALL_CODE%"=="3010" goto pending_restart
if "%PREP_INSTALL_CODE%"=="1641" goto pending_restart
echo [ERROR] Installation did not succeed. Check official prerequisites, certificate trust, system updates and the package platform.
exit /b 1
:installed
echo Installer completed. Run the check again in a new window. Install the next component separately if needed.
exit /b 0
:already_ready
echo [OK] Recommended .NET and Windows PowerShell components are already present. Nothing to install.
if "%PREP_PENDING%"=="1" goto pending_restart
if "%PREP_PENDING%"=="2" goto restart_unknown
exit /b 0
:pending_restart
echo [INFO] A restart is required. Restart manually, then run the check again. No automatic restart.
exit /b 3010
:restart_unknown
echo [ERROR] Cannot read pending-restart status. No installation was started. Ask your administrator to inspect it.
exit /b 1
:powershell_missing
echo [ERROR] Windows PowerShell is missing. Repair the Windows component first; see README.md.
exit /b 1
:probe_failed
echo [ERROR] Metadata query failed. Windows PowerShell, WMI or command execution may be restricted or damaged.
echo No PS1 file was loaded and no execution policy was changed. See README.md.
exit /b 1
:need_admin
echo [ERROR] Right-click 02_*.bat and select Run as administrator. No self-elevation was attempted.
exit /b 1
:admin_unknown
echo [ERROR] Could not verify administrator rights. No installation was started.
exit /b 1
:wmf_manual
echo [INFO] This Windows version must obtain or repair PowerShell 5.1 through Windows servicing, not old WMF packages.
exit /b 1
:package_missing
echo [ERROR] Missing package: "%PREP_PACKAGE_PATH%"
echo Download the exact Microsoft package listed in README.md and put it in the installer subfolder.
exit /b 1
:signature_failed
echo [ERROR] Installer was not trusted as Microsoft-signed. No installation was started.
exit /b 1
:log_start_failed
echo [ERROR] Software-directory log is not writable. No installation was started. No fallback location is used.
exit /b 1
:log_end_failed
echo [ERROR] Installer returned %PREP_INSTALL_CODE%, but its result log could not be written. Keep this screen and recheck the environment.
exit /b 1
:cancelled
echo Cancelled. No installation was started.
exit /b 0
