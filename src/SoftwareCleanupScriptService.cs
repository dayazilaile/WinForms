using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
namespace ITDesktopToolkit
{
    public static class SoftwareCleanupScriptService
    {
        internal const string TemplateSha256="7f932b9900aacf31f092e98f51b5db6be433dfa4731923f5b5b342424d9c95f4";
        internal static string TemplatePath { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Scripts","SoftwareCleanup.ps1"); } }
        internal static string ReadTemplate(string path)
        {
            if(!File.Exists(path))throw new IOException("缺少批量清理明文模板 Scripts\\SoftwareCleanup.ps1，请完整解压并重新编译同版本软件。");
            if(new FileInfo(path).Length>4*1024*1024)throw new InvalidDataException("批量清理模板超过允许大小，未生成脚本。");
            string text=File.ReadAllText(path,new UTF8Encoding(false,true)).TrimStart('\uFEFF').Replace("\r\n","\n").Replace("\r","\n");
            using(var sha=System.Security.Cryptography.SHA256.Create())
                if(BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-","").ToLowerInvariant()!=TemplateSha256)
                    throw new InvalidDataException("批量清理模板与程序版本不匹配或内容被修改，未生成脚本。请核对同版本完整包。");
            return text;
        }
        public static string MakeScript(IEnumerable<SoftwareBaselineEntry> entries,bool automatic)
        {
            var rules=SoftwareBaselineService.Checked(entries);SoftwareBaselineService.Serialize(rules);var b=new StringBuilder("$ErrorActionPreference='Stop'\r\n");b.AppendLine(ReadTemplate(TemplatePath));b.AppendLine("$standard=@(");
            foreach(var rule in rules)b.AppendLine("[pscustomobject]@{Name="+PowerShellService.Quote(rule.Name)+";Publisher="+PowerShellService.Quote(rule.Publisher)+"}");
            b.AppendLine(")");b.AppendLine("$machineStage=$env:IT_SOFTWARE_CLEANUP_STAGE -eq '--machine-stage'; if(Invoke-SWCleanup $standard $"+(automatic?"true":"false")+" $machineStage){exit 0}elseif($script:SWNeedsReboot){exit 2}else{exit 1}");return b.ToString();
        }
        public static void ExportBat(string path,IEnumerable<SoftwareBaselineEntry> entries,bool automatic)
        {
            string script=MakeScript(entries,automatic);var text=BatScriptPackage.Begin();
            text.AppendLine("set \"IT_SOFTWARE_CLEANUP_STAGE=%~1\"");
            BatScriptPackage.AppendForeground(text);text.AppendLine("exit /b %ERRORLEVEL%");
            BatScriptPackage.Write(path,text,script,true);
        }
    }
}
