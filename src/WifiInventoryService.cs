using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
namespace WinForms
{
    public sealed class WifiAdapterRow
    {
        public string Name="未知",Mac="未知",State="未知",Ssid="未连接",Bssid="未知",Type="未知",Phy="未知",Frequency="未知",Band="未知",Channel="未知",Signal="未知",Rx="未知",Tx="未知";
        public object[] Cells(){return new object[]{Ssid,Mac,Bssid,WifiInventoryService.SignalText(Signal),Frequency,Rx,Tx,Phy,Band,Channel,Type,State,Name};}
    }
    public sealed class WifiBssRow
    {
        public string Adapter="未知",Ssid="未知",Bssid="未知",Type="未知",Phy="未知",Frequency="未知",Band="未知",Channel="未知",Signal="未知",Rssi="未知",BasicRates="未知",SupportedRates="未知",Security="未知";
        public object[] Cells(){return new object[]{Ssid,Bssid,Rssi,WifiInventoryService.SignalText(Signal),Phy,Band,Frequency,Channel,BasicRates,Security,Type,Adapter,SupportedRates};}
    }
    public sealed class WifiInventory
    {
        public readonly List<WifiAdapterRow> Adapters=new List<WifiAdapterRow>();
        public readonly List<WifiBssRow> Networks=new List<WifiBssRow>();
        public readonly List<string> Notices=new List<string>();
    }
    public static class WifiInventoryService
    {
        internal static string SignalText(string signal)
        {int value;return Int32.TryParse(signal,NumberStyles.Integer,CultureInfo.InvariantCulture,out value)&&value>=0&&value<=100?value.ToString(CultureInfo.InvariantCulture)+"%":signal;}

        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] internal struct InterfaceInfo
        {public Guid Id;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)]public string Description;public uint State;}
        [StructLayout(LayoutKind.Sequential)] internal struct Ssid
        {public uint Length;[MarshalAs(UnmanagedType.ByValArray,SizeConst=32)]public byte[] Bytes;}
        [StructLayout(LayoutKind.Sequential)] internal struct Association
        {public Ssid Ssid;public uint Type;[MarshalAs(UnmanagedType.ByValArray,SizeConst=6)]public byte[] Mac;public uint Phy,PhyIndex,Quality,RxRate,TxRate;}
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] internal struct Connection
        {public uint State,Mode;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)]public string Profile;public Association Association;public int Security,OneX;public uint Auth,Cipher;}
        [StructLayout(LayoutKind.Sequential)] internal struct RateSet
        {public uint Length;[MarshalAs(UnmanagedType.ByValArray,SizeConst=126)]public ushort[] Values;}
        [StructLayout(LayoutKind.Sequential,Pack=8)] internal struct BssEntry
        {
            public Ssid Ssid;public uint PhyId;[MarshalAs(UnmanagedType.ByValArray,SizeConst=6)]public byte[] Mac;
            public uint Type,Phy;public int Rssi;public uint Quality;[MarshalAs(UnmanagedType.U1)]public bool InDomain;
            public ushort Beacon;public ulong Timestamp,HostTimestamp;public ushort Capabilities;public uint Frequency;public RateSet Rates;public uint IeOffset,IeSize;
        }
        [DllImport("wlanapi.dll")]private static extern uint WlanOpenHandle(uint version,IntPtr reserved,out uint negotiated,out IntPtr handle);
        [DllImport("wlanapi.dll")]private static extern uint WlanCloseHandle(IntPtr handle,IntPtr reserved);
        [DllImport("wlanapi.dll")]private static extern void WlanFreeMemory(IntPtr data);
        [DllImport("wlanapi.dll")]private static extern uint WlanEnumInterfaces(IntPtr handle,IntPtr reserved,out IntPtr list);
        [DllImport("wlanapi.dll")]private static extern uint WlanQueryInterface(IntPtr handle,ref Guid id,uint opcode,IntPtr reserved,out uint size,out IntPtr data,out uint valueType);
        [DllImport("wlanapi.dll")]private static extern uint WlanGetNetworkBssList(IntPtr handle,ref Guid id,IntPtr ssid,uint type,[MarshalAs(UnmanagedType.Bool)]bool secure,IntPtr reserved,out IntPtr list);
        public static string Frequency(uint khz){return khz==0?"未知":(khz/1000.0).ToString("0.###",CultureInfo.InvariantCulture);}
        public static string Band(uint khz){double mhz=khz/1000.0;return mhz>=2400&&mhz<=2500?"2.4 GHz":mhz>=4900&&mhz<5925?"5 GHz":mhz>=5925&&mhz<=7125?"6 GHz":mhz>=57000&&mhz<=71000?"60 GHz":"未知";}
        public static string Channel(uint khz)
        {
            if(khz%1000!=0)return "未知";int mhz=(int)(khz/1000),ch=-1;
            if(mhz==2484)ch=14;
            else if(mhz>=2412&&mhz<=2472&&(mhz-2407)%5==0)ch=(mhz-2407)/5;
            else if(mhz>=4910&&mhz<=4980&&mhz%5==0)ch=(mhz-4000)/5;
            else if(mhz>=5005&&mhz<=5895&&mhz%5==0)ch=(mhz-5000)/5;
            else if(mhz==5935)ch=2;
            else if(mhz>=5955&&mhz<=7115&&(mhz-5950)%5==0)ch=(mhz-5950)/5;
            else if(mhz>=58320&&mhz<=70200&&(mhz-56160)%2160==0)ch=(mhz-56160)/2160;
            return ch<0?"未知":ch.ToString(CultureInfo.InvariantCulture);
        }
        public static string Rates(ushort[] values,uint length,bool basicOnly)
        {
            if(values==null||length>126||length>values.Length)throw new FormatException("无线速率列表无效。");
            var rates=new SortedSet<double>();for(int i=0;i<(int)length;i++){ushort n=values[i];if(basicOnly&&(n&0x8000)==0)continue;double rate=(n&0x7fff)/2.0;if(rate>0)rates.Add(rate);}
            var text=new List<string>();foreach(double r in rates)text.Add(r.ToString("0.###",CultureInfo.InvariantCulture));return text.Count==0?"未知":String.Join(", ",text.ToArray());
        }
        internal static string SsidText(Ssid ssid)
        {
            if(ssid.Length>32||ssid.Bytes==null||ssid.Bytes.Length<ssid.Length)throw new FormatException("SSID 长度无效。");if(ssid.Length==0)return "(隐藏网络)";
            try{string value=new UTF8Encoding(false,true).GetString(ssid.Bytes,0,(int)ssid.Length);foreach(char c in value)if(Char.IsControl(c))return "0x"+BitConverter.ToString(ssid.Bytes,0,(int)ssid.Length).Replace("-","");return value;}
            catch(DecoderFallbackException){return "0x"+BitConverter.ToString(ssid.Bytes,0,(int)ssid.Length).Replace("-","");}
        }
        private static string Mac(byte[] bytes){return bytes==null||bytes.Length!=6?"未知":BitConverter.ToString(bytes).Replace('-',':');}
        private static string TypeName(uint type){return type==1?"基础结构":type==2?"Ad-hoc":"未知";}
        private static string Phy(uint value){switch(value){case 4:return "802.11a";case 5:return "802.11b";case 6:return "802.11g";case 7:return "802.11n";case 8:return "802.11ac";case 9:return "802.11ad";case 10:return "802.11ax";case 11:return "802.11be";default:return "未知 ("+value+")";}}
        private static string State(uint value){switch(value){case 0:return "未就绪";case 1:return "已连接";case 2:return "Ad-hoc启动中";case 3:return "断开中";case 4:return "未连接";case 5:return "关联中";case 6:return "发现中";case 7:return "认证中";default:return "未知";}}
        private static string Rate(uint kbps){return kbps==0?"未知":(kbps/1000.0).ToString("0.###",CultureInfo.InvariantCulture);}
        private static string Error(uint code){return new Win32Exception((int)code).Message+" ("+code+")"+(code==5?"；请检查 Windows 无线/位置权限及组织策略":"");}
        internal static List<WifiBssRow> ParseBssList(IntPtr list,string adapter)
        {
            if(list==IntPtr.Zero)throw new FormatException("无线网络缓冲区为空。");
            uint size=unchecked((uint)Marshal.ReadInt32(list)),count=unchecked((uint)Marshal.ReadInt32(list,4));int stride=Marshal.SizeOf(typeof(BssEntry));
            if(size<8||count>1024||8L+(long)count*stride>size)throw new FormatException("无线网络缓冲区大小无效。");
            var rows=new List<WifiBssRow>();
            for(int i=0;i<(int)count;i++)
            {
                var entry=(BssEntry)Marshal.PtrToStructure(IntPtr.Add(list,8+i*stride),typeof(BssEntry));
                rows.Add(new WifiBssRow{Adapter=adapter,Ssid=SsidText(entry.Ssid),Bssid=Mac(entry.Mac),Type=TypeName(entry.Type),Phy=Phy(entry.Phy),Frequency=Frequency(entry.Frequency),Band=Band(entry.Frequency),Channel=Channel(entry.Frequency),Signal=entry.Quality<=100?entry.Quality.ToString(CultureInfo.InvariantCulture):"未知",Rssi=entry.Rssi.ToString(CultureInfo.InvariantCulture),BasicRates=Rates(entry.Rates.Values,entry.Rates.Length,true),SupportedRates=Rates(entry.Rates.Values,entry.Rates.Length,false),Security=(entry.Capabilities&0x10)!=0?"加密":"开放"});
            }
            return rows;
        }
        public static WifiInventory Read(CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();if(Environment.OSVersion.Platform!=PlatformID.Win32NT)throw new PlatformNotSupportedException("无线表格读取需要 Windows WLAN API；当前平台无法提供真实无线信息。");
            IntPtr handle;uint version;uint status;try{status=WlanOpenHandle(2,IntPtr.Zero,out version,out handle);}catch(DllNotFoundException){throw new NotSupportedException("系统没有无线 LAN 组件；Windows Server 请核对 Wireless LAN Service 角色/服务，无需为有线功能安装它。");}if(status!=0)throw new InvalidOperationException("无法打开无线接口："+Error(status));
            var result=new WifiInventory();
            try
            {
                IntPtr list=IntPtr.Zero;
                try
                {
                    status=WlanEnumInterfaces(handle,IntPtr.Zero,out list);if(status!=0)throw new InvalidOperationException(Error(status));
                    if(list==IntPtr.Zero)throw new FormatException("无线接口缓冲区为空。");
                    int count=Marshal.ReadInt32(list),stride=Marshal.SizeOf(typeof(InterfaceInfo));if(count<0||count>64)throw new FormatException("无线接口数量无效。");
                    foreach(int index in Indices(count))
                    {
                        cancel.ThrowIfCancellationRequested();var nic=(InterfaceInfo)Marshal.PtrToStructure(IntPtr.Add(list,8+index*stride),typeof(InterfaceInfo));
                        var row=new WifiAdapterRow{Name=nic.Description,State=State(nic.State)};
                        foreach(var n in NetworkInterface.GetAllNetworkInterfaces()){Guid id;if(Guid.TryParse(n.Id,out id)&&id==nic.Id){byte[] mac=n.GetPhysicalAddress().GetAddressBytes();if(mac.Length==6)row.Mac=Mac(mac);break;}}
                        if(nic.State==1)
                        {
                            row.Ssid="未知";
                            IntPtr data=IntPtr.Zero;uint size,valueType;
                            try
                            {
                                status=WlanQueryInterface(handle,ref nic.Id,7,IntPtr.Zero,out size,out data,out valueType);
                                if(status==0&&data!=IntPtr.Zero&&size>=Marshal.SizeOf(typeof(Connection)))
                                {
                                    var c=(Connection)Marshal.PtrToStructure(data,typeof(Connection));row.Ssid=SsidText(c.Association.Ssid);row.Bssid=Mac(c.Association.Mac);row.Type=TypeName(c.Association.Type);row.Phy=Phy(c.Association.Phy);row.Signal=c.Association.Quality<=100?c.Association.Quality.ToString(CultureInfo.InvariantCulture):"未知";row.Rx=Rate(c.Association.RxRate);row.Tx=Rate(c.Association.TxRate);
                                }
                                else result.Notices.Add(nic.Description+"：当前连接读取失败 "+(status==0?"数据长度无效":Error(status)));
                            }finally{if(data!=IntPtr.Zero)WlanFreeMemory(data);}
                        }
                        IntPtr bss=IntPtr.Zero;
                        try
                        {
                            cancel.ThrowIfCancellationRequested();status=WlanGetNetworkBssList(handle,ref nic.Id,IntPtr.Zero,3,false,IntPtr.Zero,out bss);
                            if(status==0)
                            {
                                foreach(var network in ParseBssList(bss,nic.Description)){result.Networks.Add(network);if(network.Bssid==row.Bssid){row.Frequency=network.Frequency;row.Band=network.Band;row.Channel=network.Channel;}}
                            }
                            else result.Notices.Add(nic.Description+"：附近网络读取失败 "+Error(status));
                        }finally{if(bss!=IntPtr.Zero)WlanFreeMemory(bss);}
                        result.Adapters.Add(row);
                    }
                }finally{if(list!=IntPtr.Zero)WlanFreeMemory(list);}
            }finally{WlanCloseHandle(handle,IntPtr.Zero);}
            cancel.ThrowIfCancellationRequested();return result;
        }
        private static IEnumerable<int> Indices(int count){for(int i=0;i<count;i++)yield return i;}
    }
}
