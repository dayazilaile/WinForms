using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ITDesktopToolkit;
class TrialTests
{
    static int passed;
    static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);passed++;}
    static void Reject(Action action,string name){try{action();}catch(ArgumentException){Check(true,name);return;}throw new Exception("Expected argument rejection: "+name);}
    static void Dns()
    {
        Check(TrialNetworkService.DnsName(" example.com. ","A")=="example.com","FQDN normalization");
        Check(TrialNetworkService.DnsName("例子.测试","AAAA")=="xn--fsqu00a.xn--0zwm56d","IDN query normalized to ASCII");
        Check(TrialNetworkService.DnsName("_ldap._tcp.example.com","SRV")=="_ldap._tcp.example.com","SRV underscore labels supported");
        Check(TrialNetworkService.DnsName("1.2.3.4","PTR")=="4.3.2.1.in-addr.arpa","IPv4 PTR reverse name");
        Check(TrialNetworkService.DnsName("::1","PTR")=="1."+String.Concat(Enumerable.Repeat("0.",31))+"ip6.arpa","IPv6 PTR reverse nibbles");
        Reject(()=>TrialNetworkService.DnsName("example.com; exit 0","A"),"Command syntax rejected from DNS name");
        Reject(()=>TrialNetworkService.DnsName("example.com","A;exit"),"Only supported DNS types allowed");
        Reject(()=>TrialNetworkService.DnsQuery("example.com","A","1.1.1.1;exit",CancellationToken.None),"DNS server must be literal IP before command execution");
        var types=TrialNetworkService.DnsTypes;types[0]="bad";Check(TrialNetworkService.DnsTypes[0]=="A","DNS type catalog cannot be mutated externally");
        string[] values={"中文名称","TXT","line1\n=example","60","Answer"};string raw="返回内容\r\n返回代码：0\r\nITTRIAL|"+String.Join("|",values.Select(x=>Convert.ToBase64String(Encoding.UTF8.GetBytes(x))));
        var rows=TrialNetworkService.Parse(raw,5);Check(rows.Rows.Count==1&&(string)rows.Rows[0][2]==values[2],"Unicode and multiline DNS records round-trip structurally");
        bool rejected=false;try{TrialNetworkService.Parse("\n返回代码：1\n",5);}catch(InvalidOperationException){rejected=true;}Check(rejected,"Failed process cannot become successful empty results");
        rejected=false;try{TrialNetworkService.Parse("\n返回代码：0\nITTRIAL|bad",5);}catch(FormatException){rejected=true;}Check(rejected,"Partial table records rejected");
    }
    static void Local()
    {
        string proxy=TrialNetworkService.RedactProxy("https://user:secret@proxy.example:8080/private-token/config.pac?token=abc#secret");
        Check(!proxy.Contains("user")&&!proxy.Contains("secret")&&!proxy.Contains("private-token")&&!proxy.Contains("abc")&&proxy.Contains("proxy.example:8080"),"Proxy credentials, paths and query tokens redacted");
        Check(TrialNetworkService.RedactProxy("http://[::1]:8080")=="http://[::1]:8080"&&TrialNetworkService.RedactProxy(null)=="未设置","IPv6 proxy formatting and missing settings");
        Check(TrialNetworkService.RedactProxy("http=u:p@host:3128;https=private:pass@other:8080").Contains("host:3128")&&!TrialNetworkService.RedactProxy("u:p@host").Contains("u:p"),"Non-URI proxy specification redaction");
        proxy=TrialNetworkService.RedactProxy("WinHTTP:\r\nhttps://user:secret@proxy.example/private-token/pac?token=abc\r\nend");
        Check(proxy.Contains("proxy.example")&&!proxy.Contains("secret")&&!proxy.Contains("private-token")&&!proxy.Contains("abc"),"URLs embedded in multiline WinHTTP output are redacted");
        var rows=TrialNetworkService.Evaluate("test","192.168.1.20","255.255.255.0","192.168.1.1",true);Check(rows.Any(r=>(string)r[1]=="网关与子网"&&(string)r[2]=="同一子网"),"Gateway subnet bit comparison");
        rows=TrialNetworkService.Evaluate("test","169.254.1.2","255.0.255.0","192.168.1.1",false);Check(rows.Count(r=>(string)r[2]=="需核对")>=3&&rows.Any(r=>r[3].ToString().Contains("可能")),"APIPA, noncontiguous mask and missing DNS are checks, not definitive LAN failures");
        rows=TrialNetworkService.Evaluate("test","10.0.0.2","255.255.255.0","",true);Check(rows.Any(r=>r[3].ToString().Contains("隔离网络")),"Missing gateway may be valid isolated LAN");
        Check(TrialNetworkService.Evaluate("test","::1","::","",true).Count==0,"IPv6 not incorrectly treated as IPv4 audit");
    }
    static async Task Tracing()
    {
        int calls=0;var rows=new List<TraceHop>();Func<int,int,CancellationToken,Task<TraceHop>> fake=delegate(int ttl,int ms,CancellationToken cancel){calls++;return Task.FromResult(new TraceHop{Ttl=ttl,Address="192.0.2."+ttl,Status=ttl==3?"Success":"TtlExpired",Elapsed=2,Reached=ttl==3});};
        await TrialNetworkService.TraceCore(16,1200,CancellationToken.None,fake,rows.Add);Check(calls==3&&rows.Last().Reached,"ICMP trace stops at reached target");
        calls=0;rows.Clear();await TrialNetworkService.TraceCore(2,1200,CancellationToken.None,fake,rows.Add);Check(calls==2&&!rows.Last().Reached,"Trace never exceeds configured hop cap");
        using(var cancel=new CancellationTokenSource()){cancel.Cancel();calls=0;try{await TrialNetworkService.TraceCore(30,1200,cancel.Token,fake,rows.Add);}catch(OperationCanceledException){}Check(calls==0,"Cancelled trace sends no probes");}
        bool rejected=false;try{await TrialNetworkService.Trace("127.0.0.1",31,1200,CancellationToken.None,r=>{});}catch(ArgumentException){rejected=true;}Check(rejected,"Public trace rejects excess hops before resolution");
    }
    static void Database()
    {
        string dir=Path.Combine(Path.GetTempPath(),"toolkit-catalog-"+Guid.NewGuid());Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir,"pci.v052.ids"),"10de  NVIDIA\n\t2684  AD102 RTX4090\n\t\t1043 1234 Board variant\nC 00  Class\n\t1234  NOT A DEVICE\n");
            File.WriteAllText(Path.Combine(dir,"pci.ids"),"10de  Local vendor override\n\t2684  Local device override\n");
            File.WriteAllText(Path.Combine(dir,"usb.v052.ids"),"0bda  Realtek\n\t8153  RTL8153\nC 00 Class\nAT  Audio\n0100  NOT A VENDOR\n\t0001 NOT A DEVICE\n");
            var ids=new IdCatalogService(dir);ids.Reload();var gpu=new HardwareItem{Identifier=@"PCI\VEN_10DE&DEV_2684&SUBSYS_12341043"};ids.MatchHardware(gpu);
            Check(ids.PciVendorCount==1&&ids.PciDeviceCount==1&&gpu.IdDevice=="Local device override"&&gpu.IdVendor=="Local vendor override","User ID overlay wins while PCI subsystem/class records are excluded");
            Check(ids.UsbVendorCount==1&&ids.UsbDeviceCount==1,"USB audio/class sections cannot pollute vendor map");
            File.WriteAllText(Path.Combine(dir,"hardware_models.tsv"),"主板\tASUSTeK\t华硕\tASUS 主板\t系统厂商规则\n内存\tKF560C36BBE-16\t本地品牌\t本地完整型号\t用户规则\n");
            File.WriteAllText(Path.Combine(dir,"board_models_v052.tsv"),"主板\tTUF GAMING B650-PLUS\t\t已登记型号\t不推断规格\thttps://example.com/board\texact\t\n");
            File.WriteAllText(Path.Combine(dir,"component_models_v052.tsv"),"内存\tKF5\tKingston\tDDR5 家族\t前缀非完整型号\thttps://example.com/ram\tprefix\t\n");
            byte[] original=File.ReadAllBytes(Path.Combine(dir,"hardware_models.tsv"));var db=new DatabaseService(dir);db.Reload();var board=new HardwareItem{Type="主板",Name="TUF GAMING B650-PLUS",Model="TUF GAMING B650-PLUS",Manufacturer="ASUSTeK COMPUTER INC."};db.MatchHardware(board);
            Check(board.DatabaseLabel=="已登记型号"&&board.DatabaseBrand=="华硕"&&board.DatabaseSource.Contains("example.com"),"Exact model metadata retains independent reliable brand rule and source");
            board.Model=board.Name="TUF GAMING B650-PLUS WIFI";db.MatchHardware(board);Check(board.DatabaseSource==""&&board.DatabaseLabel=="ASUS 主板","Exact motherboard matching does not confuse WIFI variants");
            board.Model=board.Name="TUF GAMING B650-PLUS";board.Manufacturer="Other OEM";db.MatchHardware(board);Check(board.DatabaseBrand=="","Model-name catalog never guesses motherboard vendor");
            var ram=new HardwareItem{Type="内存",Model="KF560C36BBE-16"};db.MatchHardware(ram);Check(ram.DatabaseLabel=="本地完整型号"&&ram.DatabaseBrand=="本地品牌","Existing full model user rule wins over bundled family prefix");
            ram.Model="KF564C32BB-32";db.MatchHardware(ram);Check(ram.DatabaseBrand=="Kingston"&&ram.MatchState=="家族前缀匹配","Prefix identifies family with explicitly limited match state");
            Check(File.ReadAllBytes(Path.Combine(dir,"hardware_models.tsv")).SequenceEqual(original),"Read/reload/matching never overwrite user database");
        }finally{Directory.Delete(dir,true);}
        var full=new DatabaseService();full.Reload();Check(full.HardwareRuleCount>500&&full.Ids.PciVendorCount>1000&&full.Ids.PciDeviceCount>10000&&full.Ids.UsbVendorCount>1000&&full.Ids.UsbDeviceCount>10000,"Bundled hardware/PCI/USB expansion loaded at runtime");
        var known=new HardwareItem{Type="显卡",Identifier=@"PCI\VEN_10DE&DEV_2684"};full.MatchHardware(known);Check(known.IdDevice.Contains("RTX 4090"),"Real upstream GPU ID resolves without inventing retail board brand");
        var disk=new HardwareItem{Type="硬盘",Model="CT1000P3PSSD8"};full.MatchHardware(disk);Check(disk.DatabaseLabel=="Crucial P3 Plus 系列","Longer model prefix distinguishes P3 Plus from P3");
        var missing=new HardwareItem{Type="主板",Name="To be filled by O.E.M.",Model="To be filled by O.E.M.",Manufacturer="Unknown"};full.MatchHardware(missing);Check(missing.DatabaseSource=="","Unknown SMBIOS placeholders excluded from exact model catalog");
        Console.WriteLine("HARDWARE COUNTS rules="+full.HardwareRuleCount+" PCI="+full.Ids.PciVendorCount+"/"+full.Ids.PciDeviceCount+" USB="+full.Ids.UsbVendorCount+"/"+full.Ids.UsbDeviceCount);
    }
    static void WriteScriptFixtures()
    {
        string dir=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"generated-tests");Directory.CreateDirectory(dir);
        // Stubs exercise production PowerShell formatting on Windows without DNS/network activity.
        string dns=@"function Resolve-DnsName { [pscustomobject]@{Name='example.test';Type='TXT';Strings=@('line1','中文');TTL=60;Section='Answer'} }
$rows=@(& { "+TrialNetworkService.DnsScript("example.test","TXT","")+@" })
if($rows.Count -ne 1 -or $rows[0].Split('|').Count -ne 6){throw 'DNS table contract failed'}
$decoded=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($rows[0].Split('|')[3]))
if($decoded -ne 'line1 | 中文'){throw 'TXT content lost'}
'PASS trial fixture DNS'
";
        string connections=@"function Get-NetTCPConnection { [pscustomobject]@{LocalAddress='127.0.0.1';LocalPort=12345;RemoteAddress='0.0.0.0';RemotePort=0;State='Listen';OwningProcess=$PID} }
function Get-NetUDPEndpoint { [pscustomobject]@{LocalAddress='::';LocalPort=54321;OwningProcess=$PID} }
$rows=@(& { "+TrialNetworkService.ConnectionsScript(true)+@" })
if($rows.Count -ne 2){throw 'Connection table count failed'}
foreach($row in $rows){if($row.Split('|').Count -ne 9){throw 'Connection columns failed'}}
'PASS trial fixture connections'
";
        File.WriteAllText(Path.Combine(dir,"trial-dns-fixture.ps1"),dns,new UTF8Encoding(true));
        File.WriteAllText(Path.Combine(dir,"trial-connections-fixture.ps1"),connections,new UTF8Encoding(true));
    }
    static int Main(){try{Dns();Local();Tracing().GetAwaiter().GetResult();Database();WriteScriptFixtures();Console.WriteLine("TRIAL TOTAL PASS "+passed);return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
