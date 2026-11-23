using System;
using System.Collections.Generic;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;
namespace WinForms
{
    // Windows 7 / Server 2008 R2: read-only WMI routes and IPv4 IP Helper ARP table.
    internal static class LegacyNetworkReader
    {
        [DllImport("iphlpapi.dll",ExactSpelling=true)]private static extern uint GetIpNetTable(IntPtr table,ref uint size,[MarshalAs(UnmanagedType.Bool)]bool order);
        internal static List<object[]> ParseNeighbors(byte[] data,IDictionary<int,string> names)
        {
            if(data==null||data.Length<4)throw new FormatException("ARP 表头不完整。");
            uint count=BitConverter.ToUInt32(data,0);if(count>(data.Length-4)/24)throw new FormatException("ARP 表记录长度无效。");
            var result=new List<object[]>();
            for(int i=0;i<(int)count;i++)
            {
                int at=4+i*24,index=BitConverter.ToInt32(data,at);uint len=BitConverter.ToUInt32(data,at+4),type=BitConverter.ToUInt32(data,at+20);
                if(len!=6||type<3||type>4)continue;
                string name;if(!names.TryGetValue(index,out name))name="接口 "+index;
                byte[] address=new byte[4];Array.Copy(data,at+16,address,0,4);string mac=BitConverter.ToString(data,at+8,6).Replace('-',':');
                if(mac=="00:00:00:00:00:00"||mac=="FF:FF:FF:FF:FF:FF")continue;
                result.Add(new object[]{name,new IPAddress(address).ToString(),mac,type==4?"静态":"动态缓存","IPv4"});
            }
            return result;
        }
        private static byte[] NeighborBytes()
        {
            uint size=0,code=GetIpNetTable(IntPtr.Zero,ref size,true);if(code!=122&&code!=0)throw new InvalidOperationException("ARP 读取错误 "+code);
            if(size==0)return new byte[4];
            for(int attempt=0;attempt<3;attempt++)
            {
                if(size<4||size>2*1024*1024)throw new InvalidOperationException("ARP 表大小超出限制。");
                IntPtr memory=Marshal.AllocHGlobal((int)size);
                try{uint length=size;code=GetIpNetTable(memory,ref length,true);if(code==122){size=length;continue;}if(code!=0||length>size)throw new InvalidOperationException("ARP 读取错误 "+code);var data=new byte[length];Marshal.Copy(memory,data,0,data.Length);return data;}
                finally{Marshal.FreeHGlobal(memory);}
            }
            throw new InvalidOperationException("ARP 表连续变化，请刷新重试。");
        }
        internal static void Read(RouteInventory result,CancellationToken cancel)
        {
            var names=new Dictionary<int,string>();
            foreach(var nic in NetworkInterface.GetAllNetworkInterfaces()){cancel.ThrowIfCancellationRequested();try{var ip=nic.GetIPProperties().GetIPv4Properties();if(ip!=null)names[ip.Index]=nic.Name;}catch(NetworkInformationException){}}
            try
            {
                using(var search=new ManagementObjectSearcher("SELECT Destination,Mask,NextHop,InterfaceIndex,Metric1,Protocol FROM Win32_IP4RouteTable"))
                using(var rows=search.Get())foreach(ManagementObject row in rows)using(row)
                {
                    cancel.ThrowIfCancellationRequested();string mask=Convert.ToString(row["Mask"]);int? prefix=RouteInventoryService.MaskPrefix(mask);if(!prefix.HasValue)continue;
                    int index=Convert.ToInt32(row["InterfaceIndex"]);string name;if(!names.TryGetValue(index,out name))name="接口 "+index;
                    result.Routes.Add(new object[]{name,"IPv4",Convert.ToString(row["Destination"])+"/"+prefix,Convert.ToString(row["NextHop"]),Convert.ToString(row["Metric1"]),"WMI "+Convert.ToString(row["Protocol"])});
                }
            }catch(OperationCanceledException){throw;}catch(Exception ex){result.Warning+="IPv4 路由未完整读取："+ex.Message+"\r\n";}
            cancel.ThrowIfCancellationRequested();
            try{result.Neighbors.AddRange(ParseNeighbors(NeighborBytes(),names));}catch(Exception ex){result.Warning+="ARP 表未完整读取："+ex.Message+"\r\n";}
            result.Warning+="旧系统兼容读取：仅显示 IPv4 路由与 ARP 缓存；IPv6 路由/邻居不在此兼容表中。缓存不代表设备当前在线。";
        }
    }
}
