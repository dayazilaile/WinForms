using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ITDesktopToolkit
{
    // Windows 11 guest share helper. All changes require an explicit UI confirmation.
    // The public share action is deliberately scoped to one existing, non-system folder.
    public static class Win11MutualShareService
    {
        private const string RuleName = "ITToolkit-Guest-Private-TCP445-v043";
        private static string BackupRoot { get { return Path.Combine(Paths.MachineDir,"Win11GuestShareBackups"); } }
        private static string Ps(string command) { return PowerShellService.RunScript(command,3); }
        private static bool Ok(string result) { return result.Contains("返回代码：0"); }
        private static string Q(string text) { return PowerShellService.Quote(text); }
        private static string Header(string title, string result) { return "【"+title+"】\r\n"+result+"\r\n"; }

        private static string ShareBackup(string folder,string share)
        {
            string seed = Path.GetFullPath(folder).ToUpperInvariant()+"|"+share.ToUpperInvariant();
            byte[] hash;
            using (System.Security.Cryptography.SHA256 sha=System.Security.Cryptography.SHA256.Create())
                hash=sha.ComputeHash(Encoding.UTF8.GetBytes(seed));
            string part=BitConverter.ToString(hash).Replace("-","").Substring(0,20);
            return Path.Combine(BackupRoot,"share_"+part+".json");
        }
        public static string ValidateFolder(string folder,string name)
        {
            if(String.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))return "请先选择真实存在的本地共享目录。";
            if(String.IsNullOrWhiteSpace(name) || !Regex.IsMatch(name,@"^[\p{L}\p{N}_ .-]{1,50}$") ||
                name.EndsWith(".") || name.EndsWith("$") || name.Equals("IPC$",StringComparison.OrdinalIgnoreCase))
                return "共享名称无效：长度 1～50，只能使用字母、数字、中文、空格、点、横线和下划线。";
            try
            {
                string full=Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
                if(full.StartsWith(@"\\"))return "不能把网络路径再共享为本机目录，请选择本地磁盘文件夹。";
                string root=Path.GetPathRoot(full).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
                if(String.Equals(full,root,StringComparison.OrdinalIgnoreCase))return "禁止将磁盘根目录直接开放为免账号共享。";
                string[] blocked={Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)};
                foreach(string b in blocked)
                {
                    if(String.IsNullOrWhiteSpace(b))continue;
                    string basePath=Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar);
                    if(String.Equals(full,basePath,StringComparison.OrdinalIgnoreCase) ||
                       full.StartsWith(basePath+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
                        return "禁止直接共享系统目录或当前用户个人目录。建议先建立 D:\\PublicShare 等独立文件夹。";
                }
                DirectoryInfo d=new DirectoryInfo(full);
                while(d!=null)
                {
                    if((d.Attributes & FileAttributes.ReparsePoint)!=0)return "共享路径包含符号链接/目录联接点，出于安全考虑不自动授予访问权限。";
                    d=d.Parent;
                }
            }
            catch(Exception ex){return "共享路径检查失败："+ex.Message;}
            return "";
        }
        private static string CommonGuard
        {
            get { return @"
                $ErrorActionPreference='Stop';
                $machine=Get-CimInstance Win32_ComputerSystem -ErrorAction Stop;
                if($machine.PartOfDomain){throw '已加入域的电脑不能使用此来宾兼容向导。请联系域管理员。'};
                $profiles=@(Get-NetConnectionProfile -ErrorAction Stop | Where-Object {$_.NetworkCategory -eq 'Private'});
                if($profiles.Count -lt 1){throw '没有专用网络配置文件：请先确认局域网确实可信，再将对应网络切换为专用。'};
            "; }
        }
        // Read-only preflight, with a plain-text failure reason (no CLIXML error dump).
        // This is intentionally separate from modifying SMB or firewall settings.
        public static string PreflightReport()
        {
            string script=@"
                $ErrorActionPreference='Stop';
                try {
                    '=== Windows 11 SMB 互访预检（只读） ===';
                    $machine=Get-CimInstance -ClassName Win32_ComputerSystem -ErrorAction Stop;
                    '计算机='+$env:COMPUTERNAME;
                    '是否加入域='+[string]$machine.PartOfDomain;
                    if($machine.PartOfDomain){throw '此设备已加入域，不自动修改域内共享安全策略。'};
                    $profiles=@(Get-NetConnectionProfile -ErrorAction Stop);
                    if($profiles.Count -eq 0){throw '没有读取到网络配置文件。请检查 NLA/网络服务与网卡驱动。'};
                    '=== 当前网络配置文件 ===';
                    foreach($p in $profiles){
                        '网卡='+[string]$p.InterfaceAlias+'；类别='+[string]$p.NetworkCategory+'；IPv4='+[string]$p.IPv4Connectivity;
                    };
                    $private=@($profiles|Where-Object {$_.NetworkCategory -eq 'Private'});
                    if($private.Count -eq 0){throw '没有专用(Private)网络。请确认用于互访的局域网可信，再仅把该网卡的网络类别改为专用，或停止使用来宾免密模式。'};
                    '预检通过：此电脑未加入域，检测到至少一个专用网络。';
                    '提醒：如果实际连接对端的网卡仍为公用(Public)，请先调整对应网卡，不能只依赖其他虚拟网卡为专用。';
                } catch {
                    '[预检失败] '+$_.Exception.Message;
                    '未修改 SMB、账户或防火墙。';
                    exit 1;
                }";
            return Ps(script);
        }
        public static string ConfigureThisPc()
        {
            if(!CommandService.IsAdministrator())return "需要以管理员身份运行工具箱。";
            string r=PreflightReport();
            if(!Ok(r))return "预检未通过，未修改 SMB：\r\n"+r;
            StringBuilder sb=new StringBuilder();
            r=ShareAccessModeService.ApplyClient(true,false);sb.Append(Header("客户端允许来宾",r));if(!Ok(r))return sb.ToString()+"中止：客户端设置失败。请检查策略。";
            r=ShareAccessModeService.ApplyServer(true,false);sb.Append(Header("服务端来宾兼容",r));if(!Ok(r))return sb.ToString()+"中止：服务端设置失败。此前的操作可以使用恢复按钮撤销。";
            r=ShareAccessModeService.SetLocalGuestEnabled(true);sb.Append(Header("本机 Guest 账户",r));if(!Ok(r))return sb.ToString()+"中止：Guest 账户不可用。请检查系统账户限制。";
            string ps="$rule="+Q(RuleName)+"; try {"+CommonGuard+@"
                 $existing=Get-NetFirewallRule -Name $rule -ErrorAction SilentlyContinue;
                 if($existing){
                   if($existing.Description -ne 'ITToolkit Win11 Mutual Guest Share'){throw '规则名被其他应用占用，未修改防火墙'};
                   Set-NetFirewallRule -Name $rule -Enabled True -Profile Private -ErrorAction Stop;
                 } else {
                   New-NetFirewallRule -Name $rule -DisplayName 'IT工具箱：专用网络 SMB 445 入站' -Description 'ITToolkit Win11 Mutual Guest Share' -Enabled True -Direction Inbound -Action Allow -Protocol TCP -LocalPort 445 -RemoteAddress LocalSubnet -Profile Private -ErrorAction Stop | Out-Null;
                 };
                 '已设置：只允许专用网络，同一子网来源访问本机 TCP 445；不开放公用网络。';
               } catch { Write-Output ('[操作失败] '+$_.Exception.Message); exit 1 }";
            r=Ps(ps);sb.Append(Header("限制在专用网络的防火墙规则",r));
            if(!Ok(r))return sb.ToString()+"SMB 设置已更改，但防火墙阶段失败。请检查状态并按需恢复。";
            sb.Append("\r\n已尝试准备本机访问方与共享方。还必须在“选定共享文件夹”步骤设置共享及 NTFS 权限，并在另一台 Win11 上重复配置。\r\n");
            sb.Append("注意：若组策略禁止 Guest 网络登录、SMB 加密被强制或曾缓存错误凭据，仍可能被拒绝。不会绕过这些限制。\r\n");
            return sb.ToString()+"\r\n"+ReadStatus("","","");
        }
        public static string RestoreThisPc()
        {
            if(!CommandService.IsAdministrator())return "需要管理员权限。";
            StringBuilder sb=new StringBuilder();
            string r=ShareAccessModeService.ApplyClient(false,true);sb.Append(Header("恢复 SMB 客户端原值",r));
            r=ShareAccessModeService.ApplyServer(false,true);sb.Append(Header("恢复 SMB 服务端原值",r));
            r=ShareAccessModeService.RestoreLocalGuest();sb.Append(Header("恢复 Guest 账户原值",r));
            string script="$rule="+Q(RuleName)+@"; try{
                $existing=Get-NetFirewallRule -Name $rule -ErrorAction SilentlyContinue;
                if($existing -and $existing.Description -eq 'ITToolkit Win11 Mutual Guest Share'){
                    Remove-NetFirewallRule -Name $rule -ErrorAction Stop;
                    '已移除工具箱专用 TCP445 防火墙规则。';
                }else{'没有由本工具创建的专用规则，无需移除。'}
              } catch { Write-Output ('[操作失败] '+$_.Exception.Message); exit 1 }";
            r=Ps(script);sb.Append(Header("恢复防火墙规则",r));
            sb.Append("恢复共享文件夹的 NTFS/共享权限，需要另按“恢复此文件夹权限”单独执行。\r\n");
            return sb.ToString();
        }
        public static string PreparePublicFolder(string folder,string name)
        {
            string error=ValidateFolder(folder,name);if(error.Length>0)return error;
            if(!CommandService.IsAdministrator())return "需要管理员权限。";
            Directory.CreateDirectory(BackupRoot);
            string full=Path.GetFullPath(folder);
            string backup=ShareBackup(full,name);
            string ps="$folder="+Q(full)+"; $name="+Q(name)+"; $backup="+Q(backup)+"; try {"+CommonGuard+@"
                $everyone=([Security.Principal.SecurityIdentifier]'S-1-1-0').Translate([Security.Principal.NTAccount]).Value;
                $old=Get-SmbShare -Name $name -ErrorAction SilentlyContinue;
                if($old -and -not ([string]::Equals([IO.Path]::GetFullPath($old.Path).TrimEnd('\'),[IO.Path]::GetFullPath($folder).TrimEnd('\'),[StringComparison]::OrdinalIgnoreCase)))
                    {throw '共享名已存在，但指向了不同目录。未修改。'};
                $acl=Get-Acl -LiteralPath $folder -ErrorAction Stop;
                $existingAccess=@();
                if($old){$existingAccess=@(Get-SmbShareAccess -Name $name -ErrorAction Stop | Where-Object {$_.AccountName -eq $everyone})};
                if(@($existingAccess|Where-Object {$_.AccessControlType -eq 'Deny'}).Count -gt 0){throw '共享已有 Everyone 拒绝规则，不自动覆盖。'};
                if(-not(Test-Path -LiteralPath $backup)){
                    $originalRight=if($existingAccess.Count -gt 0){[string]$existingAccess[0].AccessRight}else{''};
                    $save=@{Folder=$folder;Name=$name;Sddl=$acl.Sddl;ShareExisted=($null -ne $old);
                        EveryoneRight=$originalRight;Timestamp=[string](Get-Date)};
                    $save | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $backup -Encoding UTF8 -ErrorAction Stop;
                }else{
                    $save=Get-Content -LiteralPath $backup -Raw -ErrorAction Stop|ConvertFrom-Json;
                    if($save.Folder -ne $folder -or $save.Name -ne $name){throw '已有备份归属不一致，已停止。'};
                };
                if(-not $old){New-SmbShare -Name $name -Path $folder -ReadAccess $everyone -ErrorAction Stop | Out-Null}
                elseif($existingAccess.Count -eq 0){Grant-SmbShareAccess -Name $name -AccountName $everyone -AccessRight Read -Force -ErrorAction Stop|Out-Null};
                $rights=[Security.AccessControl.FileSystemRights]::ReadAndExecute;
                $inherit=[Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit';
                $propagate=[Security.AccessControl.PropagationFlags]::None;
                $allow=[Security.AccessControl.AccessControlType]::Allow;
                $sid=[Security.Principal.SecurityIdentifier]'S-1-1-0';
                $rule=[Security.AccessControl.FileSystemAccessRule]::new($sid,$rights,$inherit,$propagate,$allow);
                $acl.AddAccessRule($rule);
                Set-Acl -LiteralPath $folder -AclObject $acl -ErrorAction Stop;
                '【共享访问权限】';
                Get-SmbShareAccess -Name $name -ErrorAction Stop | Format-Table AccountName,AccessControlType,AccessRight -AutoSize | Out-String;
                '【文件夹 NTFS 权限】';
                (Get-Acl -LiteralPath $folder -ErrorAction Stop).Access | Select-Object IdentityReference,FileSystemRights,AccessControlType,IsInherited | Format-Table -AutoSize | Out-String;
                '备份：'+$backup;
                '共享地址：\\'+$env:COMPUTERNAME+'\'+$name;
                '本步骤仅给这个目录添加 Everyone 读取权限。已存在的拒绝 ACL、父目录限制、客户端与服务端来宾策略仍可能阻断。';
              } catch { Write-Output ('[操作失败] '+$_.Exception.Message); exit 1 }";
            return Ps(ps);
        }
        public static string RestorePublicFolder(string folder,string name)
        {
            string error=ValidateFolder(folder,name);if(error.Length>0)return error;
            if(!CommandService.IsAdministrator())return "需要管理员权限。";
            string full=Path.GetFullPath(folder), backup=ShareBackup(full,name);
            if(!File.Exists(backup))return "没有找到该文件夹首次修改前的权限备份。未执行恢复。";
            string ps="$folder="+Q(full)+"; $name="+Q(name)+"; $backup="+Q(backup)+"; try {"+@"
                $old=Get-Content -LiteralPath $backup -Raw -ErrorAction Stop | ConvertFrom-Json;
                if($old.Folder -ne $folder -or $old.Name -ne $name){throw '备份记录的路径/共享名称不匹配'};
                $share=Get-SmbShare -Name $name -ErrorAction SilentlyContinue;
                if($share -and -not ([string]::Equals([IO.Path]::GetFullPath($share.Path).TrimEnd('\'),[IO.Path]::GetFullPath($folder).TrimEnd('\'),[StringComparison]::OrdinalIgnoreCase)))
                    {throw '现在的共享名指向不同目录，不能自动恢复'};
                if(-not $old.ShareExisted){if($share){Remove-SmbShare -Name $name -Force -ErrorAction Stop}}
                elseif($share){
                    $everyone=([Security.Principal.SecurityIdentifier]'S-1-1-0').Translate([Security.Principal.NTAccount]).Value;
                    $current=@(Get-SmbShareAccess -Name $name -ErrorAction Stop|Where-Object{$_.AccountName -eq $everyone});
                    if($current.Count -gt 0){Revoke-SmbShareAccess -Name $name -AccountName $everyone -Force -ErrorAction Stop|Out-Null};
                    if($old.EveryoneRight -in @('Read','Change','Full')){
                        Grant-SmbShareAccess -Name $name -AccountName $everyone -AccessRight $old.EveryoneRight -Force -ErrorAction Stop|Out-Null};
                };
                $acl=Get-Acl -LiteralPath $folder -ErrorAction Stop;
                $acl.SetSecurityDescriptorSddlForm([string]$old.Sddl);
                Set-Acl -LiteralPath $folder -AclObject $acl -ErrorAction Stop;
                '已恢复首次备份的 NTFS ACL，以及工具箱修改前的共享 Everyone 权限（或删除本工具新建的共享）。';
                '注意：恢复 ACL 会覆盖备份后其他人对该文件夹权限做出的改动。';
              } catch { Write-Output ('[操作失败] '+$_.Exception.Message); exit 1 }";
            return Ps(ps);
        }
        // Only disconnect existing SMB mappings to the chosen host for the current logon context.
        // This does NOT delete Windows Credential Manager entries or affect other hosts.
        public static string DisconnectPeer(string peer)
        {
            if(!Regex.IsMatch(peer??"",@"^[A-Za-z0-9_.-]{1,253}$"))return "对端主机名/IP 不合法。";
            string script="$hostName="+Q(peer)+@"; try {
                $ErrorActionPreference='Stop';
                $prefix='\\'+$hostName+'\';
                $maps=@(Get-SmbMapping -ErrorAction SilentlyContinue | Where-Object {
                    $_.RemotePath -and $_.RemotePath.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)
                });
                foreach($m in $maps){
                    Remove-SmbMapping -RemotePath $m.RemotePath -Force -UpdateProfile:$false -ErrorAction Stop;
                    '已断开：'+$m.RemotePath
                };
                if($maps.Count -eq 0){'当前登录会话没有找到指向指定主机的 SMB 映射。'};
                '提示：Windows 凭据管理器中的保存凭据不会被自动删除；提权进程与普通资源管理器会话可能不同。';
              } catch { Write-Output ('[操作失败] '+$_.Exception.Message); exit 1 }";
            return Ps(script);
        }
        public static string ReadStatus(string folder,string name,string peer)
        {
            string script="$folder="+Q(folder??"")+"; $name="+Q(name??"")+"; $peer="+Q(peer??"")+"; "+@"
                $ErrorActionPreference='Continue';
                '=== 本机 SMB 兼容状态 ===';
                $c=Get-SmbClientConfiguration -ErrorAction SilentlyContinue;
                $s=Get-SmbServerConfiguration -ErrorAction SilentlyContinue;
                if($c){'客户端允许 Guest='+$c.EnableInsecureGuestLogons+' / 强制签名='+$c.RequireSecuritySignature};
                if($s){'服务端强制签名='+$s.RequireSecuritySignature+' / 强制加密='+$s.EncryptData};
                $force=(Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Lsa' -Name forceguest -ErrorAction SilentlyContinue).forceguest;
                '网络登录映射 ForceGuest='+$force;
                $guest=Get-LocalUser -ErrorAction SilentlyContinue | Where-Object {$_.SID.Value -match '-501$'} | Select-Object -First 1;
                if($guest){'本机 Guest 账户='+$guest.Name+' / Enabled='+$guest.Enabled};
                Get-NetConnectionProfile -ErrorAction SilentlyContinue | ForEach-Object {'网卡='+$_.InterfaceAlias+' / 网络类别='+$_.NetworkCategory};
                $rule=Get-NetFirewallRule -Name 'ITToolkit-Guest-Private-TCP445-v043' -ErrorAction SilentlyContinue;
                if($rule){'专用网络 445 规则='+$rule.Enabled+' / '+$rule.Profile}else{'工具箱专用445规则不存在'};
                if($folder -and $name){
                  '=== 选定共享的权限 ===';
                  $share=Get-SmbShare -Name $name -ErrorAction SilentlyContinue;
                  if($share){
                    '共享目录='+$share.Path;
                    Get-SmbShareAccess -Name $name -ErrorAction SilentlyContinue | Format-Table AccountName,AccessControlType,AccessRight -AutoSize | Out-String;
                  }else{'未找到指定共享'};
                  if(Test-Path -LiteralPath $folder){
                    (Get-Acl -LiteralPath $folder -ErrorAction SilentlyContinue).Access | Select-Object IdentityReference,FileSystemRights,AccessControlType,IsInherited | Format-Table -AutoSize | Out-String;
                  }
                };
                if($peer -match '^[a-zA-Z0-9_.-]+$'){
                  '=== 到对端 TCP445 检测 ===';
                  try {$ok=Test-NetConnection -ComputerName $peer -Port 445 -InformationLevel Quiet -WarningAction SilentlyContinue -ErrorAction Stop;
                    '对端 '+$peer+' TCP445：'+$ok} catch {'445 检测失败：'+$_.Exception.Message}
                };
                '=== 最近 SMB 客户端安全事件（若有） ===';
                try {Get-WinEvent -FilterHashtable @{LogName='Microsoft-Windows-SMBClient/Security';Id=31017,31018,31022;StartTime=(Get-Date).AddHours(-8)} -MaxEvents 5 -ErrorAction Stop |
                     Select-Object TimeCreated,Id,Message | Format-List | Out-String}catch{'没有可读取的相关事件'};
                '提示：双方都要配置；访问时使用 \\对方IP\共享名。若仍弹密码框，请检查凭据缓存、组策略禁止来宾登录和已存在的 SMB 会话。';";
            return Ps(script);
        }
    }
}
