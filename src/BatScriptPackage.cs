using System;
using System.IO;
using System.Text;
namespace ITDesktopToolkit
{
    // Both exporters use the same standalone BAT header, visible launcher and payload format.
    internal static class BatScriptPackage
    {
        internal const string Marker=":__ITTOOLKIT_PAYLOAD_V1__";
        internal static StringBuilder Begin()
        {
            var text=new StringBuilder();text.AppendLine("@echo off");text.AppendLine("setlocal DisableDelayedExpansion");text.AppendLine("set \"IT_TOOLKIT_BAT=%~f0\"");return text;
        }
        internal static void AppendForeground(StringBuilder text)
        {
            text.AppendLine("powershell.exe -NoLogo -NoProfile -STA -Command \"$f=$env:IT_TOOLKIT_BAT; $t=[IO.File]::ReadAllText($f); $m=':__ITTOOLKIT_PAYLOAD_V1__'; $x=$t.Substring($t.LastIndexOf($m)+$m.Length).Trim(); $s=[Text.Encoding]::Unicode.GetString([Convert]::FromBase64String($x)); & ([ScriptBlock]::Create($s))\"");
        }
        internal static void Write(string path,StringBuilder text,string script,bool windowsLines)
        {
            text.AppendLine(Marker);string encoded=Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            for(int i=0;i<encoded.Length;i+=100)text.AppendLine(encoded.Substring(i,Math.Min(100,encoded.Length-i)));
            string content=text.ToString();if(windowsLines)content=content.Replace("\r\n","\n").Replace("\n","\r\n");
            string target=Path.GetFullPath(path),temporary=Path.Combine(Path.GetDirectoryName(target),"it-bat-"+Guid.NewGuid().ToString("N")+".tmp");
            try{File.WriteAllText(temporary,content,new UTF8Encoding(false));if(File.Exists(target))File.Replace(temporary,target,null);else File.Move(temporary,target);}finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
    }
}
