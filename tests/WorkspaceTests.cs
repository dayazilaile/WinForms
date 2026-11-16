using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ITDesktopToolkit;
class WorkspaceTests
{
    static int passed;
    static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);passed++;}
    static void FormatReject(Action action,string name){try{action();}catch(FormatException){Check(true,name);return;}throw new Exception("Expected invalid-data rejection: "+name);}
    static WifiInventoryService.Ssid Ssid(byte[] text){var bytes=new byte[32];Array.Copy(text,bytes,text.Length);return new WifiInventoryService.Ssid{Length=(uint)text.Length,Bytes=bytes};}
    static void Wifi()
    {
        Check(Marshal.SizeOf(typeof(WifiInventoryService.InterfaceInfo))==532&&Marshal.SizeOf(typeof(WifiInventoryService.Connection))==604,"WLAN interface and connection ABI layouts");
        Check(Marshal.SizeOf(typeof(WifiInventoryService.BssEntry))==360&&Marshal.SizeOf(typeof(WifiInventoryService.RateSet))==256,"WLAN BSS and rate-set ABI sizes");
        Check(Marshal.OffsetOf(typeof(WifiInventoryService.BssEntry),"Frequency").ToInt32()==92&&Marshal.OffsetOf(typeof(WifiInventoryService.BssEntry),"Rates").ToInt32()==96,"WLAN frequency/rate ABI offsets");
        Check(WifiInventoryService.Channel(2412000)=="1"&&WifiInventoryService.Channel(2484000)=="14"&&WifiInventoryService.Band(2412000)=="2.4 GHz","2.4GHz channels including special channel 14");
        Check(WifiInventoryService.Frequency(5180000)=="5180"&&WifiInventoryService.Channel(5180000)=="36"&&WifiInventoryService.Band(5180000)=="5 GHz","Frequency converts kHz to MHz");
        Check(WifiInventoryService.Channel(5955000)=="1"&&WifiInventoryService.Channel(5935000)=="2"&&WifiInventoryService.Band(5955000)=="6 GHz","6GHz channel mapping including channel 2");
        Check(WifiInventoryService.Channel(0)=="未知"&&WifiInventoryService.Frequency(0)=="未知"&&WifiInventoryService.Band(0)=="未知","Unknown frequency never invented");
        ushort[] rates={0x8002,0x8004,11,108};Check(WifiInventoryService.Rates(rates,4,true)=="1, 2"&&WifiInventoryService.Rates(rates,4,false)=="1, 2, 5.5, 54","Basic-rate flag and 0.5Mbps unit conversion");
        Check(WifiInventoryService.Rates(new ushort[]{12,24},2,true)=="未知","Absent basic-rate flags not guessed");
        FormatReject(()=>WifiInventoryService.Rates(rates,5,false),"Malformed rate-set length rejected");
        Check(WifiInventoryService.SsidText(Ssid(Encoding.UTF8.GetBytes("测试网络")))=="测试网络","Unicode SSID decode");
        Check(WifiInventoryService.SsidText(Ssid(new byte[]{255}))=="0xFF"&&WifiInventoryService.SsidText(Ssid(new byte[0]))=="(隐藏网络)","Binary and hidden SSIDs preserved accurately");
        int size=8+Marshal.SizeOf(typeof(WifiInventoryService.BssEntry));IntPtr buffer=Marshal.AllocHGlobal(size);
        try
        {
            var entry=new WifiInventoryService.BssEntry{Ssid=Ssid(Encoding.UTF8.GetBytes("测试网络")),Mac=new byte[]{2,0,0,0,0,1},Type=1,Phy=8,Quality=82,Rssi=-48,Frequency=5180000,Capabilities=0x10,Rates=new WifiInventoryService.RateSet{Length=4,Values=new ushort[126]}};
            Array.Copy(rates,entry.Rates.Values,4);Marshal.WriteInt32(buffer,size);Marshal.WriteInt32(buffer,4,1);Marshal.StructureToPtr(entry,IntPtr.Add(buffer,8),false);
            var rows=WifiInventoryService.ParseBssList(buffer,"样例网卡");var row=rows.Single();
            Check(row.Bssid=="02:00:00:00:00:01"&&row.Frequency=="5180"&&row.Phy=="802.11ac"&&row.BasicRates=="1, 2"&&row.SupportedRates=="1, 2, 5.5, 54","Native BSS buffer parsed into explicit table columns");
            Check(row.Cells().Length==13&&(string)row.Cells()[8]==row.BasicRates&&(string)row.Cells()[6]==row.Frequency&&new WifiAdapterRow().Cells().Length==13&&(string)new WifiAdapterRow{Rx="866.7"}.Cells()[5]=="866.7","Adapter and hotspot table mappings retain basic, frequency and link-rate values");
            Marshal.WriteInt32(buffer,4,2);FormatReject(()=>WifiInventoryService.ParseBssList(buffer,"test"),"Truncated native BSS list rejected before traversal");
        }finally{Marshal.FreeHGlobal(buffer);}
        FormatReject(()=>WifiInventoryService.ParseBssList(IntPtr.Zero,"test"),"Null native WLAN pointer rejected");
        Check((string)new WifiBssRow{Signal="82",Rssi="-48"}.Cells()[3]=="82%"&&(string)new WifiBssRow{Rssi="-48"}.Cells()[2]=="-48","Percent suffix is in signal values and RSSI remains numeric in leading columns");
        Check(WifiInventoryService.SignalText("未知")=="未知"&&WifiInventoryService.SignalText("0")=="0%"&&WifiInventoryService.SignalText("100")=="100%","Unknown signals never receive a misleading percent suffix");
    }
    static async Task Discovery()
    {
        int calls=0;var results=new List<HostProbe>();object gate=new object();
        Func<string,int,CancellationToken,Task<HostProbe>> fake=async delegate(string host,int timeout,CancellationToken cancel){Interlocked.Increment(ref calls);await Task.Delay(1,cancel);return new HostProbe{Address=host,Icmp=host=="one"?"Success":"TimedOut",Milliseconds=host=="one"?(long?)7:null};};
        await NetworkDiscoveryService.DiscoverCore(new[]{"one","two"},2,800,CancellationToken.None,fake,r=>{lock(gate)results.Add(r);});
        Check(calls==2&&results.Count==2&&results.Count(r=>r.Responded)==1,"Host discovery invokes one Ping probe per host, independent of TCP services");
        Check(!results[0].ToString().Contains("TCP")&&results.Single(r=>!r.Responded).Milliseconds==null,"Ping-only results contain no port/open-host assumptions");
        using(var cancel=new CancellationTokenSource()){cancel.Cancel();calls=0;try{await NetworkDiscoveryService.DiscoverCore(new[]{"one"},1,800,cancel.Token,fake,r=>{});}catch(OperationCanceledException){}Check(calls==0,"Cancelled discovery starts no Ping probes");}
    }
    static void Catalogs()
    {
        int[] ports=NetworkDiscoveryService.ParsePorts(CommonPorts.DefaultText);Check(ports.Length==CommonPorts.Count&&ports.Length>=100&&ports.Distinct().Count()==ports.Length,"Comprehensive common TCP preset, unique and within scan cap");
        Check(new[]{21,22,23,25,53,80,135,139,443,445,993,1433,1521,3306,3389,5432,6379,8080,8443,9100,27017}.All(p=>ports.Contains(p)),"Common infrastructure, database, web, mail and printer ports covered");
        Check(CommonPorts.Service(22).Contains("SSH")&&CommonPorts.Service(65000)=="自定义","Service names are hints, unknown ports explicit");
        Check(NavigationCatalog.All.Select(e=>e.Group).Distinct().Count()==6&&NavigationCatalog.Find("U 盘检查与防护").Group=="系统与硬件","Safety functions merged into system/hardware");
        var result=new RouteInventory();string encoded=String.Join("|",new[]{"以太网","2","0.0.0.0/0","192.168.1.1","25","NetMgmt"}.Select(x=>Convert.ToBase64String(Encoding.UTF8.GetBytes(x)))).ToString();
        RouteInventoryService.ParseRows("command header\nITNET|route|"+encoded,result);Check(result.Routes.Count==1&&(string)result.Routes[0][0]=="以太网"&&(string)result.Routes[0][1]=="IPv4","Structured route rows independent of localized text layout");
        string neighbor=String.Join("|",new[]{"以太网","::1","00:00:00:00:00:00","5","23"}.Select(x=>Convert.ToBase64String(Encoding.UTF8.GetBytes(x))));RouteInventoryService.ParseRows("ITNET|neighbor|"+neighbor,result);Check((string)result.Neighbors[0][3]=="可达"&&(string)result.Neighbors[0][4]=="IPv6","Numeric native neighbor/family enums normalized for readable tables");
        FormatReject(()=>RouteInventoryService.ParseRows("ITNET|route|bad",new RouteInventory()),"Incomplete route record not silently accepted");
    }
    static int Main(){try{Wifi();Catalogs();Discovery().GetAwaiter().GetResult();Console.WriteLine("WORKSPACE TOTAL PASS "+passed);return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
