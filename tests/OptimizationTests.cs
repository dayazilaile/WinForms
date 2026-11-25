using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;
using WinForms;

class OptimizationTests
{
    static int passed;
    static void Check(bool value,string name){if(!value)throw new Exception(name);passed++;Console.WriteLine("PASS "+name);}
    static void Fails(Action action,string name){try{action();}catch{Check(true,name);return;}throw new Exception("Expected rejection: "+name);}
    sealed class Backend:IOptimizationBackend
    {
        public string Context {get;set;}public readonly Dictionary<string,string> Values=new Dictionary<string,string>();public readonly List<string> Calls=new List<string>();public string Backup,Unsupported="",BadVerify="";public Action<string> Mutated;
        public Backend(){Context="PC|GUID|SID|BUILD";foreach(var option in OptimizationService.Options)Values[option.Id]="old-"+option.Id;}
        public Dictionary<string,string> Capture(string id,CancellationToken token){Calls.Add("capture:"+id);token.ThrowIfCancellationRequested();if(id==Unsupported)throw new NotSupportedException("模拟不支持");return new Dictionary<string,string>{{"value",Values[id]}};}
        public string Apply(string id,Dictionary<string,string> before,CancellationToken token){Calls.Add("apply:"+id);var journal=OptimizationJournal.Load(Backup);Check(journal.Entries.Single(e=>e.Id==id).Phase=="执行中"&&journal.Entries.Single(e=>e.Id==id).Before["value"]==Values[id],"Durable original and intent exist before mutation: "+id);Values[id]="new";if(Mutated!=null)Mutated(id);return "模拟写入";}
        public string Verify(string id,Dictionary<string,string> before,bool restored,CancellationToken token){if(!restored&&id==BadVerify)throw new IOException("模拟核验失败");if(Values[id]!=(restored?before["value"]:"new"))throw new IOException("状态不符");return "核验";}
        public void Restore(string id,Dictionary<string,string> before,CancellationToken token){Calls.Add("restore:"+id);Values[id]=before["value"];}
    }
    static string Root;
    static string FileAt(string name){return Path.Combine(Root,name);}
    static void Catalog()
    {
        Check(OptimizationService.Options.Length==14&&OptimizationService.Options.Count(o=>o.Recommended)==10,"Fourteen scoped options with ten recommended settings, no automatic maintenance");
        Check(!OptimizationService.Options.Any(o=>new[]{"defender","uac","temp","startup","longpath"}.Contains(o.Id)),"Default optimization cannot disable protection/UAC or silently clean Temp/startup items");
        var values=OptimizationRegistry.Values("taskbar",19045);Check(values.Any(v=>v.Name=="SearchboxTaskbarMode"&&(int)v.Desired==1)&&values.Any(v=>v.Name=="ShellFeedsTaskbarViewMode"&&(int)v.Desired==2),"Win10 taskbar selects search icon and hides weather");
        Check(OptimizationRegistry.Values("taskbar",26100).Any(v=>v.Name=="TaskbarDa"&&(int)v.Desired==0),"Win11 widget config is distinct from Win10 feeds");Fails(()=>OptimizationRegistry.Values("taskbar",7601),"Unsupported Win7 taskbar is not represented as applied");
        Check((int)OptimizationRegistry.Values("extensions",7601).Single().Desired==0&&OptimizationRegistry.Values("desktop",7601).Count==7,"Win7 supported extension and desktop icon settings remain available");
        Check(OptimizationRegistry.Values("updates",26100).Single().Name=="NoAutoUpdate"&&OptimizationRegistry.Values("delivery",26100).Single().Desired.Equals(0),"Update control is separate from HTTP-only delivery optimization");
        Check(OptimizationRegistry.Values("preview",26100).Any(v=>v.Name=="IconsOnly"&&v.Desired.Equals(0))&&OptimizationRegistry.Values("preview",26100).Any(v=>v.Name=="ShowPreviewHandlers"&&v.Desired.Equals(1)),"Preview enables supported handlers/thumbnails without blindly toggling an existing pane");
        Check(OptimizationRegistry.Values("web",26100).Count==3&&OptimizationRegistry.Values("web",26100).All(v=>!v.Machine)&&OptimizationRegistry.Values("web",26100).Any(v=>v.Name=="AutoConfigURL"&&v.Desired==null),"Web reset is explicitly current-user proxy/PAC/legacy homepage, not broad policy or DNS deletion");
        foreach(var kind in new[]{RegistryValueKind.DWord,RegistryValueKind.QWord,RegistryValueKind.String,RegistryValueKind.ExpandString,RegistryValueKind.Binary,RegistryValueKind.MultiString}){
            object value=kind==RegistryValueKind.DWord?(object)(-2147483647):kind==RegistryValueKind.QWord?(object)Int64.MinValue:kind==RegistryValueKind.Binary?(object)new byte[]{0,1,255}:kind==RegistryValueKind.MultiString?(object)new[]{"中文","second"}:(object)"%USERPROFILE%\\中文";
            string encoded=OptimizationRegistry.Encode(value,kind);Check(OptimizationRegistry.Encode(OptimizationRegistry.Decode(encoded,kind),kind)==encoded,"Registry backup roundtrip preserves original type/value: "+kind);
        }
        Check(OptimizationPower.Settings.Where(s=>s.Key=="cpuMin"||s.Key=="cpuMax").All(s=>s.Desired==100)&&OptimizationPower.Settings.Where(s=>s.Key=="screen"||s.Key=="sleep"||s.Key=="hibernate"||s.Key=="unattended").All(s=>s.Desired==0),"High-performance profile targets CPU bounds100 and zero timeout values");
        Check(OptimizationPower.Settings.Single(s=>s.Key=="lid").LaptopOnly&&OptimizationPower.Settings.Single(s=>s.Key=="lid").Desired==0,"Lid do-nothing applies only to recognized portable devices");
        Check(!OptimizationFolders.InternalBus(7)&&!OptimizationFolders.InternalBus(4)&&!OptimizationFolders.InternalBus(9)&&OptimizationFolders.InternalBus(17)&&OptimizationFolders.InternalBus(11),"D drive guard rejects USB/FireWire/iSCSI and recognizes NVMe/SATA");
        Check(OptimizationWindowsBackend.ExitCode("命令：x\r\n返回代码：1056\r\n")==1056,"Command outcomes use recorded numeric status");Fails(()=>OptimizationWindowsBackend.ExitCode("已成功"),"Missing exit code never implies success");
    }
    static void JournalAndRollback()
    {
        string path=FileAt("journal.xml");var backend=new Backend{Backup=path,BadVerify="extensions"};var rows=new List<OptimizationResult>();OptimizationService.Apply(new[]{"ads","extensions","power"},backend,path,CancellationToken.None,rows.Add);
        Check(backend.Calls.Where(s=>s.StartsWith("apply:")).SequenceEqual(new[]{"apply:power","apply:extensions","apply:ads"})&&rows[1].Status.Contains("失败"),"Canonical order continues with explicit partial verification failure");
        var saved=OptimizationJournal.Load(path);Check(saved.Entries.Count==3&&saved.Entries[1].Phase=="需恢复检查"&&saved.Context==backend.Context,"Partial outcomes persist with machine/user/build context");
        OptimizationService.Restore(path,backend,CancellationToken.None,rows.Add);Check(backend.Calls.Where(s=>s.StartsWith("restore:")).SequenceEqual(new[]{"restore:ads","restore:extensions","restore:power"})&&backend.Values["power"]=="old-power","Restore reverses successful and partial changes using exact originals");
        int restored=backend.Calls.Count(s=>s.StartsWith("restore:"));OptimizationService.Restore(path,backend,CancellationToken.None,rows.Add);Check(restored==backend.Calls.Count(s=>s.StartsWith("restore:")),"Already-restored entries are idempotently skipped");
        backend.Context="different machine/user";Fails(()=>OptimizationService.Restore(path,backend,CancellationToken.None,rows.Add),"Cross-device/user/build restore blocked before any mutation");
        path=FileAt("skip.xml");backend=new Backend{Backup=path,Unsupported="history"};rows.Clear();OptimizationService.Apply(new[]{"history","extensions"},backend,path,CancellationToken.None,rows.Add);Check(rows.Count==2&&rows.Single(r=>r.Id=="history").Status.Contains("跳过")&&!backend.Calls.Contains("apply:history"),"Unsupported capture skips mutation without losing supported options");
        string blocked=FileAt("blocked");File.WriteAllText(blocked,"file");backend=new Backend{Backup=Path.Combine(blocked,"backup.xml")};Fails(()=>OptimizationService.Apply(new[]{"extensions"},backend,backend.Backup,CancellationToken.None,rows.Add),"Unwritable backup path stops before changes");Check(!backend.Calls.Any(),"No capture or native mutation precedes initial durable backup");
        using(var cancel=new CancellationTokenSource()){path=FileAt("cancel.xml");backend=new Backend{Backup=path};backend.Mutated=delegate{cancel.Cancel();};bool stopped=false;try{OptimizationService.Apply(new[]{"extensions","ads"},backend,path,cancel.Token,rows.Add);}catch(OperationCanceledException){stopped=true;}Check(stopped&&!backend.Calls.Contains("apply:ads")&&OptimizationJournal.Load(path).Entries.Single().Phase=="已核验","Cancellation waits for current mutation verification/journal, then stops next item");}
        using(var cancel=new CancellationTokenSource()){cancel.Cancel();path=FileAt("never.xml");backend=new Backend{Backup=path};Fails(()=>OptimizationService.Apply(new[]{"extensions"},backend,path,cancel.Token,rows.Add),"Pre-cancelled optimization stops");Check(!File.Exists(path)&&backend.Calls.Count==0,"Pre-cancelled operation creates neither backup nor native calls");}
        var journal=new OptimizationJournal{Context="中文上下文"};journal.Entries.Add(new OptimizationEntry{Id="extensions",Phase="执行中",Before=new Dictionary<string,string>{{"raw","中文\0\r\n%PATH%"}}});path=FileAt("unicode.xml");journal.Save(path);Check(OptimizationJournal.Load(path).Entries.Single().Before["raw"]=="中文\0\r\n%PATH%","Backup data preserves Unicode, NUL and literal environment text without execution");
        byte[] original=File.ReadAllBytes(path);journal.Entries[0].Before["raw"]=new string('x',4*1024*1024);Fails(()=>journal.Save(path),"Oversized captured state cannot replace a restorable backup");Check(File.ReadAllBytes(path).SequenceEqual(original),"Oversized write preserves the original readable backup");File.WriteAllBytes(FileAt("large.xml"),new byte[4*1024*1024+1]);Fails(()=>OptimizationJournal.Load(FileAt("large.xml")),"Oversized external backup is rejected before XML parsing");journal.Entries[0].Before["raw"]="\uD800";Fails(()=>journal.Save(path),"Invalid UTF16 cannot silently corrupt original backup values");Check(File.ReadAllBytes(path).SequenceEqual(original)&&Directory.GetFiles(Root,"journal-*.tmp").Length==0,"Failed atomic backup leaves prior file intact and removes temporary files");
        string xml=File.ReadAllText(path);File.WriteAllText(FileAt("unknown.xml"),xml.Replace("id=\"extensions\"","id=\"arbitrary-command\""));Fails(()=>OptimizationJournal.Load(FileAt("unknown.xml")),"Backup cannot introduce unknown actions or arbitrary commands");File.WriteAllText(FileAt("phase.xml"),xml.Replace("执行中","unexpected"));Fails(()=>OptimizationJournal.Load(FileAt("phase.xml")),"Unknown backup phase is rejected");
        File.WriteAllText(FileAt("dtd.xml"),"<!DOCTYPE Optimization [<!ENTITY x SYSTEM 'file:///etc/passwd'>]><Optimization version='1' context='x'>&x;</Optimization>");Fails(()=>OptimizationJournal.Load(FileAt("dtd.xml")),"DTD/external entities are rejected before backup processing");
    }
    [DllImport("libc",SetLastError=true)]static extern int symlink(string target,string name);
    static void TempSafety()
    {
        string local=FileAt("fake-local"),temp=Path.Combine(local,"Temp"),outside=FileAt("outside");Directory.CreateDirectory(temp);Directory.CreateDirectory(outside);string old=Path.Combine(temp,"old.tmp"),recent=Path.Combine(temp,"recent.tmp"),locked=Path.Combine(temp,"locked.tmp"),tool=Path.Combine(temp,"tool");Directory.CreateDirectory(tool);
        foreach(string file in new[]{old,recent,locked,Path.Combine(tool,"backup.xml"),Path.Combine(outside,"valuable.txt")}){File.WriteAllText(file,"1234567890");File.SetLastWriteTimeUtc(file,DateTime.UtcNow.AddDays(-2));}File.SetLastWriteTimeUtc(recent,DateTime.UtcNow);
        Func<string,string,long> remove=delegate(string file,string root){if(file==locked)throw new IOException("locked");Check(OptimizationFolders.Under(file,root),"Temp deletion candidate remains beneath selected Temp root");long length=new FileInfo(file).Length;File.Delete(file);return length;};
        bool linked=false;if(Environment.OSVersion.Platform!=PlatformID.Win32NT){linked=symlink(outside,Path.Combine(temp,"outside-link"))==0;Check(linked,"Link safety fixture created");}
        var result=OptimizationMaintenance.CleanTempCore(local,true,CancellationToken.None,remove,new[]{tool});Check(result.Deleted==1&&result.Bytes==10&&!File.Exists(old)&&File.Exists(recent)&&File.Exists(locked)&&File.Exists(Path.Combine(tool,"backup.xml"))&&File.Exists(Path.Combine(outside,"valuable.txt")),"Temp cleaning preserves recent/locked/tool/outside files and counts deleted bytes");
        result=OptimizationMaintenance.CleanTempCore(local,false,CancellationToken.None,remove,new[]{tool});Check(result.Deleted==1&&!File.Exists(recent)&&File.Exists(locked),"Explicit recent-file cleanup still skips locked files and protected subtree");
        if(linked){string alias=FileAt("root-alias");Directory.CreateDirectory(alias);Check(symlink(outside,Path.Combine(alias,"Temp"))==0,"Root redirect fixture created");Fails(()=>OptimizationMaintenance.CleanTempCore(alias,false,CancellationToken.None,remove),"A redirected Temp root is rejected without traversing target");Check(File.Exists(Path.Combine(outside,"valuable.txt")),"Reparse rejection preserves outside data");}
        using(var cancel=new CancellationTokenSource()){string a=Path.Combine(temp,"a.tmp"),b=Path.Combine(temp,"b.tmp");File.WriteAllText(a,"a");File.WriteAllText(b,"b");result=OptimizationMaintenance.CleanTempCore(local,false,cancel.Token,delegate(string file,string root){if(file==locked)throw new IOException();long size=new FileInfo(file).Length;File.Delete(file);cancel.Cancel();return size;},new[]{tool});Check(result.Stopped&&result.Deleted==1&&(File.Exists(a)||File.Exists(b)),"Stop returns partial Temp counts and leaves subsequent files untouched");}
        Check(OptimizationMaintenance.CleanTempCore(local,false,CancellationToken.None,remove,new[]{temp}).Deleted==0,"Running tool whose protected root is Temp cannot delete its own contents");
    }
    static void Scripts()
    {
        string volume=@"\\?\Volume{12345678-1234-1234-1234-1234567890ab}\";Check(OptimizationWindowsBackend.AppxSetScript(volume).Contains("Set-AppxDefaultVolume")&&OptimizationWindowsBackend.AppxSetScript(null).Contains(@"D:\WindowsApps"),"Appx scripts use exact D path and validated original volume");Fails(()=>OptimizationWindowsBackend.AppxSetScript("'; Remove-Item C:\\ -Recurse; '"),"Untrusted volume text cannot enter a management command");
        string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"generated-optimization");Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"appx-read.ps1"),OptimizationWindowsBackend.AppxReadScript,new UTF8Encoding(false));File.WriteAllText(Path.Combine(folder,"appx-d.ps1"),OptimizationWindowsBackend.AppxSetScript(null),new UTF8Encoding(false));File.WriteAllText(Path.Combine(folder,"appx-restore.ps1"),OptimizationWindowsBackend.AppxSetScript(volume),new UTF8Encoding(false));
        Check(!OptimizationWindowsBackend.AppxSetScript(null).Contains("EncodedCommand")&&!OptimizationWindowsBackend.AppxSetScript(null).Contains("Bypass"),"New management scripts remain plaintext commands without policy bypass");
    }
    static void WindowsNativeFixtures()
    {
        if(Environment.OSVersion.Platform!=PlatformID.Win32NT){Console.WriteLine("SKIP Windows-only native fixtures");return;}
        Guid active=OptimizationPower.Active();var power=OptimizationPower.Capture(null);Check(OptimizationPower.Schemes().Contains(active)&&power["active"]==active.ToString(),"Windows native power capture is read-only and preserves actual active scheme");
        string key=@"Software\WinForms-OptimizationTests\"+Guid.NewGuid().ToString("N");try{
            using(var root=OptimizationRegistry.Root(false))using(var reg=root.CreateSubKey(key)){reg.SetValue("number",7,RegistryValueKind.DWord);reg.SetValue("expanded","%USERPROFILE%",RegistryValueKind.ExpandString);}
            var values=new List<OptimizationRegistryValue>{new OptimizationRegistryValue(false,key,"number",0),new OptimizationRegistryValue(false,key,"expanded",null,RegistryValueKind.String),new OptimizationRegistryValue(false,key,"absent",1)};var before=OptimizationRegistry.Capture(values);OptimizationRegistry.Write(values,before,false);OptimizationRegistry.Verify(values,before,false);OptimizationRegistry.Write(values,before,true);OptimizationRegistry.Verify(values,before,true);Check(before["1.kind"]==((int)RegistryValueKind.ExpandString).ToString()&&before["2.present"]=="0","Windows disposable HKCU fixture restores original kind/value and absent value exactly");
        }finally{using(var root=OptimizationRegistry.Root(false))root.DeleteSubKeyTree(key,false);}
        string folder=FileAt("native-files");Directory.CreateDirectory(folder);string inside=Path.Combine(folder,"inside.tmp"),outside=FileAt("not-temp.txt");File.WriteAllText(inside,"abc");File.WriteAllText(outside,"keep");Check(OptimizationMaintenance.DeleteHeld(inside,folder)==3&&!File.Exists(inside),"Windows native handle deletion removes only verified in-scope fixture");Fails(()=>OptimizationMaintenance.DeleteHeld(outside,folder),"Windows native final-path check refuses an outside fixture");Check(File.Exists(outside),"Native scope rejection leaves outside file intact");
    }
    static int Main(){Root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"optimization-fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Root);try{Catalog();JournalAndRollback();TempSafety();Scripts();WindowsNativeFixtures();Console.WriteLine("OPTIMIZATION TOTAL PASS "+passed);return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}finally{try{Directory.Delete(Root,true);}catch{}}}
}
