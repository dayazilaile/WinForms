using System;
using System.IO;

namespace ITDesktopToolkit
{
    public static class NetworkSharingOptionsService
    {
        private static string Prefix(string section)
        {
            if(section=="Discovery")return "NETDIS-*";
            if(section=="Files")return "FPS-*";
            throw new ArgumentException("未知共享防火墙项目");
        }
        private static string Profile(string profile)
        {
            if(profile=="Private"||profile=="Public")return profile;
            throw new ArgumentException("仅支持专用/公用网络");
        }
        public static string ReadOptions()
        {
            string ps=@"$ErrorActionPreference='Continue';
              '=== 专用 / 公用网络直接状态 ===';
              $rules=Get-NetFirewallRule -ErrorAction SilentlyContinue;
              foreach($profile in @('Private','Public')){
                foreach($kind in @('NETDIS-*','FPS-*')){
                  $set=@($rules | Where-Object {$_.Name -like $kind -and [string]$_.Profile -match $profile});
                  $on=@($set | Where-Object {$_.Enabled -eq 'True'}).Count;
                  $total=$set.Count;
                  $title=if($kind -eq 'NETDIS-*'){'网络发现'}else{'文件和打印机共享'};
                  $state=if($total -eq 0){'规则不可读取'}elseif($on -eq $total){'全部启用'}elseif($on -eq 0){'全部关闭'}else{'部分启用'};
                  $profile+' / '+$title+'：'+$state+'（已启用 '+$on+'/'+$total+' 条）'
                }
              };
              '=== 相关服务 ===';
              Get-Service FDResPub,fdPHost,SSDPSRV,upnphost,LanmanServer,LanmanWorkstation -ErrorAction SilentlyContinue |
                  Select-Object Name,Status,StartType|Format-Table -AutoSize|Out-String;
              '=== 公用文件夹共享检测（仅枚举真实 SMB 共享） ===';
              Get-SmbShare -ErrorAction SilentlyContinue | Where-Object {$_.Path -and $_.Path.TrimEnd('\') -eq $env:PUBLIC} |
                  Select-Object Name,Path,EncryptData | Format-List | Out-String;
              '【说明】公用文件夹访问与实际 NTFS ACL 仍需单独检查。';
              '【说明】“密码保护共享”没有被简化为一个虚假的 SMB 签名开关；在本工具“共享认证模式”中分别管理访问方/共享方。';";
            return PowerShellService.RunScript(ps,2);
        }
        public static string SetFirewall(string profile,string section,bool enabled,bool restore)
        {
            if(!CommandService.IsAdministrator())return "需要管理员权限。";
            profile=Profile(profile);string wildcard=Prefix(section);
            string path=Path.Combine(Paths.UserDir,"share_fw_original_"+profile+"_"+section+".txt");
            string ps="$ErrorActionPreference='Stop'; $profile='"+profile+"'; $pattern='"+wildcard+"'; $backup="+PowerShellService.Quote(path)+"; $restoring="+(restore?"$true":"$false")+"; "+@"
              $rules=@(Get-NetFirewallRule -ErrorAction Stop | Where-Object {$_.Name -like $pattern -and [string]$_.Profile -match $profile});
              if($rules.Count -eq 0){throw '未找到对应的防火墙规则，没有进行任何修改'};
              if($restoring -and -not(Test-Path -LiteralPath $backup)){throw '未找到原始防火墙规则备份'};
              if(-not(Test-Path -LiteralPath $backup)){
                $rules | ForEach-Object {[pscustomobject]@{Name=[string]$_.Name;Enabled=[string]$_.Enabled}} | ConvertTo-Json -Compress | Set-Content -LiteralPath $backup -Encoding UTF8 -ErrorAction Stop
              };";
            if(restore)ps+=@"
              $saved=@(Get-Content -LiteralPath $backup -Raw -ErrorAction Stop | ConvertFrom-Json);
              if($saved.Count -eq 0){throw '原值备份为空'};
              foreach($r in $saved){
                if([string]$r.Name -notlike $pattern){throw '备份规则不属于所选项目'};
                $old=if([string]$r.Enabled -eq 'True'){$true}elseif([string]$r.Enabled -eq 'False'){$false}else{throw '备份状态不合法'};
                Set-NetFirewallRule -Name ([string]$r.Name) -Enabled $old -ErrorAction Stop
              };";
            else ps+="$enabled="+(enabled?"$true":"$false")+"; foreach($r in $rules){Set-NetFirewallRule -Name $r.Name -Enabled $enabled -ErrorAction Stop}; ";
            ps+=@"
              $check=@(Get-NetFirewallRule -ErrorAction Stop | Where-Object {$_.Name -like $pattern -and [string]$_.Profile -match $profile});
              $check | Select-Object Name,Enabled,Profile | Format-Table -AutoSize|Out-String;
              '已尝试更新 '+$check.Count+' 条规则。备份：'+$backup;
              '请再检查 FDResPub/FDProviderHost 等相关服务和共享目录权限；组策略可能覆盖本地规则。';";
            return PowerShellService.RunScript(ps,2);
        }
    }
}
