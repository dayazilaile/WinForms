# Windows-only end-to-end regression. Reads real metadata; never supplies installer packages.
# PSExecutionPolicyPreference applies only to child processes, not the machine/user policy.
$ErrorActionPreference='Stop'
if($env:OS -ne 'Windows_NT'){Write-Output 'SKIP: native cmd/Restricted regression requires Windows';return}
$repo=Split-Path (Split-Path $MyInvocation.MyCommand.Path -Parent) -Parent
$native=Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe'
if(Test-Path -LiteralPath (Join-Path $env:SystemRoot 'Sysnative/WindowsPowerShell/v1.0/powershell.exe')){$native=Join-Path $env:SystemRoot 'Sysnative/WindowsPowerShell/v1.0/powershell.exe'}
$root=Join-Path ([IO.Path]::GetTempPath()) ('环境 O''Reilly &100%! [test] '+[guid]::NewGuid())
$before=Get-ExecutionPolicy -List | Out-String
function Run-Child([string]$Program,[string]$Arguments,[string]$Directory){
    $info=New-Object Diagnostics.ProcessStartInfo;$info.FileName=$Program;$info.Arguments=$Arguments;$info.WorkingDirectory=$Directory;$info.UseShellExecute=$false;$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true;$info.CreateNoWindow=$true
    $info.EnvironmentVariables['PSExecutionPolicyPreference']='Restricted';$info.EnvironmentVariables['WINFORMS_ENV_NO_PAUSE']='1'
    $process=New-Object Diagnostics.Process;$process.StartInfo=$info
    try{[void]$process.Start();$output=$process.StandardOutput.ReadToEnd();$errorText=$process.StandardError.ReadToEnd();$process.WaitForExit();return [pscustomobject]@{Code=$process.ExitCode;Text=($output+$errorText)}}finally{$process.Dispose()}
}
try{
    [void][IO.Directory]::CreateDirectory((Join-Path $root 'Installers'))
    foreach($file in @('01_检测环境.bat','02_安装缺少组件.bat','Environment.cmd','README.md')){Copy-Item -LiteralPath (Join-Path (Join-Path $repo '环境准备') $file) -Destination (Join-Path $root $file)}
    Copy-Item -LiteralPath (Join-Path $repo '环境准备/Installers/README.md') -Destination (Join-Path $root 'Installers/README.md')
    if(@(Get-ChildItem -LiteralPath (Join-Path $root 'Installers') -File | Where-Object {$_.Name -ne 'README.md'}).Count -ne 0){throw 'Refuse to test a folder that contains installers'}
    $effective=Run-Child $native '-NoLogo -NoProfile -NonInteractive -Command "Get-ExecutionPolicy"' $root
    if($effective.Text.Trim() -ne 'Restricted'){Write-Output ('SKIP: organization policy overrides child Restricted test: '+$effective.Text.Trim());return}
    $control=Join-Path $root 'inert-control.ps1';[IO.File]::WriteAllText($control,"Write-Output 'INERT_FILE_EXECUTED'")
    $result=Run-Child $native ('-NoLogo -NoProfile -NonInteractive -File "'+$control+'"') $root
    if($result.Code -eq 0 -or $result.Text.Contains('INERT_FILE_EXECUTED')){throw ('Restricted control did not block PS1: '+$result.Text)}
    Remove-Item -LiteralPath $control
    Write-Output 'PASS native Restricted control: PS1 is blocked as in the reported screenshot'
    $check=Run-Child $env:ComSpec '/d /c 01_检测环境.bat' $root
    if($check.Code -ne 0 -or -not $check.Text.Contains('Read-only check complete') -or $check.Text.Contains('PSSecurityException')){throw ('Detection entry failed under Restricted: '+$check.Text)}
    if(Test-Path -LiteralPath (Join-Path $root 'Logs')){throw 'Read-only detection unexpectedly created logs'}
    Write-Output 'PASS native Restricted detection: Chinese, spaces, apostrophe, ampersand, percent and exclamation in folder'
    if($check.Text -match '\.NET Release:\s*(\d+)' -and [int]$matches[1] -ge 528040){
        $install=Run-Child $env:ComSpec '/d /c 02_安装缺少组件.bat' $root
        if(($install.Code -ne 0 -and $install.Code -ne 3010 -and -not ($install.Code -eq 1 -and $install.Text.Contains('Cannot read pending-restart'))) -or -not $install.Text.Contains('Nothing to install')){throw ('Already-ready install entry failed under Restricted: '+$install.Text)}
        if(Test-Path -LiteralPath (Join-Path $root 'Logs')){throw 'No-op install unexpectedly created logs'}
        Write-Output 'PASS native Restricted install entry: already-installed components do not launch any installer'
    }else{Write-Output 'SKIP no-op install check: this test host does not meet recommended .NET 4.8; no installer is run'}
    Remove-Item -LiteralPath (Join-Path $root 'Environment.cmd')
    $missing=Run-Child $env:ComSpec '/d /c 01_检测环境.bat' $root
    if($missing.Code -eq 0 -or -not $missing.Text.Contains('Environment.cmd is missing')){throw 'Missing helper must not search PATH or report success'}
    Write-Output 'PASS missing engine stops before commands or PATH fallback'
    if((Get-ExecutionPolicy -List | Out-String) -ne $before){throw 'Persistent or parent execution policy changed'}
    Write-Output 'PASS parent/machine/user execution-policy list is unchanged'
}finally{if(Test-Path -LiteralPath $root){Remove-Item -LiteralPath $root -Recurse -Force}}
