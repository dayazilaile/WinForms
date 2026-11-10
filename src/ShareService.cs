using System;
using System.IO;
using System.Text.RegularExpressions;

namespace ITDesktopToolkit
{
    public static class ShareService
    {
        public static string ReadShares()
        {
            return CommandService.Run("net.exe", "share", 2) + "\r\n【网络映射】\r\n" + CommandService.Run("net.exe", "use", 2) +
                "\r\n提示：共享权限与 NTFS 权限共同决定最终访问；请勿随意变更管理共享。";
        }
        public static string Create(string folder,string name,bool readonlyEveryone)
        {
            if(!CommandService.IsAdministrator())return "需要管理员权限。";
            if(String.IsNullOrWhiteSpace(folder)||!Directory.Exists(folder))return "文件夹路径不存在。";
            if(String.IsNullOrWhiteSpace(name)||!Regex.IsMatch(name,@"^[\p{L}\p{N}_ .-]{1,60}$") || name.EndsWith("."))
                return "共享名不合法：允许字母、数字、中文、空格、下划线、点和短横线，长度不超过60。";
            if(!readonlyEveryone)return "当前便捷模式仅建立只读共享。";
            // Resolves Everyone by well-known SID rather than assuming English localization.
            string script=@"$ErrorActionPreference='Stop'; " +
                "$folder="+PowerShellService.Quote(Path.GetFullPath(folder))+"; "+
                "$name="+PowerShellService.Quote(name)+"; "+
                "if(Get-SmbShare -Name $name -ErrorAction SilentlyContinue){throw '共享名称已经存在'}; "+
                "$everyone=(New-Object System.Security.Principal.SecurityIdentifier('S-1-1-0')).Translate([System.Security.Principal.NTAccount]).Value; "+
                "New-SmbShare -Name $name -Path $folder -ReadAccess $everyone -ErrorAction Stop | Out-Null; "+
                "Get-SmbShare -Name $name | Select-Object Name,Path,Description | Format-List | Out-String; "+
                "Get-SmbShareAccess -Name $name | Select-Object AccountName,AccessControlType,AccessRight | Format-Table | Out-String";
            return "【创建只读共享】\r\n"+PowerShellService.RunScript(script,2)+
                "\r\n提示：本工具只设置共享层权限，不更改 NTFS ACL；目标账户还需具备文件系统读取权限。";
        }
        public static string Remove(string name)
        {
            if(!CommandService.IsAdministrator())return "需要管理员权限。";
            if(String.IsNullOrWhiteSpace(name)||!Regex.IsMatch(name,@"^[\p{L}\p{N}_ .-]{1,60}$"))return "共享名不合法。";
            if(name.EndsWith("$",StringComparison.OrdinalIgnoreCase)||name.Equals("ADMIN$",StringComparison.OrdinalIgnoreCase)||name.Equals("IPC$",StringComparison.OrdinalIgnoreCase))
                return "工具箱不允许删除管理共享。";
            return PowerShellService.RunScript("$ErrorActionPreference='Stop'; Remove-SmbShare -Name "+PowerShellService.Quote(name)+" -Force -ErrorAction Stop; '共享已移除（原文件夹仍保留）'",2);
        }
        public static string Map(string drive,string unc)
        {
            if(!Regex.IsMatch(drive??"",@"^[D-Zd-z]:$"))return "请选择 D: 至 Z: 的盘符。";
            if(String.IsNullOrWhiteSpace(unc)||!unc.StartsWith(@"\\")||unc.Split(new char[]{'\\'},StringSplitOptions.RemoveEmptyEntries).Length!=2||unc.Contains("\""))return @"共享路径应为 \\电脑名\共享名。";
            return CommandService.Run("net.exe","use "+drive.ToUpperInvariant()+" \""+unc+"\" /persistent:no",2);
        }

        public sealed class ShareEntry
        {
            public string Name, Folder;
            public override string ToString() { return Name + "   (" + Folder + ")"; }
        }

        // List only ordinary shares. Administrative $ shares must never be edited here.
        public static System.Collections.Generic.List<ShareEntry> ListOrdinaryShares(out string report)
        {
            string script = @"$ErrorActionPreference='Stop'; Get-SmbShare -ErrorAction Stop | Where-Object { -not $_.Special -and -not $_.Name.EndsWith('$') } | ForEach-Object {
              $name=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes([string]$_.Name));
              $path=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes([string]$_.Path));
              Write-Output ('SHARE|'+$name+'|'+$path)
            }";
            report=PowerShellService.RunScript(script,2);
            System.Collections.Generic.List<ShareEntry> result = new System.Collections.Generic.List<ShareEntry>();
            foreach(string line in report.Split(new char[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries))
            {
                if(!line.StartsWith("SHARE|",StringComparison.Ordinal))continue;
                string[] parts=line.Split('|');
                if(parts.Length!=3)continue;
                try { result.Add(new ShareEntry { Name=System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(parts[1])), Folder=System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(parts[2])) }); }catch{}
            }
            return result;
        }
        public sealed class ShareAccountEntry
        {
            public string Name, Rights;
            public override string ToString() { return Name + "（" + Rights + "）"; }
        }
        public static System.Collections.Generic.List<ShareAccountEntry> ListShareAccounts(string name)
        {
            CheckSharedName(name);
            string script="$ErrorActionPreference='Stop'; Get-SmbShareAccess -Name " + PowerShellService.Quote(name) +
                " -ErrorAction Stop | Where-Object {$_.AccessControlType -eq 'Allow'} | ForEach-Object {" +
                "$id=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes([string]$_.AccountName));" +
                "'PRINCIPAL|'+$id+'|'+[string]$_.AccessRight }";
            string raw=PowerShellService.RunScript(script,2);
            System.Collections.Generic.List<ShareAccountEntry> result=new System.Collections.Generic.List<ShareAccountEntry>();
            foreach(string line in raw.Split(new char[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries)){
                if(!line.StartsWith("PRINCIPAL|",StringComparison.Ordinal))continue;
                string[] parts=line.Split('|');if(parts.Length!=3)continue;
                try{result.Add(new ShareAccountEntry{Name=System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(parts[1])),Rights=parts[2]});}catch{}
            }
            return result;
        }
        private static void CheckSharedName(string name)
        {
            if(String.IsNullOrWhiteSpace(name)|| !Regex.IsMatch(name,@"^[\p{L}\p{N}_ .-]{1,60}$") || name.EndsWith(".") || name.EndsWith("$"))
                throw new ArgumentException("只允许操作普通共享，不支持管理共享或非法共享名。");
        }
        private static string Account(string account)
        {
            if(String.IsNullOrWhiteSpace(account)) throw new ArgumentException("请选择 Everyone 或填写具体用户/用户组。");
            if(account.Equals("Everyone",StringComparison.OrdinalIgnoreCase) || account.Equals("所有人",StringComparison.OrdinalIgnoreCase))
                return "S-1-1-0";
            if(account.Length>180 || !Regex.IsMatch(account,@"^[\p{L}\p{N}_ .\\@-]{1,180}$"))
                throw new ArgumentException("用户或组名称含不支持的字符。");
            return account.Trim();
        }
        public static string ReadPermissions(string name)
        {
            CheckSharedName(name);
            string quoted=PowerShellService.Quote(name);
            string script = "$ErrorActionPreference='Stop'; $share=Get-SmbShare -Name " + quoted + " -ErrorAction Stop; " +
                "'【共享属性】'; $share | Select-Object Name,Path,Description,EncryptData | Format-List | Out-String; " +
                "'【SMB 共享访问权限】'; Get-SmbShareAccess -Name " + quoted + " -ErrorAction Stop | Format-Table AccountName,AccessControlType,AccessRight -AutoSize | Out-String; " +
                "'【NTFS 权限（只读检查）】'; if(Test-Path -LiteralPath $share.Path){ " +
                "(Get-Acl -LiteralPath $share.Path).Access | Select-Object IdentityReference,FileSystemRights,AccessControlType,IsInherited | Format-Table -AutoSize | Out-String }else{'共享目录当前不存在'}";
            return PowerShellService.RunScript(script,2);
        }
        public static string ChangePermissions(string name,string principal,string permission)
        {
            CheckSharedName(name);
            if(!CommandService.IsAdministrator())return "请以管理员权限运行工具箱。";
            if(permission!="Read"&&permission!="Change"&&permission!="Full")throw new ArgumentException("无效共享权限。");
            string account=Account(principal), quoted=PowerShellService.Quote(name);
            string script="$ErrorActionPreference='Stop'; $name="+quoted+"; $account="+PowerShellService.Quote(account)+"; "+
                "if($account -eq 'S-1-1-0'){$account=(New-Object Security.Principal.SecurityIdentifier('S-1-1-0')).Translate([Security.Principal.NTAccount]).Value}; "+
                "$share=Get-SmbShare -Name $name -ErrorAction Stop; if($share.Special -or $share.Name.EndsWith('$')){throw '不能更改管理共享'}; "+
                "$before=@(Get-SmbShareAccess -Name $name -ErrorAction Stop | Where-Object {$_.AccountName -eq $account}); "+
                "if(@($before|Where-Object {$_.AccessControlType -eq 'Deny'}).Count -gt 0){throw '当前主体存在拒绝条目，请到高级共享管理核对，不自动覆盖'}; "+
                "if(@($before).Count -gt 0){Revoke-SmbShareAccess -Name $name -AccountName $account -Force -ErrorAction Stop | Out-Null}; "+
                "try { Grant-SmbShareAccess -Name $name -AccountName $account -AccessRight "+permission+" -Force -ErrorAction Stop | Out-Null }"+
                "catch { foreach($old in $before){if($old.AccessControlType -eq 'Allow'){Grant-SmbShareAccess -Name $name -AccountName $account -AccessRight $old.AccessRight -Force -ErrorAction SilentlyContinue | Out-Null}}; throw }; "+
                "$after=@(Get-SmbShareAccess -Name $name -ErrorAction Stop|Where-Object {$_.AccountName -eq $account -and $_.AccessControlType -eq 'Allow'}); "+
                "if(@($after|Where-Object {$_.AccessRight -eq '"+permission+"'}).Count -eq 0){throw '验证失败：权限未达到目标值'}; "+
                "'已确认目标 SMB 共享权限：'; $after | Format-Table AccountName,AccessRight -AutoSize | Out-String; "+
                "'注意：NTFS 文件夹权限未修改；最终访问仍同时受 NTFS 权限限制。'";
            return PowerShellService.RunScript(script,2);
        }
        public static string RevokePermissions(string name,string principal)
        {
            CheckSharedName(name);
            if(!CommandService.IsAdministrator())return "请以管理员权限运行工具箱。";
            string account=Account(principal);
            string script="$ErrorActionPreference='Stop'; $name="+PowerShellService.Quote(name)+"; $account="+PowerShellService.Quote(account)+"; "+
                "if($account -eq 'S-1-1-0'){$account=(New-Object Security.Principal.SecurityIdentifier('S-1-1-0')).Translate([Security.Principal.NTAccount]).Value}; "+
                "$share=Get-SmbShare -Name $name -ErrorAction Stop; if($share.Special -or $share.Name.EndsWith('$')){throw '不能修改管理共享'}; "+
                "Revoke-SmbShareAccess -Name $name -AccountName $account -Force -ErrorAction Stop | Out-Null; "+
                "Get-SmbShareAccess -Name $name | Format-Table AccountName,AccessRight -AutoSize | Out-String";
            return PowerShellService.RunScript(script,2);
        }
        // This SMB server property is NOT a reliable substitute for the distinct
        // Windows UI password-protected-sharing switch on every Windows build.
        public static string ShareSecurityState()
        {
            string script="$ErrorActionPreference='Continue'; $s=Get-SmbServerConfiguration -ErrorAction SilentlyContinue; "+
                "if($s){'SERVERAUTH|'+[string]$s.EnableAuthenticateUserSharing; 'SERVERSIGN|'+[string]$s.RequireSecuritySignature}else{'SERVERAUTH|Unavailable'}; "+
                "$c=Get-SmbClientConfiguration -ErrorAction SilentlyContinue; if($c){'CLIENTGUEST|'+[string]$c.EnableInsecureGuestLogons;'CLIENTSIGN|'+[string]$c.RequireSecuritySignature}else{'CLIENTGUEST|Unavailable'}; "+
                "'重要：SMB 已认证共享属性不等于 Windows 密码保护共享开关。Everyone 也不等于匿名。'";
            return PowerShellService.RunScript(script,2);
        }
        public static string SetAuthenticatedUserSharing(bool enabled)
        {
            if(!CommandService.IsAdministrator())return "需要管理员权限。";
            string value=enabled?"$true":"$false";
            string script="$ErrorActionPreference='Stop'; "+
                "Set-SmbServerConfiguration -EnableAuthenticateUserSharing "+value+" -Confirm:$false -ErrorAction Stop; "+
                "$v=(Get-SmbServerConfiguration -ErrorAction Stop).EnableAuthenticateUserSharing; "+
                "if($v -ne "+value+"){throw 'SMB 认证共享状态验证失败'}; "+
                "'已读取确认 EnableAuthenticateUserSharing='+$v; "+
                "'提示：这不是 Windows 密码保护共享 UI 的完整替代；不会绕过来宾登录与 SMB 签名策略。'";
            return PowerShellService.RunScript(script,2);
        }
    }
}
