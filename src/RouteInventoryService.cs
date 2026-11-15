using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
namespace ITDesktopToolkit
{
    public sealed class RouteInventory
    {
        public readonly List<object[]> Adapters=new List<object[]>(),Routes=new List<object[]>(),Neighbors=new List<object[]>();public string Warning="";
    }
    public static class RouteInventoryService
    {
        public static RouteInventory Read(CancellationToken cancel)
        {
            var result=new RouteInventory();
            foreach(var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                cancel.ThrowIfCancellationRequested();var ip=nic.GetIPProperties();var gateways=new List<string>();foreach(var g in ip.GatewayAddresses)gateways.Add(g.Address.ToString());
                var dns=new List<string>();foreach(var d in ip.DnsAddresses)dns.Add(d.ToString());byte[] mac=nic.GetPhysicalAddress().GetAddressBytes();string hardware=mac.Length==0?"未知":BitConverter.ToString(mac).Replace('-',':');
                string speed="未知";try{if(nic.Speed>0)speed=(nic.Speed/1000000.0).ToString("0.###",CultureInfo.InvariantCulture);}catch(NetworkInformationException){}
                if(ip.UnicastAddresses.Count==0)result.Adapters.Add(new object[]{nic.Name,AdapterState(nic.OperationalStatus),hardware,"无地址","未知",String.Join(", ",gateways.ToArray()),String.Join(", ",dns.ToArray()),speed});
                foreach(var a in ip.UnicastAddresses)result.Adapters.Add(new object[]{nic.Name,AdapterState(nic.OperationalStatus),hardware,a.Address.ToString(),a.Address.AddressFamily==AddressFamily.InterNetwork?a.IPv4Mask.ToString():"未知",String.Join(", ",gateways.ToArray()),String.Join(", ",dns.ToArray()),speed});
            }
            string raw=PowerShellService.RunScript(@"$ErrorActionPreference='Stop';function Emit([string]$kind,[object[]]$cells){$values=@($cells|ForEach-Object{[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes([string]$_))});'ITNET|'+$kind+'|'+($values -join '|')};try{
foreach($r in Get-NetRoute){Emit 'route' @($r.InterfaceAlias,$r.AddressFamily,$r.DestinationPrefix,$r.NextHop,$r.RouteMetric,$r.Protocol)}
foreach($n in Get-NetNeighbor){Emit 'neighbor' @($n.InterfaceAlias,$n.IPAddress,$n.LinkLayerAddress,$n.State,$n.AddressFamily)}
}catch{Write-Output $_.Exception.Message;exit 1}",2,cancel);
            if(!raw.Replace("\r","").Contains("\n返回代码：0\n")){result.Warning="路由/邻居读取未完成。"+raw;return result;}
            ParseRows(raw,result);cancel.ThrowIfCancellationRequested();return result;
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
