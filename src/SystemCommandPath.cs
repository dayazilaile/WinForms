using System;
using System.IO;
using System.Text;
namespace WinForms
{
    // Resolve bundled Windows commands without searching the current directory or PATH.
    internal static class SystemCommandPath
    {
        internal static string Resolve(string name)
        {
            if(Environment.OSVersion.Platform!=PlatformID.Win32NT)return name;
            return ResolveWindows(name,Environment.SystemDirectory,Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        }
        internal static string ResolveWindows(string name,string system,string windows)
        {
            if(String.IsNullOrWhiteSpace(name))throw new ArgumentException("程序名称不能为空。");
            if(name.IndexOfAny(new[]{'\0','\r','\n'})>=0)throw new ArgumentException("程序名称含非法字符。");
            if(name.IndexOf('\\')>=0||name.IndexOf('/')>=0||name.IndexOf(':')>=0)return name;
            if(name.Equals("powershell.exe",StringComparison.OrdinalIgnoreCase))return Path.Combine(system,"WindowsPowerShell","v1.0","powershell.exe");
            if(name.Equals("explorer.exe",StringComparison.OrdinalIgnoreCase))return Path.Combine(windows,name);
            if(name.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)||name.EndsWith(".msc",StringComparison.OrdinalIgnoreCase)||name.EndsWith(".cpl",StringComparison.OrdinalIgnoreCase))return Path.Combine(system,name);
            return name;
        }
        // Windows CommandLineToArgvW quoting; this is not cmd.exe escaping.
        internal static string Argument(string value)
        {
            value=value??"";if(value.IndexOf('\0')>=0)throw new ArgumentException("参数不能含 NUL。");
            var result=new StringBuilder("\"");int slashes=0;
            foreach(char c in value){if(c=='\\'){slashes++;continue;}if(c=='"'){result.Append('\\',slashes*2+1);result.Append(c);}else{result.Append('\\',slashes);result.Append(c);}slashes=0;}
            result.Append('\\',slashes*2);return result.Append('"').ToString();
        }
    }
}
