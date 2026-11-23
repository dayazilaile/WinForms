using System;
using System.IO;
using Microsoft.Win32;
namespace ITDesktopToolkit
{
    internal static class WindowsCompatibility
    {
        internal const int MinimumFrameworkRelease=378389;
        internal static int HighestRelease(params int[] values){int best=0;foreach(int value in values)if(value>best)best=value;return best;}
        internal static string StartupProblem(Version os,string servicePack,int frameworkRelease)
        {
            if(os<new Version(6,1))return "本版要求 Windows 7 SP1 / Server 2008 R2 SP1 或更新的带桌面系统。";
            if(os.Major==6&&os.Minor==1&&String.IsNullOrWhiteSpace(servicePack))return "Windows 7 / Server 2008 R2 请先安装 SP1。";
            if(frameworkRelease<MinimumFrameworkRelease)return "本版最低需要 .NET Framework 4.5（建议 4.8）；当前检测 Release="+frameworkRelease+"。若未读取到，请运行 Diagnose_Runtime.bat 核对，勿直接认定系统版本过低。";
            return "";
        }
        internal static string CurrentStartupProblem()
        {
            if(Environment.OSVersion.Platform!=PlatformID.Win32NT)return "";
            int release=0;
            foreach(var view in new[]{RegistryView.Registry64,RegistryView.Registry32})try{
                using(var root=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,view))
                using(var key=root.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))
                {int value;if(key!=null&&Int32.TryParse(Convert.ToString(key.GetValue("Release")),out value))release=HighestRelease(release,value);}
            }catch{}
            return StartupProblem(Environment.OSVersion.Version,Environment.OSVersion.ServicePack,release);
        }
        internal static bool Legacy {get{return Environment.OSVersion.Platform==PlatformID.Win32NT&&Environment.OSVersion.Version<new Version(6,2);}}
        internal static void ManagementTarget(ref string target,ref string args,bool legacy)
        {
            if(!legacy||!target.StartsWith("ms-settings:",StringComparison.OrdinalIgnoreCase))return;
            string page=target.Substring(12).ToLowerInvariant();
            switch(page){
                case "appsfeatures":target="appwiz.cpl";args="";break;
                case "windowsupdate":target="control.exe";args="/name Microsoft.WindowsUpdate";break;
                case "network-advancedsettings":case "network-status":target="ncpa.cpl";args="";break;
                case "network-proxy":target="inetcpl.cpl";args="";break;
                case "remotedesktop":target="SystemPropertiesRemote.exe";args="";break;
                case "powersleep":target="powercfg.cpl";args="";break;
                case "regionlanguage":case "typing":case "keyboard-advanced":target="intl.cpl";args="";break;
                case "defaultapps":target="control.exe";args="/name Microsoft.DefaultPrograms";break;
                case "taskbar":target="control.exe";args="/name Microsoft.TaskbarAndStartMenu";break;
                default:throw new NotSupportedException("此设置页属于新版 Windows，当前系统没有等价入口："+target);
            }
        }
    }
}
