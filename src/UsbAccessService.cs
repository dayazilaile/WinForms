using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace ITDesktopToolkit
{
    // NTFS permissions travel with the volume. This protects against ordinary
    // Windows users, not administrators or operating systems that ignore NTFS ACLs.
    public static class UsbAccessService
    {
        private static readonly SecurityIdentifier Everyone=new SecurityIdentifier(WellKnownSidType.WorldSid,null);
        private static readonly SecurityIdentifier SystemAccount=new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null);
        private static readonly SecurityIdentifier Administrators=new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
        private static extern bool GetVolumeInformation(string root,StringBuilder name,int nameLength,out uint serial,out uint maxComponent,out uint flags,StringBuilder fileSystem,int fileSystemLength);
        private static string VolumeIdentity(string root)
        {
            uint serial,maxComponent,flags;
            if(!GetVolumeInformation(root,null,0,out serial,out maxComponent,out flags,null,0))
                throw new IOException("读取 U 盘卷序列号失败："+Marshal.GetLastWin32Error());
            return serial.ToString("X8",System.Globalization.CultureInfo.InvariantCulture)+"|"+new DriveInfo(root).TotalSize.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        private static string BackupPath(string root)
        {
            string key=VolumeIdentity(root);
            using(var sha=System.Security.Cryptography.SHA256.Create())
                return Path.Combine(Paths.UserDir,"usb_ntfs_acl_"+BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(key))).Replace("-","").Substring(0,24)+".txt");
        }
        private static string NtfsRoot(string root)
        {
            root=UsbProtectionService.ValidateDrive(root);
            if(!String.Equals(new DriveInfo(root).DriveFormat,"NTFS",StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("只有 NTFS U 盘支持此权限设置；当前文件系统不是 NTFS。不会格式化 U 盘。");
            return root;
        }
        private static List<string> Entries(string root)
        {
            var paths=new List<string>();var pending=new Stack<string>();pending.Push(root);
            while(pending.Count>0)
            {
                string dir=pending.Pop();paths.Add(dir);
                foreach(string path in Directory.GetFileSystemEntries(dir))
                {
                    string name=Path.GetFileName(path.TrimEnd('\\'));
                    if(dir==root&&(name.Equals("System Volume Information",StringComparison.OrdinalIgnoreCase)||name.Equals("$RECYCLE.BIN",StringComparison.OrdinalIgnoreCase)))continue;
                    FileAttributes a=File.GetAttributes(path);
                    if((a&FileAttributes.ReparsePoint)!=0)throw new IOException("发现重解析点，未修改任何权限："+path);
                    if((a&FileAttributes.Directory)!=0)pending.Push(path);else paths.Add(path);
                    if(paths.Count+pending.Count>100000)throw new IOException("文件超过 100000 项，未修改权限。");
                }
            }
            return paths;
        }
        private static FileSystemSecurity Security(string path)
        {return Directory.Exists(path)?(FileSystemSecurity)Directory.GetAccessControl(path):(FileSystemSecurity)File.GetAccessControl(path);}
        private static void Apply(string path,FileSystemSecurity security)
        {if(Directory.Exists(path))Directory.SetAccessControl(path,(DirectorySecurity)security);else File.SetAccessControl(path,(FileSecurity)security);}
        private static string Encode(string s){return Convert.ToBase64String(Encoding.UTF8.GetBytes(s));}
        private static string Decode(string s){return Encoding.UTF8.GetString(Convert.FromBase64String(s));}
        public static string Protect(string selectedRoot)
        {
            if(!CommandService.IsAdministrator())throw new InvalidOperationException("请以管理员身份运行工具箱。");
            string root=NtfsRoot(selectedRoot),backup=BackupPath(root);
            if(File.Exists(backup))throw new IOException("已有此 U 盘的权限备份。请先恢复权限，或手动核对备份："+backup);
            List<string> paths=Entries(root);
            // Complete the readable backup before touching any ACL.
            try
            {
                using(var writer=new StreamWriter(new FileStream(backup,FileMode.CreateNew,FileAccess.Write),new UTF8Encoding(false)))
                {
                    writer.WriteLine("ITUSBACL1|"+Encode(root)+"|"+Encode(VolumeIdentity(root)));
                    foreach(string path in paths)
                        writer.WriteLine(Encode(path)+"|"+Encode(Security(path).GetSecurityDescriptorSddlForm(AccessControlSections.Access|AccessControlSections.Owner)));
                }
            }
            catch {if(File.Exists(backup))File.Delete(backup);throw;}
            int changed=0;
            try
            {
                // Children first: the elevated process retains access to the parent until the end.
                for(int i=paths.Count-1;i>=0;i--)
                {
                    string path=paths[i];bool folder=Directory.Exists(path);
                    FileSystemSecurity acl=Security(path);
                    acl.SetAccessRuleProtection(true,false);
                    foreach(FileSystemAccessRule rule in acl.GetAccessRules(true,false,typeof(SecurityIdentifier)))
                        acl.RemoveAccessRuleSpecific(rule);
                    InheritanceFlags inherit=folder?InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit:InheritanceFlags.None;
                    acl.AddAccessRule(new FileSystemAccessRule(Everyone,FileSystemRights.ReadAndExecute,inherit,PropagationFlags.None,AccessControlType.Allow));
                    acl.AddAccessRule(new FileSystemAccessRule(SystemAccount,FileSystemRights.FullControl,inherit,PropagationFlags.None,AccessControlType.Allow));
                    acl.AddAccessRule(new FileSystemAccessRule(Administrators,FileSystemRights.FullControl,inherit,PropagationFlags.None,AccessControlType.Allow));
                    acl.SetOwner(SystemAccount);
                    Apply(path,acl);changed++;
                }
            }
            catch(Exception ex)
            {
                return "[部分完成] 已修改 "+changed+" 项，随后失败："+ex.Message+"\r\n请用本机备份恢复权限："+backup;
            }
            return "已为 "+changed+" 项设置 NTFS 权限：普通用户读取和运行、SYSTEM 为所有者且有完全控制；管理员保留完全控制以便维护和恢复。\r\n备份："+backup+"\r\n请换一台普通用户 Windows 电脑测试新建文件。管理员仍能写入；非 Windows 系统可能不遵守 NTFS 权限。";
        }
        public static string Restore(string selectedRoot)
        {
            if(!CommandService.IsAdministrator())throw new InvalidOperationException("请以管理员身份运行工具箱。");
            string root=NtfsRoot(selectedRoot),backup=BackupPath(root);
            if(!File.Exists(backup))throw new FileNotFoundException("没有找到此 U 盘的本机权限备份。",backup);
            string[] lines=File.ReadAllLines(backup,Encoding.UTF8);
            string[] header=lines.Length>0?lines[0].Split('|'):new string[0];
            if(lines.Length<2||header.Length!=3||header[0]!="ITUSBACL1"||Decode(header[2])!=VolumeIdentity(root))
                throw new IOException("备份格式或 U 盘卷序列号不匹配，未恢复。");
            string originalRoot=Decode(header[1]);
            if(!String.Equals(Path.GetPathRoot(originalRoot),originalRoot,StringComparison.OrdinalIgnoreCase))throw new IOException("备份根目录无效，未恢复。");
            int restored=0,missing=0;
            // Restore parent first to regain access, then individual children.
            for(int i=1;i<lines.Length;i++)
            {
                string[] parts=lines[i].Split('|');if(parts.Length!=2)throw new IOException("备份格式错误，停止恢复。");
                string oldPath=Decode(parts[0]);
                if(!oldPath.StartsWith(originalRoot,StringComparison.OrdinalIgnoreCase))throw new IOException("备份路径超出所选 U 盘，停止恢复。");
                string path=Path.Combine(root,oldPath.Substring(originalRoot.Length));
                if(!File.Exists(path)&&!Directory.Exists(path)){missing++;continue;}
                FileSystemSecurity acl=Security(path);
                acl.SetSecurityDescriptorSddlForm(Decode(parts[1]),AccessControlSections.Access|AccessControlSections.Owner);
                Apply(path,acl);restored++;
            }
            if(missing==0)
            {
                File.Delete(backup);
                return "已恢复 "+restored+" 项原权限；备份已清除，可以再次设置只读权限。";
            }
            return "已恢复 "+restored+" 项权限；原路径不存在 "+missing+" 项。备份保留在："+backup;
        }
        public static string TakeFolder(string folder)
        {
            if(!CommandService.IsAdministrator())throw new InvalidOperationException("请以管理员身份运行工具箱。");
            if(String.IsNullOrWhiteSpace(folder)||!Directory.Exists(folder))throw new DirectoryNotFoundException("请选择存在的文件夹。");
            string full=Path.GetFullPath(folder);
            if(String.Equals(full,Path.GetPathRoot(full),StringComparison.OrdinalIgnoreCase))throw new ArgumentException("不能对整个磁盘根目录批量取得权限。");
            full=full.TrimEnd('\\');
            if((File.GetAttributes(full)&FileAttributes.ReparsePoint)!=0)throw new IOException("不能对重解析目录批量取得权限。");
            string result=CommandService.Run("takeown.exe","/F \""+full+"\" /A /R /D Y",10);
            if(!result.Contains("返回代码：0"))return "取得所有权失败或未完成：\r\n"+result;
            string grant=CommandService.Run("icacls.exe","\""+full+"\" /grant *S-1-5-32-544:(OI)(CI)F /T /C",10);
            return "目标文件夹："+full+"\r\n所有者已设置为 Administrators 组。\r\n"+grant+"\r\n如有失败项，请按上面的命令输出逐项核对。";
        }
    }
}
