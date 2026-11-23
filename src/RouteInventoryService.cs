using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
namespace WinForms
{
    public sealed class RouteAdapterRow
    {
        public string Name="",State="",Mac="",IPv4="无地址",Prefix="—",IPv6="无地址",Gateway4="",Gateway6="",Dns4="",Dns6="",Speed="未知";
        public object[] Cells(string family){return new object[]{Name,State,Mac,IPv4,Prefix,IPv6,family=="IPv6"?Gateway6:Gateway4,family=="IPv6"?Dns6:Dns4,Speed};}
    }
    public sealed class RouteInventory
    {
        public readonly List<RouteAdapterRow> Adapters=new List<RouteAdapterRow>();
        public readonly List<object[]> Routes=new List<object[]>(),Neighbors=new List<object[]>();public string Warning="";
    }
    public static class RouteInventoryService
    {
        public static RouteInventory Read(CancellationToken cancel)
        {
            var result=new RouteInventory();
            foreach(var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                cancel.ThrowIfCancellationRequested();var ip=nic.GetIPProperties();byte[] mac=nic.GetPhysicalAddress().GetAddressBytes();string hardware=mac.Length==0?"未知":BitConverter.ToString(mac).Replace('-',':');
                string speed="未知";try{if(nic.Speed>0)speed=(nic.Speed/1000000.0).ToString("0.###",CultureInfo.InvariantCulture);}catch(NetworkInformationException){}
                var v4=new SortedDictionary<string,string>(StringComparer.Ordinal);var v6=new SortedSet<string>(StringComparer.Ordinal);
                foreach(var a in ip.UnicastAddresses){if(a.Address.AddressFamily==AddressFamily.InterNetwork)v4[a.Address.ToString()]=PrefixText(a.IPv4Mask==null?null:a.IPv4Mask.ToString());else if(a.Address.AddressFamily==AddressFamily.InterNetworkV6)v6.Add(a.Address.ToString());}
                result.Adapters.Add(new RouteAdapterRow{Name=nic.Name,State=AdapterState(nic.OperationalStatus),Mac=hardware,IPv4=v4.Count==0?"无地址":String.Join("; ",v4.Keys),Prefix=v4.Count==0?"—":String.Join("; ",v4.Values),IPv6=v6.Count==0?"无地址":String.Join("; ",v6),Gateway4=GatewayText(ip,AddressFamily.InterNetwork),Gateway6=GatewayText(ip,AddressFamily.InterNetworkV6),Dns4=DnsText(ip,AddressFamily.InterNetwork),Dns6=DnsText(ip,AddressFamily.InterNetworkV6),Speed=speed});
            }
            if(WindowsCompatibility.Legacy){LegacyNetworkReader.Read(result,cancel);return result;}
            string raw=PowerShellService.RunScript(@"$ErrorActionPreference='Stop';function Emit([string]$kind,[object[]]$cells){$values=@($cells|ForEach-Object{[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes([string]$_))});'ITNET|'+$kind+'|'+($values -join '|')};try{
foreach($r in Get-NetRoute){Emit 'route' @($r.InterfaceAlias,$r.AddressFamily,$r.DestinationPrefix,$r.NextHop,$r.RouteMetric,$r.Protocol)}
foreach($n in Get-NetNeighbor){Emit 'neighbor' @($n.InterfaceAlias,$n.IPAddress,$n.LinkLayerAddress,$n.State,$n.AddressFamily)}
}catch{Write-Output $_.Exception.Message;exit 1}",2,cancel);
            if(!raw.Replace("\r","").Contains("\n返回代码：0\n")){result.Warning="路由/邻居读取未完成。"+raw;return result;}
            ParseRows(raw,result);cancel.ThrowIfCancellationRequested();return result;
        }
        private static string GatewayText(IPInterfaceProperties properties,AddressFamily family)
        {var values=new List<string>();foreach(var g in properties.GatewayAddresses)if(g.Address.AddressFamily==family)values.Add(g.Address.ToString());return String.Join(", ",values.ToArray());}
        private static string DnsText(IPInterfaceProperties properties,AddressFamily family)
        {var values=new List<string>();foreach(var d in properties.DnsAddresses)if(d.AddressFamily==family)values.Add(d.ToString());return String.Join(", ",values.ToArray());}
        internal static int? MaskPrefix(string mask)
        {
            IPAddress address;if(!IPAddress.TryParse(mask,out address)||address.AddressFamily!=AddressFamily.InterNetwork)return null;
            int bits=0;bool zero=false;foreach(byte value in address.GetAddressBytes())for(int bit=7;bit>=0;bit--){bool one=(value&(1<<bit))!=0;if(one&&zero)return null;if(one)bits++;else zero=true;}return bits;
        }
        internal static string PrefixText(string mask)
        {int? prefix=MaskPrefix(mask);return prefix.HasValue?prefix.Value.ToString(CultureInfo.InvariantCulture):"未知";}
        internal static object[] RouteCells(object[] row)
        {
            string cidr=Convert.ToString(row[2],CultureInfo.InvariantCulture);string[] parts=cidr.Split('/');IPAddress address;int prefix;
            if(parts.Length!=2||!IPAddress.TryParse(parts[0],out address)||!Int32.TryParse(parts[1],NumberStyles.None,CultureInfo.InvariantCulture,out prefix)||prefix<0||prefix>(address.AddressFamily==AddressFamily.InterNetwork?32:128))throw new FormatException("路由目标或掩码位数无效："+cidr);
            string family=address.AddressFamily==AddressFamily.InterNetwork?"IPv4":"IPv6";if(Convert.ToString(row[1])!=family)throw new FormatException("路由地址族与目标地址不一致。");
            return new object[]{address.ToString(),prefix,row[3],row[0],row[4],row[5],family};
        }
        private static string AdapterState(OperationalStatus state){switch(state){case OperationalStatus.Up:return "已连接";case OperationalStatus.Down:return "未连接";case OperationalStatus.Dormant:return "休眠";case OperationalStatus.NotPresent:return "设备未就绪";default:return state.ToString();}}
        private static string Family(string value){return value=="2"?"IPv4":value=="23"?"IPv6":value;}
        private static string NeighborState(string value){switch(value){case "0":case "Unreachable":return "不可达";case "1":case "Incomplete":return "解析中";case "2":case "Probe":return "探测中";case "3":case "Delay":return "延迟";case "4":case "Stale":return "缓存过期";case "5":case "Reachable":return "可达";case "6":case "Permanent":return "静态";default:return value;}}
        internal static void ParseRows(string text,RouteInventory result)
        {
            foreach(string row in text.Replace("\r","").Split('\n'))if(row.StartsWith("ITNET|",StringComparison.Ordinal))
            {
                string[] cells=row.Split('|');int expected=cells.Length>1&&cells[1]=="route"?6:cells.Length>1&&cells[1]=="neighbor"?5:0;
                if(expected==0||cells.Length!=expected+2)throw new FormatException("网络表格记录字段无效。");
                var values=new object[expected];for(int i=0;i<expected;i++)values[i]=Encoding.UTF8.GetString(Convert.FromBase64String(cells[i+2]));
                if(cells[1]=="route"){values[1]=Family(Convert.ToString(values[1]));result.Routes.Add(values);}else{values[3]=NeighborState(Convert.ToString(values[3]));values[4]=Family(Convert.ToString(values[4]));result.Neighbors.Add(values);}
            }
        }
    }
}
