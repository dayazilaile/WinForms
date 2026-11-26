$ErrorActionPreference='Stop'
$base=Join-Path (Split-Path $PSScriptRoot -Parent) 'build/generated-stop-actions'
$script:passed=0
function Assert-Stop($condition,[string]$message){if(-not $condition){throw $message};$script:passed++;Write-Output ('PASS '+$message)}
function Expect-StopFailure([scriptblock]$action,[string]$message){$failed=$false;try{& $action}catch{$failed=$true};Assert-Stop $failed $message}
foreach($file in @('helper.ps1','SendInput.ps1','Legacy.ps1','rename-helper.ps1','rename-SendInput.ps1','rename-Legacy.ps1')){
 $tokens=$null;$errors=$null;$text=[IO.File]::ReadAllText((Join-Path $base $file));[void][Management.Automation.Language.Parser]::ParseInput($text,[ref]$tokens,[ref]$errors);Assert-Stop ($errors.Count -eq 0) ('Exact generated stop/rename script parses: '+$file)
}
. (Join-Path $base 'helper.ps1')
function New-StopService([string]$name,[string]$state='Running'){
 $row=[pscustomobject]@{ServiceName=$name;Status=$state;CanStop=$true;DependentServices=@();Disposed=0;NoStop=$false;Restart=$false}
 $row | Add-Member ScriptMethod Refresh {}
 $row | Add-Member ScriptMethod Dispose {$this.Disposed++}
 $row | Add-Member ScriptMethod WaitForStatus {param($wanted,$timeout);if($timeout.TotalSeconds -lt 1 -or $timeout.TotalSeconds -gt 120){throw 'bad timeout'};if($this.NoStop){throw 'mock timeout'};if($this.Restart){$this.Status='Running'}else{$this.Status='Stopped'}}
 return $row
}
$script:services=@();$script:controls=0;$script:denied=$false
function Get-ITLocalStopServices {return $script:services}
function Invoke-ITServiceStopControl([string]$name){$script:controls++;if($script:denied){throw 'mock denied'};foreach($s in $script:services){if($s.ServiceName -eq $name){$s.Status='StopPending'}}}
$one=New-StopService 'DemoSvc';$other=New-StopService 'DemoSvcSuffix';$script:services=@($one,$other)
Invoke-ITStopService 'DemoSvc' 30
Assert-Stop ($one.Status -eq 'Stopped' -and $other.Status -eq 'Running' -and $script:controls -eq 1 -and $one.Disposed -gt 0 -and $other.Disposed -gt 0) 'Service stop matches internal name exactly and disposes controllers'
$one=New-StopService 'DemoSvc' 'Stopped';$script:services=@($one);$script:controls=0
Invoke-ITStopService 'DemoSvc' 30
Assert-Stop ($script:controls -eq 0) 'Already stopped service is idempotent without native control'
Expect-StopFailure {Invoke-ITStopService 'MissingSvc' 30} 'Missing service is a failure rather than an invented stopped state'
$one=New-StopService 'DemoSvc';$dependency=New-StopService 'OtherSvc';$one.DependentServices=@($dependency);$script:services=@($one);$script:controls=0
Expect-StopFailure {Invoke-ITStopService 'DemoSvc' 30} 'Running dependent service blocks stop without cascading'
Assert-Stop ($script:controls -eq 0 -and $dependency.Status -eq 'Running') 'Dependent service remains unchanged'
$one=New-StopService 'DemoSvc';$one.CanStop=$false;$script:services=@($one)
Expect-StopFailure {Invoke-ITStopService 'DemoSvc' 30} 'CanStop=false is not overridden'
$one=New-StopService 'DemoSvc' 'StopPending';$script:services=@($one);$script:controls=0
Invoke-ITStopService 'DemoSvc' 5
Assert-Stop ($script:controls -eq 0 -and $one.Status -eq 'Stopped') 'Existing stop-pending state waits for actual stop'
$one=New-StopService 'DemoSvc';$one.NoStop=$true;$script:services=@($one)
Expect-StopFailure {Invoke-ITStopService 'DemoSvc' 1} 'Service timeout propagates failure'
$one=New-StopService 'DemoSvc';$one.Restart=$true;$script:services=@($one)
Expect-StopFailure {Invoke-ITStopService 'DemoSvc' 5} 'Service that restarts is not reported stopped'
$script:denied=$true;$script:services=@((New-StopService 'DemoSvc'))
Expect-StopFailure {Invoke-ITStopService 'DemoSvc' 5} 'Native stop access failure propagates';$script:denied=$false
$before=$script:controls
Expect-StopFailure {Invoke-ITStopService 'RpcSs' 30} 'Runtime also rejects critical services'
Expect-StopFailure {Invoke-ITStopService 'DemoSvc' 0} 'Runtime validates service timeout before mutation'
Assert-Stop ($before -eq $script:controls) 'Rejected critical/invalid service action makes no control request'
$proc=[Diagnostics.Process]::GetCurrentProcess();try{$script:session=$proc.SessionId;$script:own=$proc.Id}finally{$proc.Dispose()}
function New-StopProcess([int]$number,[bool]$window=$true){
 $row=[pscustomobject]@{Id=$number;ProcessName='Demo';SessionId=$script:session;HasExited=$false;MainWindowHandle=[IntPtr]::Zero;Opened=0;Killed=0;Closed=0;Disposed=0;NoHandle=$false;DenyKill=$false;Stay=$false;LastWait=0}
 if($window){$row.MainWindowHandle=[IntPtr]1}
 $row | Add-Member ScriptProperty Handle {if($this.NoHandle){throw 'mock handle denied'};$this.Opened++;return [IntPtr]123}
 $row | Add-Member ScriptMethod Dispose {$this.Disposed++}
 $row | Add-Member ScriptMethod Kill {$this.Killed++;if($this.DenyKill){throw 'mock kill denied'};$this.HasExited=$true}
 $row | Add-Member ScriptMethod CloseMainWindow {$this.Closed++;if(-not $this.Stay){$this.HasExited=$true};return $true}
 $row | Add-Member ScriptMethod WaitForExit {param($millis);$this.LastWait=$millis;return $this.HasExited}
 return $row
}
$script:processes=@()
function Get-ITNamedStopProcesses([string]$name){return $script:processes}
$a=New-StopProcess 500001;$b=New-StopProcess 500002;$foreign=New-StopProcess 500003;$foreign.SessionId=$script:session+1;$wrong=New-StopProcess 500004;$wrong.ProcessName='DemoOther';$script:processes=@($a,$b,$foreign,$wrong)
Invoke-ITEndProcess 'Demo' '正常关闭' 15
Assert-Stop ($a.Closed -eq 1 -and $b.Closed -eq 1 -and $a.Killed -eq 0 -and $foreign.Closed -eq 0 -and $wrong.Closed -eq 0 -and $a.Opened -gt 0) 'Graceful name match handles only current-session exact instances with retained handles'
$a=New-StopProcess 500001;$a.Stay=$true;$script:processes=@($a)
Expect-StopFailure {Invoke-ITEndProcess 'Demo' '正常关闭' 1} 'Unaccepted/unsaved graceful exit remains failure'
Assert-Stop ($a.Killed -eq 0 -and $a.LastWait -le 1000) 'Graceful timeout never silently escalates to kill and uses bounded budget'
$a=New-StopProcess 500001 $false;$script:processes=@($a)
Expect-StopFailure {Invoke-ITEndProcess 'Demo' '正常关闭' 15} 'Headless process requires explicit force mode'
Invoke-ITEndProcess 'Demo' '强制结束' 15
Assert-Stop ($a.Killed -eq 1 -and $a.HasExited) 'Explicit force terminates the held selected instance'
$a=New-StopProcess 500001;$b=New-StopProcess 500002;$b.NoHandle=$true;$script:processes=@($a,$b)
Expect-StopFailure {Invoke-ITEndProcess 'Demo' '强制结束' 15} 'Failed preflight handle stops all termination requests'
Assert-Stop ($a.Killed -eq 0 -and $a.Disposed -gt 0) 'Earlier preflight handles close without killing on later preflight failure'
$a=New-StopProcess $script:own;$script:processes=@($a)
Expect-StopFailure {Invoke-ITEndProcess 'Demo' '强制结束' 15} 'Renamed script host is still protected by current instance identity'
$a=New-StopProcess 500001;$a.DenyKill=$true;$script:processes=@($a)
Expect-StopFailure {Invoke-ITEndProcess 'Demo' '强制结束' 15} 'Force access failure is not treated as successful exit'
$script:processes=@();Invoke-ITEndProcess 'Demo' '正常关闭' 15
Assert-Stop $true 'Absent process name is idempotent'
Expect-StopFailure {Invoke-ITEndProcess 'powershell' '强制结束' 15} 'Runtime rejects critical/runner process names'
Expect-StopFailure {Invoke-ITEndProcess 'Demo' '强制结束' 121} 'Runtime rejects unbounded process wait'
. (Join-Path $base 'rename-helper.ps1')
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('workflow-rename-'+[Guid]::NewGuid())
New-Item -ItemType Directory -Path $fixture | Out-Null
$savedBat=$env:IT_TOOLKIT_BAT
try{
 $old=Join-Path $fixture 'old[1].txt';$target=Join-Path $fixture 'new.txt';[IO.File]::WriteAllText($old,'payload')
 Invoke-ITRenameItem $old 'new.txt' $false
 Assert-Stop ((Test-Path -LiteralPath $target) -and -not(Test-Path -LiteralPath $old) -and [IO.File]::ReadAllText($target) -eq 'payload') 'File rename is literal and preserves bytes without moving parent'
 Invoke-ITRenameItem $target 'new.txt' $false
 Assert-Stop ([IO.File]::ReadAllText($target) -eq 'payload') 'Already named file is idempotent'
 $collision=Join-Path $fixture 'collision.txt';[IO.File]::WriteAllText($collision,'keep')
 Expect-StopFailure {Invoke-ITRenameItem $target 'collision.txt' $false} 'Existing destination cannot be overwritten'
 Assert-Stop ([IO.File]::ReadAllText($target) -eq 'payload' -and [IO.File]::ReadAllText($collision) -eq 'keep') 'Collision preserves source and destination contents'
 $dir=Join-Path $fixture 'old-folder';New-Item -ItemType Directory -Path $dir | Out-Null;[IO.File]::WriteAllText((Join-Path $dir 'child.txt'),'child')
 Invoke-ITRenameItem $dir 'new-folder' $true
 Assert-Stop ([IO.File]::ReadAllText((Join-Path (Join-Path $fixture 'new-folder') 'child.txt')) -eq 'child') 'Folder rename preserves child data'
 Expect-StopFailure {Invoke-ITRenameItem $target 'dir' $true} 'File/folder type mismatch makes no change'
 Expect-StopFailure {Invoke-ITRenameItem $target '../escape' $false} 'Rename cannot escape parent with new-name traversal'
 Expect-StopFailure {Invoke-ITRenameItem $target 'NUL.txt' $false} 'Runtime also rejects reserved Windows device names'
 $env:IT_TOOLKIT_BAT=$target
 Expect-StopFailure {Invoke-ITRenameItem $target 'self.txt' $false} 'Current script path cannot rename itself'
 Expect-StopFailure {Invoke-ITRenameItem $fixture 'parent-renamed' $true} 'Current script parent cannot be renamed'
}finally{if($null -eq $savedBat){Remove-Item Env:IT_TOOLKIT_BAT -ErrorAction SilentlyContinue}else{$env:IT_TOOLKIT_BAT=$savedBat};Remove-Item -LiteralPath $fixture -Recurse -Force}
Write-Output ('AUTOMATION STOP SCRIPT TOTAL PASS '+$script:passed)
$global:LASTEXITCODE=0
