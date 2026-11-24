# Execute the literal commands shipped in Environment.cmd, using harmless substitutes.
param([string]$ScratchDirectory=[IO.Path]::GetTempPath())
$ErrorActionPreference='Stop'
$repo=Split-Path (Split-Path $MyInvocation.MyCommand.Path -Parent) -Parent
$helper=Join-Path $repo '环境准备';$engine=[IO.File]::ReadAllText((Join-Path $helper 'Environment.cmd'));$script:checks=0
function Check([bool]$Value,[string]$Message){if(-not $Value){throw ('FAIL: '+$Message)};$script:checks++;Write-Output ('PASS '+$Message)}
function Inline([string]$Name){$result='';$prefix='%'+$Name+'%';foreach($line in $engine -split '\r?\n'){if($line -match ('^set "'+[regex]::Escape($Name)+'=(.*)"$')){$part=$matches[1];if($part.StartsWith($prefix)){$result+=$part.Substring($prefix.Length)}else{$result=$part}}};if(-not $result){throw $Name};return $result}
$probe=Inline 'PREP_PROBE';$verify=Inline 'PREP_VERIFY';$logger=Inline 'PREP_LOG';$admin=Inline 'PREP_ADMIN'
foreach($code in @($probe,$verify,$logger,$admin)){
    $tokens=$null;$errors=$null;[void][Management.Automation.Language.Parser]::ParseInput($code,[ref]$tokens,[ref]$errors)
    Check ($errors.Count -eq 0 -and $code.Length -lt 6500) 'Shipped command parses and fits Windows command limits'
    Check ($code -notmatch '\$PSScriptRoot|\[pscustomobject\]|::new\(|Get-CimInstance|Get-FileHash|FromBase64String|Invoke-Expression|ScriptBlock') 'Shipped command avoids newer bootstrap APIs and dynamic loading'
}
Check ($engine -notmatch '(?i)-File\s|ExecutionPolicy|WindowStyle|EncodedCommand') 'No PS1 execution or execution-policy changes in the production engine'
Check (@(Get-ChildItem -LiteralPath $helper -Filter *.ps1).Count -eq 0) 'Legacy PS1 entry files are removed'
foreach($name in @('01_检测环境.bat','02_安装缺少组件.bat')){$text=[IO.File]::ReadAllText((Join-Path $helper $name));Check ($text.Contains('call ".\Environment.cmd" --') -and $text.Contains('pushd "%~dp0"') -and $text -notmatch '(?i)-File\s|ExecutionPolicy') ('Batch entry uses its own folder: '+$name)}
$temp=Join-Path $ScratchDirectory ('environment-inline-'+[guid]::NewGuid());[void][IO.Directory]::CreateDirectory((Join-Path $temp 'System32'))
$saved=@{};foreach($key in @('SystemRoot','PROCESSOR_ARCHITECTURE','PROCESSOR_ARCHITEW6432','PREP_TEST_REG_64','PREP_TEST_REG_32','PREP_TEST_REG_native','PREP_PACKAGE_PATH','PREP_LOG_PATH','PREP_PACKAGE_NAME','PREP_STAGE','PREP_INSTALL_CODE')){$saved[$key]=[Environment]::GetEnvironmentVariable($key,'Process')}
try{
    $reg=Join-Path $temp 'System32/reg.exe'
    if($env:OS -eq 'Windows_NT'){
        $src=Join-Path $temp 'reg.cs';[IO.File]::WriteAllText($src,@'
using System;
class FakeRegistry {static int Main(string[] args){string view="native";foreach(string a in args)if(a.StartsWith("/reg:"))view=a.Substring(5);string v=Environment.GetEnvironmentVariable("PREP_TEST_REG_"+view);if(String.IsNullOrEmpty(v)||v=="missing")return 1;Console.WriteLine("    Release    "+(v=="wrongtype"?"REG_SZ":"REG_DWORD")+"    "+v);return 0;}}
'@)
        $compiler=Join-Path $saved['SystemRoot'] 'Microsoft.NET/Framework64/v4.0.30319/csc.exe';if(-not (Test-Path -LiteralPath $compiler)){$compiler=Join-Path $saved['SystemRoot'] 'Microsoft.NET/Framework/v4.0.30319/csc.exe'}
        & $compiler /nologo "/out:$reg" $src | Out-Null;if($LASTEXITCODE -ne 0){throw 'Fake registry compilation failed'}
    }else{
        [IO.File]::WriteAllText($reg,@'
#!/bin/sh
view=native
for arg in "$@"; do case "$arg" in /reg:64) view=64;; /reg:32) view=32;; esac; done
case "$view" in 64) v=$PREP_TEST_REG_64;; 32) v=$PREP_TEST_REG_32;; *) v=$PREP_TEST_REG_native;; esac
case "$v" in ''|missing) exit 1;; wrongtype) printf '    Release    REG_SZ    wrongtype\n';; *) printf '    Release    REG_DWORD    %s\n' "$v";; esac
'@)
        & /bin/chmod +x $reg;if($LASTEXITCODE -ne 0){throw 'Could not prepare fake registry'}
    }
    $env:SystemRoot=$temp
    function Run-Probe($Case){
        function Get-WmiObject {param($Class)if($Case.Fail){throw 'mock WMI denied'};return [pscustomobject]@{Version=$Case.V;ProductType=$Case.Type;ServicePackMajorVersion=$Case.SP}}
        function Test-Path {param($LiteralPath)if($Case.Pending -eq 2){throw 'mock denied'};return $Case.Pending -eq 1}
        function Get-ItemProperty {param($LiteralPath)return [pscustomobject]@{PendingFileRenameOperations=$Case.Rename}}
        $old=$PSVersionTable.PSVersion;try{$PSVersionTable.PSVersion=[version]$Case.PS;$env:PROCESSOR_ARCHITECTURE=$Case.Arch;$env:PROCESSOR_ARCHITEW6432=$null;$env:PREP_TEST_REG_64=$Case.R64;$env:PREP_TEST_REG_32=$Case.R32;$env:PREP_TEST_REG_native=$Case.RNative;return (& ([scriptblock]::Create($probe)) | Out-String).Trim()}finally{$PSVersionTable.PSVersion=$old}
    }
    foreach($c in @(
        @('6.1.7601',1,1,'AMD64','2.0','missing','0x5c615','missing',0,378389,1,'win7-x64',1,1),
        @('6.1.7601',1,1,'x86','5.1','528040','missing','missing',0,528040,1,'win7-x86',0,0),
        @('6.1.7601',1,3,'AMD64','2.0','528040','missing','missing',0,528040,1,'win7-x64',0,1),
        @('6.1.7600',0,1,'AMD64','2.0','missing','missing','missing',0,0,0,'none',1,1),
        @('6.2.9200',0,3,'AMD64','4.0','528040','missing','missing',0,528040,1,'server2012-x64',0,1),
        @('6.2.9200',0,1,'AMD64','4.0','528040','missing','missing',0,528040,0,'none',0,1),
        @('6.3.9600',0,1,'x86','4.0','528040','missing','missing',0,528040,1,'win81-x86',0,1),
        @('6.3.9600',0,3,'AMD64','5.1','528040','missing','missing',0,528040,1,'win81-x64',0,0),
        @('10.0.10240',0,1,'AMD64','5.0','378389','missing','missing',0,378389,0,'none',1,1),
        @('10.0.26100',0,1,'AMD64','5.1','533320','missing','missing',1,533320,1,'none',0,0),
        @('10.0.19045',0,1,'AMD64','5.1','378389','533320','missing',2,533320,1,'none',0,0),
        @('10.0.19045',0,1,'AMD64','5.1','missing','missing','528040',0,528040,1,'none',0,0),
        @('10.0.19045',0,1,'ARM64','5.1','528040','missing','missing',0,528040,0,'none',0,0),
        @('10.0.19045',0,1,'unexpected|arch','5.1','528040','missing','missing',0,528040,0,'none',0,0),
        @('6.1.7601',1,3,'x86','2.0','wrongtype','garbage','missing',0,0,0,'none',1,1)
    )){
        $row=(Run-Probe @{V=$c[0];SP=$c[1];Type=$c[2];Arch=$c[3];PS=$c[4];R64=$c[5];R32=$c[6];RNative=$c[7];Pending=$c[8]}) -split '\|'
        Check ($row.Count -eq 15 -and $row[0] -eq 'PREP_META' -and $row[9] -eq [string]$c[8] -and $row[10] -eq [string]$c[9] -and $row[11] -eq [string]$c[10] -and $row[12] -eq $c[11] -and $row[13] -eq [string]$c[12] -and $row[14] -eq [string]$c[13]) ('Exact shipped probe: '+($c[0..4] -join '/'))
    }
    $renameCase=@{V='10.0.19045';SP=0;Type=1;Arch='AMD64';PS='5.1';R64='528040';R32='missing';RNative='missing';Pending=0;Rename=@('old-path','')}
    Check (((Run-Probe $renameCase) -split '\|')[9] -eq '1') 'Actual pending file-renames block installation'
    $renameCase.Rename=@('','');Check (((Run-Probe $renameCase) -split '\|')[9] -eq '0') 'Empty file-rename entries do not invent a pending restart'
    Check ((Run-Probe @{Fail=$true;PS='5.1'}) -eq 'PREP_PROBE_ERROR') 'Probe read failure never invents an installed or missing version'
    foreach($key in @('SystemRoot','PROCESSOR_ARCHITECTURE','PROCESSOR_ARCHITEW6432')){[Environment]::SetEnvironmentVariable($key,$saved[$key],'Process')}
    $exe=(Get-Process -Id $PID).Path;$env:PREP_PACKAGE_PATH=Join-Path $temp "O'Reilly &100%! [test]/setup.exe"
    foreach($c in @(@('Valid','Microsoft Corporation',0),@('NotSigned','Microsoft Corporation',12),@('HashMismatch','Microsoft Corporation',12),@('Valid','Other Company',12))){
        $prefix="function Get-AuthenticodeSignature {param(`$FilePath) if(-not `$FilePath.Contains('``[test``]')){throw 'path escaping lost'};[pscustomobject]@{Status='"+$c[0]+"';SignerCertificate=[pscustomobject]@{Subject='CN=Microsoft Corporation, O="+$c[1]+", C=US'}}}; "
        $output=& $exe -NoLogo -NoProfile -NonInteractive -Command ($prefix+$verify) 2>&1;$exitCode=$LASTEXITCODE;Check ($exitCode -eq $c[2]) ('Exact certificate check: '+$c[0]+'/'+$c[1])
    }
    $env:PREP_LOG_PATH=Join-Path $temp "O'Reilly &100%! [test]/Logs/environment.tsv";$env:PREP_PACKAGE_NAME="package`tline`r`n.exe";$env:PREP_STAGE='begin';$env:PREP_INSTALL_CODE='not-started'
    $prefix="function Get-WmiObject {param(`$Class,`$Filter) if(`$Class -eq 'Win32_OperatingSystem'){return [pscustomobject]@{Caption='Test OS'}};return [pscustomobject]@{MACAddress='00:11:22:33:44:55';IPAddress=@('192.0.2.2','2001:db8::2')}}; "
    $output=& $exe -NoLogo -NoProfile -NonInteractive -Command ($prefix+$logger) 2>&1;Check ($LASTEXITCODE -eq 0) 'Exact logger writes to the portable path'
    $env:PREP_STAGE='end';$env:PREP_INSTALL_CODE='3010';$output=& $exe -NoLogo -NoProfile -NonInteractive -Command ($prefix+$logger) 2>&1;$exitCode=$LASTEXITCODE;$rows=[IO.File]::ReadAllLines($env:PREP_LOG_PATH)
    Check ($exitCode -eq 0 -and $rows.Count -eq 3 -and @($rows | Where-Object {($_ -split "`t").Count -ne 8}).Count -eq 0) 'Begin/end log rows retain eight columns despite input control characters'
    Check ($rows[1].Contains('00:11:22:33:44:55') -and $rows[1].Contains('192.0.2.2') -and $rows[1] -match '^\d{4}-\d\d-\d\dT.+[+-]\d\d:\d\d\t') 'Log rows include NIC identity and a timezone-qualified timestamp'
    $unread="function Get-WmiObject {param(`$Class,`$Filter) if(`$Class -eq 'Win32_OperatingSystem'){return [pscustomobject]@{Caption='Test OS'}};throw 'mock NIC denied'}; "
    $output=& $exe -NoLogo -NoProfile -NonInteractive -Command ($unread+$logger) 2>&1
    Check ($LASTEXITCODE -eq 0 -and [IO.File]::ReadAllLines($env:PREP_LOG_PATH)[3].Contains("`tunread`tunread`t")) 'Unreadable NIC identity is explicit and never fabricated'
    $blocker=Join-Path $temp 'not-a-directory';[IO.File]::WriteAllText($blocker,'fixture');$env:PREP_LOG_PATH=Join-Path $blocker 'cannot-write.tsv';$output=& $exe -NoLogo -NoProfile -NonInteractive -Command ($prefix+$logger) 2>&1;Check ($LASTEXITCODE -eq 14) 'Unwritable portable logging reports failure before any installer'
}finally{foreach($key in $saved.Keys){[Environment]::SetEnvironmentVariable($key,$saved[$key],'Process')};[IO.Directory]::Delete($temp,$true)}
Write-Output ('PASS environment preparation: '+$script:checks+' checks; no installers executed.')
