using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace ITDesktopToolkit
{
    public static class StartupIconService
    {
        private const uint SHGFI_ICON=0x00000100;
        private const uint SHGFI_SMALLICON=0x00000001;
        [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=80)] public string szTypeName;
        }
        [DllImport("shell32.dll",CharSet=CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(string file,uint attributes,ref SHFILEINFO info,uint infoSize,uint flags);
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
        private static string FindFile(string command)
        {
            if(String.IsNullOrWhiteSpace(command)) return null;
            string value=Environment.ExpandEnvironmentVariables(command.Trim());
            if(File.Exists(value))return value;
            if(value.StartsWith("\""))
            {
                int end=value.IndexOf('\"',1);
                if(end>1) {string path=value.Substring(1,end-1);if(File.Exists(path))return path;}
            }
            // Handles unquoted command paths with spaces; never launches anything.
            Match m=Regex.Match(value,@"^(.+?\.(?:exe|com|bat|cmd|lnk))(?=\s|$)",RegexOptions.IgnoreCase);
            if(m.Success && File.Exists(m.Groups[1].Value))return m.Groups[1].Value;
            return null;
        }
        public static Image ForCommand(string command)
        {
            try
            {
                string path=FindFile(command);
                if(path==null)return SystemIcons.Application.ToBitmap();
                SHFILEINFO info=new SHFILEINFO();
                IntPtr found=SHGetFileInfo(path,0,ref info,(uint)Marshal.SizeOf(typeof(SHFILEINFO)),SHGFI_ICON|SHGFI_SMALLICON);
                if(found!=IntPtr.Zero && info.hIcon!=IntPtr.Zero)
                {
                    try { using(Icon icon=(Icon)Icon.FromHandle(info.hIcon).Clone()) return icon.ToBitmap(); }
                    finally { DestroyIcon(info.hIcon); }
                }
            }
            catch(Exception ex) {Paths.Log("Startup icon: "+ex.Message);}
            return SystemIcons.Application.ToBitmap();
        }
    }
}
