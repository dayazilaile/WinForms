using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace WinForms
{
    public sealed class UsbFinding
    {
        public string Path, Kind, Detail, Hash;
        public override string ToString(){return "["+Kind+"] "+Path+"\r\n"+Detail+(String.IsNullOrEmpty(Hash)?"":"\r\nSHA256="+Hash);}
    }
    public sealed class UsbScanResult
    {
        public int Files, Folders, Errors; public bool Cancelled, Limited;
        public readonly List<UsbFinding> Findings=new List<UsbFinding>();
        public string Report()
        {
            StringBuilder b=new StringBuilder("U 盘只读检查 · "+DateTimeOffset.Now.ToString("o")+"\r\n");
            b.AppendLine("文件 "+Files+"，文件夹 "+Folders+"，无法读取 "+Errors+"；"+(Cancelled?"已停止":Limited?"达到扫描上限":"遍历完成"));
            b.AppendLine("可疑迹象不是病毒确诊；未发现迹象不代表安全。不会执行文件/快捷方式，不修改属性，不删除资料。跳过重解析点。\r\n");
            foreach(var f in Findings)b.AppendLine(f+"\r\n");
            if(Findings.Count==0)b.AppendLine("未发现本次规则覆盖的异常迹象；仍需使用已更新的杀毒软件检查。");
            return b.ToString();
        }
    }
    public static class UsbProtectionService
    {
        private static readonly object PolicyLock=new object();
        public static string ValidateDrive(string root)
        {
            string full=System.IO.Path.GetFullPath(root);
            string driveRoot=System.IO.Path.GetPathRoot(full);
            if(!String.Equals(full,driveRoot,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("必须选择 U 盘根目录。");
            DriveInfo drive=new DriveInfo(driveRoot);
            if(!drive.IsReady||drive.DriveType!=DriveType.Removable)throw new ArgumentException("请选择已就绪、被 Windows 识别为可移动存储的 U 盘。");
            return full;
        }
        public static UsbScanResult Scan(string root,CancellationToken cancel)
        { return ScanFolder(ValidateDrive(root),cancel); }
        internal static UsbScanResult ScanFolder(string root,CancellationToken cancel)
        {
            var result=new UsbScanResult(); var pending=new Stack<string>(); pending.Push(root);
            if((File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0)throw new IOException("不扫描重解析目录。");
            while(pending.Count>0)
            {
                if(cancel.IsCancellationRequested){result.Cancelled=true;break;}
                if(result.Files+result.Folders>=50000||result.Findings.Count>=5000){result.Limited=true;break;}
                string dir=pending.Pop();
                try
                {
                    foreach(string path in Directory.EnumerateFileSystemEntries(dir))
                    {
                        if(cancel.IsCancellationRequested){result.Cancelled=true;break;}
                        if(result.Files+result.Folders>=50000||result.Findings.Count>=5000){result.Limited=true;break;}
                        try
                        {
                            FileAttributes attr=File.GetAttributes(path);
                            if((attr&FileAttributes.ReparsePoint)!=0){result.Findings.Add(new UsbFinding{Path=path,Kind="跳过",Detail="重解析点：未跟随目标。"});continue;}
                            bool folder=(attr&FileAttributes.Directory)!=0;
                            string name=System.IO.Path.GetFileName(path), ext=System.IO.Path.GetExtension(path).ToLowerInvariant();
                            if(folder){result.Folders++;if(!name.Equals("System Volume Information",StringComparison.OrdinalIgnoreCase)&&!name.Equals("$RECYCLE.BIN",StringComparison.OrdinalIgnoreCase))pending.Push(path);}
                            else result.Files++;
                            var reasons=new List<string>();
                            if((attr&FileAttributes.Hidden)!=0)reasons.Add("存在隐藏属性，可能是正常设置，也可能是资料被隐藏。");
                            if(name.Equals("autorun.inf",StringComparison.OrdinalIgnoreCase))reasons.Add("存在自动运行配置；不读取执行指令、不执行。文件本身不能证明感染。");
                            if(!folder&&ext==".lnk")
                            {
                                string target=ReadShortcut(path); reasons.Add(target);
                                string same=System.IO.Path.Combine(dir,System.IO.Path.GetFileNameWithoutExtension(path));
                                if(Directory.Exists(same))reasons.Add("存在同名文件夹，请核对是否为快捷方式伪装。");
                            }
                            if(!folder&&(ext==".exe"||ext==".scr"||ext==".com"||ext==".cmd"||ext==".bat"||ext==".vbs"||ext==".js"||ext==".ps1"))
                            {
                                if(dir==root)reasons.Add("根目录可执行文件/脚本，可能是正常工具，需核对来源。");
                                string inner=System.IO.Path.GetExtension(System.IO.Path.GetFileNameWithoutExtension(path)).ToLowerInvariant();
                                if(inner==".doc"||inner==".docx"||inner==".pdf"||inner==".jpg"||inner==".xlsx"||inner==".txt")reasons.Add("文档/图片后缀后又接可执行扩展名，存在伪装迹象。");
                                if(Directory.Exists(System.IO.Path.Combine(dir,System.IO.Path.GetFileNameWithoutExtension(path))))reasons.Add("可执行文件与文件夹同名，存在伪装迹象。");
                            }
                            if(reasons.Count>0)
                            {
                                string hash=""; if(!folder){var info=new FileInfo(path);if(info.Length<=64*1024*1024)hash=Hash(path);else reasons.Add("文件超过 64 MiB，未计算哈希。");}
                                result.Findings.Add(new UsbFinding{Path=path,Kind="需核对",Detail=String.Join("\r\n",reasons.ToArray()),Hash=hash});
                            }
                        }
                        catch(Exception ex){result.Errors++;result.Findings.Add(new UsbFinding{Path=path,Kind="无法检查",Detail=ex.Message});}
                    }
                }
                catch(Exception ex){result.Errors++;result.Findings.Add(new UsbFinding{Path=dir,Kind="无法遍历",Detail=ex.Message});}
            }
            return result;
        }
        private static string Hash(string path)
        {
            using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))
            using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","");
        }
        private static string ReadShortcut(string path)
        {
            if(Environment.OSVersion.Platform!=PlatformID.Win32NT)return "快捷方式：当前平台未解析目标。";
            object shell=null,link=null;
            try
            {
                Type t=Type.GetTypeFromProgID("WScript.Shell"); shell=Activator.CreateInstance(t);
                link=t.InvokeMember("CreateShortcut",System.Reflection.BindingFlags.InvokeMethod,null,shell,new object[]{path});
                Type lt=link.GetType();
                string target=Convert.ToString(lt.InvokeMember("TargetPath",System.Reflection.BindingFlags.GetProperty,null,link,null));
                string args=Convert.ToString(lt.InvokeMember("Arguments",System.Reflection.BindingFlags.GetProperty,null,link,null));
                string baseName=System.IO.Path.GetFileName(target).ToLowerInvariant();
                bool interpreter=baseName=="cmd.exe"||baseName=="powershell.exe"||baseName=="wscript.exe"||baseName=="cscript.exe"||baseName=="mshta.exe"||baseName=="rundll32.exe";
                return "快捷方式目标（未执行）："+target+"\r\n参数："+args+(interpreter?"\r\n目标为脚本/命令解释器，需要重点核对。":"");
            }
            catch(Exception ex){return "快捷方式目标未能读取："+ex.Message;}
            finally{if(link!=null&&Marshal.IsComObject(link))Marshal.FinalReleaseComObject(link);if(shell!=null&&Marshal.IsComObject(shell))Marshal.FinalReleaseComObject(shell);}
        }
        private static string PolicyPath(bool writeProtect){return writeProtect?@"SYSTEM\CurrentControlSet\Control\StorageDevicePolicies":@"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";}
        private static string PolicyName(bool writeProtect){return writeProtect?"WriteProtect":"NoDriveTypeAutoRun";}
        private static RegistryKey PolicyRoot(bool writeProtect){return writeProtect?Registry.LocalMachine:Registry.CurrentUser;}
        private static string BackupFile(bool writeProtect){return System.IO.Path.Combine(Paths.MachineDir,writeProtect?"usb_writeprotect_before_v045.txt":"autoplay_before_v045.txt");}
        public static string PolicyStatus()
        {
            StringBuilder b=new StringBuilder();
            foreach(bool wp in new bool[]{false,true})using(RegistryKey k=PolicyRoot(wp).OpenSubKey(PolicyPath(wp)))
                b.AppendLine((wp?"本机 USB 存储写保护":"当前账户自动播放策略")+"："+(k==null?"未设置":Convert.ToString(k.GetValue(PolicyName(wp),"未设置")))+"；原值备份："+(File.Exists(BackupFile(wp))?"已有":"无"));
            b.AppendLine("NoDriveTypeAutoRun=255 表示该账户禁用所有驱动器自动播放；WriteProtect=1 表示请求本机存储写保护，通常需重新插拔。");
            b.AppendLine("策略可能受组织策略或设备驱动影响，设置成功后仍须验证。仅影响本机，不能保护 U 盘插入其他电脑后的写入，也不阻止手动运行文件。");
            return b.ToString();
        }
        public static string SetPolicy(bool writeProtect,bool restore)
        {
            if(writeProtect&&!CommandService.IsAdministrator())throw new InvalidOperationException("本机 USB 写保护需要管理员权限。");
            lock(PolicyLock)
            {
                string file=BackupFile(writeProtect), name=PolicyName(writeProtect); RegistryKey root=PolicyRoot(writeProtect);
                if(restore&&!File.Exists(file))throw new InvalidOperationException("没有修改前备份，未更改策略。");
                using(RegistryKey key=root.CreateSubKey(PolicyPath(writeProtect)))
                {
                    if(!restore&&!File.Exists(file))
                    {
                        object old=key.GetValue(name); if(old!=null&&key.GetValueKind(name)!=RegistryValueKind.DWord)throw new InvalidOperationException("现有策略不是 DWORD，未修改。");
                        string saved=old==null?"absent":Convert.ToInt32(old).ToString(System.Globalization.CultureInfo.InvariantCulture);
                        using(var f=new FileStream(file,FileMode.CreateNew,FileAccess.Write))using(var w=new StreamWriter(f,Encoding.UTF8))w.Write(saved);
                    }
                    if(restore)
                    {
                        string saved=File.ReadAllText(file,Encoding.UTF8).Trim();
                        if(saved=="absent")key.DeleteValue(name,false);else key.SetValue(name,Int32.Parse(saved,System.Globalization.CultureInfo.InvariantCulture),RegistryValueKind.DWord);
                        object actual=key.GetValue(name);
                        if(saved=="absent"?actual!=null:!Object.Equals(actual,Int32.Parse(saved,System.Globalization.CultureInfo.InvariantCulture)))throw new IOException("还原后核对失败。");
                    }
                    else
                    {
                        int value=writeProtect?1:255; key.SetValue(name,value,RegistryValueKind.DWord);
                        if(!Object.Equals(key.GetValue(name),value))throw new IOException("策略写入后核对失败。");
                    }
                }
                return (restore?"已恢复首次保存的原策略值。":"已写入并核对策略值。")+"\r\n"+PolicyStatus();
            }
        }
        public static string DefenderHistory(string root)
        {
            root=ValidateDrive(root);
            return PowerShellService.RunScript("$ErrorActionPreference='Stop'; try { $root="+PowerShellService.Quote(root)+
                "; if(-not(Get-Command Get-MpThreatDetection -ErrorAction SilentlyContinue)){throw 'Defender 管理接口不可用，请查看已安装杀毒软件的报告'}; $items=@(Get-MpThreatDetection | Where-Object { $matched=$false; foreach($r in $_.Resources){if(([string]$r).IndexOf($root,[StringComparison]::OrdinalIgnoreCase) -ge 0){$matched=$true}}; $matched }); if($items.Count -eq 0){'没有查询到此盘符的 Defender 历史，不代表安全；其他杀毒软件的记录不在此处。'}; $names=@(Get-MpThreat); foreach($i in $items){ $n=$names|Where-Object {$_.ThreatID -eq $i.ThreatID}|Select-Object -First 1; $i | Select-Object @{Name='ThreatName';Expression={$n.ThreatName}},ThreatID,InitialDetectionTime,LastThreatStatusChangeTime,ActionSuccess,ThreatStatusID,Resources | Format-List | Out-String -Width 240 }; '历史按盘符筛选，盘符可能被其他设备复用，不能仅据此认定当前 U 盘感染。' }catch {Write-Output $_.Exception.Message;exit 1}",2);
        }
    }
}
