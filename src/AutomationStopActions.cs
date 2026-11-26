using System;

namespace WinForms
{
    public static partial class AutomationService
    {
        private static readonly string[] ProtectedServices={"RpcSs","DcomLaunch","RpcEptMapper","SamSs","LSM","Power","EventLog","BFE","MpsSvc","WinDefend","WdNisSvc","SecurityHealthService","wscsvc","Sense","mpssvc"};
        private static readonly string[] ProtectedProcesses={"idle","system","registry","smss","csrss","wininit","winlogon","lsass","lsaiso","services","svchost","fontdrvhost","dwm","sihost","logonui","msmpeng","nissrv","securityhealthservice","mpdefendercoreservice","cmd","powershell","pwsh","conhost","openconsole","windowsterminal","wscript","cscript"};
        public static string CheckStopServiceName(string value)
        {
            string name=(value??"").Trim();if(name.Length==0||name.Length>256||name.IndexOfAny(new[]{'\\','/','*','?','"','\0','\r','\n'})>=0||name=="."||name=="..")throw new ArgumentException("请使用准确的服务内部名称，不支持路径或通配符。");foreach(char c in name)if(Char.IsControl(c))throw new ArgumentException("服务名称不能含控制字符。");
            foreach(string item in ProtectedServices)if(String.Equals(item,name,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("该关键系统/Windows安全服务不能通过自动化停止："+name);return name;
        }
        public static string CheckEndProcessName(string value)
        {
            string name=(value??"").Trim();if(name.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))name=name.Substring(0,name.Length-4);
            if(name.Length==0||name.Length>240||name.IndexOfAny(new[]{'\\','/',':','*','?','"','<','>','|','\0','\r','\n'})>=0||name=="."||name==".."||name.EndsWith(".",StringComparison.Ordinal))throw new ArgumentException("请填写进程文件名或选择EXE，不使用PID、路径或通配符。");foreach(char c in name)if(Char.IsControl(c))throw new ArgumentException("进程名称不能含控制字符。");
            foreach(string item in ProtectedProcesses)if(String.Equals(item,name,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("不支持结束关键系统、安全或脚本宿主进程："+name);return name;
        }
        public static string ProcessNameFromFile(string path)
        {
            string file=System.IO.Path.GetFileName((path??"").Replace('\\','/'));if(!file.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("请选择一个EXE文件；BAT不是独立进程名称。");CheckEndProcessName(file);return file;
        }
        public static string CheckProcessEndMode(string value)
        {string mode=String.IsNullOrWhiteSpace(value)?"正常关闭":value.Trim();if(mode!="正常关闭"&&mode!="强制结束")throw new ArgumentException("结束方式只能为正常关闭或强制结束。");return mode;}
        public static int StopActionTimeout(string value,int fallback)
        {int seconds;if(String.IsNullOrWhiteSpace(value))return fallback;if(!Int32.TryParse(value.Trim(),out seconds)||seconds<1||seconds>120)throw new ArgumentException("等待时间须为1–120秒。");return seconds;}
        public static void CheckStopAction(AutoStep step)
        {
            if(step.Type=="停止服务"){CheckStopServiceName(step.A);StopActionTimeout(step.B,30);if(!String.IsNullOrWhiteSpace(step.C))throw new ArgumentException("停止服务的参数3应留空。");}
            else if(step.Type=="结束进程"){CheckEndProcessName(step.A);CheckProcessEndMode(step.B);StopActionTimeout(step.C,15);}
        }
        private const string StopActionHelper=@"
function Get-ITLocalStopServices {
 Add-Type -AssemblyName System.ServiceProcess -ErrorAction Stop
 return [System.ServiceProcess.ServiceController]::GetServices()
}
function Invoke-ITServiceStopControl([string]$name) {
 $sc=[IO.Path]::Combine([Environment]::SystemDirectory,'sc.exe')
 & $sc stop $name | ForEach-Object {Write-Host $_}
 $code=$LASTEXITCODE
 if($code -ne 0 -and $code -ne 1062){throw ('停止服务请求失败，代码 '+$code+'：'+$name)}
}
function Invoke-ITStopService([string]$name,[int]$seconds) {
 if($seconds -lt 1 -or $seconds -gt 120){throw '服务等待时间须为1–120秒'}
 $protected=@('RpcSs','DcomLaunch','RpcEptMapper','SamSs','LSM','Power','EventLog','BFE','MpsSvc','WinDefend','WdNisSvc','SecurityHealthService','wscsvc','Sense')
 if($protected -contains $name){throw ('不能停止关键系统/Windows安全服务：'+$name)}
 $target=$null
 try {
  foreach($service in @(Get-ITLocalStopServices)) {
   if($service.ServiceName -ieq $name){if($null -ne $target){$service.Dispose();throw '服务内部名称不唯一'};$target=$service}else{$service.Dispose()}
  }
  if($null -eq $target){throw ('未找到准确的服务内部名称：'+$name)}
  $target.Refresh()
  if([string]$target.Status -eq 'Stopped'){Write-Host ('[OK] 服务已经停止：'+$name);return}
  if([string]$target.Status -eq 'StopPending'){$target.WaitForStatus('Stopped',[TimeSpan]::FromSeconds($seconds))}
  else {
   if(-not $target.CanStop){throw ('服务不接受停止控制：'+$name)}
   $dependent=@($target.DependentServices)
   try {foreach($item in $dependent){$item.Refresh();if([string]$item.Status -ne 'Stopped'){throw ('仍有依赖服务运行，未连带停止：'+$item.ServiceName)}}}finally{foreach($item in $dependent){$item.Dispose()}}
   Invoke-ITServiceStopControl $name
   $target.WaitForStatus('Stopped',[TimeSpan]::FromSeconds($seconds))
  }
  $target.Refresh();if([string]$target.Status -ne 'Stopped'){throw ('服务仍未停止或已被重新启动：'+$name)}
  Write-Host ('[OK] 已核对服务停止：'+$name+'；未修改启动类型')
 } finally {if($null -ne $target){$target.Dispose()}}
}
function Get-ITNamedStopProcesses([string]$name) {return [Diagnostics.Process]::GetProcessesByName($name)}
function Invoke-ITEndProcess([string]$name,[string]$mode,[int]$seconds) {
 if($seconds -lt 1 -or $seconds -gt 120){throw '进程等待时间须为1–120秒'}
 $protected=@('idle','system','registry','smss','csrss','wininit','winlogon','lsass','lsaiso','services','svchost','fontdrvhost','dwm','sihost','logonui','msmpeng','nissrv','securityhealthservice','mpdefendercoreservice','cmd','powershell','pwsh','conhost','openconsole','windowsterminal','wscript','cscript')
 if($protected -contains $name){throw ('不能结束关键系统、安全或脚本宿主进程：'+$name)}
 $current=[Diagnostics.Process]::GetCurrentProcess();try{$session=$current.SessionId;$own=$current.Id}finally{$current.Dispose()}
 $targets=New-Object Collections.ArrayList;$errors=New-Object Collections.ArrayList
 try {
  foreach($process in @(Get-ITNamedStopProcesses $name)) {
   try {
    if($process.HasExited){$process.Dispose();continue}
    if($process.SessionId -ne $session -or $process.ProcessName -ine $name){$process.Dispose();continue}
    if($process.Id -eq $own){throw '禁止结束当前脚本自身'}
    # Keep the handle open for the snapshot lifetime; do not re-resolve a reused PID.
    $heldHandle=$process.Handle
    if($null -eq $heldHandle -or $heldHandle -eq [IntPtr]::Zero){throw '无法取得可确认的目标进程句柄，未执行结束'}
    [void]$targets.Add($process)
   } catch {$process.Dispose();throw}
  }
  if($targets.Count -eq 0){Write-Host ('[OK] 当前会话没有该名称的进程：'+$name);return}
  if($mode -eq '正常关闭') {
   $windows=@($targets | Where-Object {$_.MainWindowHandle -ne [IntPtr]::Zero})
   if($windows.Count -eq 0){throw '目标没有主窗口，正常关闭不适用；未自动改为强制结束'}
  } elseif($mode -ne '强制结束'){throw '结束方式无效'}
  $deadline=[DateTime]::UtcNow.AddSeconds($seconds)
  foreach($process in $targets) {
   try {
    if($process.HasExited){continue}
    if($mode -eq '强制结束'){$process.Kill()}
    elseif($process.MainWindowHandle -ne [IntPtr]::Zero -and -not $process.CloseMainWindow()) {if(-not $process.HasExited){throw '程序没有接受关闭窗口请求'}}
   } catch {[void]$errors.Add(('实例 '+$process.Id+'：'+$_.Exception.Message))}
  }
  foreach($process in $targets) {
   if($process.HasExited){continue}
   $left=[Math]::Max(0,[Math]::Min(120000,[int]($deadline-[DateTime]::UtcNow).TotalMilliseconds))
   try {if(-not $process.WaitForExit($left)){[void]$errors.Add(('实例 '+$process.Id+' 未在时限内退出'))}}catch{[void]$errors.Add($_.Exception.Message)}
  }
  $remaining=0
  foreach($process in @(Get-ITNamedStopProcesses $name)){try{if(-not $process.HasExited -and $process.SessionId -eq $session -and $process.ProcessName -ieq $name){$remaining++}}finally{$process.Dispose()}}
  if($remaining -gt 0 -or $errors.Count -gt 0){throw ('未全部结束（部分实例可能已经退出）；仍有 '+$remaining+' 个，'+($errors -join '；'))}
  Write-Host ('[OK] 当前会话同名进程已退出：'+$name+'；没有删除程序文件，也不禁止以后启动')
 } finally {foreach($process in $targets){$process.Dispose()}}
}
";
    }
}
