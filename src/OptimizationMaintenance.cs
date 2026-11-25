using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32.SafeHandles;

namespace WinForms
{
    internal sealed class TempCleanResult
    {
        internal long Bytes;internal int Deleted,Skipped;internal string Root="";internal bool Stopped;
        internal string Summary {get{return (Stopped?"已停止后续清理。\r\n":"")+"临时文件目录："+Root+"\r\n已删除文件 "+Deleted+" 个，释放约 "+Bytes.ToString("N0")+" 字节；跳过/无法处理 "+Skipped+" 项。\r\n此操作不可还原；欢迎界面慢仍可能由用户配置、网络或服务引起。";}}
    }
    internal static class OptimizationMaintenance
    {
        [DllImport("shell32.dll")]private static extern void SHChangeNotify(uint eventId,uint flags,IntPtr first,IntPtr second);
        [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern IntPtr SendMessageTimeout(IntPtr window,uint message,IntPtr wparam,string lparam,uint flags,uint timeout,out IntPtr result);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern uint GetFinalPathNameByHandle(SafeFileHandle file,StringBuilder path,uint length,uint flags);
        [DllImport("kernel32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool GetFileSizeEx(SafeFileHandle file,out long length);
        [DllImport("kernel32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool SetFileInformationByHandle(SafeFileHandle file,int infoClass,byte[] info,uint size);
        internal static long DeleteHeld(string path,string root)
        {
            // DELETE + GENERIC_READ, exclusive handle, open the reparse object rather than its target.
            using(var file=CreateFile(path,0x80010000,0,IntPtr.Zero,3,0x00200000,IntPtr.Zero)){
                if(file.IsInvalid)throw new IOException("文件占用或无删除权限。");var final=new StringBuilder(32768);uint count=GetFinalPathNameByHandle(file,final,(uint)final.Capacity,0);if(count==0||count>=final.Capacity)throw new IOException("无法核对待删除文件的最终路径。");
                string actual=final.ToString();if(actual.StartsWith(@"\\?\",StringComparison.Ordinal))actual=actual.Substring(4);if(!OptimizationFolders.Under(actual,root)||OptimizationFolders.SamePath(actual,root))throw new IOException("最终路径越出Temp，拒绝删除。");long length;if(!GetFileSizeEx(file,out length))throw new IOException("无法读取文件大小。");
                if(!SetFileInformationByHandle(file,4,new byte[]{1},1))throw new IOException("系统拒绝删除文件。");return length;
            }
        }
        internal static string RefreshIcons(){SHChangeNotify(0x08000000,0,IntPtr.Zero,IntPtr.Zero);IntPtr result;SendMessageTimeout(new IntPtr(0xffff),0x001A,IntPtr.Zero,"ShellState",2,1000,out result);return "已发送系统图标/关联刷新通知；不保证所有应用已立即重绘。";}
        internal static void NoLinks(string root)
        {
            string current=Path.GetFullPath(root);while(!String.IsNullOrEmpty(current)){if(Directory.Exists(current)&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("路径经过重解析点/符号链接，拒绝递归清理："+current);string parent=Path.GetDirectoryName(current);if(parent==current)break;current=parent;}
        }
        internal static TempCleanResult CleanTemp(string localAppData,bool keepRecent,CancellationToken token)
        {return CleanTempCore(localAppData,keepRecent,token,DeleteHeld,new[]{Paths.BaseDir});}
        internal static TempCleanResult CleanTempCore(string localAppData,bool keepRecent,CancellationToken token,Func<string,string,long> remove,IEnumerable<string> protectedRoots=null)
        {
            string local=Path.GetFullPath(localAppData);if(OptimizationFolders.SamePath(local,Path.GetPathRoot(local)))throw new InvalidOperationException("用户Local路径无效。");string root=Path.Combine(local,"Temp");NoLinks(root);var result=new TempCleanResult{Root=root};if(!Directory.Exists(root))return result;
            var protectedPaths=new List<string>(protectedRoots??new string[0]);DateTime cutoff=DateTime.UtcNow.AddDays(-1);var pending=new Stack<string>();pending.Push(root);
            while(pending.Count>0){if(token.IsCancellationRequested){result.Stopped=true;return result;}string folder=pending.Pop();bool protect=false;foreach(string p in protectedPaths)if(OptimizationFolders.Under(folder,p))protect=true;if(protect){result.Skipped++;continue;}
                try{NoLinks(folder);foreach(string path in Directory.EnumerateFileSystemEntries(folder)){
                    if(token.IsCancellationRequested){result.Stopped=true;return result;}try{protect=false;foreach(string p in protectedPaths)if(OptimizationFolders.Under(path,p))protect=true;if(protect){result.Skipped++;continue;}var attributes=File.GetAttributes(path);if((attributes&FileAttributes.ReparsePoint)!=0){result.Skipped++;continue;}if((attributes&FileAttributes.Directory)!=0){pending.Push(path);continue;}
                        if(keepRecent&&File.GetLastWriteTimeUtc(path)>=cutoff){result.Skipped++;continue;}if((attributes&FileAttributes.ReadOnly)!=0){result.Skipped++;continue;}
                        NoLinks(Path.GetDirectoryName(path));if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0){result.Skipped++;continue;}long length=remove(path,root);if(!File.Exists(path)){result.Deleted++;result.Bytes+=length;}else result.Skipped++;
                    }catch(OperationCanceledException){throw;}catch{result.Skipped++;}
                }}catch(OperationCanceledException){throw;}catch{result.Skipped++;}
            }return result;
        }
        internal static string Explorer(bool rebuild)
        {
            string local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);NoLinks(local);var paths=new List<string>();string cache=Path.Combine(local,"IconCache.db"),folder=Path.Combine(local,@"Microsoft\Windows\Explorer");
            if(rebuild){if(File.Exists(cache))paths.Add(cache);if(Directory.Exists(folder)){NoLinks(folder);foreach(string file in Directory.GetFiles(folder,"iconcache*.db",SearchOption.TopDirectoryOnly))paths.Add(file);}}
            int session=Process.GetCurrentProcess().SessionId;var processes=new List<Process>();string expected=SystemCommandPath.Resolve("explorer.exe");int removed=0,skipped=0;bool shellStopped=false,foreign=false;
            try{
                string sid;using(var identity=WindowsIdentity.GetCurrent())sid=identity.User.Value;
                foreach(var process in Process.GetProcessesByName("explorer")){try{if(process.SessionId!=session){process.Dispose();continue;}if(!String.Equals(process.MainModule.FileName,expected,StringComparison.OrdinalIgnoreCase)){process.Dispose();continue;}using(var owner=new ManagementObject("Win32_Process.Handle='"+process.Id+"'"))using(var data=owner.InvokeMethod("GetOwnerSid",null,null)){if(data==null||Convert.ToUInt32(data["ReturnValue"])!=0||Convert.ToString(data["Sid"])!=sid){foreign=true;process.Dispose();continue;}}processes.Add(process);}catch{foreign=true;process.Dispose();}}
                if(processes.Count==0){if(foreign)throw new InvalidOperationException("本会话Explorer属于其他账户或无法确认，请使用桌面登录账户运行工具。");shellStopped=true;}
                foreach(var process in processes){process.Kill();shellStopped=true;if(!process.WaitForExit(5000))throw new InvalidOperationException("Explorer尚未退出，停止缓存清理。");}
                if(rebuild)foreach(string path in paths)try{NoLinks(Path.GetDirectoryName(path));if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0){skipped++;continue;}DeleteHeld(path,Path.GetDirectoryName(path));if(File.Exists(path))skipped++;else removed++;}catch{skipped++;}
            }finally{
                foreach(var process in processes)process.Dispose();
                if(shellStopped){CommandService.NotifyProcess(expected);using(var started=Process.Start(new ProcessStartInfo(expected){UseShellExecute=true})){} }
            }
            return (rebuild?"图标缓存删除 "+removed+" 项，跳过 "+skipped+" 项。":"")+"已提交本会话资源管理器重启请求，请核对任务栏/窗口；打开的文件夹窗口可能关闭。";
        }
    }
}
