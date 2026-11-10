using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ITDesktopToolkit
{
    public static class ShareDiagnosticsService
    {
        public sealed class ShareRow
        {
            public string Kind="", Name="", Address="", State="", Folder="";
            public string OpenPath { get { return Kind=="本机共享" ? @"\\localhost\" + Name : Address; } }
        }
        private static string Decode(string text)
        {
            try{return Encoding.UTF8.GetString(Convert.FromBase64String(text));}catch{return "";}
        }
        public static List<ShareRow> ReadRows(out string report)
        {
            List<ShareRow> rows=new List<ShareRow>();
            StringBuilder log=new StringBuilder();
            try {
                string details;
                List<ShareService.ShareEntry> local=ShareService.ListOrdinaryShares(out details);
                foreach(ShareService.ShareEntry x in local)
                    rows.Add(new ShareRow{Kind="本机共享",Name=x.Name,Address=@"\\"+Environment.MachineName+@"\"+x.Name,State="已创建（认证状态待检测）",Folder=x.Folder});
                if(local.Count==0)log.AppendLine("本地共享：暂无普通文件共享，或枚举权限不足。\r\n"+details);
            }catch(Exception ex){log.AppendLine("本机共享枚举失败："+ex.Message);}
            try {
                string script=@"$ErrorActionPreference='Continue';
                  if(Get-Command Get-SmbMapping -ErrorAction SilentlyContinue) {
                    Get-SmbMapping -ErrorAction SilentlyContinue | ForEach-Object {
                      $path=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes([string]$_.RemotePath));
                      $drive=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes([string]$_.LocalPath));
                      $st=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes([string]$_.Status));
                      Write-Output ('REMOTE|'+$drive+'|'+$path+'|'+$st)
                    }
                  }
                  if(Get-Command Get-SmbConnection -ErrorAction SilentlyContinue) {
                    Get-SmbConnection -ErrorAction SilentlyContinue | ForEach-Object {
                      $path=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes(('\\'+[string]$_.ServerName+'\'+[string]$_.ShareName)));
                      $v=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes(('SMB '+[string]$_.Dialect)));
                      Write-Output ('CONNECTION||'+$path+'|'+$v)
                    }
                  }";
                string raw=PowerShellService.RunScript(script,2);
                foreach(string line in raw.Split(new char[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries)){
                    if(!line.StartsWith("REMOTE|",StringComparison.Ordinal) && !line.StartsWith("CONNECTION|",StringComparison.Ordinal))continue;
                    string[] p=line.Split('|');if(p.Length!=4)continue;
                    string drive=Decode(p[1]),path=Decode(p[2]),state=Decode(p[3]);
                    if(!path.StartsWith(@"\\"))continue;
                    bool duplicate=false;
                    foreach(ShareRow x in rows)if(x.Kind=="远程连接" && x.Address.Equals(path,StringComparison.OrdinalIgnoreCase)){duplicate=true;break;}
                    if(!duplicate)rows.Add(new ShareRow{Kind=line.StartsWith("REMOTE|",StringComparison.Ordinal)?"远程映射":"远程连接",Name=drive.Length>0?drive:path,Address=path,State=state,Folder=""});
                }
                if(raw.IndexOf("[错误]",StringComparison.OrdinalIgnoreCase)>=0)log.AppendLine(raw);
            }catch(Exception ex){log.AppendLine("远程 SMB 连接枚举失败："+ex.Message);}
            report=log.ToString();
            return rows;
        }
        public static string ReadSecurityStatus()
        {
            string ps=@"$ErrorActionPreference='Continue';
              '=== 网络配置文件（专用/公用/域） ===';
              if(Get-Command Get-NetConnectionProfile -ErrorAction SilentlyContinue) {
                Get-NetConnectionProfile | Select-Object Name,InterfaceAlias,InterfaceIndex,NetworkCategory,IPv4Connectivity | Format-Table -AutoSize | Out-String;
              }else{'无法读取 Get-NetConnectionProfile'};
              '=== 专用/公用网络发现与文件共享（防火墙规则） ===';
              if(Get-Command Get-NetFirewallRule -ErrorAction SilentlyContinue) {
                Get-NetFirewallRule -ErrorAction SilentlyContinue | Where-Object {$_.Name -like 'FPS-*' -or $_.Name -like 'NETDIS-*'} |
                  Select-Object Name,DisplayName,Enabled,Profile,Direction,Action | Format-Table -AutoSize | Out-String;
              }else{'无法读取防火墙规则'};
              '=== 共享相关服务 ===';
              Get-Service -Name LanmanWorkstation,LanmanServer,FDResPub,fdPHost,SSDPSRV,upnphost -ErrorAction SilentlyContinue |
                Select-Object Name,Status,StartType | Format-Table -AutoSize | Out-String;
              '=== 所有网络：客户端 / 服务端 SMB 状态 ===';
              $c=Get-SmbClientConfiguration -ErrorAction SilentlyContinue;
              $s=Get-SmbServerConfiguration -ErrorAction SilentlyContinue;
              if($c){'客户端强制签名：'+$c.RequireSecuritySignature; '允许不安全来宾：'+$c.EnableInsecureGuestLogons; '客户端启用 SMB2/3（由连接协商确认）'}else{'客户端 SMB 配置不可读取'};
              if($s){'服务端强制签名：'+$s.RequireSecuritySignature; '已认证用户共享：'+$s.EnableAuthenticateUserSharing; '服务端 SMB2：'+$s.EnableSMB2Protocol; '服务端 SMB1：'+$s.EnableSMB1Protocol}else{'服务端 SMB 配置不可读取'};
              '=== SMB1 Windows 可选组件 ===';
              if(Get-Command Get-WindowsOptionalFeature -ErrorAction SilentlyContinue){
                Get-WindowsOptionalFeature -Online -FeatureName 'SMB1Protocol' -ErrorAction SilentlyContinue |
                  Select-Object FeatureName,State | Format-List | Out-String
              }else{'组件状态无法读取'};
              '=== 注意 ===';
              '密码保护共享/公用文件夹共享与 SMB 认证或签名并非同一个开关。具体显示以 Windows 高级共享设置为准。';
              '企业组策略可能覆盖本地配置，且网络发现要同时满足服务及防火墙条件。';";
            return PowerShellService.RunScript(ps,3);
        }
        public static string Diagnose(string input)
        {
            if(String.IsNullOrWhiteSpace(input))return "请填写 IP、主机名或 UNC 路径。";
            string host=input.Trim();
            if(host.StartsWith(@"\\"))host=host.Substring(2).Split('\\')[0];
            else if(host.Contains("\\") || host.Contains("/"))return "只支持主机名、IPv4/IPv6 地址或 \\\\主机\\共享 形式。";
            if(host.StartsWith("[") && host.EndsWith("]")) host=host.Substring(1,host.Length-2);
            if(host.Length>253 || !Regex.IsMatch(host,@"^[a-zA-Z0-9_.:\-]+$"))return "目标格式不正确：请填写主机名或 IP 地址。";
            string ps="$ErrorActionPreference='Continue'; $target="+PowerShellService.Quote(host)+"; "+@"
              '目标主机：'+$target;
              '【DNS/IP 解析】';try{[Net.Dns]::GetHostAddresses($target)|ForEach-Object{$_.IPAddressToString}}catch{'解析失败：'+$_.Exception.Message};
              '【TCP 445 端口】';$tcp=New-Object Net.Sockets.TcpClient;
              try { $ar=$tcp.BeginConnect($target,445,$null,$null);
                if($ar.AsyncWaitHandle.WaitOne(2500,$false)){$tcp.EndConnect($ar);'445 可建立 TCP 连接（不代表认证成功）'}
                else{'445 连接超时（可能防火墙、网络隔离或服务未监听）'}
              }catch{'445 连接失败：'+$_.Exception.Message}
              finally{$tcp.Close()};
              '【本机 SMB 客户端】';
              Get-Service LanmanWorkstation -ErrorAction SilentlyContinue|Select-Object Name,Status|Format-Table|Out-String;
              $c=Get-SmbClientConfiguration -ErrorAction SilentlyContinue;
              if($c){'强制签名：'+$c.RequireSecuritySignature;'允许不安全来宾：'+$c.EnableInsecureGuestLogons};
              '【已建立 SMB 连接】';
              Get-SmbConnection -ErrorAction SilentlyContinue | Where-Object {$_.ServerName -eq $target} |
                Select-Object ServerName,ShareName,Dialect,Signed,Encrypted | Format-Table -AutoSize | Out-String;
              '结果说明：0x80004005 是泛化错误；本检测无法凭此确认 SMB1/签名/权限哪一项是根因。';";
            return PowerShellService.RunScript(ps,2);
        }
        // Only four well-defined SMB properties are exposed; no arbitrary PowerShell input is executed.
        private static string PropertyName(string key)
        {
            if(key=="ClientSignature"||key=="ServerSignature")return "RequireSecuritySignature";
            if(key=="ClientGuest")return "EnableInsecureGuestLogons";
            if(key=="ServerAuthenticated")return "EnableAuthenticateUserSharing";
            throw new ArgumentException("未支持的 SMB 配置选项。", "key");
        }
        public static string ChangeOption(string key,bool value,bool restore)
        {
            if(!CommandService.IsAdministrator())return "需要管理员权限。";
            string prop=PropertyName(key);
            bool client=key.StartsWith("Client",StringComparison.Ordinal);
            string getter=client?"Get-SmbClientConfiguration":"Get-SmbServerConfiguration";
            string setter=client?"Set-SmbClientConfiguration":"Set-SmbServerConfiguration";
            string backup=Path.Combine(Paths.UserDir,"smb_original_"+key+".txt");
            string ps="$ErrorActionPreference='Stop'; $path="+PowerShellService.Quote(backup)+"; "+
                "$name='"+prop+"'; $before=("+getter+" -ErrorAction Stop).$name; "+
                "if($null -eq $before){throw '当前 Windows 版本没有此 SMB 属性'}; ";
            if(restore)
                ps+="if(-not(Test-Path -LiteralPath $path)){throw '没有原始设置备份，未执行修改'}; "+
                    "$original=(Get-Content -LiteralPath $path -Raw -ErrorAction Stop).Trim(); "+
                    "if($original -notin @('True','False')){throw '备份值不合法，已中止'}; $want=[bool]::Parse($original); ";
            else
                ps+="if(-not(Test-Path -LiteralPath $path)){[IO.File]::WriteAllText($path,([string]$before),[Text.Encoding]::ASCII)}; "+
                    "$want="+(value?"$true":"$false")+"; ";
            ps+=setter+" -"+prop+" $want -Confirm:$false -ErrorAction Stop; "+
                "$after=("+getter+" -ErrorAction Stop).$name; "+
                "if($after -ne $want){throw '修改后状态校验失败'}; "+
                "'已读取确认：'+$name+' = '+$after; '原始备份位置：'+$path; "+
                "'修改可能影响正在进行的 SMB 会话，组策略也可能覆盖本机设置。'";
            return PowerShellService.RunScript(ps,2);
        }
    }
}
