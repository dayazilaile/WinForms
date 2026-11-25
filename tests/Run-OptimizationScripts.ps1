$ErrorActionPreference = 'Stop'
$folder = Join-Path (Split-Path $PSScriptRoot -Parent) 'build/generated-optimization'
$script:passed = 0
function Assert-Optimization($condition, [string]$message) {
    if (-not $condition) { throw $message }
    $script:passed++
    Write-Output ('PASS ' + $message)
}
$read = [IO.File]::ReadAllText((Join-Path $folder 'appx-read.ps1'))
$setD = [IO.File]::ReadAllText((Join-Path $folder 'appx-d.ps1'))
$restore = [IO.File]::ReadAllText((Join-Path $folder 'appx-restore.ps1'))
foreach ($code in @($read,$setD,$restore)) {
    $tokens=$null; $errors=$null
    [void][Management.Automation.Language.Parser]::ParseInput($code,[ref]$tokens,[ref]$errors)
    Assert-Optimization ($errors.Count -eq 0) 'Exact exported Appx command parses'
}
$script:bs = [string][char]92
$script:dPath = 'D:' + $script:bs + 'WindowsApps'
$prefix = $script:bs + $script:bs + '?' + $script:bs + 'Volume{'
$script:c = [pscustomobject]@{ Name=($prefix+'12345678-1234-1234-1234-1234567890ab}'+$script:bs); PackageStorePath=('C:'+$script:bs+'Program Files'+$script:bs+'WindowsApps') }
$script:d = [pscustomobject]@{ Name=($prefix+'22345678-1234-1234-1234-1234567890ab}'+$script:bs); PackageStorePath=$script:dPath }
$script:volumes=@($script:c,$script:d); $script:current=$script:c; $script:adds=0; $script:sets=0; $script:denyAdd=$false
function Get-AppxDefaultVolume { return $script:current }
function Get-AppxVolume { return $script:volumes }
function Add-AppxVolume([string]$Path) {
    if ($Path -ne $script:dPath) { throw ('Unexpected install path: '+$Path) }
    if ($script:denyAdd) { throw 'Simulated access denied' }
    $script:adds++; $script:volumes+=@($script:d); return $script:d
}
function Set-AppxDefaultVolume($Volume) { $script:sets++; $script:current=$Volume }
$output = & ([scriptblock]::Create($read))
Assert-Optimization ($output -eq ('WF_VOLUME:'+$script:c.Name)) 'Default Appx query uses an explicit marker and original volume name'
& ([scriptblock]::Create($setD))
Assert-Optimization ($script:current.PackageStorePath -eq $script:dPath -and $script:adds -eq 0 -and $script:sets -eq 1) 'Existing D volume is selected without registering a duplicate'
& ([scriptblock]::Create($restore))
Assert-Optimization ($script:current.Name -eq $script:c.Name) 'Exact original volume is restored by validated name'
$script:volumes=@($script:c); $script:current=$script:c; $script:adds=0; $script:sets=0
& ([scriptblock]::Create($setD))
Assert-Optimization ($script:adds -eq 1 -and $script:sets -eq 1 -and $script:current.PackageStorePath -eq $script:dPath) 'Missing D volume is registered once before setting default'
$script:volumes=@($script:c,$script:d,$script:d); $script:sets=0; $failed=$false
try { & ([scriptblock]::Create($setD)) } catch { $failed=$true }
Assert-Optimization ($failed -and $script:sets -eq 0) 'Ambiguous D volumes cannot silently change default'
$script:volumes=@($script:c); $script:sets=0; $script:denyAdd=$true; $failed=$false
try { & ([scriptblock]::Create($setD)) } catch { $failed=$true }
Assert-Optimization ($failed -and $script:sets -eq 0) 'Volume registration failure leaves original default intact'
$script:volumes=@($script:d); $script:sets=0; $failed=$false
try { & ([scriptblock]::Create($restore)) } catch { $failed=$true }
Assert-Optimization ($failed -and $script:sets -eq 0) 'Missing original volume stops restore instead of guessing'
Write-Output ('OPTIMIZATION SCRIPT TOTAL PASS ' + $script:passed)
$global:LASTEXITCODE=0
