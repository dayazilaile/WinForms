using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WinForms;
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
    static async Task Power()
    {
        var net=WakeNetwork.Create("test","192.0.2.1","255.255.255.0");var local=new[]{"192.0.2.1","192.0.2.99"};StudentMachine one=Machine(),two=Machine("02:00:00:00:00:02","192.0.2.11");
        Reject(()=>StudentPowerService.Prepare(new[]{Machine(ip:"")},net,StudentPowerAction.Shutdown,60,local),"Remote shutdown requires a current IP, not only a MAC");
        foreach(string ip in new[]{"127.0.0.1","192.0.2.1","192.0.2.99","192.0.2.0","192.0.2.255","198.51.100.10","224.0.0.1"})Reject(()=>StudentPowerService.Prepare(new[]{Machine(ip:ip)},net,StudentPowerAction.Restart,60,local),"Remote power rejects local, special, subnet and off-link targets: "+ip);
        Reject(()=>StudentPowerService.Prepare(new[]{one,Machine("02:00:00:00:00:02",one.IPv4)},net,StudentPowerAction.Shutdown,60,local),"Duplicate IPs cannot address another student through an alias");
        Reject(()=>StudentPowerService.Prepare(new[]{one,Machine(one.Mac,"192.0.2.12")},net,StudentPowerAction.Shutdown,60,local),"Duplicate MAC records rejected before remote shutdown");
        Reject(()=>StudentPowerService.Prepare(new[]{one},net,StudentPowerAction.Shutdown,0,local),"Immediate shutdown is not a supported default");
        Reject(()=>StudentPowerService.Prepare(new[]{one},net,(StudentPowerAction)99,60,local),"Unknown remote power actions cannot dispatch");
        Reject(()=>StudentPowerService.Prepare(new StudentMachine[0],net,StudentPowerAction.Shutdown,60,local),"Empty power target selection is rejected");
        var plan=StudentPowerService.Prepare(new[]{one,two},net,StudentPowerAction.Restart,60,local);one.Name="after plan";Check(plan.Targets[0].Name!="after plan"&&plan.Seconds==60,"Power plan snapshots identities and countdown before confirmation");
        int probes=0,requests=0;var results=new List<StudentPowerResult>();Func<string,WakeNetwork,CancellationToken,Task<string>> arp=delegate(string ip,WakeNetwork n,CancellationToken t){probes++;return Task.FromResult(ip==two.IPv4?two.Mac:one.Mac);};
        var batch=await StudentPowerService.RunCore(plan,net,CancellationToken.None,arp,delegate(StudentMachine machine,StudentPowerAction action,int seconds){requests++;Check(action==StudentPowerAction.Restart&&seconds==60,"Remote transport receives only the reviewed operation and delay");return requests==1?0:5;},results.Add);
        Check(batch.Completed==2&&batch.Accepted==1&&results[0].Summary.Contains("未确认")&&results[1].Summary.Contains("权限不足"),"Accepted power request is distinguished from physical completion and access denial");
        probes=0;requests=0;results.Clear();batch=await StudentPowerService.RunCore(plan,net,CancellationToken.None,delegate{return Task.FromResult("02:00:00:00:00:EE");},delegate{requests++;return 0;},results.Add);
        Check(requests==0&&batch.Accepted==0&&results.All(r=>r.Summary.Contains("MAC 与记录不符")),"Stale IP to MAC mismatch never submits a power request");
        results.Clear();batch=await StudentPowerService.RunCore(plan,net,CancellationToken.None,delegate{throw new IOException("模拟 ARP 不可用");},delegate{requests++;return 0;},results.Add);Check(requests==0&&batch.Completed==2&&results.All(r=>!r.Accepted),"Failed identity checks are reported without remote RPC");
        using(var cancel=new CancellationTokenSource()){cancel.Cancel();probes=0;try{await StudentPowerService.RunCore(plan,net,cancel.Token,arp,delegate{requests++;return 0;},results.Add);}catch(OperationCanceledException){}Check(probes==0&&requests==0,"Pre-cancelled power operation does not even probe a target");}
        using(var cancel=new CancellationTokenSource()){batch=await StudentPowerService.RunCore(plan,net,cancel.Token,delegate{cancel.Cancel();return Task.FromResult(one.Mac);},delegate{requests++;return 0;},results.Add);Check(batch.Stopped&&requests==0&&batch.Completed==0,"Cancellation after identity check stops before submission");}
        results.Clear();using(var cancel=new CancellationTokenSource()){batch=await StudentPowerService.RunCore(plan,net,cancel.Token,arp,delegate{requests++;cancel.Cancel();return 0;},results.Add);Check(batch.Stopped&&batch.Accepted==1&&batch.Completed==1&&results.Count==1&&results[0].Accepted,"Cancellation during an in-flight RPC still reports its late accepted result");}
        var abort=StudentPowerService.Prepare(new[]{two},net,StudentPowerAction.Abort,0,local);results.Clear();batch=await StudentPowerService.RunCore(abort,net,CancellationToken.None,arp,delegate(StudentMachine m,StudentPowerAction action,int seconds){Check(action==StudentPowerAction.Abort&&seconds==0,"Abort uses the cancellation API path, not another shutdown");return 0;},results.Add);Check(batch.Accepted==1&&results[0].Summary.Contains("取消请求"),"Abort acceptance does not claim the student is on or off");
        Check(StudentPowerService.ErrorText(1722).Contains("RPC")&&StudentPowerService.ErrorText(1116).Contains("没有可取消"),"Remote service and no-pending-request errors provide useful causes");
        var native=typeof(StudentPowerService).GetNestedType("TokenPrivileges",System.Reflection.BindingFlags.NonPublic);Check(System.Runtime.InteropServices.Marshal.SizeOf(native)==16,"Privilege structure keeps the native DWORD/LUID layout on x86 and x64");
    }
    static async Task PowerDiagnostics()
    {
        var net=WakeNetwork.Create("test","192.0.2.1","255.255.255.0");var machine=Machine();var plan=StudentPowerService.Prepare(new[]{machine},net,StudentPowerAction.Shutdown,60,new[]{net.Source.ToString()});
        var ports=new List<int>();var rows=new List<StudentPowerDiagnostic>();
        Func<string,WakeNetwork,CancellationToken,Task<string>> arp=delegate{return Task.FromResult(machine.Mac);};
        Func<string,int,CancellationToken,Task<string>> connect=delegate(string ip,int port,CancellationToken token){ports.Add(port);return Task.FromResult(port==135?"可连接（仅TCP）":"超时/未响应");};
        await StudentPowerDiagnostics.InspectCore(plan,net,CancellationToken.None,arp,connect,rows.Add);
        Check(ports.SequenceEqual(new[]{135,445})&&rows.Count==1&&rows[0].Smb.Contains("超时"),"Read-only power diagnostics probes only bounded RPC/SMB ports and preserves separate outcomes");
        Check(rows[0].Details.Contains(machine.Mac)&&rows[0].Details.Contains(machine.IPv4)&&rows[0].Details.Contains("没有验证远程账户权限")&&rows[0].Summary.Contains("错误53"),"Diagnostic details identify the target without claiming remote permission or completed power action");
        ports.Clear();rows.Clear();await StudentPowerDiagnostics.InspectCore(plan,net,CancellationToken.None,delegate{return Task.FromResult("02:00:00:00:00:EE");},connect,rows.Add);
        Check(ports.Count==0&&rows.Single().Note.Contains("MAC 与记录不符"),"Stale MAC identity skips all diagnostic port connections");
        rows.Clear();await StudentPowerDiagnostics.InspectCore(plan,net,CancellationToken.None,delegate{throw new IOException("模拟解析失败");},connect,rows.Add);
        Check(ports.Count==0&&rows.Single().Note.Contains("模拟解析失败"),"Failed ARP is reported as incomplete without connecting to an unverified identity");
        using(var cancel=new CancellationTokenSource()){rows.Clear();ports.Clear();bool stopped=false;try{await StudentPowerDiagnostics.InspectCore(plan,net,cancel.Token,arp,delegate(string ip,int port,CancellationToken token){ports.Add(port);cancel.Cancel();return Task.FromResult("可连接");},rows.Add);}catch(OperationCanceledException){stopped=true;}Check(stopped&&ports.SequenceEqual(new[]{135})&&rows.Count==0,"Cancellation between diagnostic ports prevents the next connection and a false completed result");}
        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int open=((IPEndPoint)listener.LocalEndpoint).Port;
        try{Check((await StudentPowerDiagnostics.Connect("127.0.0.1",open,CancellationToken.None)).Contains("仅TCP"),"Diagnostic transport verifies a real loopback TCP connection without RPC or credentials");}finally{listener.Stop();}
        Check((await StudentPowerDiagnostics.Connect("127.0.0.1",open,CancellationToken.None)).Contains("不可连接"),"Refused diagnostic connection remains a port failure");
        Check(StudentPowerService.ErrorText(53).StartsWith("错误 53：找不到网络路径")&&!StudentPowerService.ErrorText(53).Contains("RPC 服务不可用")&&StudentPowerService.ErrorText(1722).StartsWith("错误 1722："),"Network-path error 53 and RPC error 1722 stay distinct with code before clipped text");
        string script=StudentPowerDiagnostics.LocalCheckScript();
        Check(script.All(c=>c<128)&&script.Contains("%~dp0Logs")&&script.Contains("ipconfig.exe")&&script.Contains("hostname.exe")&&script.Contains("%DATE% %TIME%"),"Local diagnostic script is plain ASCII CMD with device/time identity and script-directory logs");
        Check(script.Contains("sc.exe\" query")&&script.Contains("advfirewall show currentprofile")&&script.Contains("/areas USER_RIGHTS")&&!new[]{"shutdown.exe"," /configure ","sc.exe\" start","advfirewall set","powershell","Bypass","%TEMP%","%APPDATA%"}.Any(x=>script.IndexOf(x,StringComparison.OrdinalIgnoreCase)>=0),"Local script only reads services/firewall/rights and never executes power, policy repair or PowerShell");
        string dir=Path.Combine(Path.GetTempPath(),"power-check-"+Guid.NewGuid());Directory.CreateDirectory(dir);string path=Path.Combine(dir,"check.bat");
        try{File.WriteAllText(path,"old");StudentPowerDiagnostics.ExportLocalCheck(path);Check(File.ReadAllText(path)==script&&Directory.GetFiles(dir).Length==1,"Diagnostic export atomically replaces only its selected BAT and does not execute it");}finally{Directory.Delete(dir,true);}
    }
    static async Task CrossRoomWake()
    {
        var source=WakeNetwork.Create("teacher","192.168.10.8","255.255.255.0");var pc=Machine(ip:"192.168.20.17");string[] local={source.Source.ToString(),"192.168.20.99"};
        var plan=CrossRoomWakeService.Prepare(new[]{pc},source,24,CrossRoomWakeMode.Unicast,local);
        Check(CrossRoomWakeService.Destinations(plan,plan.Targets[0]).Single().ToString()=="192.168.20.17:9","Cross-room unicast addresses saved target IP rather than local broadcast");
        pc.Name="changed";Check(plan.Targets[0].Name!="changed","Cross-room plan snapshots roster before review");
        foreach(CrossRoomWakeMode mode in Enum.GetValues(typeof(CrossRoomWakeMode))){var p=CrossRoomWakeService.Prepare(new[]{pc},source,24,mode,local);var endpoints=CrossRoomWakeService.Destinations(p,p.Targets[0]);Check(endpoints.Count==(mode==CrossRoomWakeMode.Combined?3:1)&&endpoints.All(e=>e.Port==9),"Each explicit wake mode has bounded destinations and UDP9: "+mode);}
        plan=CrossRoomWakeService.Prepare(new[]{pc},source,24,CrossRoomWakeMode.Combined,local);
        Check(CrossRoomWakeService.Destinations(plan,plan.Targets[0]).Select(e=>e.Address.ToString()).SequenceEqual(new[]{"192.168.20.17","192.168.20.255","192.168.10.255"}),"Combination tries target unicast, actual target subnet broadcast then local broadcast");
        var same=CrossRoomWakeService.Prepare(new[]{Machine(ip:"192.168.10.17")},source,24,CrossRoomWakeMode.Combined,local);Check(CrossRoomWakeService.Destinations(same,same.Targets[0]).Count==2,"Identical local and directed broadcasts are not sent twice");
        Check(CrossRoomWakeService.Broadcast("192.168.20.17",23).ToString()=="192.168.21.255"&&CrossRoomWakeService.Broadcast("10.2.3.5",30).ToString()=="10.2.3.7","Target broadcasts use configured prefix, not a hardcoded last octet");
        foreach(string ip in new[]{"","8.8.8.8","127.0.0.1","224.0.0.1","169.254.1.2","192.168.10.8","192.168.20.99","192.168.20.0","192.168.20.255"})Reject(()=>CrossRoomWakeService.Prepare(new[]{Machine(ip:ip)},source,24,CrossRoomWakeMode.Unicast,local),"Cross-room rejects missing/public/special/local/subnet/broadcast target: "+ip);
        Reject(()=>CrossRoomWakeService.Prepare(new[]{pc,Machine("02:00:00:00:00:02",pc.IPv4)},source,24,CrossRoomWakeMode.Combined,local),"Duplicate target IP fails the whole cross-room preflight");
        Reject(()=>CrossRoomWakeService.Prepare(new[]{pc,Machine(pc.Mac,"192.168.20.18")},source,24,CrossRoomWakeMode.Combined,local),"Duplicate target MAC fails before any packet");
        var other=Machine("02:00:00:00:00:02","192.168.30.17");other.Room="另一个机房";Reject(()=>CrossRoomWakeService.Prepare(new[]{pc,other},source,24,CrossRoomWakeMode.Combined,local),"Cross-room batch cannot silently expand across different rooms");
        foreach(int prefix in new[]{0,15,31,32})Reject(()=>CrossRoomWakeService.Prepare(new[]{pc},source,prefix,CrossRoomWakeMode.Unicast,local),"Invalid or overly broad target prefix rejected: "+prefix);
        Reject(()=>CrossRoomWakeService.Prepare(new StudentMachine[0],source,24,CrossRoomWakeMode.Unicast,local),"Empty cross-room targets rejected");
        Reject(()=>CrossRoomWakeService.Prepare(new[]{pc},source,24,(CrossRoomWakeMode)99,local),"Unknown wake mode cannot create endpoints");
        Reject(()=>CrossRoomWakeService.Prepare(new[]{pc},WakeNetwork.Create("public","192.0.2.2","255.255.255.0"),24,CrossRoomWakeMode.Unicast,local),"Trial requires private source network");
        var calls=new List<string>();var results=new List<CrossRoomWakeResult>();
        await CrossRoomWakeService.RunCore(plan,CancellationToken.None,delegate(byte[] packet,IPEndPoint endpoint,short ttl,CancellationToken token){calls.Add(endpoint+"/"+ttl);Check(packet.SequenceEqual(StudentWakeService.MagicPacket(pc.Mac)),"Each cross-room path carries only the reviewed MAC magic packet");return Task.FromResult(0);},results.Add);
        Check(calls.Count==9&&calls.Take(6).All(x=>x.EndsWith("/16"))&&calls.Skip(6).All(x=>x.EndsWith("/1")),"Routed attempts use TTL16 while local broadcast stays TTL1 with at most three packets per path");
        Check(results.Single().Packets==9&&results.Single().Failures==0&&results.Single().Summary.Contains("未确认送达/开机")&&results.Single().Details.Contains(pc.IPv4)&&results.Single().Details.Contains(pc.Mac),"Results and portable audit detail never infer delivery or power state from UDP send");
        calls.Clear();results.Clear();await CrossRoomWakeService.RunCore(plan,CancellationToken.None,delegate(byte[] packet,IPEndPoint endpoint,short ttl,CancellationToken token){calls.Add(endpoint.Address.ToString());if(endpoint.Address.ToString()==pc.IPv4)throw new SocketException((int)SocketError.NetworkUnreachable);return Task.FromResult(0);},results.Add);
        Check(calls.Count==7&&results.Single().Packets==6&&results.Single().Failures==1&&results.Single().Details.Contains("失败"),"One path failure remains visible while remaining explicit paths can be tried");
        using(var cancel=new CancellationTokenSource()){calls.Clear();results.Clear();bool stopped=false;try{await CrossRoomWakeService.RunCore(plan,cancel.Token,delegate(byte[] packet,IPEndPoint endpoint,short ttl,CancellationToken token){calls.Add(endpoint.ToString());cancel.Cancel();return Task.FromResult(0);},results.Add);}catch(OperationCanceledException){stopped=true;}Check(stopped&&calls.Count==1&&results.Single().Packets==1&&results.Single().Stopped,"Stop preserves partial sent count and prevents later routes/packets");}
        using(var cancel=new CancellationTokenSource()){cancel.Cancel();calls.Clear();bool stopped=false;try{await CrossRoomWakeService.RunCore(plan,cancel.Token,delegate{calls.Add("bad");return Task.FromResult(0);},results.Add);}catch(OperationCanceledException){stopped=true;}Check(stopped&&calls.Count==0,"Pre-cancelled cross-room attempt sends nothing");}
        using(var receiver=new UdpClient(new IPEndPoint(IPAddress.Loopback,0)))using(var sender=new UdpClient(new IPEndPoint(IPAddress.Loopback,0))){
            var endpoint=(IPEndPoint)receiver.Client.LocalEndPoint;var pending=receiver.ReceiveAsync();byte[] packet=StudentWakeService.MagicPacket(pc.Mac);await CrossRoomWakeService.Send(sender,packet,endpoint,16,CancellationToken.None);if(await Task.WhenAny(pending,Task.Delay(3000))!=pending)throw new Exception("Loopback wake packet timed out");
            Check((await pending).Buffer.SequenceEqual(packet)&&sender.Ttl==16,"Real UDP loopback transport preserves exactly 102 reviewed bytes and routed TTL");
        }
    }
    static void LegacyBranding()
    {
        string text=StudentRoster.Serialize(new[]{new StudentMachine{Room="Room",Name="PC1",IPv4="192.0.2.9",Mac="02:00:00:00:00:09"}});Check(text.StartsWith("# WinForms students")&&StudentRoster.Parse(text.Replace(StudentRoster.Header,StudentRoster.LegacyHeader)).Single().Name=="PC1","WinForms roster export keeps original-brand student lists importable");
    }
    static int Main(){try{LegacyBranding();Packets();Storage();Sending().GetAwaiter().GetResult();Discovery().GetAwaiter().GetResult();Power().GetAwaiter().GetResult();PowerDiagnostics().GetAwaiter().GetResult();CrossRoomWake().GetAwaiter().GetResult();Console.WriteLine("STUDENT WAKE TOTAL PASS "+passed);return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
