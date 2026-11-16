using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;
namespace ITDesktopToolkit
{
    public sealed class SoftwareUninstallPlan
    {
        public string Name="",FileName="",Arguments="",Reason="";
        public bool CanStart{get{return FileName.Length>0&&Reason.Length==0;}}
    }
    public static class SoftwareUninstallService
    {
        private const string RegistryPath=@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        private static bool Flag(RegistryKey key,string name){return Convert.ToString(key.GetValue(name,"0"))=="1";}
        public static SoftwareUninstallPlan Prepare(SoftwareEntry item)
        {
            if(item==null||String.IsNullOrEmpty(item.RegistryName)||(item.RegistryHive!=RegistryHive.LocalMachine&&item.RegistryHive!=RegistryHive.CurrentUser)||(item.RegistryView!=RegistryView.Registry32&&item.RegistryView!=RegistryView.Registry64))return new SoftwareUninstallPlan{Reason="该项目没有可核对的卸载登记，请使用 Windows 应用设置。"};
            using(var root=RegistryKey.OpenBaseKey(item.RegistryHive,item.RegistryView))
            using(var key=root.OpenSubKey(RegistryPath+"\\"+item.RegistryName))
            {
                if(key==null||!String.Equals(Convert.ToString(key.GetValue("DisplayName")).Trim(),(item.Name??"").Trim(),StringComparison.Ordinal))return new SoftwareUninstallPlan{Name=item.Name,Reason="软件登记已变化，请先刷新清单。"};
                return Build(item.Name,Convert.ToString(key.GetValue("UninstallString")),Flag(key,"WindowsInstaller"),item.RegistryName,Flag(key,"NoRemove"),Environment.SystemDirectory,File.Exists);
            }
        }
        private static bool Absolute(string path){return Regex.IsMatch(path??"",@"^(?:[A-Za-z]:[\\/]|\\\\[^\\]+\\[^\\]+\\)");}
        private static string SystemFile(string name,string directory)
        {foreach(string system in new[]{"msiexec.exe","rundll32.exe","cmd.exe"})if(name.Equals(system,StringComparison.OrdinalIgnoreCase))return directory.TrimEnd('\\','/')+"\\"+system;return name;}
        private static SoftwareUninstallPlan Unavailable(string name,string reason){return new SoftwareUninstallPlan{Name=name,Reason=reason};}
        internal static SoftwareUninstallPlan Build(string name,string command,bool msi,string productCode,bool noRemove,string systemDirectory,Func<string,bool> exists)
        {
            if(noRemove)return Unavailable(name,"该软件登记为不可直接卸载，请在 Windows 应用设置中核对。");Guid id;
            if(msi&&Guid.TryParse(productCode,out id))
            {
                string installer=SystemFile("msiexec.exe",systemDirectory);return exists(installer)?new SoftwareUninstallPlan{Name=name,FileName=installer,Arguments="/x "+id.ToString("B").ToUpperInvariant()}:Unavailable(name,"Windows Installer 不可用。");
            }
            command=Environment.ExpandEnvironmentVariables((command??"").Trim());if(command.Length==0)return Unavailable(name,"未登记卸载程序，可在 Windows 应用设置中管理。");if(command.Length>32767||command.IndexOfAny(new[]{'\0','\r','\n'})>=0)return Unavailable(name,"卸载命令格式无效，请使用 Windows 应用设置。");
            string file="",args="";
            if(command[0]=='"')
            {
                int end=command.IndexOf('"',1);if(end<0||(end+1<command.Length&&!Char.IsWhiteSpace(command[end+1])))return Unavailable(name,"卸载程序路径引号不完整。");file=SystemFile(command.Substring(1,end-1),systemDirectory);args=command.Substring(end+1).TrimStart();
            }
            else
            {
                var candidates=new List<KeyValuePair<string,int>>();foreach(Match match in Regex.Matches(command,@"\.exe(?=\s|$)",RegexOptions.IgnoreCase))
                {int end=match.Index+match.Length;string prefix=SystemFile(command.Substring(0,end),systemDirectory);if(Absolute(prefix)&&exists(prefix))candidates.Add(new KeyValuePair<string,int>(prefix,end));}
                if(candidates.Count!=1)return Unavailable(name,candidates.Count>1?"卸载程序路径存在歧义，请使用 Windows 应用设置。":"找不到已登记的卸载程序，请使用 Windows 应用设置。");file=candidates[0].Key;args=command.Substring(candidates[0].Value).TrimStart();
            }
            if(!Absolute(file)||!file.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)||!exists(file))return Unavailable(name,"卸载程序路径不可用，请使用 Windows 应用设置。");
            string basename=file.Substring(Math.Max(file.LastIndexOf('\\'),file.LastIndexOf('/'))+1);
            if(basename.Equals("msiexec.exe",StringComparison.OrdinalIgnoreCase))
            {
                var match=Regex.Match(args,@"^/[ix]\s*(\{[0-9a-fA-F-]{36}\})(?:\s|$)",RegexOptions.IgnoreCase);Guid code;
                if(match.Success&&Guid.TryParse(match.Groups[1].Value,out code)){file=SystemFile("msiexec.exe",systemDirectory);if(!exists(file))return Unavailable(name,"Windows Installer 不可用。");args="/x "+code.ToString("B").ToUpperInvariant();}
            }
            return new SoftwareUninstallPlan{Name=name,FileName=file,Arguments=args};
        }
        public static void Start(SoftwareUninstallPlan plan)
        {
            if(plan==null||!plan.CanStart)throw new ArgumentException("没有可执行的卸载计划。");
            Process process=Process.Start(new ProcessStartInfo(plan.FileName,plan.Arguments){UseShellExecute=true});if(process==null)throw new InvalidOperationException("系统未启动卸载程序。");process.Dispose();
        }
    }
}
