using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace WinForms
{
    public sealed class StudentScanRow
    {
        public string IPv4="",Mac="",Name="",Status="";public bool Conflict;
        public bool CanSave{get{return Mac.Length>0;}}
    }
    public sealed class StudentScanResult
    {
        public readonly List<StudentScanRow> Rows=new List<StudentScanRow>();public string Warning="";
    }
    public static class StudentDiscoveryService
    {
        [DllImport("iphlpapi.dll",ExactSpelling=true)]private static extern uint SendARP(uint destination,uint source,[Out]byte[] mac,ref uint length);
        private static uint Number(IPAddress address){byte[] b=address.GetAddressBytes();return ((uint)b[0]<<24)|((uint)b[1]<<16)|((uint)b[2]<<8)|b[3];}
        private static string Address(uint value){return new IPAddress(new[]{(byte)(value>>24),(byte)(value>>16),(byte)(value>>8),(byte)value}).ToString();}
        public static string DefaultSubnet(WakeNetwork network)
        {int prefix=RouteInventoryService.MaskPrefix(network.Mask.ToString()).Value;return network.Source+"/"+Math.Max(22,prefix).ToString(CultureInfo.InvariantCulture);}
        public static string[] Targets(string text,WakeNetwork network)
        {
            if(network==null)throw new ArgumentException("请先选择本机网卡。");string[] parts=(text??"").Trim().Split('/');int prefix=32;
            if(parts.Length>2||!StudentWakeService.IsHostIPv4(parts[0])||(parts.Length==2&&(!Int32.TryParse(parts[1],NumberStyles.None,CultureInfo.InvariantCulture,out prefix)||prefix<22||prefix>32)))throw new ArgumentException("请输入本机网段内的 IPv4 或 /22 至 /32 网段，每次最多 1024 个地址。");
            uint address=Number(StudentWakeService.HostAddress(parts[0])),mask=UInt32.MaxValue<<(32-prefix),first=address&mask,count=1u<<(32-prefix);if(prefix<=30){first++;count-=2;}
            uint source=Number(network.Source),localNetwork=source&Number(network.Mask),broadcast=Number(network.Broadcast);var hosts=new List<string>();
            for(uint i=0;i<count;i++){string ip=Address(first+i);if(!network.Contains(ip))throw new ArgumentException("扫描范围必须完全位于所选本机网卡的局域网内。");uint host=first+i;if(host!=source&&host!=localNetwork&&host!=broadcast)hosts.Add(ip);}return hosts.ToArray();
        }
        private static Dictionary<string,string> ReadCache(WakeNetwork network,CancellationToken token)
        {
            string script=@"$ErrorActionPreference='Stop';$ips=@(Get-NetIPAddress -AddressFamily IPv4 -IPAddress "+PowerShellService.Quote(network.Source.ToString())+@" -ErrorAction Stop | Where-Object {$_.InterfaceAlias -eq "+PowerShellService.Quote(network.Name)+@"});if($ips.Count -ne 1){throw '无法确定所选网卡接口'};foreach($n in Get-NetNeighbor -AddressFamily IPv4 -InterfaceIndex $ips[0].InterfaceIndex -ErrorAction Stop){if($n.State -notin @('Incomplete','Unreachable')){'ITSTUDENT|'+[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes([string]$n.IPAddress))+'|'+[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes([string]$n.LinkLayerAddress))}}";
            string raw=PowerShellService.RunScript(script,1,token);return ParseCache(raw,network);
        }
        internal static Dictionary<string,string> ParseCache(string raw,WakeNetwork network)
        {
            if(!raw.Replace("\r","").Contains("\n返回代码：0\n"))throw new InvalidOperationException("本机邻居缓存读取未完成，将仅使用 ARP 查询。");var result=new Dictionary<string,string>(StringComparer.Ordinal);
            foreach(string row in raw.Replace("\r","").Split('\n'))if(row.StartsWith("ITSTUDENT|",StringComparison.Ordinal)){string[] fields=row.Split('|');if(fields.Length!=3)throw new FormatException("邻居缓存记录不完整。");string ip=Encoding.UTF8.GetString(Convert.FromBase64String(fields[1])),mac=Encoding.UTF8.GetString(Convert.FromBase64String(fields[2]));if(!network.Contains(ip))continue;try{result[ip]=StudentWakeService.NormalizeMac(mac);}catch(ArgumentException){}}return result;
        }
        private static Task<StudentScanRow> Resolve(string ip,WakeNetwork network,CancellationToken token)
        {
            return Task.Run(new Func<StudentScanRow>(delegate{token.ThrowIfCancellationRequested();byte[] bytes=new byte[6];uint size=6;uint code=SendARP(BitConverter.ToUInt32(StudentWakeService.HostAddress(ip).GetAddressBytes(),0),BitConverter.ToUInt32(network.Source.GetAddressBytes(),0),bytes,ref size);token.ThrowIfCancellationRequested();
                var result=new StudentScanRow{IPv4=ip,Name="设备-"+ip,Status=code==0?"未取得有效 MAC":"ARP 未解析（错误 "+code+"）"};if(code==0&&size==6)try{result.Mac=StudentWakeService.NormalizeMac(BitConverter.ToString(bytes));result.Status="ARP 解析所得，可能使用系统缓存；未确认设备类型";}catch(ArgumentException){}return result;}));
        }
        public static async Task<StudentScanResult> Scan(string subnet,WakeNetwork network,CancellationToken token,Action<StudentScanRow> receive)
        {
            string[] hosts=Targets(subnet,network);token.ThrowIfCancellationRequested();var cache=new Dictionary<string,string>();string warning="";
            try{cache=await Task.Run(new Func<Dictionary<string,string>>(delegate{return ReadCache(network,token);}));}catch(OperationCanceledException){throw;}catch(Exception ex){warning=ex.Message;}
            var result=await ScanCore(hosts,network,cache,token,delegate(string ip,CancellationToken cancel){return Resolve(ip,network,cancel);},receive);result.Warning=warning;return result;
        }
        internal static async Task<StudentScanResult> ScanCore(string[] hosts,WakeNetwork network,IDictionary<string,string> cache,CancellationToken token,Func<string,CancellationToken,Task<StudentScanRow>> probe,Action<StudentScanRow> receive)
        {
            if(hosts==null||hosts.Length>1024||network==null)throw new ArgumentException("扫描范围无效。");foreach(string ip in hosts)if(!network.Contains(ip))throw new ArgumentException("不能扫描所选局域网之外的地址。");token.ThrowIfCancellationRequested();
            var result=new StudentScanResult();int cursor=-1;object gate=new object();var workers=new List<Task>();
            for(int i=0;i<Math.Min(12,hosts.Length);i++)workers.Add(Task.Run(new Func<Task>(async delegate{while(!token.IsCancellationRequested){int index=Interlocked.Increment(ref cursor);if(index>=hosts.Length)break;string ip=hosts[index];StudentScanRow row;
                try{row=await probe(ip,token);}catch(OperationCanceledException){throw;}catch(Exception ex){row=new StudentScanRow{Status="ARP 查询未完成："+ex.GetType().Name};}token.ThrowIfCancellationRequested();row.IPv4=ip;if(String.IsNullOrWhiteSpace(row.Name))row.Name="设备-"+ip;
                try{row.Mac=StudentWakeService.NormalizeMac(row.Mac);}catch(ArgumentException){row.Mac="";}string cached;if(row.Mac.Length==0&&cache.TryGetValue(ip,out cached))try{row.Mac=StudentWakeService.NormalizeMac(cached);row.Status="来自系统缓存，ARP 未确认，需核对设备";}catch(ArgumentException){}
                lock(gate)result.Rows.Add(row);receive(row);
            }})));
            await Task.WhenAll(workers);token.ThrowIfCancellationRequested();MarkConflicts(result.Rows);return result;
        }
        public static void MarkConflicts(IList<StudentScanRow> rows)
        {
            var groups=new Dictionary<string,List<StudentScanRow>>(StringComparer.OrdinalIgnoreCase);foreach(var row in rows)if(row.CanSave){List<StudentScanRow> group;if(!groups.TryGetValue(row.Mac,out group)){group=new List<StudentScanRow>();groups.Add(row.Mac,group);}group.Add(row);}
            foreach(var group in groups.Values)if(group.Count>1)foreach(var row in group){row.Conflict=true;row.Status="同 MAC 对应多个 IP，可能为多地址设备或 ARP 代理；请核对后仅选一条";}
        }
        public static List<StudentMachine> MergeDiscovered(IEnumerable<StudentMachine> current,IEnumerable<StudentScanRow> selected,string room)
        {
            room=(room??"").Trim();if(room.Length==0)throw new ArgumentException("请填写保存到的机房名称。");var old=StudentRoster.Merge(current,new StudentMachine[0]);var existing=new Dictionary<string,StudentMachine>(StringComparer.OrdinalIgnoreCase);foreach(var item in old)existing.Add(item.Mac,item);var incoming=new List<StudentMachine>();var unique=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var row in selected){if(!row.CanSave)throw new ArgumentException("未取得 MAC 的结果不能保存。");string mac=StudentWakeService.NormalizeMac(row.Mac);if(!unique.Add(mac))throw new ArgumentException("选择了同一 MAC 的多条结果，请核对后仅保留一条。");StudentMachine known;if(existing.TryGetValue(mac,out known))incoming.Add(new StudentMachine{Room=known.Room,Name=known.Name,Mac=mac,IPv4=row.IPv4,Note=known.Note});else incoming.Add(new StudentMachine{Room=room,Name=row.Name,IPv4=row.IPv4,Mac=mac,Note="网段扫描记录；设备类型由使用者确认"});}
            if(incoming.Count==0)throw new ArgumentException("请勾选要保存的学生机。");return StudentRoster.Merge(old,incoming);
        }
    }
}
