using System;
using System.Management;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace WinForms
{
    public static class ComputerNameService
    {
        public static bool JoinedToDomain()
        {
            using(ManagementObjectSearcher q = new ManagementObjectSearcher("SELECT PartOfDomain FROM Win32_ComputerSystem"))
                foreach(ManagementObject m in q.Get()) return Convert.ToBoolean(m["PartOfDomain"]);
            throw new InvalidOperationException("无法确认设备是否加入域，已停止自动修改名称。");
        }
        public static string Status()
        {
            string domain = "未知"; bool joined=false;
            try {
                using(ManagementObjectSearcher q = new ManagementObjectSearcher("SELECT Name,Domain,PartOfDomain FROM Win32_ComputerSystem"))
                    foreach(ManagementObject m in q.Get()){
                        joined=Convert.ToBoolean(m["PartOfDomain"]);
                        domain=Convert.ToString(m["Domain"]);
                        break;
                    }
            }catch(Exception ex){domain="无法读取："+ex.Message;}
            string pending = "未知";
            try {using(RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\ComputerName\ComputerName"))
                if(k!=null)pending=Convert.ToString(k.GetValue("ComputerName", "未知"));}catch{}
            return "当前计算机名："+Environment.MachineName+"\r\n预计下次启动名称："+pending+
                "\r\n设备类型："+(joined?"已加入域（需要域管理员审批）":"工作组/非域设备")+"\r\n域或工作组："+domain;
        }
        public static string Validate(string name)
        {
            if(String.IsNullOrWhiteSpace(name))return "名称不能为空。";
            if(name.Length>15)return "为兼容旧版共享与 NetBIOS，建议且在此工具中要求最多 15 个字符。";
            if(!Regex.IsMatch(name,@"^[A-Za-z0-9][A-Za-z0-9-]*[A-Za-z0-9]$") && !Regex.IsMatch(name,@"^[A-Za-z0-9]$"))
                return "只允许英文大小写、数字及中间的短横线，不能以短横线开头或结尾。";
            if(Regex.IsMatch(name,@"^[0-9]+$"))return "名称不能全部由数字组成。";
            return "";
        }
        public static string Rename(string name)
        {
            string err=Validate(name);if(err.Length>0)return "检查不通过："+err;
            if(!CommandService.IsAdministrator())return "请以管理员身份启动工具箱。";
            if(JoinedToDomain())return "设备已加入域：为避免影响域信任关系，本工具不直接执行改名。请联系域管理员使用正式流程。";
            if(String.Equals(Environment.MachineName,name,StringComparison.OrdinalIgnoreCase))return "新名称与当前名称相同，无需修改。";
            string script="$ErrorActionPreference='Stop'; Rename-Computer -NewName "+PowerShellService.Quote(name)+
                " -Confirm:$false -ErrorAction Stop; '已提交计算机名变更，请保存工作并手动重启。';"+
                "'当前运行中的名称：'+$env:COMPUTERNAME; "+
                "'注册表待生效名称：'+(Get-ItemProperty -LiteralPath 'HKLM:\\SYSTEM\\CurrentControlSet\\Control\\ComputerName\\ComputerName').ComputerName";
            return PowerShellService.RunScript(script,2);
        }
    }
}
