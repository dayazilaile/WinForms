using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Xml;
namespace ITDesktopToolkit
{
    public sealed class SavedWifiProfile
    {
        public string Name="",Ssid="未知",Authentication="未知",Encryption="未知",Password="无法读取",Status="",Adapter="";
        public bool HasPassword;
        public object[] Cells(){return new object[]{Name,Ssid,Authentication,Encryption,Password,Status,Adapter};}
        public void Clear(){Password="";HasPassword=false;}
    }
    public sealed class SavedWifiInventory : IDisposable
    {
        public readonly List<SavedWifiProfile> Profiles=new List<SavedWifiProfile>();
        public readonly List<string> Notices=new List<string>();
        public void Dispose(){foreach(var profile in Profiles)profile.Clear();Profiles.Clear();Notices.Clear();}
    }
    public static class SavedWifiService
    {
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]internal struct ProfileInfo
        {[MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)]public string Name;public uint Flags;}
        [DllImport("wlanapi.dll")]private static extern uint WlanOpenHandle(uint version,IntPtr reserved,out uint negotiated,out IntPtr handle);
        [DllImport("wlanapi.dll")]private static extern uint WlanCloseHandle(IntPtr handle,IntPtr reserved);
        [DllImport("wlanapi.dll")]private static extern void WlanFreeMemory(IntPtr data);
        [DllImport("wlanapi.dll")]private static extern uint WlanEnumInterfaces(IntPtr handle,IntPtr reserved,out IntPtr list);
        [DllImport("wlanapi.dll")]private static extern uint WlanGetProfileList(IntPtr handle,ref Guid id,IntPtr reserved,out IntPtr list);
        [DllImport("wlanapi.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]private static extern uint WlanGetProfile(IntPtr handle,ref Guid id,string name,IntPtr reserved,out IntPtr xml,ref uint flags,out uint grantedAccess);
        private static string Node(XmlNode root,string path){XmlNode node=root.SelectSingleNode(path);return node==null?"":node.InnerText;}
        private static string Child(string name){return "*[local-name()='"+name+"']";}
        internal static SavedWifiProfile ParseProfile(string name,string adapter,string xml)
        {
            if(xml==null||xml.Length>1024*1024)throw new FormatException("无线配置内容无效，未显示密码。");
            var document=new XmlDocument{XmlResolver=null};
            try{using(var reader=XmlReader.Create(new StringReader(xml),new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=1024*1024}))document.Load(reader);}
            catch(XmlException){throw new FormatException("无线配置 XML 无效，未显示密码。");}
            var root=document.DocumentElement;if(root==null||root.LocalName!="WLANProfile")throw new FormatException("无线配置类型无效。");
            var profile=new SavedWifiProfile{Name=name,Adapter=adapter};string ssidPath=Child("SSIDConfig")+"/"+Child("SSID")+"/";string hex=Node(root,ssidPath+Child("hex"));
            if(hex.Length>0)
            {
                if(hex.Length%2!=0||hex.Length>64)throw new FormatException("无线配置 SSID 无效。");var bytes=new byte[32];
                for(int i=0;i<hex.Length/2;i++)if(!Byte.TryParse(hex.Substring(i*2,2),NumberStyles.HexNumber,CultureInfo.InvariantCulture,out bytes[i]))throw new FormatException("无线配置 SSID 无效。");
                profile.Ssid=WifiInventoryService.SsidText(new WifiInventoryService.Ssid{Length=(uint)(hex.Length/2),Bytes=bytes});
            }
            else {string ssid=Node(root,ssidPath+Child("name"));if(ssid.Length>0)profile.Ssid=ssid;}
            XmlNode security=root.SelectSingleNode(Child("MSM")+"/"+Child("security"));
            if(security==null){profile.Status="配置未提供安全信息";return profile;}
            string auth=Node(security,Child("authEncryption")+"/"+Child("authentication")),cipher=Node(security,Child("authEncryption")+"/"+Child("encryption"));
            if(auth.Length>0)profile.Authentication=auth;if(cipher.Length>0)profile.Encryption=cipher;
            XmlNode key=security.SelectSingleNode(Child("sharedKey"));
            if(key!=null)
            {
                string protection=Node(key,Child("protected")),material=Node(key,Child("keyMaterial"));
                if(protection=="false"||protection=="0")
                {if(material.Length>256){profile.Status="系统返回的密码长度异常，未显示";}else if(material.Length>0){profile.Password=material;profile.HasPassword=true;profile.Status="已读取";}else{profile.Password="未保存";profile.Status="配置未保存共享密码";}}
                else {profile.Password="无法读取";profile.Status="系统未返回明文密码，可能受权限或策略限制";}
            }
            else if(auth.Equals("open",StringComparison.OrdinalIgnoreCase)&&cipher.Equals("none",StringComparison.OrdinalIgnoreCase))
            {profile.Password="无密码";profile.Status="开放网络";}
            else if(Node(security,Child("authEncryption")+"/"+Child("useOneX"))=="true")
            {profile.Password="无共享密码";profile.Status="企业认证，非共享 Wi-Fi 密码";}
            else {profile.Password="未保存";profile.Status="未保存共享密码或此认证方式不使用共享密码";}
            return profile;
        }
        internal static List<ProfileInfo> ParseProfileList(IntPtr list,int? available=null)
        {
            if(list==IntPtr.Zero||(available.HasValue&&available.Value<8))throw new FormatException("无线配置列表为空或不完整。");int count=Marshal.ReadInt32(list),stride=Marshal.SizeOf(typeof(ProfileInfo));
            if(count<0||count>2048||(available.HasValue&&8L+(long)count*stride>available.Value))throw new FormatException("无线配置列表长度无效。");
            var result=new List<ProfileInfo>();for(int i=0;i<count;i++){var item=(ProfileInfo)Marshal.PtrToStructure(IntPtr.Add(list,8+i*stride),typeof(ProfileInfo));if(String.IsNullOrEmpty(item.Name))throw new FormatException("无线配置名称无效。");result.Add(item);}return result;
        }
        private static string Error(uint code){return code==5?"Windows 拒绝访问（错误 5）":code==1062?"Windows WLAN 服务未运行（错误 1062）":"Windows 无线接口错误 "+code;}
        private static SavedWifiProfile ReadProfile(IntPtr handle,Guid id,ProfileInfo item,string adapter,CancellationToken token)
        {
            IntPtr xml=IntPtr.Zero;uint flags=4,access;
            try
            {
                token.ThrowIfCancellationRequested();uint status=WlanGetProfile(handle,ref id,item.Name,IntPtr.Zero,out xml,ref flags,out access);
                if(status!=0)
                {
                    if(xml!=IntPtr.Zero){WlanFreeMemory(xml);xml=IntPtr.Zero;}token.ThrowIfCancellationRequested();flags=0;status=WlanGetProfile(handle,ref id,item.Name,IntPtr.Zero,out xml,ref flags,out access);
                }
                token.ThrowIfCancellationRequested();if(status!=0||xml==IntPtr.Zero)return new SavedWifiProfile{Name=item.Name,Adapter=adapter,Status=status==0?"系统未返回配置内容":Error(status)};
                try{return ParseProfile(item.Name,adapter,Marshal.PtrToStringUni(xml));}catch(FormatException){return new SavedWifiProfile{Name=item.Name,Adapter=adapter,Status="配置解析失败，未显示密码"};}
            }finally{if(xml!=IntPtr.Zero)WlanFreeMemory(xml);}
        }
        public static SavedWifiInventory Read(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();var result=new SavedWifiInventory();IntPtr handle=IntPtr.Zero,interfaces=IntPtr.Zero;
            try
            {
                uint version;uint status=WlanOpenHandle(2,IntPtr.Zero,out version,out handle);if(status!=0)throw new InvalidOperationException(Error(status));
                token.ThrowIfCancellationRequested();status=WlanEnumInterfaces(handle,IntPtr.Zero,out interfaces);if(status!=0)throw new InvalidOperationException(Error(status));if(interfaces==IntPtr.Zero)throw new FormatException("无线接口列表为空。");
                int count=Marshal.ReadInt32(interfaces),stride=Marshal.SizeOf(typeof(WifiInventoryService.InterfaceInfo));if(count<0||count>64)throw new FormatException("无线接口数量无效。");
                for(int i=0;i<count;i++)
                {
                    token.ThrowIfCancellationRequested();var nic=(WifiInventoryService.InterfaceInfo)Marshal.PtrToStructure(IntPtr.Add(interfaces,8+i*stride),typeof(WifiInventoryService.InterfaceInfo));IntPtr profiles=IntPtr.Zero;
                    try
                    {
                        status=WlanGetProfileList(handle,ref nic.Id,IntPtr.Zero,out profiles);if(status!=0){result.Notices.Add(nic.Description+"："+Error(status));continue;}
                        foreach(var profile in ParseProfileList(profiles)){token.ThrowIfCancellationRequested();if(result.Profiles.Count>=2048){result.Notices.Add("本次最多显示 2048 条配置。");return result;}result.Profiles.Add(ReadProfile(handle,nic.Id,profile,nic.Description,token));}
                    }finally{if(profiles!=IntPtr.Zero)WlanFreeMemory(profiles);}
                }
                token.ThrowIfCancellationRequested();return result;
            }
            catch(DllNotFoundException){result.Dispose();throw new NotSupportedException("系统没有无线 LAN 组件；Windows Server 未安装/启用无线 LAN 服务时无法读取无线配置。");}
            catch{result.Dispose();throw;}
            finally{if(interfaces!=IntPtr.Zero)WlanFreeMemory(interfaces);if(handle!=IntPtr.Zero)WlanCloseHandle(handle,IntPtr.Zero);}
        }
    }
}
