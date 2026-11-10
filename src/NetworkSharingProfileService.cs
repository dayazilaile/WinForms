using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ITDesktopToolkit
{
    public static class NetworkSharingProfileService
    {
        public sealed class Profile
        {
            public int Index;
            public string Name="", Category="", Interface="";
            public override string ToString(){return Interface+" ["+Index+"] · "+Category+" ("+Name+")";}
        }
        public static List<Profile> Enumerate(out string note)
        {
            string script=@"$ErrorActionPreference='Stop';Get-NetConnectionProfile -ErrorAction Stop | ForEach-Object {
                $name=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes([string]$_.Name));
                $nic=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes([string]$_.InterfaceAlias));
                'PROFILE|'+$_.InterfaceIndex+'|'+[string]$_.NetworkCategory+'|'+$nic+'|'+$name
            }";
            string raw=PowerShellService.RunScript(script,2);
            List<Profile> profiles=new List<Profile>();
            foreach(string line in raw.Split(new char[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries)){
                if(!line.StartsWith("PROFILE|"))continue;
                string[] p=line.Split('|');int i;
                if(p.Length!=5||!Int32.TryParse(p[1],out i))continue;
                try{profiles.Add(new Profile{Index=i,Category=p[2],Interface=Encoding.UTF8.GetString(Convert.FromBase64String(p[3])),
                    Name=Encoding.UTF8.GetString(Convert.FromBase64String(p[4]))});}catch{}
            }
            note=profiles.Count==0?raw:"已读取 "+profiles.Count+" 个网络配置文件。域认证网络不能通过本工具直接变更。";
            return profiles;
        }
        public static string SetProfile(Profile profile,string value,bool restore)
        {
            if(!CommandService.IsAdministrator())return "需要管理员权限。";
            if(profile==null)return "未选择网络。";
            if(profile.Category=="DomainAuthenticated")return "域认证网络不允许在此切换网络类别。";
            if(value!="Private" && value!="Public")return "只能设置专用/公用网络。";
            string backup=Path.Combine(Paths.UserDir,"network_profile_"+profile.Index+".txt");
            string ps="$ErrorActionPreference='Stop'; $idx="+profile.Index+"; $path="+PowerShellService.Quote(backup)+"; "+
                "$now=Get-NetConnectionProfile -InterfaceIndex $idx -ErrorAction Stop; "+
                "if($now.NetworkCategory -eq 'DomainAuthenticated'){throw '域认证网络禁止修改'}; ";
            if(restore)
                ps+="if(-not(Test-Path -LiteralPath $path)){throw '无原状态备份'}; "+
                    "$goal=(Get-Content -LiteralPath $path -Raw).Trim(); "+
                    "if($goal -notin @('Private','Public')){throw '备份内容不是有效网络类别'}; ";
            else
                ps+="if(-not(Test-Path -LiteralPath $path)){[IO.File]::WriteAllText($path,[string]$now.NetworkCategory,[Text.Encoding]::ASCII)}; "+
                    "$goal='"+value+"'; ";
            ps+="Set-NetConnectionProfile -InterfaceIndex $idx -NetworkCategory $goal -ErrorAction Stop; "+
                "$after=(Get-NetConnectionProfile -InterfaceIndex $idx -ErrorAction Stop).NetworkCategory; "+
                "if($after -ne $goal){throw '切换后状态未得到验证'}; '已验证：'+$after; '原状态备份：'+$path";
            return PowerShellService.RunScript(ps,2);
        }
    }
}
