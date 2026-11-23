using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
namespace ITDesktopToolkit
{
    internal sealed class AuditIdentity
    {
        public string Host="未读取",DeviceId="未读取",IdentitySource="未读取",Uuid="未读取",Serial="未读取",Adapters="未读取",Account="未读取",Permission="未读取",System="未读取";
    }
    internal static class OperationAudit
    {
        internal const string Header="时间\t主机名\t设备标识\t标识依据\t硬件UUID\tBIOS序列号\t网卡_MAC_IP\t运行账户\t权限\t系统与架构\t软件版本\t会话ID\t操作ID\t上级操作ID\t操作\t阶段\t结果\t耗时毫秒\t详情";
        internal static readonly string SessionId=Guid.NewGuid().ToString("N");
        private static readonly object fileLock=new object();
        // Logical call context flows through .NET 4.5 Tasks without the .NET 4.6 AsyncLocal dependency.
        private sealed class AuditContext
        {
            private const string Slot="ITDesktopToolkit.OperationAudit.Current.v1";
            internal string Value
            {
                get{return System.Runtime.Remoting.Messaging.CallContext.LogicalGetData(Slot) as string;}
                set{if(String.IsNullOrEmpty(value))System.Runtime.Remoting.Messaging.CallContext.FreeNamedDataSlot(Slot);else System.Runtime.Remoting.Messaging.CallContext.LogicalSetData(Slot,value);}
            }
        }
        private static readonly AuditContext current=new AuditContext();
        private static readonly Lazy<System.Threading.Tasks.Task<string[]>> hardware=new Lazy<System.Threading.Tasks.Task<string[]>>(delegate{return System.Threading.Tasks.Task.Run(new Func<string[]>(ReadHardware));},true);
        internal static string LogPath {get{return Path.Combine(Paths.BaseDir,"Logs","operations-v1.tsv");}}
        private static string[] ReadHardware()
        {
            var result=new[]{"",""};if(Environment.OSVersion.Platform!=PlatformID.Win32NT)return result;
            string[] classes={"Win32_ComputerSystemProduct","Win32_BIOS"},properties={"UUID","SerialNumber"};
            for(int i=0;i<2;i++)try{using(var search=new ManagementObjectSearcher("SELECT "+properties[i]+" FROM "+classes[i])){search.Options.Timeout=TimeSpan.FromSeconds(3);using(var rows=search.Get())foreach(ManagementObject row in rows)using(row){result[i]=Convert.ToString(row[properties[i]]);break;}}}catch{}
            return result;
        }
        internal static string ValidUuid(string value)
        {Guid id;if(!Guid.TryParse(value,out id)||id==Guid.Empty||id.ToString("N")==new string('f',32))return "";return id.ToString("D").ToUpperInvariant();}
        internal static string ValidSerial(string value)
        {
            string s=(value??"").Trim();string lower=s.ToLowerInvariant();
            if(s.Length==0||new[]{"default string","system serial number","to be filled by o.e.m.","to be filled by oem","unknown","none","not specified","0","o.e.m.","n/a"}.Contains(lower))return "";return s;
        }
        internal static string DeviceKey(string uuid,string serial,IEnumerable<string> macs,out string source)
        {
            string basis;uuid=ValidUuid(uuid);serial=ValidSerial(serial);
            if(uuid.Length>0){source="硬件UUID";basis="uuid:"+uuid;}
            else if(serial.Length>0){source="BIOS序列号（需结合MAC核对）";basis="serial:"+serial;}
            else{var known=macs.Where(m=>!String.IsNullOrWhiteSpace(m)).Distinct().OrderBy(m=>m,StringComparer.Ordinal).ToArray();if(known.Length==0){source="仅本次会话，硬件标识未读取";return "SESSION-"+SessionId;}source="MAC集合（更换网卡后可能变化）";basis="mac:"+String.Join(";",known);}
            using(var sha=SHA256.Create())return "DEV-"+BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(basis))).Replace("-","").Substring(0,24);
        }
        internal static AuditIdentity Capture()
        {
            var identity=new AuditIdentity();var macs=new List<string>();var adapters=new List<string>();
            try{identity.Host=Environment.MachineName;identity.Account=Environment.UserDomainName+"\\"+Environment.UserName;identity.System=Environment.OSVersion.VersionString+" / "+(Environment.Is64BitOperatingSystem?"64位":"32位");identity.Permission=CommandService.IsAdministrator()?"管理员":"普通用户";}catch{}
            try{foreach(var nic in NetworkInterface.GetAllNetworkInterfaces())try{
                if(nic.NetworkInterfaceType==NetworkInterfaceType.Loopback||nic.NetworkInterfaceType==NetworkInterfaceType.Tunnel)continue;
                byte[] bytes=nic.GetPhysicalAddress().GetAddressBytes();if(bytes.Length!=6||bytes.All(b=>b==0)||bytes.All(b=>b==255))continue;
                string mac=BitConverter.ToString(bytes).Replace('-',':');macs.Add(mac);string ips="未读取",dns="未读取",gateway="未读取",dhcp="未读取";
                try{var ip=nic.GetIPProperties();ips=String.Join(",",ip.UnicastAddresses.Select(a=>a.Address.ToString()+(a.Address.AddressFamily==System.Net.Sockets.AddressFamily.InterNetwork&&a.IPv4Mask!=null?"/"+a.IPv4Mask.ToString():"")));dns=String.Join(",",ip.DnsAddresses.Select(a=>a.ToString()));gateway=String.Join(",",ip.GatewayAddresses.Select(a=>a.Address.ToString()));try{var v4=ip.GetIPv4Properties();if(v4!=null)dhcp=v4.IsDhcpEnabled?"自动":"静态";}catch{}}catch{}
                adapters.Add("网卡="+nic.Name+";ID="+nic.Id+";MAC="+mac+";类型="+nic.NetworkInterfaceType+";状态="+nic.OperationalStatus+";IP="+ips+";DHCP="+dhcp+";网关="+gateway+";DNS="+dns);
            }catch{}}catch{}
            identity.Adapters=adapters.Count==0?"未读取到有效网卡MAC/IP":String.Join(" | ",adapters.OrderBy(x=>x,StringComparer.Ordinal));
            var pending=hardware.Value;string[] h=pending.Status==System.Threading.Tasks.TaskStatus.RanToCompletion?pending.Result:new[]{"",""};string uuid=ValidUuid(h[0]),serial=ValidSerial(h[1]);identity.Uuid=uuid.Length==0?"未读取":uuid;identity.Serial=serial.Length==0?"未读取":serial;
            identity.DeviceId=DeviceKey(uuid,serial,macs,out identity.IdentitySource);return identity;
        }
        internal static string Stamp(string operation)
        {
            var who=Capture();return "时间："+DateTimeOffset.Now.ToString("o")+"\r\n主机名："+Cell(who.Host)+"\r\n设备标识："+Cell(who.DeviceId)+"\r\n标识依据："+Cell(who.IdentitySource)+"\r\n硬件UUID："+Cell(who.Uuid)+"\r\nBIOS序列号："+Cell(who.Serial)+"\r\n网卡_MAC_IP："+Cell(who.Adapters)+"\r\n操作："+Cell(operation)+"\r\n";
        }
        internal static string Cell(string value)
        {
            string text=(value??"");text=Regex.Replace(text,@"(?i)(password|passwd|pwd|密码)\s*[:=]\s*[^;\r\n]*","$1=[已省略]");
            if(text.Length>16000)text=text.Substring(0,16000)+"[截断]";
            if(text.Length>0&&"=+-@".IndexOf(text[0])>=0)text="'"+text;
            return text.Replace("\\","\\\\").Replace("\t","\\t").Replace("\r","\\r").Replace("\n","\\n");
        }
        internal static string Format(AuditIdentity identity,DateTimeOffset time,string operationId,string parent,string operation,string phase,string result,long elapsed,string detail)
        {
            return String.Join("\t",new[]{time.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz",CultureInfo.InvariantCulture),identity.Host,identity.DeviceId,identity.IdentitySource,identity.Uuid,identity.Serial,identity.Adapters,identity.Account,identity.Permission,identity.System,Assembly.GetExecutingAssembly().GetName().Version.ToString(),SessionId,operationId,parent,operation,phase,result,elapsed.ToString(CultureInfo.InvariantCulture),detail}.Select(Cell))+Environment.NewLine;
        }
        internal static void Append(string path,string line)
        {
            lock(fileLock){Directory.CreateDirectory(Path.GetDirectoryName(path));bool empty=!File.Exists(path)||new FileInfo(path).Length==0;using(var writer=new StreamWriter(new FileStream(path,FileMode.Append,FileAccess.Write,FileShare.Read),new UTF8Encoding(true))){if(empty)writer.WriteLine(Header);writer.Write(line);}}
        }
        private static bool Write(string id,string parent,string operation,string phase,string result,long elapsed,string detail)
        {
            try{Append(LogPath,Format(Capture(),DateTimeOffset.Now,id,parent,operation,phase,result,elapsed,detail));return true;}
            catch(Exception ex){Paths.LastLogError="软件目录日志不可写："+ex.Message;return false;}
        }
        internal static void Record(string operation,string result,string detail)
        {string token=current.Value??"";string[] parts=token.Split('|');Write(token.Length==0?Guid.NewGuid().ToString("N"):parts[0],parts.Length<2?"":parts[1],operation,"记录",result,0,detail);}
        internal static AuditOperation Begin(string operation,string detail="")
        {var scope=new AuditOperation(operation,current.Value);current.Value=scope.Id+"|"+scope.ParentId;scope.StartWritten=Write(scope.Id,scope.ParentId,operation,"开始","执行中",0,detail);return scope;}
        internal static string Outcome(string output)
        {
            if((output??"").Contains("【配置结果】成功"))return "配置已核验";
            if((output??"").Contains("失败")||(output??"").Contains("命令无法执行")||(output??"").Contains("操作超时")||Regex.IsMatch(output??"",@"返回代码：\s*(?!0(?:\D|$))-?\d+"))return "失败或未完成";
            return "已返回，业务结果需核对";
        }
        internal sealed class AuditOperation:IDisposable
        {
            internal readonly string Id=Guid.NewGuid().ToString("N"),ParentId;internal bool StartWritten;
            private readonly string name;private readonly string parent;private readonly System.Diagnostics.Stopwatch clock=System.Diagnostics.Stopwatch.StartNew();private bool complete,disposed;
            internal AuditOperation(string name,string parent){this.name=name;this.parent=parent;ParentId=String.IsNullOrEmpty(parent)?"":parent.Split('|')[0];}
            internal void End(string result,string detail=""){if(complete)return;complete=true;Write(Id,ParentId,name,"结束",result,clock.ElapsedMilliseconds,detail);}
            public void Dispose(){if(disposed)return;disposed=true;if(!complete)End("异常或未完成","操作没有正常返回；不据此认定成功。");if(current.Value==Id+"|"+ParentId)current.Value=parent;}
        }
    }
}
