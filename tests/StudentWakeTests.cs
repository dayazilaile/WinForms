using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ITDesktopToolkit;
class StudentWakeTests
{
    static int passed;
    static void Check(bool value,string name){if(!value)throw new Exception(name);passed++;Console.WriteLine("PASS "+name);}
    static void Reject(Action work,string name){try{work();}catch(ArgumentException){Check(true,name);return;}throw new Exception("Expected argument rejection: "+name);}
    static StudentMachine Machine(string mac="02:00:00:00:00:01",string ip="192.0.2.10"){return new StudentMachine{Room="机房一",Name="学生机 01",IPv4=ip,Mac=mac,Note="普通清单"};}
    static void Packets()
    {
        Check(StudentWakeService.NormalizeMac("02-00-00-00-00-ab")=="02:00:00:00:00:AB","MAC forms normalize to one identity");
        byte[] packet=StudentWakeService.MagicPacket("0200000000AB"),mac={2,0,0,0,0,171};
        Check(packet.Length==102&&packet.Take(6).All(b=>b==255)&&Enumerable.Range(0,16).All(i=>packet.Skip(6+i*6).Take(6).SequenceEqual(mac)),"WOL packet is exactly six FF bytes plus sixteen MAC repetitions");
        Reject(()=>StudentWakeService.MagicPacket("FF:FF:FF:FF:FF:FF"),"Broadcast MAC cannot become wake target");Reject(()=>StudentWakeService.MagicPacket("01:00:5E:00:00:01"),"Multicast MAC rejected");Reject(()=>StudentWakeService.MagicPacket("00:00:00:00:00:00"),"All-zero MAC rejected");Reject(()=>StudentWakeService.MagicPacket("not-a-mac"),"Malformed MAC rejected");
        var net=WakeNetwork.Create("test","192.0.2.1","255.255.255.0");Check(net.Broadcast.ToString()=="192.0.2.255"&&net.Contains("192.0.2.10")&&!net.Contains("198.51.100.10"),"Broadcast and membership derive from selected local subnet");
        Check(WakeNetwork.Create("test","10.2.17.5","255.255.240.0").Broadcast.ToString()=="10.2.31.255","Non-/24 LAN broadcast uses the actual mask");
        Reject(()=>WakeNetwork.Create("test","192.0.2.1","255.0.255.0"),"Noncontiguous wake mask rejected");Reject(()=>WakeNetwork.Create("test","192.0.2.1","255.255.255.255"),"Host-only interface cannot broadcast WOL");Reject(()=>WakeNetwork.Create("test","192.0.2.255","255.255.255.0"),"Local source cannot be subnet broadcast");
        Reject(()=>StudentRoster.Validate(Machine(ip:"192.2")),"Abbreviated IPv4 input rejected");Reject(()=>StudentRoster.Validate(Machine(ip:"::1")),"Student address cannot be IPv6 in IPv4 wake roster");
    }
    static void Storage()
    {
        string dir=Path.Combine(Path.GetTempPath(),"student-roster-"+Guid.NewGuid());Directory.CreateDirectory(dir);string path=Path.Combine(dir,"students.tsv");
        try{
            var machine=Machine();machine.Room="#机房一";StudentRoster.Save(path,new[]{machine});var loaded=StudentRoster.Load(path);
            Check(loaded.Count==1&&loaded[0].Room==machine.Room&&loaded[0].Name==machine.Name,"Chinese roster and hash-prefixed room names round-trip without lost rows");
            Check(StudentRoster.Parse(StudentRoster.Serialize(new[]{machine}).Replace(StudentRoster.Header,StudentRoster.Header+"\t\t\t\t")).Count==1,"Spreadsheet-padded comment headers do not break TSV import");
            machine.Note="第二次保存";StudentRoster.Save(path,new[]{machine});Check(StudentRoster.Load(path)[0].Note==machine.Note,"Atomic save replaces existing roster successfully");
            byte[] before=File.ReadAllBytes(path);Reject(()=>StudentRoster.Save(path,new[]{machine,machine}),"Duplicate MAC rejected before persistence");Check(File.ReadAllBytes(path).SequenceEqual(before)&&Directory.GetFiles(dir,"*.tmp").Length==0,"Failed save preserves original file and leaves no temporary file");
            var update=Machine();update.Name="新名称";var merged=StudentRoster.Merge(loaded,new[]{update,Machine("02:00:00:00:00:02")});Check(merged.Count==2&&merged.Single(x=>x.Mac==update.Mac).Name=="新名称","Import updates matching MAC while retaining unique entries");
            Reject(()=>StudentRoster.Validate(new StudentMachine{Name="bad\nrow",Mac=machine.Mac}),"Control characters cannot inject roster records");
            Reject(()=>StudentRoster.Serialize(Enumerable.Range(0,501).Select(i=>Machine("02:00:00:00:"+(i/256).ToString("X2")+":"+(i%256).ToString("X2")))),"Roster size capped at 500");
            Check(StudentRoster.Validate(Machine(ip:"")).IPv4==""&&StudentRoster.Validate(Machine(ip:"192.000.002.010")).IPv4=="192.0.2.10","Optional IP and decimal normalization do not change MAC-based wake identity");
        }finally{Directory.Delete(dir,true);}
    }
    static async Task Sending()
    {
        var net=WakeNetwork.Create("test","192.0.2.1","255.255.255.0");int sends=0;var results=new List<WakeResult>();Func<byte[],CancellationToken,Task> send=delegate(byte[] packet,CancellationToken token){sends++;return Task.FromResult(0);};
        await StudentWakeService.WakeCore(new[]{Machine(),Machine()},net,9,CancellationToken.None,send,results.Add);Check(sends==3&&results.Count==1&&results[0].Packets==3&&results[0].Summary.Contains("未确认开机"),"Duplicate selections produce one bounded three-packet wake, never a false online claim");
        sends=0;results.Clear();bool failed=false;try{await StudentWakeService.WakeCore(new[]{Machine(),Machine("02:00:00:00:00:02","198.51.100.2")},net,9,CancellationToken.None,send,results.Add);}catch(ArgumentException){failed=true;}Check(failed&&sends==0,"Whole batch is validated before sending to any student");
        using(var cancel=new CancellationTokenSource()){cancel.Cancel();sends=0;try{await StudentWakeService.WakeCore(new[]{Machine()},net,9,cancel.Token,send,results.Add);}catch(OperationCanceledException){}Check(sends==0,"Pre-cancelled wake sends no packets");}
        using(var cancel=new CancellationTokenSource()){sends=0;results.Clear();try{await StudentWakeService.WakeCore(new[]{Machine(),Machine("02:00:00:00:00:02")},net,9,cancel.Token,delegate(byte[] p,CancellationToken t){sends++;cancel.Cancel();return Task.FromResult(0);},results.Add);}catch(OperationCanceledException){}Check(sends==1&&results.Count==1&&results[0].Packets==1&&results[0].Summary.Contains("已停止"),"Cancellation stops subsequent packets and reports partial delivery honestly");}
        results.Clear();await StudentWakeService.WakeCore(new[]{Machine()},net,9,CancellationToken.None,delegate{throw new SocketException((int)SocketError.NetworkUnreachable);},results.Add);Check(results.Single().Packets==0&&results[0].Summary.Contains("发送失败"),"UDP failure is shown as failure rather than successful wake");
        failed=false;try{await StudentWakeService.WakeCore(new[]{Machine()},net,0,CancellationToken.None,send,results.Add);}catch(ArgumentException){failed=true;}Check(failed,"Invalid UDP port rejected before sending");
    }
    static async Task Discovery()
    {
        var network=WakeNetwork.Create("test","192.0.2.1","255.255.255.0");var targets=StudentDiscoveryService.Targets("192.0.2.0/24",network);
        Check(targets.Length==253&&!targets.Contains("192.0.2.1")&&!targets.Contains("192.0.2.0")&&!targets.Contains("192.0.2.255"),"Discovery excludes source, network and broadcast addresses");
        Reject(()=>StudentDiscoveryService.Targets("198.51.100.0/24",network),"Student discovery refuses another local subnet");Reject(()=>StudentDiscoveryService.Targets("192.0.2.0/21",network),"Discovery range cap rejects oversized scans");
        var broad=WakeNetwork.Create("test","10.2.1.1","255.255.0.0");Check(StudentDiscoveryService.Targets("10.2.0.0/22",broad).Length==1021,"Student scan supports bounded /22 ranges");
        int active=0,maximum=0,calls=0;object gate=new object();var rows=new List<StudentScanRow>();var cache=new Dictionary<string,string>{{"192.0.2.3","02:00:00:00:00:03"}};
        Func<string,CancellationToken,Task<StudentScanRow>> fake=async delegate(string ip,CancellationToken token){int count=Interlocked.Increment(ref active);lock(gate){maximum=Math.Max(maximum,count);calls++;}try{await Task.Delay(2,token);return new StudentScanRow{IPv4=ip,Mac=ip=="192.0.2.2"?"02:00:00:00:00:02":"",Name="发现设备",Status="ARP 未确认"};}finally{Interlocked.Decrement(ref active);}};
        var result=await StudentDiscoveryService.ScanCore(new[]{"192.0.2.2","192.0.2.3","192.0.2.4"},network,cache,CancellationToken.None,fake,delegate(StudentScanRow row){lock(gate)rows.Add(row);});
        Check(result.Rows.Count==3&&result.Rows.Single(x=>x.IPv4=="192.0.2.3").Status.Contains("缓存")&&!result.Rows.Single(x=>x.IPv4=="192.0.2.4").CanSave,"ARP/cache results are distinguished and unresolved devices cannot be saved");
        calls=0;await StudentDiscoveryService.ScanCore(targets.Take(40).ToArray(),network,new Dictionary<string,string>(),CancellationToken.None,fake,delegate{});Check(maximum<=12&&calls==40,"ARP discovery concurrency never exceeds twelve workers");
        using(var cancel=new CancellationTokenSource()){cancel.Cancel();calls=0;try{await StudentDiscoveryService.ScanCore(targets,network,cache,cancel.Token,fake,delegate{});}catch(OperationCanceledException){}Check(calls==0,"Pre-cancelled discovery makes no ARP probes");}
        var conflicts=new[]{new StudentScanRow{IPv4="192.0.2.2",Mac="02:00:00:00:00:02"},new StudentScanRow{IPv4="192.0.2.3",Mac="02:00:00:00:00:02"}};StudentDiscoveryService.MarkConflicts(conflicts);Check(conflicts.All(x=>x.Conflict&&x.Status.Contains("ARP 代理")),"Repeated MACs are flagged for review rather than treated as distinct students");
        var existing=Machine("02:00:00:00:00:02","192.0.2.2");existing.Name="手工名称";existing.Note="保留备注";
        var merged=StudentDiscoveryService.MergeDiscovered(new[]{existing,Machine("02:00:00:00:00:09","192.0.2.9")},new[]{new StudentScanRow{IPv4="192.0.2.20",Mac=existing.Mac,Name="不覆盖"},new StudentScanRow{IPv4="192.0.2.3",Mac="02:00:00:00:00:03",Name="新学生机"}},"新机房");
        Check(merged.Count==3&&merged.Single(x=>x.Mac==existing.Mac).Name=="手工名称"&&merged.Single(x=>x.Mac==existing.Mac).Room==existing.Room&&merged.Single(x=>x.Mac==existing.Mac).IPv4=="192.0.2.20","Rescan updates IP while preserving known names/rooms and nonresponding records");
        Check(merged.Single(x=>x.Mac=="02:00:00:00:00:03").Room=="新机房","Newly discovered students are placed in the chosen room");
        Reject(()=>StudentDiscoveryService.MergeDiscovered(new StudentMachine[0],conflicts,"机房"),"Duplicate selected MACs cannot silently overwrite scanned devices");Reject(()=>StudentDiscoveryService.MergeDiscovered(new StudentMachine[0],new[]{new StudentScanRow{IPv4="192.0.2.5",Name="未知"}},"机房"),"Unresolved scan rows cannot be persisted");
        string encoded="command\n返回代码：0\nITSTUDENT|"+Convert.ToBase64String(Encoding.UTF8.GetBytes("192.0.2.3"))+"|"+Convert.ToBase64String(Encoding.UTF8.GetBytes("02-00-00-00-00-03"));Check(StudentDiscoveryService.ParseCache(encoded,network)["192.0.2.3"]=="02:00:00:00:00:03","Structured neighbor cache maps normalized MACs to on-link addresses");
    }
    static int Main(){try{Packets();Storage();Sending().GetAwaiter().GetResult();Discovery().GetAwaiter().GetResult();Console.WriteLine("STUDENT WAKE TOTAL PASS "+passed);return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
