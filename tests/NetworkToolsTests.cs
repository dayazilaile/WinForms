using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using WinForms;
class NetworkToolsTests
{
    static int passed;
    static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);passed++;}
    static void Reject(Action action,string name){try{action();}catch(ArgumentException){Check(true,name);return;}throw new Exception("Should reject: "+name);}
    static void Catalog()
    {
        var entries=NavigationCatalog.All;Check(entries.Length==38&&entries.Select(e=>e.Key).Distinct().Count()==38,"38 unique reachable navigation entries");
        Check(entries.Select(e=>e.Group).Distinct().Count()==7,"Seven navigation groups (including trial)");
        Check(NavigationCatalog.Search("wifi").Count==2&&NavigationCatalog.Search("  WiFi  分析 ").Single().Key=="Wi-Fi分析","Case-insensitive multi-term WiFi aliases");
        Check(NavigationCatalog.Search("DNS").Any(e=>e.Key=="网络诊断")&&NavigationCatalog.Search("NOT_FOUND_QUERY").Count==0,"Search DNS alias and empty results");
        Check(new[]{"新增功能总览","一键系统优化","开机临时文件清理","跨机房唤醒","学生机远程电源","远程电源诊断","软件清单清理BAT"}.All(k=>NavigationCatalog.Find(k).Trial),"New and unconfirmed power entries stay in trial group");
        Check(NavigationCatalog.Find("软件清单清理BAT").Note.Contains("新入口")&&NavigationCatalog.Find("学生机远程电源").Since=="0.6.2"&&NavigationCatalog.Search("0.6.4").Count==3,"Trial metadata separates original capability version from new shortcuts");
        Check(NavigationCatalog.Find("DNS记录查询").DisplayName.Contains("历史")&&NavigationCatalog.Find("软件清单清理BAT").DisplayName.Contains("v0.6.4"),"Trial badges preserve unknown historical versions honestly");
        entries[0]=null;Check(NavigationCatalog.All[0]!=null,"Catalog caller cannot replace internal entries");
        Check(NetworkDiscoveryService.ParsePorts("80,443,80,8000-8002").SequenceEqual(new[]{80,443,8000,8001,8002}),"Port ranges sorted and deduplicated");
        Reject(()=>NetworkDiscoveryService.ParsePorts("0"),"Port zero");Reject(()=>NetworkDiscoveryService.ParsePorts("65536"),"Port overflow");Reject(()=>NetworkDiscoveryService.ParsePorts("100-50"),"Reversed port range");Reject(()=>NetworkDiscoveryService.ParsePorts("1-1025"),"Scan size cap");
        Check(NetworkDiscoveryService.ParseSubnet("192.168.2.99/24").First()=="192.168.2.1"&&NetworkDiscoveryService.ParseSubnet("192.168.2.99/24").Last()=="192.168.2.254","CIDR alignment excludes network/broadcast");
        Check(NetworkDiscoveryService.ParseSubnet("10.0.0.2/31").SequenceEqual(new[]{"10.0.0.2","10.0.0.3"})&&NetworkDiscoveryService.ParseSubnet("10.0.0.2").Length==1,"Point-to-point and single-host subnet");
        Reject(()=>NetworkDiscoveryService.ParseSubnet("10.0.0.0/16"),"Subnet cap");Reject(()=>NetworkDiscoveryService.ParseSubnet("224.0.0.1/24"),"Multicast discovery rejected");Reject(()=>NetworkDiscoveryService.ParseSubnet("192.168.1.300"),"Malformed IPv4");
        Reject(()=>NetworkDiscoveryService.SingleHost("127.0.0.1,127.0.0.1"),"Duplicate single-target injection rejected");
        Check(NetworkSystemService.Argument("name with spaces")=="\"name with spaces\""&&NetworkSystemService.Argument("a\"b")=="\"a\\\"b\""&&NetworkSystemService.Argument("a\\")=="\"a\\\\\"","Windows argv spaces, embedded quotes and trailing backslash");
        Reject(()=>NetworkSystemService.Argument("test\nkey=clear"),"Newline argv injection rejected");
    }
    static void CameraParser()
    {
        string id=Guid.NewGuid().ToString();string xml="<s:Envelope xmlns:s='http://www.w3.org/2003/05/soap-envelope' xmlns:a='http://schemas.xmlsoap.org/ws/2004/08/addressing' xmlns:d='http://schemas.xmlsoap.org/ws/2005/04/discovery'><s:Header><a:RelatesTo>uuid:"+id+"</a:RelatesTo></s:Header><s:Body><d:ProbeMatches><d:ProbeMatch><a:EndpointReference><a:Address>urn:uuid:device1</a:Address></a:EndpointReference><d:Types>dn:NetworkVideoTransmitter</d:Types><d:Scopes>onvif://name/Camera</d:Scopes><d:XAddrs>http://192.168.1.2/onvif/device_service javascript:alert(1) http://u:p@example.com/</d:XAddrs></d:ProbeMatch></d:ProbeMatches></s:Body></s:Envelope>";
        var devices=CameraDiscoveryService.Parse(Encoding.UTF8.GetBytes(xml),"192.168.1.2",id);Check(devices.Count==1&&devices[0].Addresses=="http://192.168.1.2/onvif/device_service","WS-Discovery parsing and unsafe URL filtering");
        bool rejected=false;try{CameraDiscoveryService.Parse(Encoding.UTF8.GetBytes(xml),"x",Guid.NewGuid().ToString());}catch(FormatException){rejected=true;}Check(rejected,"Reply must correlate to current probe");
        rejected=false;try{CameraDiscoveryService.Parse(Encoding.UTF8.GetBytes("<!DOCTYPE a [<!ENTITY e SYSTEM 'file:///etc/passwd'>]><a>&e;</a>"),"x",id);}catch(XmlException){rejected=true;}Check(rejected,"XML external entity prohibited");
        rejected=false;try{CameraDiscoveryService.Parse(new byte[65537],"x",id);}catch(FormatException){rejected=true;}Check(rejected,"Discovery reply size cap");
        var doc=new XmlDocument();doc.LoadXml(CameraDiscoveryService.Probe(id));Check(doc.DocumentElement!=null,"Discovery request is well-formed XML");
    }
    static async Task Scan()
    {
        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;
        try
        {
            Task<TcpClient> accepting=listener.AcceptTcpClientAsync();var probe=await NetworkDiscoveryService.Probe(IPAddress.Loopback,port,1000,CancellationToken.None);
            using(var peer=await accepting){peer.ReceiveTimeout=1500;Check(probe.State=="开放"&&peer.GetStream().ReadByte()==-1,"TCP open probe closes without sending data");}
            var cancel=new CancellationTokenSource();int callbacks=0;
            bool stopped=false;try{await NetworkDiscoveryService.ScanPorts("127.0.0.1",Enumerable.Repeat(port,20).ToArray(),1,1000,cancel.Token,r=>{callbacks++;cancel.Cancel();});}catch(OperationCanceledException){stopped=true;}
            Check(stopped&&callbacks==1,"Cancellation stops scan after first result");cancel.Dispose();
        }
        finally{listener.Stop();}
        var refused=await NetworkDiscoveryService.Probe(IPAddress.Loopback,port,1000,CancellationToken.None);Check(refused.State=="拒绝连接","Closed local port reported as refusal, not open");
        int active=0,max=0,finished=0;object gate=new object();
        Func<int,Task> work=async delegate(int index){int current=Interlocked.Increment(ref active);lock(gate)max=Math.Max(max,current);await Task.Delay(15);Interlocked.Decrement(ref active);Interlocked.Increment(ref finished);};
        var bounded=typeof(NetworkDiscoveryService).GetMethod("Bounded",BindingFlags.Static|BindingFlags.NonPublic);
        await (Task)bounded.Invoke(null,new object[]{40,3,CancellationToken.None,work});Check(max<=3&&finished==40,"Worker concurrency never exceeds configured limit");
        var cancelled=new CancellationTokenSource();cancelled.Cancel();int count=0;
        try{await NetworkDiscoveryService.ScanPorts("127.0.0.1",new[]{port},1,1000,cancelled.Token,r=>count++);}catch(OperationCanceledException){}
        Check(count==0,"Pre-cancelled scan opens no connections");cancelled.Dispose();
    }
    static async Task Http()
    {
        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;string received="";
        var server=Task.Run(new Func<Task>(async delegate{
            using(var client=await listener.AcceptTcpClientAsync())using(var stream=client.GetStream())using(var reader=new StreamReader(stream))
            {var lines=new List<string>();string line;while(!String.IsNullOrEmpty(line=await reader.ReadLineAsync()))lines.Add(line);received=String.Join("\n",lines.ToArray());byte[] response=Encoding.ASCII.GetBytes("HTTP/1.1 302 Found\r\nLocation: http://127.0.0.1:1/no-follow\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");await stream.WriteAsync(response,0,response.Length);}
        }));
        try{string result=await NetworkSystemService.HttpCheck("http://127.0.0.1:"+port+"/test",CancellationToken.None);await server;Check(result.Contains("HTTP 302")&&received.StartsWith("HEAD /test ")&&!received.Contains("Authorization:"),"HTTP HEAD, no credentials, no redirect following");}finally{listener.Stop();}
        var stalled=new TcpListener(IPAddress.Loopback,0);stalled.Start();int stalledPort=((IPEndPoint)stalled.LocalEndpoint).Port;
        var accepted=new TaskCompletionSource<bool>();
        Task hold=Task.Run(new Func<Task>(async delegate{using(var client=await stalled.AcceptTcpClientAsync()){accepted.TrySetResult(true);await Task.Delay(700);}}));
        try{using(var cancel=new CancellationTokenSource()){Task<string> checking=NetworkSystemService.HttpCheck("http://127.0.0.1:"+stalledPort+"/wait",cancel.Token);if(await Task.WhenAny(accepted.Task,Task.Delay(3000))!=accepted.Task)throw new Exception("HTTP server was not reached");cancel.Cancel();bool stopped=false;try{await checking;}catch(OperationCanceledException){stopped=true;}await hold;Check(stopped,"HTTP cancellation aborts an established pending request");}}finally{stalled.Stop();}
        bool rejected=false;try{await NetworkSystemService.HttpCheck("http://user:password@example.com",CancellationToken.None);}catch(ArgumentException){rejected=true;}Check(rejected,"Credentials in URL rejected before network request");
    }
    static async Task Commands()
    {
        bool windows=Environment.OSVersion.Platform==PlatformID.Win32NT;
        string result=CommandService.Run(windows?"cmd.exe":"/bin/echo",windows?"/d /c echo tool-test":"tool-test",1);Check(result.Contains("返回代码：0")&&result.Contains("tool-test"),"Command exit code and drained output");
        string errors=CommandService.Run(windows?"cmd.exe":"/bin/sh",windows?"/d /c \"echo tool-error 1>&2 & exit /b 7\"":"-c \"echo tool-error >&2; exit 7\"",1);
        Check(errors.Contains("返回代码：7")&&errors.Contains("tool-error"),"Nonzero exit code and stderr retained");
        using(var cancel=new CancellationTokenSource())
        {
            cancel.CancelAfter(250);var watch=Stopwatch.StartNew();bool stopped=false;
            try{await Task.Run(new Func<string>(delegate{return CommandService.Run(windows?"powershell.exe":"/bin/sleep",windows?"-NoProfile -NonInteractive -Command \"Start-Sleep -Seconds 30\"":"30",1,cancel.Token);}));}catch(OperationCanceledException){stopped=true;}
            Check(stopped&&watch.ElapsedMilliseconds<5000,"Cancellation terminates command promptly");
        }
    }
    static int Main(){try{Catalog();CameraParser();Scan().GetAwaiter().GetResult();Http().GetAwaiter().GetResult();Commands().GetAwaiter().GetResult();Console.WriteLine("NETWORK TOOLS TOTAL PASS "+passed);return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
