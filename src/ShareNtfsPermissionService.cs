using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ITDesktopToolkit
{
    public static class ShareNtfsPermissionService
    {
        private static string BackupPath(string name,string folder)
        {
            byte[] hash;
            using(SHA256 sha=SHA256.Create()) hash=sha.ComputeHash(Encoding.UTF8.GetBytes((name+"|"+folder).ToUpperInvariant()));
            return Path.Combine(Paths.MachineDir,"ntfs_share_original_"+BitConverter.ToString(hash).Replace("-","").Substring(0,24)+".txt");
        }
        private static string Validate(string name,string folder)
        {
            if(String.IsNullOrWhiteSpace(name)||String.IsNullOrWhiteSpace(folder)||!Directory.Exists(folder))throw new ArgumentException("请选择真实存在的普通共享文件夹。");
            // Reuse conservative directory checks from previous shared-folder wizard.
            string issue=Win11MutualShareService.ValidateFolder(folder,"ShareCheck");
            if(issue.Length>0)throw new ArgumentException(issue);
            return Path.GetFullPath(folder);
        }
        public static string Apply(string name,string folder,string principal,string level)
        {
            if(!CommandService.IsAdministrator())return "需要管理员权限。";
            string path=Validate(name,folder);
            if(String.IsNullOrWhiteSpace(principal)||principal.Length>180||principal.IndexOfAny(new char[]{'\r','\n','\0'})>=0)throw new ArgumentException("账号或组名称无效。 ");
            if(level!="ReadAndExecute" && level!="Modify" && level!="FullControl")throw new ArgumentException("NTFS 权限级别无效。 ");
            string backup=BackupPath(name,path);
            string script="$ErrorActionPreference='Stop'; $folder="+PowerShellService.Quote(path)+"; $who="+PowerShellService.Quote(principal)+
                "; $level="+PowerShellService.Quote(level)+"; $backup="+PowerShellService.Quote(backup)+"; try {"+@"
                $original=Get-Acl -LiteralPath $folder -ErrorAction Stop;
                if(-not(Test-Path -LiteralPath $backup)){
                    [IO.File]::WriteAllText($backup,$original.Sddl,[Text.Encoding]::UTF8)
                };
                $sid=if($who -eq 'Everyone'){
                    [Security.Principal.SecurityIdentifier]'S-1-1-0'
                }else{([Security.Principal.NTAccount]::new($who)).Translate([Security.Principal.SecurityIdentifier])};
                $rights=[Security.AccessControl.FileSystemRights]$level;
                $inherit=[Security.AccessControl.InheritanceFlags]'ContainerInherit,ObjectInherit';
                $propagate=[Security.AccessControl.PropagationFlags]::None;
                $rule=[Security.AccessControl.FileSystemAccessRule]::new($sid,$rights,$inherit,$propagate,[Security.AccessControl.AccessControlType]::Allow);
                $original.AddAccessRule($rule);
                Set-Acl -LiteralPath $folder -AclObject $original -ErrorAction Stop;
                $after=Get-Acl -LiteralPath $folder -ErrorAction Stop;
                if(-not(@($after.Access | Where-Object {try{$_.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value -eq $sid.Value}catch{$false}}).Count -gt 0)){
                    '更新成功返回，但账户 ACL 项未能通过基础状态检查，请手动核对。';exit 1
                };
                '已更新 NTFS 权限：'+$folder+'；账户/组='+$who+'；权限='+$level;
                '原始 NTFS ACL 备份：'+$backup;
                '提醒：此操作只修改 NTFS 层，不等同于 SMB 共享权限。'
             } catch {Write-Output ('[失败] '+$_.Exception.Message);exit 1}";
            return PowerShellService.RunScript(script,2);
        }
        public static string Restore(string name,string folder)
        {
            if(!CommandService.IsAdministrator())return "需要管理员权限。";
            string path=Validate(name,folder), backup=BackupPath(name,path);
            if(!File.Exists(backup))return "未找到工具箱首次修改该文件夹的 NTFS 备份，未修改权限。";
            string script="$ErrorActionPreference='Stop'; $folder="+PowerShellService.Quote(path)+"; $backup="+PowerShellService.Quote(backup)+";try {"+@"
                $sddl=[IO.File]::ReadAllText($backup,[Text.Encoding]::UTF8);
                $acl=Get-Acl -LiteralPath $folder -ErrorAction Stop;
                $acl.SetSecurityDescriptorSddlForm($sddl);
                Set-Acl -LiteralPath $folder -AclObject $acl -ErrorAction Stop;
                if((Get-Acl -LiteralPath $folder -ErrorAction Stop).Sddl -ne $sddl){throw 'NTFS ACL 恢复后与备份不一致，请人工核对'};
                'NTFS ACL 已恢复：'+$folder
            }catch {Write-Output ('[失败] '+$_.Exception.Message);exit 1}";
            return PowerShellService.RunScript(script,2);
        }
    }
}
