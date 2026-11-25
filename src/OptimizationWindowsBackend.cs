using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Diagnostics;
using System.Management;
using System.Net.NetworkInformation;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace WinForms
{
    internal sealed class OptimizationWindowsBackend:IOptimizationBackend
    {
        internal readonly int Build;internal readonly bool? Laptop;internal readonly bool Domain,DomainKnown;public string Context {get;private set;}
        internal OptimizationWindowsBackend()
        {
            if(Environment.OSVersion.Platform!=PlatformID.Win32NT)throw new NotSupportedException("此功能只在Windows上修改设置。");
            string machine="",build="";using(var root=OptimizationRegistry.Root(true)){
                using(var key=root.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))machine=key==null?"":Convert.ToString(key.GetValue("MachineGuid"));
                using(var key=root.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))build=key==null?"":Convert.ToString(key.GetValue("CurrentBuildNumber"));
            }
            int number;if(!Int32.TryParse(build,out number)||machine.Length==0)throw new InvalidOperationException("无法读取系统版本或设备标识，不进行猜测式修改。");Build=number;
            using(var user=WindowsIdentity.GetCurrent()){if(user.User==null)throw new InvalidOperationException("无法识别当前用户。");Context=Environment.MachineName+"|"+machine+"|"+user.User.Value+"|"+Build;}
            try{using(var search=new ManagementObjectSearcher("SELECT PCSystemType,PartOfDomain FROM Win32_ComputerSystem"))using(var rows=search.Get())foreach(ManagementObject row in rows)using(row){if(row["PartOfDomain"]!=null){Domain=Convert.ToBoolean(row["PartOfDomain"]);DomainKnown=true;}int type=Convert.ToInt32(row["PCSystemType"]);if(type==2)Laptop=true;else if(type==1||type==3||type==4||type==5||type==7)Laptop=false;}}
            catch{}
            if(!Laptop.HasValue)try{using(var search=new ManagementObjectSearcher("SELECT ChassisTypes FROM Win32_SystemEnclosure"))using(var rows=search.Get())foreach(ManagementObject row in rows)using(row){var types=row["ChassisTypes"] as ushort[];if(types!=null)foreach(ushort type in types)if(type==8||type==9||type==10||type==14||type==30||type==31||type==32)Laptop=true;}}
            catch{}
            string uuid="",serial="",basis;var macs=new List<string>();
            try{using(var search=new ManagementObjectSearcher("SELECT UUID FROM Win32_ComputerSystemProduct")){search.Options.Timeout=TimeSpan.FromSeconds(3);using(var rows=search.Get())foreach(ManagementObject row in rows)using(row){uuid=OperationAudit.ValidUuid(Convert.ToString(row["UUID"]));break;}}}catch{}
            if(uuid.Length==0)try{using(var search=new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_BIOS")){search.Options.Timeout=TimeSpan.FromSeconds(3);using(var rows=search.Get())foreach(ManagementObject row in rows)using(row){serial=OperationAudit.ValidSerial(Convert.ToString(row["SerialNumber"]));break;}}}catch{}
            try{foreach(var nic in NetworkInterface.GetAllNetworkInterfaces())if(nic.NetworkInterfaceType==NetworkInterfaceType.Ethernet||nic.NetworkInterfaceType==NetworkInterfaceType.Wireless80211){string raw=nic.GetPhysicalAddress().ToString();try{macs.Add(StudentWakeService.NormalizeMac(raw));}catch(ArgumentException){}}}catch{}
            string device=OperationAudit.DeviceKey(uuid,serial,macs,out basis);if(device.StartsWith("SESSION-",StringComparison.Ordinal))throw new InvalidOperationException("无法读取可持久核对的硬件标识，停止建立优化/恢复上下文。");Context+="|"+device;
        }
        internal string Description {get{return "设备："+(Laptop.HasValue?(Laptop.Value?"笔记本/便携设备":"台式机"):"类型未识别，合盖设置跳过")+"；Windows Build "+Build+"；"+(!DomainKnown?"域状态未知，自动校时跳过":Domain?"已加入域，保留域时间源":"非域成员")+"。当前账户设置按本次运行账户处理。";}}
        public Dictionary<string,string> Capture(string id,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();var option=OptimizationService.Find(id);if(option.Admin&&!CommandService.IsAdministrator())throw new UnauthorizedAccessException("此项需要管理员权限，未执行。可用“以管理员重开”后重新选择。");
            if(id=="power")return OptimizationPower.Capture(Laptop);
            if(id=="folders")return OptimizationFolders.Capture();
            if(id=="apps"){if(Build<10240)throw new NotSupportedException("新应用安装卷仅适配Win10/11的Appx应用。");OptimizationFolders.CheckDataDrive();if(!String.Equals(new DriveInfo(@"D:\").DriveFormat,"NTFS",StringComparison.OrdinalIgnoreCase))throw new NotSupportedException("D盘不是Appx安装卷要求的NTFS文件系统。");return new Dictionary<string,string>{{"volume",AppxRead()}};}
            if(id=="time"&&!DomainKnown)throw new NotSupportedException("无法确认域成员状态，跳过自动校时，避免改变单位时间源。");
            var result=OptimizationRegistry.Capture(RegistryValues(id,null));
            if(id=="time"){result["running"]=TimeRunning()?"1":"0";result["domain"]=Domain?"1":"0";}
            return result;
        }
        private List<OptimizationRegistryValue> RegistryValues(string id,Dictionary<string,string> before)
        {
            var values=OptimizationRegistry.Values(id,Build);if(id=="time"){
                values.Add(new OptimizationRegistryValue(true,@"SYSTEM\CurrentControlSet\Services\W32Time","DelayedAutoStart",0));
                string type=Domain?"NT5DS":"NTP";if(before!=null&&before.ContainsKey("3.present")&&before["3.present"]=="1"){string prior=before["3.value"];if(prior=="NTP"||prior=="AllSync"||(Domain&&prior=="NT5DS"))type=prior;}
                values.Add(new OptimizationRegistryValue(true,@"SYSTEM\CurrentControlSet\Services\W32Time\Parameters","Type",type,RegistryValueKind.String));
                string server=Domain?null:"time.windows.com,0x9";if(before!=null&&before.ContainsKey("4.present")&&before["4.present"]=="1"&&!String.IsNullOrWhiteSpace(before["4.value"]))server=before["4.value"];
                values.Add(new OptimizationRegistryValue(true,@"SYSTEM\CurrentControlSet\Services\W32Time\Parameters","NtpServer",server,RegistryValueKind.String));
            }return values;
        }
        internal static int ExitCode(string output)
        {var match=Regex.Match(output??"",@"(?m)^返回代码：(-?\d+)\s*$");int code;if(!match.Success||!Int32.TryParse(match.Groups[1].Value,out code))throw new InvalidOperationException("命令没有可核对的退出码。\r\n"+output);return code;}
        internal static string Command(string file,string args)
        {return CommandService.Run(file,args,2);}
        private static string CheckedCommand(string file,string args,params int[] accepted)
        {string output=Command(file,args);int code=ExitCode(output);if(code==0)return output;foreach(int item in accepted)if(code==item)return output;throw new InvalidOperationException(output);}
        public string Apply(string id,Dictionary<string,string> before,CancellationToken token)
        {
            if(id=="power")return OptimizationPower.Apply(before,Command);
            if(id=="folders")return OptimizationFolders.Apply(before);
            if(id=="apps"){OptimizationFolders.CheckDataDrive();return AppxSet(null);}
            OptimizationRegistry.Write(RegistryValues(id,before),before,false);
            if(id=="time"){
                CheckedCommand("sc.exe","config w32time start= auto");CheckedCommand("sc.exe","start w32time",1056);WaitTime(true);
                CheckedCommand("w32tm.exe","/config /update");string resync;
                try{resync=CheckedCommand("w32tm.exe","/resync /rediscover");}catch(Exception ex){throw new InvalidOperationException("自动校时配置已写入，但立即同步失败；可从备份还原设置。\r\n"+ex.Message);}
                return "已提交并完成一次w32tm立即同步命令，沿用域时间源或保存的NTP源。\r\n"+resync+"\r\n"+Command("w32tm.exe","/query /status");
            }
            if(id=="desktop")OptimizationMaintenance.RefreshIcons();
            return id=="updates"?"自动更新禁用策略已写入；未停用更新服务/病毒库，系统或组织可能忽略/覆盖。":id=="web"?"用户代理/PAC已解除，传统主页设为about:blank；未修改DNS、hosts或现代浏览器管理策略。":"配置已写入，任务栏/资源管理器可能需重开或刷新；没有据此声称界面已改变。";
        }
        public string Verify(string id,Dictionary<string,string> before,bool restored,CancellationToken token)
        {
            if(id=="power")return OptimizationPower.Verify(before,restored);
            if(id=="folders")return OptimizationFolders.Verify(before,restored);
            if(id=="apps"){string actual=AppxRead();if(restored?actual!=before["volume"]:!AppxOnD(actual))throw new InvalidOperationException("默认Appx卷核验不符。");return "默认商店应用安装卷已重新查询核验；不代表所有EXE应用或现有应用已迁移。";}
            OptimizationRegistry.Verify(RegistryValues(id,before),before,restored);
            if(id=="time"&&TimeRunning()!=(restored?before["running"]=="1":true))throw new InvalidOperationException("时间服务运行状态核验不符。");
            return "原始/目标配置值已重新读取核验；实际UI及策略执行效果仍需系统确认。";
        }
        public void Restore(string id,Dictionary<string,string> before,CancellationToken token)
        {
            if(id=="power"){OptimizationPower.Restore(before);return;}if(id=="folders"){OptimizationFolders.Restore(before);return;}if(id=="apps"){AppxSet(before["volume"]);return;}
            OptimizationRegistry.Write(RegistryValues(id,before),before,true);
            if(id=="time"){
                string value=before["0.value"];string start=value=="2"?"auto":value=="3"?"demand":value=="4"?"disabled":null;if(start==null)throw new InvalidOperationException("原服务启动类型不支持，未猜测替代。");
                CheckedCommand("sc.exe","config w32time start= "+start);OptimizationRegistry.Write(RegistryValues(id,before),before,true);
                if(before["running"]=="1"){CheckedCommand("sc.exe","start w32time",1056);WaitTime(true);CheckedCommand("w32tm.exe","/config /update");}else{CheckedCommand("sc.exe","stop w32time",1062);WaitTime(false);}
            }
            if(id=="desktop")OptimizationMaintenance.RefreshIcons();
        }
        private static bool TimeRunning(){using(var search=new ManagementObjectSearcher("SELECT Started FROM Win32_Service WHERE Name='W32Time'")){search.Options.Timeout=TimeSpan.FromSeconds(2);using(var rows=search.Get())foreach(ManagementObject row in rows)using(row)return Convert.ToBoolean(row["Started"]);}throw new NotSupportedException("系统没有可读的W32Time服务。");}
        private static void WaitTime(bool running){var clock=Stopwatch.StartNew();do{if(TimeRunning()==running)return;Thread.Sleep(200);}while(clock.ElapsedMilliseconds<15000);throw new TimeoutException("时间服务没有在15秒内进入预期运行状态，未将Start/StopPending视作成功。");}
        private static string Script(string code)
        {string output=PowerShellService.RunScript("$ErrorActionPreference='Stop'; try { "+code+" } catch { Write-Output $_.Exception.Message; exit 1 }",2);if(ExitCode(output)!=0)throw new InvalidOperationException(output);return output;}
        internal static string AppxReadScript {get{return "if(-not(Get-Command Get-AppxDefaultVolume -ErrorAction SilentlyContinue)){throw 'Appx volume cmdlet unavailable'}; $v=Get-AppxDefaultVolume; if($null -eq $v){throw 'Default Appx volume missing'}; Write-Output ('WF_VOLUME:'+[string]$v.Name)";}}
        private static string AppxRead(){string output=Script(AppxReadScript);var match=Regex.Match(output,@"(?m)^WF_VOLUME:(.+)\r?$");if(!match.Success)throw new InvalidOperationException("没有得到默认Appx卷名称。");string volume=match.Groups[1].Value.Trim();ValidateVolume(volume);return volume;}
        private static void ValidateVolume(string volume){if(!Regex.IsMatch(volume??"",@"^\\\\\?\\Volume\{[0-9A-Fa-f-]{36}\}\\$"))throw new InvalidOperationException("Appx卷名称格式无法安全核对。");}
        private static bool AppxOnD(string volume)
        {ValidateVolume(volume);string code="$v=@(Get-AppxVolume | Where-Object {$_.Name -eq "+PowerShellService.Quote(volume)+"}); if($v.Count -ne 1){throw 'Appx volume not unique'}; if(([string]$v[0].PackageStorePath).StartsWith('D:\\',[StringComparison]::OrdinalIgnoreCase)){Write-Output 'WF_D_OK'}";return Script(code).Contains("WF_D_OK");}
        internal static string AppxSetScript(string volume)
        {
            if(volume!=null){ValidateVolume(volume);return "$v=@(Get-AppxVolume | Where-Object {$_.Name -eq "+PowerShellService.Quote(volume)+"}); if($v.Count -ne 1){throw 'Original Appx volume missing'}; Set-AppxDefaultVolume -Volume $v[0]";}
            return "foreach($n in @('Get-AppxVolume','Add-AppxVolume','Set-AppxDefaultVolume')){if(-not(Get-Command $n -ErrorAction SilentlyContinue)){throw ('Missing '+$n)}}; $v=@(Get-AppxVolume | Where-Object {([string]$_.PackageStorePath).StartsWith('D:\\',[StringComparison]::OrdinalIgnoreCase)}); if($v.Count -eq 0){$v=@(Add-AppxVolume -Path 'D:\\WindowsApps')}; if($v.Count -ne 1){throw 'D Appx volume not unique'}; Set-AppxDefaultVolume -Volume $v[0]";
        }
        private static string AppxSet(string volume){return Script(AppxSetScript(volume));}
    }
}
