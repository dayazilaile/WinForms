using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace WinForms
{
    public sealed class StudentMachine
    {
        public string Room="",Name="",IPv4="",Mac="",Note="";
    }
    public static class StudentRoster
    {
        public const int Maximum=500;
        public const string Header="# WinForms students v1";
        internal const string LegacyHeader="# ITDesktopToolkit students v1";
        public static StudentMachine Validate(StudentMachine item)
        {
            if(item==null)throw new ArgumentException("学生机记录不能为空。");string[] fields={item.Room,item.Name,item.IPv4,item.Mac,item.Note};
            for(int i=0;i<fields.Length;i++){fields[i]=(fields[i]??"").Trim();if(fields[i].Length>(i==4?300:80)||fields[i].IndexOfAny(new[]{'\t','\r','\n','\0'})>=0)throw new ArgumentException("清单字段过长或含换行/Tab。");}
            if(fields[1].Length==0)throw new ArgumentException("学生机名称必填。");
            if(fields[2].Length>0&&!StudentWakeService.IsHostIPv4(fields[2]))throw new ArgumentException("学生机 IP 请填写有效 IPv4 地址，或留空按 MAC 唤醒。");
            return new StudentMachine{Room=fields[0].Length==0?"未分组":fields[0],Name=fields[1],IPv4=fields[2].Length==0?"":StudentWakeService.HostAddress(fields[2]).ToString(),Mac=StudentWakeService.NormalizeMac(fields[3]),Note=fields[4]};
        }
        private static List<StudentMachine> Checked(IEnumerable<StudentMachine> source)
        {
            var result=new List<StudentMachine>();var macs=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var item in source){var value=Validate(item);if(!macs.Add(value.Mac))throw new ArgumentException("清单存在重复 MAC："+value.Mac);result.Add(value);if(result.Count>Maximum)throw new ArgumentException("最多保存 "+Maximum+" 台学生机。");}return result;
        }
        public static string Serialize(IEnumerable<StudentMachine> source)
        {
            var text=new StringBuilder(Header+Environment.NewLine+"# 机房\t名称\tIPv4（可空）\tMAC\t备注（可空）"+Environment.NewLine);
            foreach(var item in Checked(source))text.AppendLine(String.Join("\t",new[]{item.Room,item.Name,item.IPv4,item.Mac,item.Note}));return text.ToString();
        }
        public static List<StudentMachine> Parse(string text)
        {
            if(text==null||text.Length>4*1024*1024)throw new FormatException("学生机清单不能超过 4 MiB。");var items=new List<StudentMachine>();
            foreach(string row in text.TrimStart('\uFEFF').Replace("\r","").Split('\n')){string comment=row.TrimEnd('\t',' ');if(String.IsNullOrWhiteSpace(row)||(comment==Header||comment==LegacyHeader||comment=="# 机房\t名称\tIPv4（可空）\tMAC\t备注（可空）"||(comment.StartsWith("#",StringComparison.Ordinal)&&comment.IndexOf('\t')<0)))continue;string[] fields=row.Split('\t');if(fields.Length<4||fields.Length>5)throw new FormatException("每行应为：机房<Tab>名称<Tab>IPv4<Tab>MAC<Tab>备注（可空）。");items.Add(new StudentMachine{Room=fields[0],Name=fields[1],IPv4=fields[2],Mac=fields[3],Note=fields.Length==5?fields[4]:""});if(items.Count>Maximum)throw new FormatException("清单最多 "+Maximum+" 行。");}
            return Checked(items);
        }
        public static List<StudentMachine> Merge(IEnumerable<StudentMachine> current,IEnumerable<StudentMachine> incoming)
        {
            var result=Checked(current);foreach(var item in Checked(incoming)){int i=result.FindIndex(delegate(StudentMachine x){return x.Mac==item.Mac;});if(i<0)result.Add(item);else result[i]=item;}return Checked(result);
        }
        public static List<StudentMachine> Load(string path)
        {if(!File.Exists(path))return new List<StudentMachine>();if(new FileInfo(path).Length>4*1024*1024)throw new IOException("清单文件超过 4 MiB。");return Parse(File.ReadAllText(path,Encoding.UTF8));}
        public static void Save(string path,IEnumerable<StudentMachine> items)
        {
            string text=Serialize(items);string folder=Path.GetDirectoryName(Path.GetFullPath(path));Directory.CreateDirectory(folder);string temp=Path.Combine(folder,"students-"+Guid.NewGuid().ToString("N")+".tmp");
            try{File.WriteAllText(temp,text,new UTF8Encoding(true));if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}
        }
    }
    public sealed class WakeNetwork
    {
        public readonly string Name;public readonly IPAddress Source,Mask,Broadcast;
        private WakeNetwork(string name,IPAddress source,IPAddress mask,IPAddress broadcast){Name=name;Source=source;Mask=mask;Broadcast=broadcast;}
        public static WakeNetwork Create(string name,string source,string mask)
        {
            int? prefix=RouteInventoryService.MaskPrefix(mask);if(!StudentWakeService.IsHostIPv4(source)||!prefix.HasValue||prefix<8||prefix>30)throw new ArgumentException("请选择掩码为 8–30 位的本机 IPv4 局域网接口。");
            var ip=StudentWakeService.HostAddress(source);var subnet=IPAddress.Parse(mask);byte[] a=ip.GetAddressBytes(),m=subnet.GetAddressBytes(),b=new byte[4];bool network=true,broadcast=true;
            for(int i=0;i<4;i++){b[i]=(byte)(a[i]|(~m[i]&255));if((a[i]&(~m[i]&255))!=0)network=false;if(a[i]!=b[i])broadcast=false;}
            if(network||broadcast)throw new ArgumentException("本机地址不能是子网号或广播地址。");return new WakeNetwork(name,ip,subnet,new IPAddress(b));
        }
        public bool Contains(string address)
        {if(!StudentWakeService.IsHostIPv4(address))return false;byte[] a=Source.GetAddressBytes(),m=Mask.GetAddressBytes(),b=StudentWakeService.HostAddress(address).GetAddressBytes();for(int i=0;i<4;i++)if((a[i]&m[i])!=(b[i]&m[i]))return false;return true;}
        public override string ToString(){return Name+" · "+Source+" → "+Broadcast;}
    }
    public sealed class WakeResult
    {
        public StudentMachine Machine;public int Packets;public string Error="";
        public string Summary{get{return Error.Length==0?"已发送 "+Packets+" 包，未确认开机":"发送失败（已发送 "+Packets+" 包）："+Error;}}
    }
    public static class StudentWakeService
    {
        private static bool TryHostAddress(string value,out IPAddress address)
        {
            address=null;string[] parts=(value??"").Split('.');if(parts.Length!=4)return false;var bytes=new byte[4];for(int i=0;i<4;i++)if(!Byte.TryParse(parts[i],NumberStyles.None,CultureInfo.InvariantCulture,out bytes[i]))return false;
            if(bytes[0]==0||bytes[0]>=224||bytes[0]==127)return false;address=new IPAddress(bytes);return true;
        }
        internal static bool IsHostIPv4(string value){IPAddress address;return TryHostAddress(value,out address);}
        internal static IPAddress HostAddress(string value){IPAddress address;if(!TryHostAddress(value,out address))throw new ArgumentException("请输入四段十进制 IPv4 地址。");return address;}
        public static string NormalizeMac(string value)
        {
            string text=(value??"").Trim().Replace(":","").Replace("-","");if(text.Length!=12)throw new ArgumentException("MAC 应有 6 组十六进制字节。");foreach(char c in text)if(!Uri.IsHexDigit(c))throw new ArgumentException("MAC 格式无效。");
            var bytes=new byte[6];bool zero=true;for(int i=0;i<6;i++){bytes[i]=Byte.Parse(text.Substring(i*2,2),NumberStyles.HexNumber,CultureInfo.InvariantCulture);if(bytes[i]!=0)zero=false;}if(zero||(bytes[0]&1)!=0)throw new ArgumentException("请使用学生机的单播网卡 MAC，不能是全零、广播或组播地址。");return BitConverter.ToString(bytes).Replace('-',':');
        }
        public static byte[] MagicPacket(string mac)
        {
            string text=NormalizeMac(mac).Replace(":","");var bytes=new byte[6];for(int i=0;i<6;i++)bytes[i]=Byte.Parse(text.Substring(i*2,2),NumberStyles.HexNumber,CultureInfo.InvariantCulture);
            var packet=new byte[102];for(int i=0;i<6;i++)packet[i]=255;for(int repeat=0;repeat<16;repeat++)Buffer.BlockCopy(bytes,0,packet,6+repeat*6,6);return packet;
        }
        public static List<WakeNetwork> Networks(CancellationToken token)
        {
            var result=new List<WakeNetwork>();foreach(var nic in NetworkInterface.GetAllNetworkInterfaces()){
                token.ThrowIfCancellationRequested();if(nic.OperationalStatus!=OperationalStatus.Up||nic.NetworkInterfaceType==NetworkInterfaceType.Loopback||nic.NetworkInterfaceType==NetworkInterfaceType.Tunnel)continue;
                try{foreach(var address in nic.GetIPProperties().UnicastAddresses){token.ThrowIfCancellationRequested();if(address.Address.AddressFamily!=AddressFamily.InterNetwork)continue;try{result.Add(WakeNetwork.Create(nic.Name,address.Address.ToString(),address.IPv4Mask==null?"":address.IPv4Mask.ToString()));}catch(ArgumentException){}}}catch(NetworkInformationException ex){Paths.Log("Wake NIC: "+ex.Message);}
            }return result;
        }
        internal static List<StudentMachine> ValidateTargets(IEnumerable<StudentMachine> machines,WakeNetwork network,int port)
        {
            if(network==null||port<1||port>65535)throw new ArgumentException("请选择本机网卡和有效 UDP 端口。");var result=new List<StudentMachine>();var macs=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var machine in machines){var item=StudentRoster.Validate(machine);if(item.IPv4.Length>0&&!network.Contains(item.IPv4))throw new ArgumentException(item.Name+" 的 IP 不在所选网段，请更换网卡或修正清单。");if(macs.Add(item.Mac))result.Add(item);if(result.Count>StudentRoster.Maximum)throw new ArgumentException("一次最多唤醒 "+StudentRoster.Maximum+" 台。");}
            if(result.Count==0)throw new ArgumentException("请先选中学生机，或选择一个机房。");return result;
        }
        public static async Task Wake(IEnumerable<StudentMachine> machines,WakeNetwork network,int port,CancellationToken token,Action<WakeResult> receive)
        {
            var targets=ValidateTargets(machines,network,port);token.ThrowIfCancellationRequested();
            using(var udp=new UdpClient(new IPEndPoint(network.Source,0)))
            {
                udp.EnableBroadcast=true;udp.Ttl=1;var endpoint=new IPEndPoint(network.Broadcast,port);
                using(token.Register(delegate{udp.Close();}))await WakeCore(targets,network,port,token,async delegate(byte[] packet,CancellationToken cancel){cancel.ThrowIfCancellationRequested();try{int sent=await udp.SendAsync(packet,packet.Length,endpoint);if(sent!=packet.Length)throw new IOException("UDP 包未完整交给系统。");}catch{cancel.ThrowIfCancellationRequested();throw;}},receive);
            }
        }
        internal static async Task WakeCore(IEnumerable<StudentMachine> machines,WakeNetwork network,int port,CancellationToken token,Func<byte[],CancellationToken,Task> send,Action<WakeResult> receive)
        {
            var targets=ValidateTargets(machines,network,port);foreach(var machine in targets){token.ThrowIfCancellationRequested();var result=new WakeResult{Machine=machine};try{byte[] packet=MagicPacket(machine.Mac);for(int i=0;i<3;i++){token.ThrowIfCancellationRequested();await send(packet,token);result.Packets++;if(i<2)await Task.Delay(80,token);}}catch(OperationCanceledException){result.Error="已停止，已发出的包无法撤回";receive(result);throw;}catch(Exception ex){token.ThrowIfCancellationRequested();result.Error=ex.Message;}receive(result);}
        }
    }
}
