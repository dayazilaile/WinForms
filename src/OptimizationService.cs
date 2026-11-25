using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Xml;

namespace WinForms
{
    public sealed class OptimizationOption
    {
        public readonly string Id,Name,Help;public readonly bool Recommended,Admin;
        public OptimizationOption(string id,string name,string help,bool recommended,bool admin){Id=id;Name=name;Help=help;Recommended=recommended;Admin=admin;}
    }
    public sealed class OptimizationResult
    {
        public string Id="",Name="",Status="",Detail="",Backup="";
    }
    internal interface IOptimizationBackend
    {
        string Context {get;}
        Dictionary<string,string> Capture(string id,CancellationToken token);
        string Apply(string id,Dictionary<string,string> before,CancellationToken token);
        string Verify(string id,Dictionary<string,string> before,bool restored,CancellationToken token);
        void Restore(string id,Dictionary<string,string> before,CancellationToken token);
    }
    internal sealed class OptimizationEntry
    {
        internal string Id,Phase="准备",Detail="";internal Dictionary<string,string> Before;
    }
    internal sealed class OptimizationJournal
    {
        [DllImport("kernel32.dll",EntryPoint="MoveFileExW",ExactSpelling=true,CharSet=CharSet.Unicode,SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool MoveFileEx(string from,string to,uint flags);
        internal string Context="",Created=DateTimeOffset.Now.ToString("o");internal readonly List<OptimizationEntry> Entries=new List<OptimizationEntry>();
        internal void Save(string path)
        {
            string directory=Path.GetDirectoryName(Path.GetFullPath(path));Directory.CreateDirectory(directory);string temp=Path.Combine(directory,"journal-"+Guid.NewGuid().ToString("N")+".tmp");
            try{using(var file=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough)){using(var writer=XmlWriter.Create(file,new XmlWriterSettings{Encoding=new UTF8Encoding(false),Indent=true})){
                writer.WriteStartElement("Optimization");writer.WriteAttributeString("version","1");writer.WriteAttributeString("context",Context);writer.WriteAttributeString("created",Created);
                foreach(var item in Entries){writer.WriteStartElement("Entry");writer.WriteAttributeString("id",item.Id);writer.WriteAttributeString("phase",item.Phase);writer.WriteElementString("Detail",item.Detail);
                    foreach(var field in item.Before){writer.WriteStartElement("Value");writer.WriteAttributeString("key",field.Key);writer.WriteString(Convert.ToBase64String(new UTF8Encoding(false,true).GetBytes(field.Value??"")));writer.WriteEndElement();}writer.WriteEndElement();}writer.WriteEndElement();}file.Flush(true);}
                if(new FileInfo(temp).Length>4*1024*1024)throw new InvalidDataException("优化备份超过4 MiB，未替换原备份，也不开始当前项修改。");
                if(Environment.OSVersion.Platform==PlatformID.Win32NT){if(!MoveFileEx(temp,path,9))throw new IOException("无法替换软件目录备份："+new Win32Exception(Marshal.GetLastWin32Error()).Message);}
                else if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
            }finally{if(File.Exists(temp))File.Delete(temp);}
        }
        internal static OptimizationJournal Load(string path)
        {
            if(!File.Exists(path)||new FileInfo(path).Length>4*1024*1024)throw new InvalidDataException("优化备份不存在或超过4 MiB。");
            var doc=new XmlDocument{XmlResolver=null};using(var reader=XmlReader.Create(path,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=4*1024*1024}))doc.Load(reader);
            var root=doc.DocumentElement;if(root==null||root.Name!="Optimization"||root.GetAttribute("version")!="1")throw new InvalidDataException("不是本功能的备份。");
            var journal=new OptimizationJournal{Context=root.GetAttribute("context"),Created=root.GetAttribute("created")};var seen=new HashSet<string>(StringComparer.Ordinal);
            foreach(XmlNode node in root.ChildNodes){var element=node as XmlElement;if(element==null||element.Name!="Entry")throw new InvalidDataException("备份结构错误。");string id=element.GetAttribute("id");OptimizationService.Find(id);if(!seen.Add(id)||seen.Count>32)throw new InvalidDataException("备份项目重复或过多。");
                string phase=element.GetAttribute("phase");if(phase!="准备"&&phase!="执行中"&&phase!="已核验"&&phase!="需恢复检查"&&phase!="已恢复")throw new InvalidDataException("备份阶段无效。");
                var entry=new OptimizationEntry{Id=id,Phase=phase,Before=new Dictionary<string,string>(StringComparer.Ordinal)};
                foreach(XmlNode child in element.ChildNodes){var item=child as XmlElement;if(item==null)throw new InvalidDataException("备份字段错误。");if(item.Name=="Detail"){entry.Detail=item.InnerText;continue;}string key=item.GetAttribute("key");if(item.Name!="Value"||key.Length==0||key.Length>100||entry.Before.ContainsKey(key)||entry.Before.Count>200)throw new InvalidDataException("备份字段重复或无效。");entry.Before.Add(key,new UTF8Encoding(false,true).GetString(Convert.FromBase64String(item.InnerText)));}journal.Entries.Add(entry);
            }return journal;
        }
    }
    public static class OptimizationService
    {
        private static readonly OptimizationOption[] options={
            new OptimizationOption("power","高性能 / 永不熄屏 / 永不睡眠","所有设备启用高性能；没有则创建名称为“高性能”的计划。AC/DC均从不，笔记本合盖不操作；保留低电量和过热保护。",true,true),
            new OptimizationOption("extensions","显示文件扩展名","显示已知类型文件的扩展名。",true,false),
            new OptimizationOption("taskbar","隐藏天气，搜索仅图标","按Win10/11配置；注册表值核验不等于已刷新任务栏。",true,false),
            new OptimizationOption("explorer","打开此电脑 / 关闭最近与常用","资源管理器默认此电脑，关闭最近文件和常用文件夹展示。",true,false),
            new OptimizationOption("ads","关闭系统广告和推荐","配置开始菜单、锁屏、资源管理器的推荐开关；不卸载现有应用。",true,false),
            new OptimizationOption("delivery","关闭跨电脑更新下载","传递优化使用HTTP下载，不进行跨电脑P2P。",true,true),
            new OptimizationOption("preview","开启预览处理器和缩略图","启用已安装的预览处理器/缩略图；预览窗格可在资源管理器按Alt+P切换，不盲目翻转当前窗口。",true,false),
            new OptimizationOption("desktop","显示此电脑 / 控制面板 / 网络","配置当前用户桌面图标显示，执行后刷新图标。",true,false),
            new OptimizationOption("history","关闭文件历史记录","设置文件历史记录禁用策略；不删除旧备份，不修改系统还原。",true,true),
            new OptimizationOption("time","启用自动校时并立即同步","保存原时间服务和配置，沿用域时间源或现有NTP源；立即同步失败会单独提示。恢复不回拨时钟。",true,true),
            new OptimizationOption("folders","新文档 / 音乐 / 图片 / 视频到D盘","修改当前用户标准文件夹指向，不搬运旧文件。OneDrive/网络重定向跳过；先核对本机D盘，文件夹创建后保留。",false,false),
            new OptimizationOption("apps","新商店应用默认安装D盘","仅Windows支持的Appx应用；普通EXE安装器自行决定。无可用D盘则跳过，可能需注册D盘应用卷。",false,true),
            new OptimizationOption("web","解除当前用户代理 / PAC / 旧主页","清除用户代理/PAC并重置传统主页，先备份；不改DNS、hosts、浏览器企业策略或快捷方式。可按备份恢复。",false,false),
            new OptimizationOption("updates","关闭Windows自动更新（可恢复）","配置NoAutoUpdate，不停用Defender病毒库、Office或商店更新；家庭版/组织策略可能忽略或覆盖，不承诺永久。",false,true)
        };
        public static OptimizationOption[] Options {get{return (OptimizationOption[])options.Clone();}}
        public static OptimizationOption Find(string id){foreach(var option in options)if(option.Id==id)return option;throw new ArgumentException("未知优化项目："+id);}
        internal static readonly SemaphoreSlim Gate=new SemaphoreSlim(1,1);
        internal static void Apply(IEnumerable<string> selected,IOptimizationBackend backend,string backup,CancellationToken token,Action<OptimizationResult> receive)
        {
            var wanted=new HashSet<string>(StringComparer.Ordinal);foreach(string id in selected){Find(id);if(!wanted.Add(id))throw new ArgumentException("优化项目重复。");}if(wanted.Count==0)throw new ArgumentException("请先勾选项目。");
            token.ThrowIfCancellationRequested();var journal=new OptimizationJournal{Context=backend.Context};journal.Save(backup);
            foreach(var option in options)if(wanted.Contains(option.Id)){
                token.ThrowIfCancellationRequested();var result=new OptimizationResult{Id=option.Id,Name=option.Name,Backup=backup};OptimizationEntry entry=null;
                try{var before=backend.Capture(option.Id,token);entry=new OptimizationEntry{Id=option.Id,Before=before};journal.Entries.Add(entry);journal.Save(backup);
                    // Durable intent precedes mutation. Never cancel between a native write and recording its outcome.
                    entry.Phase="执行中";journal.Save(backup);string applied=backend.Apply(option.Id,before,CancellationToken.None);result.Detail=applied+"\r\n"+backend.Verify(option.Id,before,false,CancellationToken.None);result.Status="配置已核验";entry.Phase="已核验";
                }catch(OperationCanceledException){if(entry==null&&token.IsCancellationRequested)throw;result.Status="失败 / 需检查";result.Detail="当前项目异常取消，可能存在部分修改，请从备份恢复检查。";if(entry!=null)entry.Phase="需恢复检查";}
                catch(NotSupportedException ex){result.Status=entry==null?"不支持 / 跳过":"未完全应用 / 需检查";result.Detail=ex.Message;if(entry!=null)entry.Phase="需恢复检查";}
                catch(Exception ex){result.Status="失败 / 需检查";result.Detail=ex.Message;if(entry!=null)entry.Phase="需恢复检查";}
                if(entry!=null){entry.Detail=result.Status+"："+result.Detail;try{journal.Save(backup);}catch(Exception ex){result.Status="备份写入失败 / 已停止";result.Detail+="\r\n"+ex.Message;receive(result);throw new IOException("备份无法更新，停止后续项目。请保留上次成功写入的备份，检查已执行项目。",ex);}}
                OperationAudit.Record("系统优化 / "+option.Name,result.Status,result.Detail);receive(result);
            }
        }
        internal static void Restore(string backup,IOptimizationBackend backend,CancellationToken token,Action<OptimizationResult> receive)
        {
            var journal=OptimizationJournal.Load(backup);if(journal.Context!=backend.Context)throw new InvalidOperationException("备份属于另一台电脑或另一个用户，或系统Build已改变，不允许猜测式恢复。");
            for(int i=journal.Entries.Count-1;i>=0;i--){var entry=journal.Entries[i];if(entry.Phase=="准备"||entry.Phase=="已恢复")continue;token.ThrowIfCancellationRequested();var option=Find(entry.Id);var result=new OptimizationResult{Id=entry.Id,Name=option.Name,Backup=backup};
                try{backend.Restore(entry.Id,entry.Before,CancellationToken.None);result.Detail=backend.Verify(entry.Id,entry.Before,true,CancellationToken.None);result.Status="原设置已核验";entry.Phase="已恢复";}
                catch(Exception ex){result.Status="恢复失败 / 可重试";result.Detail=ex.Message;}entry.Detail=result.Status+"："+result.Detail;journal.Save(backup);OperationAudit.Record("恢复优化 / "+option.Name,result.Status,result.Detail);receive(result);
            }
        }
    }
}
