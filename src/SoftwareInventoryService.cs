using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Win32;
namespace ITDesktopToolkit
{
    public sealed class SoftwareEntry
    {
        public string Name,Version,Publisher,Scope,Architecture,Location,Key;
        public RegistryHive RegistryHive;public RegistryView RegistryView;public string RegistryName="";public bool HasUninstaller;
        public object[] Cells(){return new object[]{Name,Version,Publisher,Scope,Architecture,Location};}
    }
    public sealed class SoftwareSnapshot
    {
        public readonly List<SoftwareEntry> Entries=new List<SoftwareEntry>();
        public readonly List<string> Warnings=new List<string>();
        public string FrameworkRelease="未登记";
        public Dictionary<string,string> Versions(){var result=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);foreach(var item in Entries)result[item.Key]=item.Version;return result;}
    }
    public static class SoftwareInventoryService
    {
        public static SoftwareSnapshot Read(CancellationToken token)
        {
            var result=new SoftwareSnapshot();
            foreach(var hive in new[]{RegistryHive.LocalMachine,RegistryHive.CurrentUser})
            foreach(var view in new[]{RegistryView.Registry32,RegistryView.Registry64})
            {
                token.ThrowIfCancellationRequested();if(view==RegistryView.Registry64&&!Environment.Is64BitOperatingSystem)continue;
                try {
                    using(var root=RegistryKey.OpenBaseKey(hive,view))
                    using(var uninstall=root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
                    {
                        if(uninstall==null)continue;
                        foreach(string name in uninstall.GetSubKeyNames())
                        {
                            token.ThrowIfCancellationRequested();
                            try{using(var key=uninstall.OpenSubKey(name)){
                                if(key==null)continue;string display=Convert.ToString(key.GetValue("DisplayName"));if(String.IsNullOrWhiteSpace(display))continue;
                                result.Entries.Add(new SoftwareEntry{Name=display,Version=Convert.ToString(key.GetValue("DisplayVersion")),Publisher=Convert.ToString(key.GetValue("Publisher")),Location=Convert.ToString(key.GetValue("InstallLocation")),Scope=hive==RegistryHive.LocalMachine?"所有用户":"当前用户",Architecture=view==RegistryView.Registry64?"64 位登记区":"32 位登记区",Key=display+" ["+hive+"/"+view+"]",RegistryHive=hive,RegistryView=view,RegistryName=name,HasUninstaller=Convert.ToString(key.GetValue("NoRemove","0"))!="1"&&(!String.IsNullOrWhiteSpace(Convert.ToString(key.GetValue("UninstallString")))||Convert.ToString(key.GetValue("WindowsInstaller","0"))=="1")});
                            }}catch(System.Security.SecurityException ex){result.Warnings.Add(hive+"/"+name+"："+ex.Message);}
                        }
                    }
                }catch(OperationCanceledException){throw;}catch(Exception ex){result.Warnings.Add(hive+"/"+view+"："+ex.Message);}
            }
            token.ThrowIfCancellationRequested();
            try{using(var key=Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))if(key!=null)result.FrameworkRelease=Convert.ToString(key.GetValue("Release","未登记"));}
            catch(Exception ex){result.Warnings.Add(".NET Framework："+ex.Message);}
            result.Entries.Sort(delegate(SoftwareEntry a,SoftwareEntry b){int name=StringComparer.OrdinalIgnoreCase.Compare(a.Name,b.Name);return name!=0?name:StringComparer.Ordinal.Compare(a.Key,b.Key);});
            return result;
        }
        public static List<SoftwareEntry> Filter(SoftwareSnapshot snapshot,string text)
        {
            string query=(text??"").Trim();var result=new List<SoftwareEntry>();
            foreach(var item in snapshot.Entries)if(query.Length==0||(item.Name+"\n"+item.Version+"\n"+item.Publisher+"\n"+item.Scope).IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0)result.Add(item);
            return result;
        }
    }
}
