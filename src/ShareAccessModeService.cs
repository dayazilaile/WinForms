using System;
using System.IO;

namespace ITDesktopToolkit
{
    /// <summary>
    /// Authenticated / legacy guest compatibility are separate for SMB CLIENT and SMB SERVER.
    /// No false claim is made that a global switch replaces remote share or NTFS permissions.
    /// </summary>
    public static class ShareAccessModeService
    {
        private static string Backup(string name)
        {
            return Path.Combine(Paths.MachineDir, "share_access_mode_original_" + name + ".txt");
        }
        public static string ReadStatus()
        {
            string ps=@"$ErrorActionPreference='Continue';
              '=== 本机作为访问方（SMB 客户端） ===';
              try {$c=Get-SmbClientConfiguration -ErrorAction Stop;
                '强制签名：'+$c.RequireSecuritySignature;
                '允许未验证来宾：'+$c.EnableInsecureGuestLogons} catch {'读取失败：'+$_.Exception.Message};
              '=== 本机作为共享方（SMB 服务端） ===';
              try {$s=Get-SmbServerConfiguration -ErrorAction Stop;
                '强制签名：'+$s.RequireSecuritySignature;
                '服务器加密要求：'+$s.EncryptData} catch {'读取失败：'+$_.Exception.Message};
              try { $f=(Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Lsa' -Name forceguest -ErrorAction SilentlyContinue).forceguest;
                '本地账户访问模型 ForceGuest：'+$f+'（0=经典账户认证，1=将本地账户网络登录映射为 Guest）';
              }catch{'ForceGuest 未配置或不可读取'};
              try { $g=Get-CimInstance Win32_UserAccount -Filter 'LocalAccount=True' -ErrorAction Stop | Where-Object {$_.SID -match '-501$'} | Select-Object -First 1;
                if($g){'本机 Guest 账户：'+$g.Name+' / Disabled='+$g.Disabled} else {'Guest 账户：未找到'}
              }catch{'Guest 状态读取失败：'+$_.Exception.Message};
              try {$cs=Get-CimInstance Win32_ComputerSystem -ErrorAction Stop; '已加入域：'+$cs.PartOfDomain}catch{};
              '注意：本机策略不等于远程共享实际权限，来宾模式可能仍被组策略、签名、对端账户或 NTFS 权限阻断。';";
            return PowerShellService.RunScript(ps,2);
        }
        public static string ApplyClient(bool guest, bool restore)
        {
            if(!CommandService.IsAdministrator())return "需要以管理员身份运行工具箱。";
            string ps="$ErrorActionPreference='Stop'; $backup="+PowerShellService.Quote(Backup("client"))+"; $restoring="+(restore?"$true":"$false")+"; "+@"
              $before=Get-SmbClientConfiguration -ErrorAction Stop;
              if($null -eq $before.EnableInsecureGuestLogons -or $null -eq $before.RequireSecuritySignature){throw '客户端属性不可读取'};
              if($restoring -and -not(Test-Path -LiteralPath $backup)){throw '未找到初始配置备份，不能恢复'};
              if(-not(Test-Path -LiteralPath $backup)) {
                ([string][bool]$before.EnableInsecureGuestLogons+'|'+[string][bool]$before.RequireSecuritySignature) |
                  Set-Content -LiteralPath $backup -Encoding ASCII -ErrorAction Stop
              };
              ";
            if(restore)ps+=@"
              $parts=((Get-Content -LiteralPath $backup -Raw -ErrorAction Stop).Trim()).Split('|');
              if($parts.Count -ne 2 -or ($parts | Where-Object {$_ -notin @('True','False')})){throw '原值备份格式不正确'};
              $wantGuest=[bool]::Parse($parts[0]);$wantSign=[bool]::Parse($parts[1]);";
            else ps+="$wantGuest="+(guest?"$true":"$false")+"; $wantSign="+(guest?"$false":"$true")+"; ";
            ps+=@"
              Set-SmbClientConfiguration -EnableInsecureGuestLogons $wantGuest -RequireSecuritySignature $wantSign -Confirm:$false -ErrorAction Stop;
              $after=Get-SmbClientConfiguration -ErrorAction Stop;
              if([bool]$after.EnableInsecureGuestLogons -ne $wantGuest -or [bool]$after.RequireSecuritySignature -ne $wantSign){throw '应用后检查失败，策略可能覆盖本机设置'};
              '已验证：客户端允许未验证来宾 = '+$after.EnableInsecureGuestLogons;
              '已验证：客户端强制签名 = '+$after.RequireSecuritySignature;
              '原配置备份：'+$backup;
              '这只更改当前电脑访问其他设备的策略，不能修改对端共享服务器。';";
            return PowerShellService.RunScript(ps,2);
        }
        public static string ApplyServer(bool guest, bool restore)
        {
            if(!CommandService.IsAdministrator())return "需要以管理员身份运行工具箱。";
            string ps="$ErrorActionPreference='Stop'; $backup="+PowerShellService.Quote(Backup("server"))+"; $restoring="+(restore?"$true":"$false")+"; $guestRequested="+(guest?"$true":"$false")+"; "+@"
              $machine=Get-CimInstance Win32_ComputerSystem -ErrorAction Stop;
              if($machine.PartOfDomain){throw '域成员电脑不允许在此切换为 Guest only 模式'};
              $p=Get-NetConnectionProfile -ErrorAction SilentlyContinue;
              if(-not $restoring -and $guestRequested -and $p -and -not ($p | Where-Object {$_.NetworkCategory -eq 'Private'})){throw '没有专用网络，不允许配置 Guest'};
              $before=Get-SmbServerConfiguration -ErrorAction Stop;
              $lsa='HKLM:\SYSTEM\CurrentControlSet\Control\Lsa';
              $old=Get-ItemProperty -Path $lsa -Name forceguest -ErrorAction SilentlyContinue;
              $oldForce=if($null -eq $old){0}else{[int]$old.forceguest};
              if($restoring -and -not(Test-Path -LiteralPath $backup)){throw '未找到初始配置备份，不能恢复'};
              if(-not(Test-Path -LiteralPath $backup)) {
                ([string]$oldForce+'|'+[string][bool]$before.RequireSecuritySignature) | Set-Content -LiteralPath $backup -Encoding ASCII -ErrorAction Stop
              };";
            if(restore)ps+=@"
              $parts=((Get-Content -LiteralPath $backup -Raw -ErrorAction Stop).Trim()).Split('|');
              if($parts.Count -ne 2 -or $parts[0] -notin @('0','1') -or $parts[1] -notin @('True','False')){throw '原值备份格式不正确'};
              $wantForce=[int]$parts[0];$wantSign=[bool]::Parse($parts[1]);";
            else ps+="$wantForce="+(guest?"1":"0")+"; $wantSign="+(guest?"$false":"$true")+"; ";
            ps+=@"
              Set-SmbServerConfiguration -RequireSecuritySignature $wantSign -Confirm:$false -ErrorAction Stop;
              Set-ItemProperty -Path $lsa -Name forceguest -Value $wantForce -Type DWord -ErrorAction Stop;
              $after=Get-SmbServerConfiguration -ErrorAction Stop;
              $force=(Get-ItemProperty -Path $lsa -Name forceguest -ErrorAction Stop).forceguest;
              if([bool]$after.RequireSecuritySignature -ne $wantSign -or [int]$force -ne $wantForce){throw '应用后验证失败'};
              '已验证：本机作为共享方，ForceGuest = '+$force;
              '已验证：本机服务端强制签名 = '+$after.RequireSecuritySignature;
              '原配置备份：'+$backup;
              '说明：Guest only 只影响本机账户的网络登录映射，并不自动启用 Guest 账户或授权任何共享目录。';
              '仍需检查 Guest 是否被禁用、共享权限和 NTFS 文件夹权限；通常需重建 SMB 会话后测试。';";
            return PowerShellService.RunScript(ps,2);
        }
        public static string SetLocalGuestEnabled(bool enabled)
        {
            if(!CommandService.IsAdministrator())return "需要管理员权限。";
            string ps="$ErrorActionPreference='Stop'; $enabled="+(enabled?"$true":"$false")+"; $backup="+PowerShellService.Quote(Backup("guest_account"))+"; "+@"
              if((Get-CimInstance Win32_ComputerSystem).PartOfDomain){throw '域成员电脑不允许在此修改 Guest 账户'};
              if($enabled -and (Get-NetConnectionProfile -ErrorAction SilentlyContinue | Where-Object {$_.NetworkCategory -eq 'Public'})){throw '公用网络不允许直接启用 Guest'};
              if(-not(Get-Command Get-LocalUser -ErrorAction SilentlyContinue)){throw '当前 PowerShell 没有本地账户管理模块，请在系统账户管理中设置'};
              $u=Get-LocalUser -ErrorAction Stop | Where-Object {$_.SID.Value -match '-501$'} | Select-Object -First 1;
              if(-not $u){throw '无法通过 SID -501 找到 Guest 账户'};
              if(-not(Test-Path -LiteralPath $backup)){[string][bool]$u.Enabled | Set-Content -LiteralPath $backup -Encoding ASCII -ErrorAction Stop};
              if($enabled){$u | Enable-LocalUser -ErrorAction Stop} else {$u | Disable-LocalUser -ErrorAction Stop};
              $after=Get-LocalUser -SID $u.SID -ErrorAction Stop;
              if([bool]$after.Enabled -ne $enabled){throw 'Guest 账户启用状态验证失败'};
              '已验证：Guest 本地账户 Enabled = '+$after.Enabled;
              '原状态：'+$backup;
              '启用 Guest 会增加本机共享暴露面；并不单独保证无密码访问。';";
            return PowerShellService.RunScript(ps,2);
        }
        public static string RestoreLocalGuest()
        {
            string backup=Backup("guest_account");
            if(!File.Exists(backup))return "之前没有保存 Guest 账户状态，不能直接恢复。";
            string value=File.ReadAllText(backup).Trim();
            if(value!="True" && value!="False")return "Guest 账户状态备份无效，已停止。";
            return SetLocalGuestEnabled(value=="True");
        }
    }
}
