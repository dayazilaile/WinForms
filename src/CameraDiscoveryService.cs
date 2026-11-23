using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace WinForms
{
    public sealed class CameraDevice
    {
        public string Endpoint,Types,Scopes,Addresses,Sender;
        public override string ToString(){return "来源 IP："+Sender+"\r\n设备标识："+Endpoint+"\r\n类型："+Types+"\r\n声明的地址："+Addresses+"\r\n设备范围："+Scopes;}
    }
    public sealed class CameraDiscoveryResult
    {
        public int Received,Invalid;public readonly List<CameraDevice> Devices=new List<CameraDevice>();
        public override string ToString(){var b=new StringBuilder("WS-Discovery 发现 "+Devices.Count+" 个设备声明；收到 "+Received+" 个数据报，忽略无效/不匹配回复 "+Invalid+" 个。\r\n回复未经认证，不能仅据此认定为摄像头；不登录设备、不自动打开地址、不播放视频。\r\n");foreach(var d in Devices)b.AppendLine("\r\n"+d);return b.ToString();}
    }
    public static class CameraDiscoveryService
    {
        private const string Discovery="http://schemas.xmlsoap.org/ws/2005/04/discovery";
        public static string Probe(string id)
        {
            Guid guid;if(!Guid.TryParse(id,out guid))throw new ArgumentException("无效的消息 ID。");
            return "<?xml version=\"1.0\" encoding=\"UTF-8\"?><s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\" xmlns:a=\"http://schemas.xmlsoap.org/ws/2004/08/addressing\" xmlns:d=\""+Discovery+"\" xmlns:dn=\"http://www.onvif.org/ver10/network/wsdl\"><s:Header><a:MessageID>uuid:"+guid+"</a:MessageID><a:To>urn:schemas-xmlsoap-org:ws:2005:04:discovery</a:To><a:Action>"+Discovery+"/Probe</a:Action></s:Header><s:Body><d:Probe><d:Types>dn:NetworkVideoTransmitter</d:Types></d:Probe></s:Body></s:Envelope>";
        }
        public static List<CameraDevice> Parse(byte[] bytes,string sender,string expectedId)
        {
            if(bytes==null||bytes.Length>65536)throw new FormatException("发现回复超出大小限制。");
            var settings=new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=65536};var doc=new XmlDocument{XmlResolver=null};
            using(var memory=new MemoryStream(bytes))using(var reader=XmlReader.Create(memory,settings))doc.Load(reader);
            var ns=new XmlNamespaceManager(doc.NameTable);ns.AddNamespace("s","http://www.w3.org/2003/05/soap-envelope");ns.AddNamespace("a","http://schemas.xmlsoap.org/ws/2004/08/addressing");ns.AddNamespace("d",Discovery);
            XmlNode related=doc.SelectSingleNode("/s:Envelope/s:Header/a:RelatesTo",ns);
            if(related==null||!String.Equals(related.InnerText.Trim(),"uuid:"+expectedId,StringComparison.OrdinalIgnoreCase))throw new FormatException("不是当前发现请求的回复。");
            var devices=new List<CameraDevice>();
            foreach(XmlNode match in doc.SelectNodes("/s:Envelope/s:Body/d:ProbeMatches/d:ProbeMatch",ns))
            {
                var device=new CameraDevice{Sender=sender,Endpoint=Value(match,"a:EndpointReference/a:Address",ns),Types=Value(match,"d:Types",ns),Scopes=Value(match,"d:Scopes",ns)};
                var urls=new List<string>();foreach(string item in Value(match,"d:XAddrs",ns).Split((char[])null,StringSplitOptions.RemoveEmptyEntries)){Uri uri;if(Uri.TryCreate(item,UriKind.Absolute,out uri)&&(uri.Scheme=="http"||uri.Scheme=="https")&&uri.UserInfo.Length==0)urls.Add(uri.AbsoluteUri);}
                device.Addresses=String.Join(" ",urls.ToArray());
                if(device.Endpoint.Length==0&&device.Addresses.Length==0)continue;devices.Add(device);
            }
            return devices;
        }
        private static string Value(XmlNode node,string xpath,XmlNamespaceManager ns){XmlNode item=node.SelectSingleNode(xpath,ns);return item==null?"":item.InnerText;}
        private static void Observe(Task task){task.ContinueWith(t=>{var ignored=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);}
        public static async Task<CameraDiscoveryResult> Discover(IPAddress localAddress,CancellationToken cancel)
        {
            if(localAddress.AddressFamily!=AddressFamily.InterNetwork||IPAddress.IsLoopback(localAddress)||localAddress.Equals(IPAddress.Any))throw new ArgumentException("请选择本机已连接局域网的 IPv4 网卡地址。");
            cancel.ThrowIfCancellationRequested();var result=new CameraDiscoveryResult();string id=Guid.NewGuid().ToString();byte[] probe=Encoding.UTF8.GetBytes(Probe(id));
            using(var socket=new UdpClient(new IPEndPoint(localAddress,0)))
            using(cancel.Register(delegate{socket.Close();}))
            {
                var clock=new Stopwatch();var seen=new HashSet<string>(StringComparer.Ordinal);
                try
                {
                    socket.Client.SetSocketOption(SocketOptionLevel.IP,SocketOptionName.MulticastInterface,localAddress.GetAddressBytes());socket.Ttl=1;
                    await socket.SendAsync(probe,probe.Length,new IPEndPoint(IPAddress.Parse("239.255.255.250"),3702));clock.Start();
                    while(clock.ElapsedMilliseconds<5000&&result.Received<200)
                    {
                        cancel.ThrowIfCancellationRequested();Task<UdpReceiveResult> receiving=socket.ReceiveAsync();
                        using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancel))
                        {
                            Task delay=Task.Delay(Math.Max(1,5000-(int)clock.ElapsedMilliseconds),timeout.Token);
                            if(await Task.WhenAny(receiving,delay)!=receiving){Observe(receiving);cancel.ThrowIfCancellationRequested();break;}
                            timeout.Cancel();UdpReceiveResult reply=await receiving;result.Received++;
                            try
                            {
                                foreach(var d in Parse(reply.Buffer,reply.RemoteEndPoint.Address.ToString(),id))if(seen.Add(d.Sender+"|"+d.Endpoint+"|"+d.Addresses))result.Devices.Add(d);
                            }catch(XmlException){result.Invalid++;}catch(FormatException){result.Invalid++;}
                        }
                    }
                }
                catch(SocketException){cancel.ThrowIfCancellationRequested();throw;}
                catch(ObjectDisposedException){cancel.ThrowIfCancellationRequested();throw;}
                catch(Exception){cancel.ThrowIfCancellationRequested();throw;}
            }
            return result;
        }
    }
}
