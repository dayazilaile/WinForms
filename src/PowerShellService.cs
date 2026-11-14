using System;
using System.Text;
using System.Threading;
using System.Text.RegularExpressions;
using System.Collections.Generic;

namespace ITDesktopToolkit
{
    public static class PowerShellService
    {
        public static string RunScript(string script, int minutes)
        { return RunScript(script,minutes,CancellationToken.None); }
        public static string RunScript(string script,int minutes,CancellationToken cancel)
        {
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes("$ProgressPreference='SilentlyContinue'; " + script));
            string raw=CommandService.Run("powershell.exe", "-NoLogo -NoProfile -NonInteractive -OutputFormat Text -EncodedCommand " + encoded, minutes,cancel);
            int start=raw.IndexOf("\r\n返回代码：",StringComparison.Ordinal);
            if(start<0)start=raw.IndexOf("\n返回代码：",StringComparison.Ordinal);
            if(start>=0)return FormatErrors("命令：Windows PowerShell（省略编码参数）"+raw.Substring(start));
            return FormatErrors(raw.Replace(encoded,"[编码的 PowerShell 指令]"));
        }
        // Windows PowerShell 5.1 can emit serialized CLIXML to redirected stderr.
        // Decode only for display; preserve the command return code for callers.
        private static string FormatErrors(string result)
        {
            int pos=result.IndexOf("#< CLIXML",StringComparison.Ordinal);
            if(pos<0)return result;
            MatchCollection items=Regex.Matches(result.Substring(pos),@"<S\s+S=""Error"">(.*?)</S>",RegexOptions.Singleline);
            List<string> errors=new List<string>();
            foreach(Match item in items)
            {
                string message=Regex.Replace(item.Groups[1].Value,@"_x([0-9a-fA-F]{4})_",delegate(Match m){
                    int code=Convert.ToInt32(m.Groups[1].Value,16);
                    return ((char)code).ToString();
                });
                message=message.Replace("&lt;","<").Replace("&gt;",">").Replace("&amp;","&").Replace("&quot;","\"");
                message=message.Trim();
                if(message.Length==0)continue;
                if(message.Length>1200)message=message.Substring(0,1200)+"…";
                if(errors.Contains(message))continue;
                errors.Add(message);
                if(errors.Count>=12)break;
            }
            int cut=pos;
            int prefix=result.LastIndexOf("[错误]",pos,StringComparison.Ordinal);
            if(prefix>=0 && pos-prefix<15)cut=prefix;
            string heading=result.Substring(0,cut).TrimEnd();
            if(errors.Count==0)return heading+"\r\n[PowerShell 错误] 收到序列化错误，但未能解析；请保留原文件，告知开发者返回代码。";
            return heading+"\r\n[PowerShell 错误详情]\r\n"+String.Join("\r\n",errors.ToArray());
        }
        public static string Quote(string value) { return "'" + (value ?? "").Replace("'", "''") + "'"; }
    }
}
