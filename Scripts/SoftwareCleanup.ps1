function Show-SWResult([string]$text,[bool]$ok) {
 Add-Type -AssemblyName System.Windows.Forms
 $icon=if($ok){[Windows.Forms.MessageBoxIcon]::Information}else{[Windows.Forms.MessageBoxIcon]::Warning}
 [void][Windows.Forms.MessageBox]::Show($text,'标准软件清理结果',[Windows.Forms.MessageBoxButtons]::OK,$icon)
}
function Confirm-SWRemoval([string]$text) {
 Add-Type -AssemblyName System.Windows.Forms
 return [Windows.Forms.MessageBox]::Show($text,'确认批量卸载',[Windows.Forms.MessageBoxButtons]::YesNo,[Windows.Forms.MessageBoxIcon]::Question,[Windows.Forms.MessageBoxDefaultButton]::Button2) -eq [Windows.Forms.DialogResult]::Yes
}
function Test-SWAdmin {
 $id=[Security.Principal.WindowsIdentity]::GetCurrent()
 return (New-Object Security.Principal.WindowsPrincipal($id)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}
function Read-SWRecord([string]$hive,[string]$view,[string]$keyName) {
 $base=$null; $key=$null
 try {
  $base=[Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]$hive,[Microsoft.Win32.RegistryView]$view)
  $key=$base.OpenSubKey('SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\'+$keyName)
  if($null -eq $key){return $null}
  $name=[string]$key.GetValue('DisplayName',''); if([string]::IsNullOrWhiteSpace($name)){return $null}
  return [pscustomobject]@{Name=$name.Trim();Publisher=([string]$key.GetValue('Publisher','')).Trim();Hive=$hive;View=$view;Key=$keyName;Command=[string]$key.GetValue('UninstallString','');Quiet=[string]$key.GetValue('QuietUninstallString','');Msi=([string]$key.GetValue('WindowsInstaller','0') -eq '1');NoRemove=([string]$key.GetValue('NoRemove','0') -eq '1');System=([string]$key.GetValue('SystemComponent','0') -eq '1');Parent=[string]$key.GetValue('ParentKeyName','');Release=[string]$key.GetValue('ReleaseType','')}
 } finally {if($null -ne $key){$key.Dispose()};if($null -ne $base){$base.Dispose()}}
}
function Read-SWInventory([bool]$machineOnly=$false) {
 foreach($hive in @('LocalMachine','CurrentUser')) {if($machineOnly -and $hive -ne 'LocalMachine'){continue};foreach($view in @('Registry32','Registry64')) {
  if($view -eq 'Registry64' -and -not [Environment]::Is64BitOperatingSystem){continue}
  $base=$null; $key=$null
  try {$base=[Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]$hive,[Microsoft.Win32.RegistryView]$view);$key=$base.OpenSubKey('SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall');if($null -eq $key){continue};$names=@($key.GetSubKeyNames())}
  finally {if($null -ne $key){$key.Dispose()};if($null -ne $base){$base.Dispose()}}
  foreach($keyName in $names){$row=Read-SWRecord $hive $view $keyName;if($null -ne $row){$row}}
 }}
}
function Find-SWExtras([object[]]$rows,[object[]]$rules) {
 $exact=New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
 $any=New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
 foreach($rule in $rules){$name=([string]$rule.Name).Trim();$pub=([string]$rule.Publisher).Trim();if($name.Length -eq 0){throw '标准软件名称为空，未执行清理。'};if($pub.Length -eq 0){[void]$any.Add($name)}else{[void]$exact.Add($name+[char]0+$pub)}}
 foreach($row in $rows){if(-not $any.Contains($row.Name.Trim()) -and -not $exact.Contains($row.Name.Trim()+[char]0+$row.Publisher.Trim())){$row}}
}
function Get-SWProtection($row) {
 if($row.NoRemove){return '登记为不可卸载'}
 if($row.System -or $row.Parent.Length -gt 0 -or $row.Release -match 'Update|Hotfix|更新'){return '系统组件或更新'}
 if(($row.Name+' '+$row.Publisher) -match '火绒|Huorong|360.*(?:安全|杀毒|卫士)|360 Total Security|Microsoft Defender|Windows Defender|天擎|终端安全|Endpoint|Antivirus|防病毒|杀毒|电子教室|极域|红蜘蛛|凌波|Microsoft Visual C\+\+|(?:Microsoft|Windows).*\.NET|\.NET(?: Framework| Runtime| Desktop| SDK)|驱动|\bDriver\b|NVIDIA|Realtek|Advanced Micro Devices|Intel(?:\(R\))?.*(?:Graphics|Chipset|Ethernet|Wireless|Bluetooth|Management Engine|Serial IO|Rapid Storage)'){return '安全、教学管理、驱动或基础运行组件'}
 return ''
}
function Get-SWInstaller { return Join-Path ([Environment]::SystemDirectory) 'msiexec.exe' }
function Test-SWFile([string]$file){return [IO.File]::Exists($file)}
function Parse-SWCommand([string]$command) {
 $command=[Environment]::ExpandEnvironmentVariables($command.Trim())
 if($command.Length -eq 0 -or $command.Length -gt 32767 -or $command -match '[\x00-\x1f]'){throw '卸载命令为空或格式无效'}
 $file='';$arguments=''
 if($command.StartsWith('"')) {
  $end=$command.IndexOf('"',1);if($end -lt 0 -or ($end+1 -lt $command.Length -and -not [char]::IsWhiteSpace($command[$end+1]))){throw '卸载路径引号无效'}
  $file=$command.Substring(1,$end-1);$arguments=$command.Substring($end+1).TrimStart()
 } else {
  $matchesFound=@();foreach($match in [regex]::Matches($command,'\.exe(?=\s|$)','IgnoreCase')) {
   $end=$match.Index+$match.Length;$candidate=$command.Substring(0,$end)
   if($candidate -match '^(?:[A-Za-z]:[\\/]|\\\\[^\\]+\\[^\\]+\\)' -and (Test-SWFile $candidate)){$matchesFound+=@([pscustomobject]@{File=$candidate;End=$end})}
  }
  if($matchesFound.Count -ne 1){throw '卸载路径缺失或存在歧义'};$file=$matchesFound[0].File;$arguments=$command.Substring($matchesFound[0].End).TrimStart()
 }
 if($file -notmatch '^(?:[A-Za-z]:[\\/]|\\\\[^\\]+\\[^\\]+\\)' -or -not $file.EndsWith('.exe',[StringComparison]::OrdinalIgnoreCase) -or -not (Test-SWFile $file)){throw '卸载程序不存在或不是绝对 EXE 路径'}
 $leaf=($file -split '[\\/]')[-1]
 if($leaf -match '^(?:cmd|powershell|pwsh|wscript|cscript|rundll32|regsvr32|mshta)\.exe$'){throw '脚本或通用执行器需人工核对，不参与批量卸载'}
 return [pscustomobject]@{File=$file;Arguments=$arguments}
}
function Get-SWPlan($row,[bool]$automatic) {
 $reason=Get-SWProtection $row;if($reason.Length -gt 0){throw $reason}
 $code=[Guid]::Empty;$isMsi=$row.Msi -and [Guid]::TryParse($row.Key,[ref]$code)
 if(-not $isMsi -and $row.Command -match '(?i)^\s*(?:"(?:[^"\r\n]*[\\/])?msiexec\.exe"|(?:[A-Za-z]:[\\/][^"\r\n]*[\\/])?msiexec\.exe)\s+/[ix]\s*(\{[0-9a-f-]{36}\})(?:\s|$)') {$isMsi=[Guid]::TryParse($Matches[1],[ref]$code)}
 if($isMsi){$file=Get-SWInstaller;if(-not (Test-SWFile $file)){throw 'Windows Installer 不可用'};return [pscustomobject]@{File=$file;Arguments=('/x '+$code.ToString('B')+' /norestart'+$(if($automatic){' /qn'}else{''}))}}
 $command=if(-not [string]::IsNullOrWhiteSpace($row.Quiet)){$row.Quiet}elseif($automatic){throw '未登记静默卸载命令'}else{$row.Command}
 $plan=Parse-SWCommand $command
 if(($plan.File -split '[\\/]')[-1] -ieq 'msiexec.exe'){throw '不能核对 MSI 产品代码，不使用未知安装器参数'}
 return $plan
}
function Start-SWRemoval($plan) {
 $start=New-Object Diagnostics.ProcessStartInfo;$start.FileName=$plan.File;$start.Arguments=$plan.Arguments;$start.UseShellExecute=$true
 $process=[Diagnostics.Process]::Start($start);if($null -eq $process){throw '卸载程序未启动'}
 try {if(-not $process.WaitForExit(600000)){throw (New-Object TimeoutException '卸载向导超过十分钟未结束，已停止本轮清理；未强制结束安装器')};return $process.ExitCode} finally {$process.Dispose()}
}
function Test-SWSameRecord($before,$after) {
 foreach($field in @('Name','Publisher','Hive','View','Key','Command','Quiet','Msi','NoRemove','System','Parent','Release')){if([string]$before.$field -cne [string]$after.$field){return $false}};return $true
}
function Start-SWMachineStage {
 $file=$env:IT_TOOLKIT_BAT
 if(-not [IO.File]::Exists($file)){throw '无法定位当前 BAT，未启动管理员阶段'}
 $child=Start-Process -FilePath $file -WorkingDirectory ([IO.Path]::GetDirectoryName($file)) -ArgumentList '--machine-stage' -Verb RunAs -Wait -PassThru -ErrorAction Stop
 try {return $child.ExitCode} finally {$child.Dispose()}
}
function Invoke-SWCleanup([object[]]$rules,[bool]$automatic,[bool]$machineStage=$false) {
 $script:SWNeedsReboot=$false
 try {
  if($rules.Count -eq 0){throw '没有有效标准清单，未执行卸载。'}
  $admin=Test-SWAdmin;if($machineStage -and -not $admin){throw '管理员阶段没有取得管理员权限，未执行卸载。'}
  $extras=@(Find-SWExtras @(Read-SWInventory $machineStage) $rules)
  if($extras.Count -eq 0){if(-not $machineStage){Show-SWResult '已完成：没有标准清单之外的软件。' $true};return $true}
  if(-not $automatic -and -not $machineStage) {
   $names=@($extras | Select-Object -First 20 | ForEach-Object {$_.Name}) -join "`r`n"
   if(-not (Confirm-SWRemoval ('发现 '+$extras.Count+' 项标准之外的软件：'+"`r`n"+$names+"`r`n`r`n"+'将跳过保护项；支持静默的优先静默，其余打开卸载向导。是否开始？'))){Show-SWResult '已取消，未执行任何卸载。' $false;return $false}
  }
  $issues=New-Object 'Collections.Generic.List[string]';$started=0;$reboot=$false;$unfinished=$false;$needMachine=$false
  $seen=New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
  foreach($row in $extras) {
   if(-not $admin -and $row.Hive -eq 'LocalMachine'){$needMachine=$true;continue}
   try {
    $fresh=Read-SWRecord $row.Hive $row.View $row.Key;if($null -eq $fresh){continue}
    if(-not (Test-SWSameRecord $row $fresh)){throw '软件登记已经变化，请重新生成或重新运行核对'}
    $plan=Get-SWPlan $fresh $automatic;$identity=$plan.File+[char]0+$plan.Arguments
    if(-not $seen.Add($identity)){continue}
    $started++;$code=Start-SWRemoval $plan
    if($code -eq 3010){$reboot=$true}elseif($code -notin @(0,1605,1614)){throw ('卸载程序返回 '+$code)}
   } catch {$issues.Add(($row.Name+': '+$_.Exception.Message));if($_.Exception -is [TimeoutException]){$unfinished=$true;break}}
  }
  if($needMachine -and -not $unfinished) {
   try {$machineCode=Start-SWMachineStage;if($machineCode -eq 2){$reboot=$true}elseif($machineCode -ne 0){$unfinished=$true;$issues.Add('管理员阶段未全部完成。')}}
   catch {$unfinished=$true;$issues.Add('管理员阶段未执行或失败：'+$_.Exception.Message)}
  }
  $remaining=@(Find-SWExtras @(Read-SWInventory $machineStage) $rules);$ok=$remaining.Count -eq 0 -and -not $reboot -and -not $unfinished
  $text=$(if($ok){'已完成。'}else{'未全部完成。'})+"`r`n"+'已减少 '+[Math]::Max(0,$extras.Count-$remaining.Count)+' 个非标准登记项；仍有 '+$remaining.Count+' 项不在标准内。'
  if($reboot){$text+="`r`n"+'部分项目要求重启，请重启后再核对。';$ok=$false}
  if($issues.Count -gt 0){$text+="`r`n`r`n"+(@($issues | Select-Object -First 6) -join "`r`n")}
  if($remaining.Count -gt 0){$text+="`r`n`r`n"+'仍需核对：'+(@($remaining | Select-Object -First 6 | ForEach-Object {$_.Name}) -join '、')}
  $script:SWNeedsReboot=$reboot;if(-not $machineStage){Show-SWResult $text $ok};return $ok
 } catch {if(-not $machineStage){Show-SWResult ('未完成，已停止清理：'+$_.Exception.Message) $false};return $false}
}
