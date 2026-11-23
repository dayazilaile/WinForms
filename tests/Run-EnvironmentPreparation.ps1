# Harmless helper tests: no real registry changes, installers, elevation or reboot.
param([string]$ScratchDirectory = [IO.Path]::GetTempPath())
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo = Split-Path -Parent $repo
$helper = Join-Path $repo '环境准备'
. (Join-Path $helper 'Environment.Common.ps1')
$script:checks = 0
function Assert-Preparation {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw ('FAIL: ' + $Message) }
    $script:checks++
}
function Assert-PreparationThrows {
    param([scriptblock]$Action, [string]$Message)
    $threw = $false
    try { & $Action | Out-Null } catch { $threw = $true }
    Assert-Preparation $threw $Message
}
foreach ($case in @(
    @('6.1.7601',1,1,'x86',$true,'Win7AndW2K8R2-KB3191566-x86.msu'),
    @('6.1.7601',1,1,'x64',$true,'Win7AndW2K8R2-KB3191566-x64.msu'),
    @('6.1.7601',1,3,'x64',$true,'Win7AndW2K8R2-KB3191566-x64.msu'),
    @('6.1.7600',0,1,'x64',$false,''),
    @('6.0.6002',2,3,'x64',$false,''),
    @('6.2.9200',0,3,'x64',$true,'W2K12-KB3191565-x64.msu'),
    @('6.2.9200',0,1,'x64',$false,''),
    @('6.3.9600',0,1,'x86',$true,'Win8.1AndW2K12R2-KB3191564-x86.msu'),
    @('6.3.9600',0,3,'x64',$true,'Win8.1AndW2K12R2-KB3191564-x64.msu'),
    @('10.0.10240',0,1,'x64',$false,''),
    @('10.0.14393',0,3,'x64',$true,''),
    @('10.0.19045',0,1,'x64',$true,''),
    @('10.0.26100',0,1,'x64',$true,''),
    @('10.0.26100',0,3,'x64',$true,''),
    @('10.0.26100',0,1,'ARM64',$false,''),
    @('6.1.7601',1,3,'x86',$false,''),
    @('10.0.19045',0,0,'x64',$false,'')
)) {
    $p = Get-PreparationPlatform ([version]$case[0]) $case[1] $case[2] $case[3]
    Assert-Preparation ($p.Supported -eq $case[4] -and $p.WmfPackage -eq $case[5]) ('platform ' + ($case -join ','))
}
$win7 = Get-PreparationPlatform ([version]'6.1.7601') 1 1 'x64'
$win10 = Get-PreparationPlatform ([version]'10.0.19045') 0 1 'x64'
$plan = Get-PreparationPlan $win7 0 ([version]'2.0') $false
Assert-Preparation ($plan.CanInstall -and $plan.NeedNet -and $plan.NeedWmf -and -not $plan.WmfManual) 'stock Win7 bootstrap'
$plan = Get-PreparationPlan $win7 379893 ([version]'2.0') $false
Assert-Preparation $plan.NeedNet '4.5.2 to 4.8 is recommended, not skipped'
$plan = Get-PreparationPlan $win7 528040 ([version]'5.1') $false
Assert-Preparation (-not $plan.NeedNet -and -not $plan.NeedWmf) 'skip installed components'
$plan = Get-PreparationPlan $win10 533320 ([version]'5.1') $false
Assert-Preparation (-not $plan.NeedNet) 'never downgrade 4.8.1'
$plan = Get-PreparationPlan $win10 528040 ([version]'5.0') $false
Assert-Preparation $plan.WmfManual 'never apply old WMF on Win10'
$plan = Get-PreparationPlan $win7 0 ([version]'2.0') $true
Assert-Preparation (-not $plan.CanInstall) 'pending restart blocks install'

$temp = Join-Path $ScratchDirectory ('environment O''Reilly &100%! [test] ' + [Guid]::NewGuid())
[void][IO.Directory]::CreateDirectory($temp)
try {
    $script:signatureStatus = 'Valid'; $script:signatureSubject = 'CN=Microsoft Corporation, O=Microsoft Corporation, C=US'
    function Get-AuthenticodeSignature {
        param([string]$FilePath)
        Assert-Preparation ($FilePath.Contains('`[test`]')) 'PS2 wildcard escaping for signature path'
        New-Object PSObject -Property @{
            Status=$script:signatureStatus
            SignerCertificate=(New-Object PSObject -Property @{ Subject=$script:signatureSubject })
        }
    }
    Assert-PreparationThrows { Get-PreparationInstaller $temp 'missing.exe' } 'missing installer rejected'
    Assert-PreparationThrows { Get-PreparationInstaller $temp '../wrong.exe' } 'path traversal rejected'
    $exe = Join-Path $temp 'ndp48-x86-x64-allos-enu.exe'
    [IO.File]::WriteAllText($exe, 'INERT TEST FIXTURE - NEVER EXECUTE')
    Assert-Preparation ((Get-PreparationInstaller $temp ([IO.Path]::GetFileName($exe))) -eq $exe) 'valid Microsoft signer accepted'
    $script:signatureStatus = 'NotSigned'
    Assert-PreparationThrows { Get-PreparationInstaller $temp ([IO.Path]::GetFileName($exe)) } 'unsigned executable rejected'
    $script:signatureStatus = 'HashMismatch'
    Assert-PreparationThrows { Get-PreparationInstaller $temp ([IO.Path]::GetFileName($exe)) } 'tampered executable rejected'
    $script:signatureStatus = 'Valid'; $script:signatureSubject = 'CN=Microsoft Corporation, O=Someone Else, C=US'
    Assert-PreparationThrows { Get-PreparationInstaller $temp ([IO.Path]::GetFileName($exe)) } 'misleading publisher common name rejected'
    $msu = Join-Path $temp 'Win7AndW2K8R2-KB3191566-x64.msu'
    [IO.File]::WriteAllText($msu, 'INERT MSU FIXTURE')
    Assert-Preparation ((Get-PreparationInstaller $temp ([IO.Path]::GetFileName($msu))) -eq $msu) 'MSU delegated to WUSA'
    $bad = Join-Path $temp 'wrong.zip'; [IO.File]::WriteAllText($bad, 'not an installer')
    Assert-PreparationThrows { Get-PreparationInstaller $temp 'wrong.zip' } 'ZIP not executed'

    $computer = New-Object PSObject -Property @{ HostName='学生机01'; Mac='00:11:22:33:44:55'; IP='192.168.1.2'; OS='Test Windows'; User="lab\user`tbad`r`nrow" }
    $log = Join-Path $temp 'environment-install.tsv'
    $script:launches = 0; $script:exitCode = 0
    function Get-PreparationWusa { return 'MOCK-WUSA-NOT-EXECUTABLE' }
    function Start-Process {
        param([string]$FilePath, [string]$ArgumentList, [switch]$Wait, [switch]$PassThru)
        $script:launches++
        Assert-Preparation ($Wait -and $PassThru -and $ArgumentList.Contains('/norestart')) 'wait for installer and suppress restart'
        Assert-Preparation (-not $ArgumentList.Contains('Bypass') -and -not $ArgumentList.Contains('Hidden')) 'no policy bypass or hidden process'
        if ($FilePath -eq 'MOCK-WUSA-NOT-EXECUTABLE') {
            Assert-Preparation ($ArgumentList.StartsWith('"' + $msu + '" ') -and $ArgumentList.Contains('/quiet')) 'exact MSU path and WUSA arguments'
        } else {
            Assert-Preparation ($FilePath -eq $exe -and $ArgumentList.Contains('/passive') -and $ArgumentList.Contains($temp)) 'NET installer detail log stays beside helper'
        }
        $p = New-Object PSObject -Property @{ ExitCode=$script:exitCode }
        $p | Add-Member -MemberType ScriptMethod -Name Dispose -Value {}
        return $p
    }
    foreach ($case in @(@(0,0), @(3010,3010), @(1641,3010), @(2359302,0))) {
        $script:exitCode = $case[0]
        $result = Invoke-PreparationInstaller $msu $log $computer
        Assert-Preparation ($result -eq $case[1]) ('installer result ' + $case[0])
    }
    $script:exitCode = 0
    Assert-Preparation ((Invoke-PreparationInstaller $exe $log $computer) -eq 0) 'NET success'
    $script:exitCode = 1625
    Assert-PreparationThrows { Invoke-PreparationInstaller $msu $log $computer } 'failed installer not reported as success'
    $script:exitCode = 0
    $before = $script:launches
    $blocked = Join-Path $exe 'cannot-create.tsv'
    Assert-PreparationThrows { Invoke-PreparationInstaller $msu $blocked $computer } 'unwritable log stops installer'
    Assert-Preparation ($script:launches -eq $before) 'no installer launched when portable logging fails'
    $lines = [IO.File]::ReadAllLines($log)
    Assert-Preparation ($lines.Length -eq 13) 'one header, begin/end for each mocked install'
    foreach ($line in $lines) { Assert-Preparation (($line -split "`t").Count -eq 8) 'TSV columns survive control characters' }
    Assert-Preparation ($lines[1].Contains('学生机01') -and $lines[1].Contains('00:11:22:33:44:55') -and $lines[1].Contains('192.168.1.2')) 'computer identity retained'
    Assert-Preparation ($lines[1] -match '^\d{4}-\d\d-\d\dT.+[+-]\d\d:\d\d\t') 'timestamp includes timezone'
} finally { [IO.Directory]::Delete($temp, $true) }

foreach ($file in Get-ChildItem -LiteralPath $helper -Filter *.ps1) {
    $tokens = $null; $errors = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$tokens, [ref]$errors)
    Assert-Preparation ($errors.Count -eq 0) ('PowerShell parse ' + $file.Name)
    $bytes = [IO.File]::ReadAllBytes($file.FullName)
    Assert-Preparation ($bytes[0] -eq 239 -and $bytes[1] -eq 187 -and $bytes[2] -eq 191) ('UTF8 BOM for legacy PowerShell ' + $file.Name)
    $source = [IO.File]::ReadAllText($file.FullName)
    Assert-Preparation ($source -notmatch '\$PSScriptRoot|\[pscustomobject\]|::new\(|Get-CimInstance|Get-FileHash|ExecutionPolicy\s+Bypass|WindowStyle\s+Hidden') ('avoid newer bootstrap APIs / policy changes ' + $file.Name)
}
Write-Output ('PASS environment preparation: ' + $script:checks + ' checks; all installers mocked.')
