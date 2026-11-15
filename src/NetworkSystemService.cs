using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ITDesktopToolkit
{
    public static class NetworkSystemService
    {
        // Standard Win32 argv quoting; no cmd.exe is used.
        public static string Argument(string value)
        {
            if(value==null||value.IndexOfAny(new char[]{'\0','\r','\n'})>=0)throw new ArgumentException("参数包含无效字符。");
            var b=new StringBuilder("\"");int slashes=0;
            foreach(char c in value){if(c=='\\'){slashes++;continue;}if(c=='"'){b.Append('\\',slashes*2+1);b.Append('"');}else{b.Append('\\',slashes);b.Append(c);}slashes=0;}
            b.Append('\\',slashes*2);return b.Append('"').ToString();
        }
        public static string Dhcp(CancellationToken cancel)
        {
            return PowerShellService.RunScript(@"$ErrorActionPreference='Stop';try {
'=== 当前网卡 DHCP 配置 ===';Get-CimInstance Win32_NetworkAdapterConfiguration -Filter 'IPEnabled=True' | Select-Object Description,MACAddress,DHCPEnabled,DHCPServer,DHCPLeaseObtained,DHCPLeaseExpires,IPAddress,IPSubnet,DefaultIPGateway,DNSServerSearchOrder | Format-List | Out-String -Width 240;
'=== 相关服务 ===';Get-Service Dhcp,Dnscache,NlaSvc | Select-Object Name,Status,StartType | Format-Table -AutoSize | Out-String -Width 240;
'当前信息来自本机配置与租约，不发送 DHCP 申请，不修改网络，不能用来排除其他 DHCP 服务器或服务器冲突。'
}catch{Write-Output ('读取未完成：'+$_.Exception.Message);exit 1}",2,cancel);
        }
        public static string WifiProfiles(CancellationToken cancel){return CommandService.Run("netsh.exe","wlan show profiles",1,cancel);}
        public static string WifiSecret(string profile,CancellationToken cancel)
        {
            if(String.IsNullOrWhiteSpace(profile)||profile.Length>256)throw new ArgumentException("请输入本机已保存配置的准确名称（不是附近网络列表中的任意 SSID）。");
            return CommandService.Run("netsh.exe","wlan show profile name="+Argument(profile)+" key=clear",1,cancel);
        }
        public static string DnsCheck(string host,CancellationToken cancel)
        {
            host=NetworkDiscoveryService.SingleHost(host);
            return PowerShellService.RunScript("$ErrorActionPreference='Stop';try{Resolve-DnsName -Name "+PowerShellService.Quote(host)+" -DnsOnly -ErrorAction Stop | Select-Object Name,Type,IPAddress,NameHost,TTL | Format-Table -AutoSize | Out-String -Width 240}catch{Write-Output $_.Exception.Message;exit 1}",1,cancel);
        }
        public static async Task<string> HttpCheck(string value,CancellationToken cancel)
        {
            Uri uri;if(!Uri.TryCreate(value,UriKind.Absolute,out uri)||(uri.Scheme!="http"&&uri.Scheme!="https")||uri.UserInfo.Length>0)throw new ArgumentException("请输入不含账号密码的完整 HTTP/HTTPS URL。");
            cancel.ThrowIfCancellationRequested();
            var request=(HttpWebRequest)WebRequest.Create(uri);request.Method="HEAD";request.AllowAutoRedirect=false;request.Timeout=5000;request.ReadWriteTimeout=5000;request.UseDefaultCredentials=false;
            using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancel))
            {
                timeout.CancelAfter(5000);
                using(timeout.Token.Register(delegate{request.Abort();}))
                try{using(var response=(HttpWebResponse)await request.GetResponseAsync())return HttpSummary(uri,response);}
                catch(WebException ex)
                {
                    cancel.ThrowIfCancellationRequested();
                    if(timeout.IsCancellationRequested)return "HTTP 检测超时（5 秒）。";
                    using(var response=ex.Response as HttpWebResponse){if(response!=null)return HttpSummary(uri,response);}
                    return "HTTP 请求失败："+ex.Status+"；"+ex.Message;
                }
            }
        }
        private static string HttpSummary(Uri uri,HttpWebResponse response)
        {
            return uri.GetLeftPart(UriPartial.Path)+"\r\nHTTP "+(int)response.StatusCode+" "+response.StatusDescription+"\r\nServer: "+response.Headers["Server"]+"\r\nLocation: "+response.Headers["Location"]+"\r\n仅发送 HEAD，不自动跟随重定向、不读取正文。401/403/405 等也是服务响应，不代表业务正常。";
        }
    }
}
