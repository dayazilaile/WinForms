using System;
using System.Text;

namespace WinForms
{
    /// <summary>Win10/11 SMB quick presets, not a promise that remote share ACLs are automatically fixed.</summary>
    public static class ShareModePresetService
    {
        private const string RuleName="ITToolkit-Guest-Private-TCP445-v044";
        private const string RuleDescription="ITToolkit Guest Private SMB v044";
        private static bool Ok(string s) { return s != null && s.Contains("返回代码：0"); }
        private static string Part(string title,string result) { return "【"+title+"】\r\n"+result+"\r\n"; }
        private static string Firewall(bool enable)
        {
            string script="$ErrorActionPreference='Stop'; $name="+PowerShellService.Quote(RuleName)+
                "; $description="+PowerShellService.Quote(RuleDescription)+"; try {"+@"
              $r=Get-NetFirewallRule -Name $name -ErrorAction SilentlyContinue;
              if($r -and $r.Description -ne $description){throw '同名防火墙规则由其他程序创建，禁止覆盖'};
              if($enable){
                if($r){Set-NetFirewallRule -Name $name -Enabled True -Profile Private -ErrorAction Stop}
                else{New-NetFirewallRule -Name $name -DisplayName 'IT 工具箱：专用网络 SMB 445 入站' -Description $description -Enabled True -Direction Inbound -Action Allow -Protocol TCP -LocalPort 445 -RemoteAddress LocalSubnet -Profile Private -ErrorAction Stop | Out-Null};
                $verify=Get-NetFirewallRule -Name $name -ErrorAction Stop;
                if([string]$verify.Enabled -ne 'True'){throw '防火墙规则未成功启用'};
                '已验证：只允许专用网络和 LocalSubnet 来源访问 TCP 445。'
              }else{
                if($r){Remove-NetFirewallRule -Name $name -ErrorAction Stop};
                '已移除本工具创建的 SMB 445 规则（未改其他规则）。'
              }
            }catch{Write-Output ('[失败] '+$_.Exception.Message);exit 1}";
            return PowerShellService.RunScript(script,2);
        }
        private static string Guard(bool guest)
        {
            string ps=@"$ErrorActionPreference='Stop';try {
                $sys=Get-CimInstance Win32_ComputerSystem -ErrorAction Stop;
                if($sys.PartOfDomain){throw '域成员设备拒绝自动修改 SMB 认证安全策略'};
                if($guest){
                    $profiles=@(Get-NetConnectionProfile -ErrorAction Stop);
                    if(@($profiles|Where-Object {$_.NetworkCategory -eq 'Private'}).Count -eq 0){throw '没有检测到可信专用网络，免账号模式已中止'};
                    if(@($profiles|Where-Object {$_.NetworkCategory -eq 'Public' -and $_.IPv4Connectivity -in @('Subnet','LocalNetwork','Internet')}).Count -gt 0){throw '存在公用网络连接。请先断开或明确识别用于共享的专用网卡，避免错误开放。'};
                }
                if(-not(Get-Command Get-SmbClientConfiguration -ErrorAction SilentlyContinue) -or -not(Get-Command Get-SmbServerConfiguration -ErrorAction SilentlyContinue)){throw '本系统 SMB 管理命令不可用'};
                '预检通过：Windows SMB 管理接口可用。'
            }catch{Write-Output ('[预检未通过] '+$_.Exception.Message);exit 1}";
            return PowerShellService.RunScript("$guest="+(guest?"$true;":"$false;")+ps,2);
        }
        public static string Apply(bool guest)
        {
            if(!CommandService.IsAdministrator())return "需要管理员权限，不会修改配置。";
            StringBuilder log=new StringBuilder();
            string r=Guard(guest);log.Append(Part("系统/网络预检",r));if(!Ok(r))return log.ToString()+"未修改配置。";
            r=ShareAccessModeService.ApplyClient(guest,false);log.Append(Part("客户端 SMB",r));if(!Ok(r))return log.ToString()+"[中止] 客户端失败。可通过恢复原配置撤销此前已保存的设置。";
            r=ShareAccessModeService.ApplyServer(guest,false);log.Append(Part("服务端 SMB",r));if(!Ok(r))return log.ToString()+"[中止] 服务端失败，可能部分设置已经修改，请使用恢复按钮。";
            r=ShareAccessModeService.SetLocalGuestEnabled(guest);log.Append(Part("本机 Guest 账户",r));if(!Ok(r))return log.ToString()+"[中止] Guest 账户无法设置，可能受版本/组策略限制。";
            r=Firewall(guest);log.Append(Part("专用网络防火墙",r));if(!Ok(r))return log.ToString()+"[中止] 防火墙配置失败，请核对状态。";
            log.Append("配置命令均返回成功。实际远程访问仍取决于对端策略、共享/NTFS 权限、会话凭据与 Windows 签名要求。\r\n");
            return log.ToString();
        }
        public static string Restore()
        {
            if(!CommandService.IsAdministrator())return "需要管理员权限。";
            StringBuilder b=new StringBuilder();
            // Do not stop at first error: restore each independent setting as far as possible.
            b.Append(Part("恢复客户端原值",ShareAccessModeService.ApplyClient(false,true)));
            b.Append(Part("恢复服务端原值",ShareAccessModeService.ApplyServer(false,true)));
            b.Append(Part("恢复 Guest 原值",ShareAccessModeService.RestoreLocalGuest()));
            b.Append(Part("移除本工具创建的防火墙规则",Firewall(false)));
            b.Append("请核对各步骤返回代码；如果没有初始备份，报告会明确指出，而不是直接猜测原始状态。\r\n");
            return b.ToString();
        }
        public static string ReadStatus()
        {
            string ps=@"$ErrorActionPreference='Continue';
              '=== OS ==='; (Get-CimInstance Win32_OperatingSystem).Caption+' / Build '+[Environment]::OSVersion.Version.Build;
              '=== 网络配置 ==='; Get-NetConnectionProfile | ForEach-Object {$_.InterfaceAlias+' / '+$_.NetworkCategory+' / '+$_.IPv4Connectivity};
              '=== SMB 属性 ===';
              $c=Get-SmbClientConfiguration -ErrorAction SilentlyContinue;
              $s=Get-SmbServerConfiguration -ErrorAction SilentlyContinue;
              '客户端：允许 Guest='+$c.EnableInsecureGuestLogons+'；强制签名='+$c.RequireSecuritySignature;
              '服务端：强制签名='+$s.RequireSecuritySignature+'；强制加密='+$s.EncryptData;
              'ForceGuest='+((Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Lsa' -Name forceguest -ErrorAction SilentlyContinue).forceguest);
              $u=Get-CimInstance Win32_UserAccount -Filter 'LocalAccount=True' -ErrorAction SilentlyContinue|Where-Object {$_.SID -match '-501$'}|Select-Object -First 1;
              if($u){'Guest账户：'+$u.Name+'；禁用='+$u.Disabled}else{'Guest账户状态：不可读取'};
              '=== 工具专用防火墙规则 ===';
              $r=Get-NetFirewallRule -Name 'ITToolkit-Guest-Private-TCP445-v044' -ErrorAction SilentlyContinue;
              if($r){'规则：'+$r.Name+'；启用='+$r.Enabled+'；配置文件='+$r.Profile}else{'规则：不存在'};
              '注意：账号/免账号模式是当前主机兼容设置，不能代替对端共享及 NTFS ACL。';";
            return PowerShellService.RunScript(ps,2);
        }
    }
}
