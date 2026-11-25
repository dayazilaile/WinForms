using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WinForms
{
    public enum CrossRoomWakeMode { Unicast, DirectedBroadcast, LocalBroadcast, Combined }
    public sealed class CrossRoomWakePlan
    {
        public readonly WakeNetwork Source;
        public readonly int Prefix;
        public readonly CrossRoomWakeMode Mode;
        public readonly IList<StudentMachine> Targets;
        internal CrossRoomWakePlan(WakeNetwork source,int prefix,CrossRoomWakeMode mode,List<StudentMachine> targets)
        {Source=source;Prefix=prefix;Mode=mode;Targets=targets.AsReadOnly();}
    }
    public sealed class CrossRoomWakeResult
    {
        public StudentMachine Machine;
        public int Packets,Failures;
        public bool Stopped;
        public string Details="";
        public string Summary {get{return (Stopped?"已停止；":"")+"已交给系统 "+Packets+" 包；"+(Failures>0?Failures+" 条路径发送失败；":"")+"未确认送达/开机";}}
    }
    public static class CrossRoomWakeService
    {
        public static string ModeTitle(CrossRoomWakeMode mode)
        {return mode==CrossRoomWakeMode.Unicast?"单播":mode==CrossRoomWakeMode.DirectedBroadcast?"目标网段广播":mode==CrossRoomWakeMode.LocalBroadcast?"本地广播":"组合尝试";}
        internal static bool PrivateIPv4(string value)
        {
            if(!StudentWakeService.IsHostIPv4(value))return false;
            byte[] b=StudentWakeService.HostAddress(value).GetAddressBytes();
            return b[0]==10||(b[0]==172&&b[1]>=16&&b[1]<=31)||(b[0]==192&&b[1]==168);
        }
        internal static IPAddress Broadcast(string ip,int prefix)
        {
            if(prefix<16||prefix>30)throw new ArgumentException("目标机房掩码位数请填写 16–30，常见为 24；请按实际配置填写。");
            byte[] bytes=StudentWakeService.HostAddress(ip).GetAddressBytes();
            for(int i=0;i<4;i++){int bits=Math.Max(0,Math.Min(8,prefix-i*8));int mask=bits==0?0:255<<(8-bits)&255;bytes[i]=(byte)(bytes[i]|(~mask&255));}
            return new IPAddress(bytes);
        }
        public static CrossRoomWakePlan Prepare(IEnumerable<StudentMachine> machines,WakeNetwork source,int prefix,CrossRoomWakeMode mode,IEnumerable<string> localAddresses)
        {
            if(machines==null||source==null||localAddresses==null||!Enum.IsDefined(typeof(CrossRoomWakeMode),mode))throw new ArgumentException("请选择机房、本机网卡和有效的发送方式。");
            Broadcast(source.Source.ToString(),prefix);
            if(!PrivateIPv4(source.Source.ToString()))throw new ArgumentException("跨机房试用仅支持 10/8、172.16/12、192.168/16 私有 IPv4 网络。");
            var local=new HashSet<string>(localAddresses,StringComparer.Ordinal);local.Add(source.Source.ToString());
            var ips=new HashSet<string>(StringComparer.Ordinal);var macs=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var targets=new List<StudentMachine>();string room=null;
            foreach(var raw in machines){var item=StudentRoster.Validate(raw);
                if(!PrivateIPv4(item.IPv4)||local.Contains(item.IPv4))throw new ArgumentException(item.Name+" 需要非本机的私有 IPv4；仅有 MAC 不足以跨机房尝试。");
                byte[] ip=StudentWakeService.HostAddress(item.IPv4).GetAddressBytes(),b=Broadcast(item.IPv4,prefix).GetAddressBytes();
                bool subnet=true;for(int i=0;i<4;i++){int bits=Math.Max(0,Math.Min(8,prefix-i*8));int mask=bits==0?0:255<<(8-bits)&255;if((ip[i]&~mask)!=0)subnet=false;}
                if(subnet||item.IPv4==new IPAddress(b).ToString()||item.IPv4==source.Broadcast.ToString())throw new ArgumentException(item.Name+" 的地址是子网号或广播地址，请核对目标掩码和清单。");
                if(room==null)room=item.Room;else if(!String.Equals(room,item.Room,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("一次只尝试一个具体机房，不跨多个机房批量发送。");
                if(!ips.Add(item.IPv4)||!macs.Add(item.Mac))throw new ArgumentException("所选记录有重复 IP/MAC，请先核对清单。");
                targets.Add(item);if(targets.Count>StudentRoster.Maximum)throw new ArgumentException("一次最多尝试 "+StudentRoster.Maximum+" 台。");
            }
            if(targets.Count==0)throw new ArgumentException("请先选择具体机房及学生机。");
            return new CrossRoomWakePlan(source,prefix,mode,targets);
        }
        internal static List<IPEndPoint> Destinations(CrossRoomWakePlan plan,StudentMachine machine)
        {
            var result=new List<IPEndPoint>();
            Action<IPAddress> add=delegate(IPAddress ip){foreach(var e in result)if(e.Address.Equals(ip))return;result.Add(new IPEndPoint(ip,9));};
            if(plan.Mode==CrossRoomWakeMode.Unicast||plan.Mode==CrossRoomWakeMode.Combined)add(StudentWakeService.HostAddress(machine.IPv4));
            if(plan.Mode==CrossRoomWakeMode.DirectedBroadcast||plan.Mode==CrossRoomWakeMode.Combined)add(Broadcast(machine.IPv4,plan.Prefix));
            if(plan.Mode==CrossRoomWakeMode.LocalBroadcast||plan.Mode==CrossRoomWakeMode.Combined)add(plan.Source.Broadcast);
            return result;
        }
        public static async Task Run(CrossRoomWakePlan plan,CancellationToken token,Action<CrossRoomWakeResult> receive)
        {
            // Revalidate a fresh snapshot against current local addresses before opening the socket.
            var reviewed=Prepare(plan.Targets,plan.Source,plan.Prefix,plan.Mode,StudentPowerService.LocalAddresses());
            token.ThrowIfCancellationRequested();
            using(var udp=new UdpClient(new IPEndPoint(reviewed.Source.Source,0))){
                udp.EnableBroadcast=true;
                using(token.Register(delegate{udp.Close();}))await RunCore(reviewed,token,delegate(byte[] packet,IPEndPoint endpoint,short ttl,CancellationToken cancel){return Send(udp,packet,endpoint,ttl,cancel);},receive);
            }
        }
        internal static async Task Send(UdpClient udp,byte[] packet,IPEndPoint endpoint,short ttl,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();try{udp.Ttl=ttl;int sent=await udp.SendAsync(packet,packet.Length,endpoint);if(sent!=packet.Length)throw new IOException("UDP 包未完整交给系统。");}catch{token.ThrowIfCancellationRequested();throw;}
        }
        internal static async Task RunCore(CrossRoomWakePlan plan,CancellationToken token,Func<byte[],IPEndPoint,short,CancellationToken,Task> send,Action<CrossRoomWakeResult> receive)
        {
            token.ThrowIfCancellationRequested();foreach(var machine in plan.Targets){token.ThrowIfCancellationRequested();
                var result=new CrossRoomWakeResult{Machine=machine};var detail=new StringBuilder();
                detail.AppendLine("时间："+DateTimeOffset.Now.ToString("o"));detail.AppendLine("机房："+machine.Room+"；名称："+machine.Name+"；IPv4："+machine.IPv4+"；MAC："+machine.Mac);
                detail.AppendLine("本机接口："+plan.Source.Source+"；目标掩码：/"+plan.Prefix+"；发送方式："+ModeTitle(plan.Mode));
                detail.AppendLine("单播依赖目标网关的邻居缓存/网卡支持；目标广播依赖现有转发；本地广播仅同一广播域可能有效。跨网段没有直接核对 IP/MAC 对应关系，请确认记录仍有效。");
                using(var audit=OperationAudit.Begin("跨机房唤醒试用",detail.ToString())){
                    try{foreach(var destination in Destinations(plan,machine)){
                        short ttl=destination.Address.Equals(plan.Source.Broadcast)?(short)1:(short)16;int sent=0;
                        try{byte[] packet=StudentWakeService.MagicPacket(machine.Mac);
                            for(int i=0;i<3;i++){token.ThrowIfCancellationRequested();await send(packet,destination,ttl,token);sent++;result.Packets++;if(i<2)await Task.Delay(80,token);}
                            detail.AppendLine(destination+"，TTL="+ttl+"：交给系统 "+sent+" 包。");
                        }catch(OperationCanceledException){detail.AppendLine(destination+"：停止前已交给系统 "+sent+" 包，不能撤回。");throw;}
                        catch(Exception ex){token.ThrowIfCancellationRequested();result.Failures++;detail.AppendLine(destination+"：交给系统 "+sent+" 包后失败："+ex.Message);}
                    }}catch(OperationCanceledException){result.Stopped=true;}
                    detail.AppendLine(result.Summary);detail.AppendLine("UDP 没有送达回执；没有扫描/修改地址、交换机、防火墙或发送关机请求。");
                    result.Details=detail.ToString();audit.End(result.Summary,result.Details);receive(result);
                    if(result.Stopped)throw new OperationCanceledException(token);
                }
            }
        }
    }
}
