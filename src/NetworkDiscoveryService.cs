using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ITDesktopToolkit
{
    public sealed class PortProbe
    {
        public string Host,State;public int Port;public DateTimeOffset Time;
        public override string ToString(){return Time.ToString("HH:mm:ss")+" "+Host+":"+Port+" "+State;}
    }
    public sealed class HostProbe
    {
        public string Address,Icmp;public int[] OpenPorts;
        public bool Responded{get{return Icmp=="Success"||OpenPorts.Length>0;}}
        public override string ToString(){return Address+"  ICMP="+Icmp+"  TCP开放="+(OpenPorts.Length==0?"无":String.Join(",",Array.ConvertAll(OpenPorts,p=>p.ToString(CultureInfo.InvariantCulture))))+(!Responded?"（未获响应，不能据此判定离线）":"");}
    }
    public static class NetworkDiscoveryService
    {
        public static int[] ParsePorts(string text)
        {
            var ports=new SortedSet<int>();
            foreach(string token in (text??"").Split(new char[]{',','，',';',' ','\r','\n'},StringSplitOptions.RemoveEmptyEntries))
            {
                string[] range=token.Split('-');int first,last;
                if(range.Length>2||!Int32.TryParse(range[0],NumberStyles.None,CultureInfo.InvariantCulture,out first)||first<1||first>65535)throw new ArgumentException("端口应为 1–65535 的整数或范围，例如 80,443,8000-8010。");
                last=first;if(range.Length==2&&(!Int32.TryParse(range[1],NumberStyles.None,CultureInfo.InvariantCulture,out last)||last<first||last>65535))throw new ArgumentException("端口范围无效。");
                if(last-first+1>1024)throw new ArgumentException("每次最多检查 1024 个端口。");
                for(int port=first;port<=last;port++){ports.Add(port);if(ports.Count>1024)throw new ArgumentException("每次最多检查 1024 个端口。");}
            }
            if(ports.Count==0)throw new ArgumentException("请输入端口。");int[] result=new int[ports.Count];ports.CopyTo(result);return result;
        }
        private static uint IPv4(string text)
        {
            string[] parts=text.Split('.');if(parts.Length!=4)throw new ArgumentException("请输入完整的 IPv4 地址。");uint value=0;
            foreach(string part in parts){byte b;if(!Byte.TryParse(part,NumberStyles.None,CultureInfo.InvariantCulture,out b))throw new ArgumentException("IPv4 地址无效。");value=(value<<8)|b;}
            return value;
        }
        private static string Address(uint n){return ((n>>24)&255)+"."+((n>>16)&255)+"."+((n>>8)&255)+"."+(n&255);}
        public static string[] ParseSubnet(string text)
        {
            string[] p=(text??"").Trim().Split('/');if(p.Length>2)throw new ArgumentException("请输入 IPv4 或 /24 至 /32 的 CIDR 网段，例如 192.168.1.0/24。");
            uint address=IPv4(p[0]);int prefix=32;
            if(p.Length==2&&(!Int32.TryParse(p[1],NumberStyles.None,CultureInfo.InvariantCulture,out prefix)||prefix<24||prefix>32))throw new ArgumentException("每次支持 /24 至 /32 网段，最多 256 个地址。");
            // Refuse multicast, unspecified and loopback ranges for discovery; TCP probing accepts loopback separately.
            if((address>>24)==0||(address>>24)==127||(address>>24)>=224)throw new ArgumentException("请选择有效的单播局域网 IPv4 地址。");
            uint mask=UInt32.MaxValue<<(32-prefix);uint first=address&mask;uint count=1u<<(32-prefix);
            if(prefix<=30){first++;count-=2;}
            var hosts=new List<string>();for(uint i=0;i<count;i++)hosts.Add(Address(first+i));return hosts.ToArray();
        }
        public static string SingleHost(string host)
        {
            host=(host??"").Trim();string[] parsed=NetworkMonitorService.Targets(host);
            if(parsed.Length!=1||host.IndexOfAny(new char[]{',',';',' ','\r','\n'})>=0)throw new ArgumentException("请输入单个 IP 或主机名，不含协议/端口。");return parsed[0];
        }
        private static void Observe(Task task){task.ContinueWith(t=>{var ignored=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);}
        public static async Task<PortProbe> Probe(IPAddress address,int port,int timeout,CancellationToken cancel)
        {
            if(address==null)throw new ArgumentNullException("address");
            if(port<1||port>65535||timeout<100||timeout>10000)throw new ArgumentException("端口/超时范围无效。");
            cancel.ThrowIfCancellationRequested();var result=new PortProbe{Host=address.ToString(),Port=port,Time=DateTimeOffset.Now};
            using(var client=new TcpClient(address.AddressFamily))using(var timer=CancellationTokenSource.CreateLinkedTokenSource(cancel))
            using(cancel.Register(delegate{client.Close();}))
            {
                try
                {
                    Task connecting=client.ConnectAsync(address,port);Task delay=Task.Delay(timeout,timer.Token);
                    if(await Task.WhenAny(connecting,delay)!=connecting){Observe(connecting);cancel.ThrowIfCancellationRequested();result.State="超时/未获响应";return result;}
                    timer.Cancel();await connecting;cancel.ThrowIfCancellationRequested();result.State="开放";
                }
                catch(SocketException ex){cancel.ThrowIfCancellationRequested();result.State=ex.SocketErrorCode==SocketError.ConnectionRefused?"拒绝连接":"连接错误："+ex.SocketErrorCode;}
                catch(ObjectDisposedException){cancel.ThrowIfCancellationRequested();throw;}
                catch(Exception){cancel.ThrowIfCancellationRequested();throw;}
                return result;
            }
        }
        internal static async Task<IPAddress> Resolve(string host,CancellationToken cancel)
        {
            host=SingleHost(host);IPAddress address;if(IPAddress.TryParse(host,out address))return address;
            Task<IPAddress[]> resolving=Dns.GetHostAddressesAsync(host);
            using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancel))
            {
                Task delay=Task.Delay(5000,timeout.Token);
                if(await Task.WhenAny(resolving,delay)!=resolving){Observe(resolving);cancel.ThrowIfCancellationRequested();throw new TimeoutException("DNS 解析超过 5 秒。");}
                timeout.Cancel();var addresses=await resolving;cancel.ThrowIfCancellationRequested();
                foreach(var a in addresses)if(a.AddressFamily==AddressFamily.InterNetwork)return a;
                if(addresses.Length>0)return addresses[0];throw new ArgumentException("目标没有可用 IP 地址。");
            }
        }
        private static async Task Bounded(int count,int concurrency,CancellationToken cancel,Func<int,Task> work)
        {
            if(concurrency<1||concurrency>32)throw new ArgumentException("并发数仅支持 1–32。");
            int cursor=-1;var workers=new List<Task>();
            for(int worker=0;worker<Math.Min(count,concurrency);worker++)
                workers.Add(Task.Run(new Func<Task>(async delegate{while(!cancel.IsCancellationRequested){int index=Interlocked.Increment(ref cursor);if(index>=count)break;await work(index);}})));
            await Task.WhenAll(workers);cancel.ThrowIfCancellationRequested();
        }
        private static void ValidateSettings(int concurrency,int timeout)
        {if(concurrency<1||concurrency>32||timeout<100||timeout>10000)throw new ArgumentException("并发 1–32，超时 100–10000 毫秒。");}
        public static async Task ScanPorts(string host,int[] ports,int concurrency,int timeout,CancellationToken cancel,Action<PortProbe> receive)
        {
            ValidateSettings(concurrency,timeout);
            if(ports==null||ports.Length==0||ports.Length>1024)throw new ArgumentException("端口数量须为 1–1024。");
            foreach(int p in ports)if(p<1||p>65535)throw new ArgumentException("端口无效。");
            IPAddress address=await Resolve(host,cancel);
            await Bounded(ports.Length,concurrency,cancel,async delegate(int index){PortProbe result=await Probe(address,ports[index],timeout,cancel);receive(result);});
        }
        public static async Task Discover(string subnet,int concurrency,int timeout,CancellationToken cancel,Action<HostProbe> receive)
        {
            ValidateSettings(concurrency,timeout);
            string[] hosts=ParseSubnet(subnet);
            await Bounded(hosts.Length,concurrency,cancel,async delegate(int index){
                var result=new HostProbe{Address=hosts[index],Icmp="未响应"};
                using(var ping=new Ping())try{var reply=await ping.SendPingAsync(hosts[index],timeout);result.Icmp=reply.Status.ToString();}catch(Exception ex){result.Icmp="探测错误："+ex.GetType().Name;}
                var open=new List<int>();
                foreach(int p in new int[]{80,445,3389}){cancel.ThrowIfCancellationRequested();PortProbe r=await Probe(IPAddress.Parse(hosts[index]),p,timeout,cancel);if(r.State=="开放")open.Add(p);}
                result.OpenPorts=open.ToArray();cancel.ThrowIfCancellationRequested();receive(result);
            });
        }
    }
}
