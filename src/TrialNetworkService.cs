using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
namespace ITDesktopToolkit
{
    public sealed class TrialRows
    {
        public readonly List<object[]> Rows=new List<object[]>();public string Note="";
    }
    public sealed class TraceHop
    {
        public int Ttl;public string Address,Status;public long? Elapsed;public bool Reached;
        public object[] Cells(){return new object[]{Ttl,Address,Status,Elapsed.HasValue?(object)Elapsed.Value:"未知"};}
    }
    public static class TrialNetworkService
    {
        private static readonly string[] dnsTypes={"A","AAAA","CNAME","MX","TXT","NS","SRV","PTR"};
        public static string[] DnsTypes{get{return (string[])dnsTypes.Clone();}}
        public static string DnsName(string input,string type)
        {
            if(Array.IndexOf(dnsTypes,type)<0)throw new ArgumentException("DNS 类型无效。");string name=(input??"").Trim();IPAddress address;
            if(type=="PTR"&&IPAddress.TryParse(name,out address))
            {
                byte[] bytes=address.GetAddressBytes();if(address.AddressFamily==AddressFamily.InterNetwork){Array.Reverse(bytes);return String.Join(".",Array.ConvertAll(bytes,value=>value.ToString(CultureInfo.InvariantCulture)))+".in-addr.arpa";}
                var b=new StringBuilder();for(int i=bytes.Length-1;i>=0;i--)b.Append((bytes[i]&15).ToString("x",CultureInfo.InvariantCulture)).Append('.').Append((bytes[i]>>4).ToString("x",CultureInfo.InvariantCulture)).Append('.');return b+"ip6.arpa";
            }
            name=name.TrimEnd('.');if(name.Length==0)throw new ArgumentException("请输入 DNS 名称。");
            var idn=new IdnMapping();var labels=new List<string>();foreach(string label in name.Split('.')){string ascii=label;bool unicode=false;foreach(char c in label)if(c>127){unicode=true;break;}if(unicode)ascii=idn.GetAscii(label);if(!Regex.IsMatch(ascii,@"^[A-Za-z0-9_](?:[A-Za-z0-9_-]{0,61}[A-Za-z0-9_])?$"))throw new ArgumentException("DNS 标签格式无效。");labels.Add(ascii);}
            name=String.Join(".",labels.ToArray());if(name.Length>253)throw new ArgumentException("DNS 名称太长。");return name;
        }
        private const string Emit=@"function Emit([object[]]$cells){$encoded=@($cells|ForEach-Object{[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes([string]$_))});'ITTRIAL|'+($encoded -join '|')};";
        internal static TrialRows Parse(string raw,int columns)
        {
            if(!raw.Replace("\r","").Contains("\n返回代码：0\n"))throw new InvalidOperationException(raw);
            var result=new TrialRows();foreach(string line in raw.Replace("\r","").Split('\n'))if(line.StartsWith("ITTRIAL|",StringComparison.Ordinal))
            {string[] fields=line.Split('|');if(fields.Length!=columns+1)throw new FormatException("试用工具返回的表格记录不完整。");var row=new object[columns];for(int i=0;i<columns;i++)row[i]=Encoding.UTF8.GetString(Convert.FromBase64String(fields[i+1]));result.Rows.Add(row);}
            return result;
        }
        public static TrialRows DnsQuery(string name,string type,string server,CancellationToken cancel)
        {
            var result=Parse(PowerShellService.RunScript(DnsScript(name,type,server),1,cancel),5);result.Note="查询完成 · DNS 查询可能被网络设备记录；本页不修改 DNS 配置";return result;
        }
        internal static string DnsScript(string name,string type,string server)
        {
            name=DnsName(name,type);server=(server??"").Trim();IPAddress ip;if(server.Length>0&&!IPAddress.TryParse(server,out ip))throw new ArgumentException("指定 DNS 服务器须为 IP 地址，留空使用系统设置。");
            string ps="$ErrorActionPreference='Stop';"+Emit+"try { $records=@(Resolve-DnsName -Name "+PowerShellService.Quote(name)+" -Type "+PowerShellService.Quote(type)+(server.Length==0?"":" -Server "+PowerShellService.Quote(server))+@" -DnsOnly -NoHostsFile -QuickTimeout -ErrorAction Stop);foreach($r in $records){$data=if($r.IPAddress){$r.IPAddress}elseif($r.NameExchange){$r.Preference.ToString()+' '+$r.NameExchange}elseif($r.NameTarget){$r.NameTarget+' port='+$r.Port+' priority='+$r.Priority+' weight='+$r.Weight}elseif($r.NameHost){$r.NameHost}elseif($r.Strings){$r.Strings -join ' | '}else{[string]$r};Emit @($r.Name,$r.Type,$data,$r.TTL,$r.Section)}}catch{Write-Output $_.Exception.Message;exit 1}";
            return ps;
        }
        public static TrialRows Connections(bool listenersOnly,CancellationToken cancel)
        {
            var result=Parse(PowerShellService.RunScript(ConnectionsScript(listenersOnly),2,cancel),8);result.Note="只读本机 · 每种协议最多显示 500 项；进程可能在读取中退出，UDP 绑定不等于远端可达";return result;
        }
        internal static string ConnectionsScript(bool listenersOnly)
        {
            string ps="$ErrorActionPreference='Stop';"+Emit+"try { $tcp=Get-NetTCPConnection -ErrorAction Stop"+(listenersOnly?" | Where-Object {$_.State -eq 'Listen'}":"")+@" | Select-Object -First 500;foreach($c in $tcp){$name='未知';try{$name=(Get-Process -Id $c.OwningProcess -ErrorAction Stop).ProcessName}catch{};Emit @('TCP',$c.LocalAddress,$c.LocalPort,$c.RemoteAddress,$c.RemotePort,$c.State,$c.OwningProcess,$name)};$udp=Get-NetUDPEndpoint -ErrorAction Stop | Select-Object -First 500;foreach($c in $udp){$name='未知';try{$name=(Get-Process -Id $c.OwningProcess -ErrorAction Stop).ProcessName}catch{};Emit @('UDP',$c.LocalAddress,$c.LocalPort,'—','—','本机绑定',$c.OwningProcess,$name)}}catch{Write-Output $_.Exception.Message;exit 1}";
            return ps;
        }
        private static string RedactUrl(Match match)
        {
            Uri uri;if(!Uri.TryCreate(match.Value,UriKind.Absolute,out uri)||String.IsNullOrEmpty(uri.Host))return "[代理 URL 已隐藏]";
            string host=uri.HostNameType==UriHostNameType.IPv6?"["+uri.Host.Trim('[',']')+"]":uri.Host;
            return uri.Scheme+"://"+host+(uri.IsDefaultPort?"":":"+uri.Port)+(uri.UserInfo.Length==0?"":" [凭据已隐藏]")+(uri.AbsolutePath!="/"||uri.Query.Length>0||uri.Fragment.Length>0?" [路径/参数已隐藏]":"");
        }
        public static string RedactProxy(string value)
        {
            if(String.IsNullOrEmpty(value))return "未设置";var output=new List<string>();foreach(string part in value.Split(';'))
            {
                string text=part.Trim();
                text=Regex.Replace(text,@"(?:https?|socks[0-9a-z]*)://[^\s;]+",RedactUrl,RegexOptions.IgnoreCase);
                if(text.IndexOf("file://",StringComparison.OrdinalIgnoreCase)>=0){output.Add("[本地配置 URL 已隐藏]");continue;}
                int at=text.LastIndexOf('@');if(at>=0)text="[凭据已隐藏]@"+text.Substring(at+1);
                int query=text.IndexOf('?'),fragment=text.IndexOf('#');int cut=query<0?fragment:fragment<0?query:Math.Min(query,fragment);if(cut>=0)text=text.Substring(0,cut)+" [参数已隐藏]";output.Add(text);
            }
            return String.Join("; ",output.ToArray());
        }
        public static TrialRows Proxy(CancellationToken cancel)
        {
            var result=new TrialRows();cancel.ThrowIfCancellationRequested();
            using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings"))
            foreach(string name in new[]{"ProxyEnable","ProxyServer","ProxyOverride","AutoConfigURL","AutoDetect"}){object value=key==null?null:key.GetValue(name);result.Rows.Add(new object[]{"当前用户",name,value==null?"未设置":RedactProxy(Convert.ToString(value,CultureInfo.InvariantCulture))});}
            foreach(string name in new[]{"HTTP_PROXY","HTTPS_PROXY","ALL_PROXY","NO_PROXY"}){cancel.ThrowIfCancellationRequested();string value=Environment.GetEnvironmentVariable(name);result.Rows.Add(new object[]{"当前进程环境",name,RedactProxy(value)});}
            string netsh=CommandService.Run(Path.Combine(Environment.SystemDirectory,"netsh.exe"),"winhttp show proxy",1,cancel);result.Rows.Add(new object[]{"WinHTTP","系统返回",RedactProxy(netsh)});result.Note="只读取配置，不下载 PAC、不探测 WPAD、不更改代理。显示已去除凭据及 URL 参数；应用实际行为取决于自身设置。";return result;
        }
        internal static bool SameSubnet(IPAddress ip,IPAddress mask,IPAddress gateway)
        {
            byte[] a=ip.GetAddressBytes(),m=mask.GetAddressBytes(),g=gateway.GetAddressBytes();if(a.Length!=4||m.Length!=4||g.Length!=4)throw new ArgumentException("仅比较 IPv4 子网。");for(int i=0;i<4;i++)if((a[i]&m[i])!=(g[i]&m[i]))return false;return true;
        }
        internal static List<object[]> Evaluate(string adapter,string address,string mask,string gateway,bool hasDns)
        {
            var rows=new List<object[]>();IPAddress ip,net,gw;
            if(!IPAddress.TryParse(address,out ip)||ip.AddressFamily!=AddressFamily.InterNetwork)return rows;
            rows.Add(new object[]{adapter,"IPv4 地址",ip.GetAddressBytes()[0]==169&&ip.GetAddressBytes()[1]==254?"需核对":"信息",address+(ip.GetAddressBytes()[0]==169&&ip.GetAddressBytes()[1]==254?"：自动私有地址，可能未取得 DHCP 租约":"")});
            if(IPAddress.TryParse(mask,out net)&&net.AddressFamily==AddressFamily.InterNetwork)
            {
                uint bits=0;foreach(byte b in net.GetAddressBytes())bits=(bits<<8)|b;uint inverse=~bits;bool valid=(inverse&(inverse+1))==0;
                rows.Add(new object[]{adapter,"子网掩码",valid?"格式正常":"需核对",mask});
                if(IPAddress.TryParse(gateway,out gw)&&gw.AddressFamily==AddressFamily.InterNetwork)rows.Add(new object[]{adapter,"网关与子网",valid&&SameSubnet(ip,net,gw)?"同一子网":"需核对",gateway+"；VPN、点对点及特殊路由可例外"});
                else rows.Add(new object[]{adapter,"默认网关","信息","未配置 IPv4 网关；仅局域网/隔离网络可能正常"});
            }
            rows.Add(new object[]{adapter,"DNS 配置",hasDns?"已配置":"需核对",hasDns?"存在 DNS 地址；未主动验证服务器响应":"未发现 DNS 地址；仅内网或静态解析环境可例外"});return rows;
        }
        public static TrialRows Audit(CancellationToken cancel)
        {
            var result=new TrialRows();var addresses=new Dictionary<string,List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach(var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                cancel.ThrowIfCancellationRequested();if(nic.OperationalStatus!=OperationalStatus.Up||nic.NetworkInterfaceType==NetworkInterfaceType.Loopback)continue;var p=nic.GetIPProperties();string gateway="";foreach(var g in p.GatewayAddresses)if(g.Address.AddressFamily==AddressFamily.InterNetwork){gateway=g.Address.ToString();break;}
                foreach(var a in p.UnicastAddresses)if(a.Address.AddressFamily==AddressFamily.InterNetwork){string ip=a.Address.ToString();result.Rows.AddRange(Evaluate(nic.Name,ip,a.IPv4Mask.ToString(),gateway,p.DnsAddresses.Count>0));List<string> owners;if(!addresses.TryGetValue(ip,out owners)){owners=new List<string>();addresses.Add(ip,owners);}owners.Add(nic.Name);}
            }
            foreach(var pair in addresses)if(pair.Value.Count>1)result.Rows.Add(new object[]{String.Join(", ",pair.Value.ToArray()),"本机重复地址","需核对",pair.Key+"；桥接/VPN 可共享地址，这不能证明局域网 IP 冲突"});
            result.Note="仅核对本机已连接接口的 IPv4 配置，IPv6 未核对；不发 ARP/Ping，不修改设置，不声称排除局域网冲突。";return result;
        }
        public static async Task Trace(string host,int maxHops,int timeout,CancellationToken cancel,Action<TraceHop> receive)
        {
            if(maxHops<1||maxHops>30||timeout<100||timeout>3000)throw new ArgumentException("跳数 1–30，单跳超时 100–3000 毫秒。");IPAddress target=await NetworkDiscoveryService.Resolve(host,cancel);if(target.AddressFamily!=AddressFamily.InterNetwork)throw new ArgumentException("此试用版路由跟踪仅支持 IPv4。");
            await TraceCore(maxHops,timeout,cancel,async delegate(int ttl,int ms,CancellationToken token){
                using(var ping=new Ping())
                {
                    token.ThrowIfCancellationRequested();var clock=System.Diagnostics.Stopwatch.StartNew();Task<PingReply> sending=ping.SendPingAsync(target,ms,new byte[32],new PingOptions(ttl,false));
                    using(token.Register(delegate{ThreadPool.QueueUserWorkItem(delegate{try{ping.SendAsyncCancel();}catch{}});}))
                    try{var reply=await sending;return new TraceHop{Ttl=ttl,Address=reply.Address==null?"*":reply.Address.ToString(),Status=reply.Status.ToString(),Elapsed=reply.Status==IPStatus.TimedOut?(long?)null:clock.ElapsedMilliseconds,Reached=reply.Status==IPStatus.Success};}
                    catch(Exception ex){token.ThrowIfCancellationRequested();throw new InvalidOperationException("ICMP 探测不可用："+ex.Message,ex);}
                }
            },receive);
        }
        internal static async Task TraceCore(int maxHops,int timeout,CancellationToken cancel,Func<int,int,CancellationToken,Task<TraceHop>> probe,Action<TraceHop> receive)
        {
            for(int ttl=1;ttl<=maxHops;ttl++){cancel.ThrowIfCancellationRequested();TraceHop row=await probe(ttl,timeout,cancel);cancel.ThrowIfCancellationRequested();receive(row);if(row.Reached)break;}
        }
    }
}
