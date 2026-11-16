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
    static int Main(){try{Packets();Storage();Sending().GetAwaiter().GetResult();Console.WriteLine("STUDENT WAKE TOTAL PASS "+passed);return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
