using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Win32;

namespace WinForms
{
    internal sealed class OptimizationRegistryValue
    {
        internal readonly bool Machine;internal readonly string Path,Name;internal readonly object Desired;internal readonly RegistryValueKind Kind;
        internal OptimizationRegistryValue(bool machine,string path,string name,object desired,RegistryValueKind kind=RegistryValueKind.DWord){Machine=machine;Path=path;Name=name;Desired=desired;Kind=kind;}
    }
    internal static class OptimizationRegistry
    {
        internal const string Advanced=@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        internal static List<OptimizationRegistryValue> Values(string id,int build)
        {
            var values=new List<OptimizationRegistryValue>();Action<string,string,int> user=delegate(string path,string name,int value){values.Add(new OptimizationRegistryValue(false,path,name,value));};
            switch(id){
                case "extensions":user(Advanced,"HideFileExt",0);break;
                case "taskbar":if(build<10240)throw new NotSupportedException("天气和搜索任务栏选项仅适配Win10/11。");user(@"Software\Microsoft\Windows\CurrentVersion\Search","SearchboxTaskbarMode",1);if(build>=22000)user(Advanced,"TaskbarDa",0);else if(build>=19041)user(@"Software\Microsoft\Windows\CurrentVersion\Feeds","ShellFeedsTaskbarViewMode",2);break;
                case "explorer":if(build<10240)throw new NotSupportedException("此电脑/快速访问的默认入口与最近/常用开关仅适配Win10/11。");user(Advanced,"LaunchTo",1);user(@"Software\Microsoft\Windows\CurrentVersion\Explorer","ShowRecent",0);user(@"Software\Microsoft\Windows\CurrentVersion\Explorer","ShowFrequent",0);break;
                case "ads":if(build<10240)throw new NotSupportedException("系统推荐开关仅适配Win10/11。");foreach(string key in new[]{"SoftLandingEnabled","SystemPaneSuggestionsEnabled","RotatingLockScreenEnabled","RotatingLockScreenOverlayEnabled","SubscribedContent-338387Enabled","SubscribedContent-338388Enabled","SubscribedContent-338389Enabled","SubscribedContent-353694Enabled","SubscribedContent-353696Enabled"})user(@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager",key,0);user(Advanced,"ShowSyncProviderNotifications",0);break;
                case "delivery":if(build<10240)throw new NotSupportedException("此系统没有Win10/11传递优化。");values.Add(new OptimizationRegistryValue(true,@"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization","DODownloadMode",0));break;
                case "preview":user(Advanced,"IconsOnly",0);user(Advanced,"ShowPreviewHandlers",1);break;
                case "desktop":user(Advanced,"HideIcons",0);foreach(string menu in new[]{"NewStartPanel","ClassicStartMenu"})foreach(string clsid in new[]{"{20D04FE0-3AEA-1069-A2D8-08002B30309D}","{5399E694-6CE5-4D6C-8FCE-1D8870FDCBA0}","{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}"})user(@"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\"+menu,clsid,0);break;
                case "history":if(build<9200)throw new NotSupportedException("Win7没有文件历史记录功能。");values.Add(new OptimizationRegistryValue(true,@"SOFTWARE\Policies\Microsoft\Windows\FileHistory","Disabled",1));break;
                case "updates":values.Add(new OptimizationRegistryValue(true,@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU","NoAutoUpdate",1));break;
                case "web":user(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings","ProxyEnable",0);values.Add(new OptimizationRegistryValue(false,@"Software\Microsoft\Windows\CurrentVersion\Internet Settings","AutoConfigURL",null,RegistryValueKind.String));values.Add(new OptimizationRegistryValue(false,@"Software\Microsoft\Internet Explorer\Main","Start Page","about:blank",RegistryValueKind.String));break;
                case "time":values.Add(new OptimizationRegistryValue(true,@"SYSTEM\CurrentControlSet\Services\W32Time","Start",2));values.Add(new OptimizationRegistryValue(true,@"SYSTEM\CurrentControlSet\Services\W32Time\TimeProviders\NtpClient","Enabled",1));break;
                default:throw new ArgumentException("不是注册表优化项目。");
            }return values;
        }
        internal static RegistryKey Root(bool machine){return RegistryKey.OpenBaseKey(machine?RegistryHive.LocalMachine:RegistryHive.CurrentUser,Environment.Is64BitOperatingSystem?RegistryView.Registry64:RegistryView.Registry32);}
        internal static string Encode(object value,RegistryValueKind kind)
        {
            switch(kind){case RegistryValueKind.DWord:return Convert.ToInt32(value,CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);case RegistryValueKind.QWord:return Convert.ToInt64(value,CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);case RegistryValueKind.String:case RegistryValueKind.ExpandString:return (string)value;case RegistryValueKind.Binary:return Convert.ToBase64String((byte[])value);case RegistryValueKind.MultiString:return String.Join("\0",(string[])value);default:throw new NotSupportedException("原始注册表类型不支持安全备份。");}
        }
        internal static object Decode(string value,RegistryValueKind kind)
        {
            switch(kind){case RegistryValueKind.DWord:return Int32.Parse(value,CultureInfo.InvariantCulture);case RegistryValueKind.QWord:return Int64.Parse(value,CultureInfo.InvariantCulture);case RegistryValueKind.String:case RegistryValueKind.ExpandString:return value;case RegistryValueKind.Binary:return Convert.FromBase64String(value);case RegistryValueKind.MultiString:return value.Length==0?new string[0]:value.Split('\0');default:throw new InvalidOperationException("备份类型无效。");}
        }
        internal static Dictionary<string,string> Capture(List<OptimizationRegistryValue> values)
        {
            var result=new Dictionary<string,string>(StringComparer.Ordinal);
            for(int i=0;i<values.Count;i++){var v=values[i];using(var root=Root(v.Machine))using(var key=root.OpenSubKey(v.Path,false)){
                object value=key==null?null:key.GetValue(v.Name,null,RegistryValueOptions.DoNotExpandEnvironmentNames);string n=i.ToString(CultureInfo.InvariantCulture);result[n+".present"]=value==null?"0":"1";
                if(value!=null){var kind=key.GetValueKind(v.Name);result[n+".kind"]=((int)kind).ToString(CultureInfo.InvariantCulture);result[n+".value"]=Encode(value,kind);}
            }}return result;
        }
        internal static void Write(List<OptimizationRegistryValue> values,Dictionary<string,string> before,bool restore)
        {
            // Decode every value before the first write; malformed backups cannot produce partial writes here.
            var intended=new List<object>();var kinds=new List<RegistryValueKind>();
            for(int i=0;i<values.Count;i++){var v=values[i];string n=i.ToString(CultureInfo.InvariantCulture);if(!restore){intended.Add(v.Desired);kinds.Add(v.Kind);continue;}string present=before[n+".present"];if(present!="0"&&present!="1")throw new InvalidOperationException("备份存在标志无效。");var kind=present=="0"?v.Kind:(RegistryValueKind)Int32.Parse(before[n+".kind"],CultureInfo.InvariantCulture);intended.Add(present=="0"?null:Decode(before[n+".value"],kind));kinds.Add(kind);}
            for(int i=0;i<values.Count;i++){var v=values[i];using(var root=Root(v.Machine)){
                if(intended[i]==null){using(var key=root.OpenSubKey(v.Path,true))if(key!=null)key.DeleteValue(v.Name,false);}
                else using(var key=root.CreateSubKey(v.Path))key.SetValue(v.Name,intended[i],kinds[i]);
            }}
        }
        internal static void Verify(List<OptimizationRegistryValue> values,Dictionary<string,string> before,bool restore)
        {
            var actual=Capture(values);for(int i=0;i<values.Count;i++){string n=i.ToString(CultureInfo.InvariantCulture);var v=values[i];bool exists=restore?before[n+".present"]=="1":v.Desired!=null;
                if(actual[n+".present"]!=(exists?"1":"0"))throw new InvalidOperationException("注册表存在状态与预期不符："+v.Name);
                if(exists){string kind=restore?before[n+".kind"]:((int)v.Kind).ToString(CultureInfo.InvariantCulture),value=restore?before[n+".value"]:Encode(v.Desired,v.Kind);if(actual[n+".kind"]!=kind||actual[n+".value"]!=value)throw new InvalidOperationException("配置值核验不符："+v.Name);}
            }
        }
    }
}
