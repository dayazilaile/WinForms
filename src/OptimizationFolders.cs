using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace WinForms
{
    internal static class OptimizationFolders
    {
        internal static readonly string[] Names={"Documents","Music","Pictures","Videos"};
        private static readonly Guid[] Ids={new Guid("FDD39AD0-238F-46AF-ADB4-6C85480369C7"),new Guid("4BD8D571-6D19-48D3-BE97-422220080E43"),new Guid("33E28130-4E1E-4676-835A-98395C3BC3BB"),new Guid("18989B1D-99B5-455B-841C-AB7C74E4DDFC")};
        [DllImport("shell32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]private static extern int SHGetKnownFolderPath(ref Guid id,uint flags,IntPtr token,out IntPtr path);
        [DllImport("shell32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]private static extern int SHSetKnownFolderPath(ref Guid id,uint flags,IntPtr token,string path);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern SafeFileHandle CreateFile(string file,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
        [DllImport("kernel32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool DeviceIoControl(SafeFileHandle device,uint code,byte[] input,int inputSize,byte[] output,int outputSize,out int returned,IntPtr overlapped);
        internal static bool InternalBus(int bus){return bus==1||bus==3||bus==8||bus==10||bus==11||bus==16||bus==17;}
        internal static void CheckDataDrive()
        {
            var drive=new DriveInfo(@"D:\");if(!drive.IsReady||drive.DriveType!=DriveType.Fixed)throw new NotSupportedException("D盘不是已就绪的本机固定数据盘，跳过；不会自动选U盘或其他盘符。");
            if(String.Equals(Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)),@"D:\",StringComparison.OrdinalIgnoreCase))throw new NotSupportedException("D盘是当前Windows系统盘，不按数据盘处理。");
            using(var handle=CreateFile(@"\\.\D:",0,7,IntPtr.Zero,3,0,IntPtr.Zero)){
                if(handle.IsInvalid)throw new NotSupportedException("无法确认D盘的物理连接类型，未猜测为内置盘。");var input=new byte[12];var output=new byte[1024];int length;
                if(!DeviceIoControl(handle,0x002D1400,input,input.Length,output,output.Length,out length,IntPtr.Zero)||length<36||!InternalBus(BitConverter.ToInt32(output,28)))throw new NotSupportedException("D盘不是可确认的内置磁盘（USB/移动/网络/未知总线跳过）。");
            }
            string probe=Path.Combine(@"D:\",".write-check-"+Guid.NewGuid().ToString("N")+".tmp");try{using(var file=new FileStream(probe,FileMode.CreateNew,FileAccess.Write,FileShare.None))file.Flush();}finally{if(File.Exists(probe))File.Delete(probe);}
        }
        private static string Read(int index){IntPtr pointer=IntPtr.Zero;try{int code=SHGetKnownFolderPath(ref Ids[index],0,IntPtr.Zero,out pointer);Marshal.ThrowExceptionForHR(code);return Marshal.PtrToStringUni(pointer);}finally{if(pointer!=IntPtr.Zero)Marshal.FreeCoTaskMem(pointer);}}
        private static void Write(int index,string path){int code=SHSetKnownFolderPath(ref Ids[index],0,IntPtr.Zero,path);Marshal.ThrowExceptionForHR(code);}
        internal static bool SamePath(string a,string b){return String.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase);}
        internal static bool Under(string path,string root){return SamePath(path,root)||Path.GetFullPath(path).StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);}
        private static void Validate(string path)
        {
            if(String.IsNullOrWhiteSpace(path)||!Path.IsPathRooted(path)||path.StartsWith(@"\\",StringComparison.Ordinal)||path.IndexOfAny(new[]{'\0','\r','\n'})>=0)throw new InvalidOperationException("标准文件夹不是有效的本机路径。");
            string full=Path.GetFullPath(path),root=Path.GetPathRoot(full);if(SamePath(full,root)||new DriveInfo(root).DriveType!=DriveType.Fixed||Under(full,Environment.GetFolderPath(Environment.SpecialFolder.Windows)))throw new InvalidOperationException("文件夹路径不能是盘符根目录、Windows或非固定盘。");
        }
        internal static Dictionary<string,string> Capture()
        {
            CheckDataDrive();string user=Regex.Replace(Environment.UserName,@"[^\p{L}\p{N}_.-]","_");string sid;using(var identity=WindowsIdentity.GetCurrent())sid=identity.User.Value;string target=Path.Combine(@"D:\UserData",user+"-"+sid.Substring(sid.Length-8));
            var state=new Dictionary<string,string>();for(int i=0;i<Names.Length;i++){
                string old=Read(i);Validate(old);OptimizationMaintenance.NoLinks(old);foreach(string env in new[]{"OneDrive","OneDriveConsumer","OneDriveCommercial"}){string cloud=Environment.GetEnvironmentVariable(env);if(!String.IsNullOrWhiteSpace(cloud)&&Under(old,cloud))throw new NotSupportedException("当前标准文件夹受OneDrive重定向，整组跳过，请先在系统中核对同步设置。");}
                state[Names[i]+".old"]=old;state[Names[i]+".new"]=Path.Combine(target,Names[i]);
            }return state;
        }
        private static void Set(Dictionary<string,string> state,bool restore)
        {
            var paths=new string[Names.Length];for(int i=0;i<Names.Length;i++){paths[i]=state[Names[i]+(restore?".old":".new")];Validate(paths[i]);OptimizationMaintenance.NoLinks(paths[i]);if(!restore&&!Under(paths[i],@"D:\UserData"))throw new InvalidOperationException("目标文件夹不在预期D盘目录。");}
            for(int i=0;i<Names.Length;i++){Directory.CreateDirectory(paths[i]);OptimizationMaintenance.NoLinks(paths[i]);Write(i,paths[i]);}
        }
        internal static string Apply(Dictionary<string,string> state){CheckDataDrive();Set(state,false);return "当前用户文档/音乐/图片/视频的标准文件夹已指向D盘。旧文件未移动；应用可能需重新打开。";}
        internal static void Restore(Dictionary<string,string> state){Set(state,true);}
        internal static string Verify(Dictionary<string,string> state,bool restored){for(int i=0;i<Names.Length;i++)if(!SamePath(Read(i),state[Names[i]+(restored?".old":".new")]))throw new InvalidOperationException("标准文件夹核验不符："+Names[i]);return "已重新查询四类标准文件夹路径；恢复不搬运文件，不删除D盘已创建的文件夹/内容。";}
    }
}
