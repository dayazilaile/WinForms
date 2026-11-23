using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace WinForms
{
    public sealed class DefaultApplicationOption
    {
        public string Name,RegisteredName,ProgId; public bool PerUser;
    }
    public sealed class DefaultApplicationChoice
    {
        public DefaultApplicationOption Application;
    }
    public sealed class DefaultAppFormat
    {
        public readonly string Category, Extension, Description;
        public DefaultAppFormat(string category,string extension,string description)
        {Category=category;Extension=extension;Description=description;}
        public bool Protocol {get{return !Extension.StartsWith(".",StringComparison.Ordinal);}}
    }

    public static class DefaultAppsService
    {
        private static readonly DefaultAppFormat[] formats=CreateFormats();
        private static DefaultAppFormat[] CreateFormats()
        {
            var result=new List<DefaultAppFormat>();
            Add(result,"Office 文档","Word 文档",".doc .docx .docm .dot .dotx");
            Add(result,"Office 文档","Excel 表格",".xls .xlsx .xlsm .xlsb .csv");
            Add(result,"Office 文档","PowerPoint 演示",".ppt .pptx .pptm .pps .ppsx");
            Add(result,"Office 文档","开放文档 / 富文本",".odt .ods .odp .rtf");
            Add(result,"压缩与镜像","压缩包 / 磁盘镜像",".zip .rar .7z .tar .gz .tgz .bz2 .xz .iso");
            Add(result,"音乐","音频文件",".mp3 .wav .wma .flac .aac .m4a .ogg .ape .opus .mid");
            Add(result,"视频","视频文件",".mp4 .mkv .avi .mov .wmv .flv .webm .mpeg .mpg .m4v .3gp .ts");
            Add(result,"图片","图片文件",".jpg .jpeg .png .bmp .gif .webp .tif .tiff .heic .avif");
            Add(result,"PDF 与文本","PDF 文档",".pdf");
            Add(result,"PDF 与文本","文本文件",".txt .log .xml .json");
            Add(result,"网页与邮件","网页文件",".htm .html");
            Add(result,"网页与邮件","链接 / 邮件协议","http https mailto");
            return result.ToArray();
        }
        private static void Add(List<DefaultAppFormat> list,string category,string description,string extensions)
        {foreach(string ext in extensions.Split(' '))list.Add(new DefaultAppFormat(category,ext,description));}
        public static IList<DefaultAppFormat> Formats {get{return Array.AsReadOnly(formats);}}
        public static DefaultAppFormat Find(string extension)
        {foreach(var format in formats)if(String.Equals(format.Extension,extension,StringComparison.OrdinalIgnoreCase))return format;throw new ArgumentException("请选择列表中的格式或协议。");}
        public static bool SupportsNativeSelection(Version version){return version.Major<10;}
        public static string SettingsTarget(Version version){return SupportsNativeSelection(version)?"control.exe":"ms-settings:defaultapps";}
        public static string SettingsArguments(Version version){return SupportsNativeSelection(version)?"/name Microsoft.DefaultPrograms":"";}
        public static string ApplicationSettingsTarget(Version version,DefaultApplicationOption application)
        {
            // The documented per-app deep link is present on all Windows 11 23H2+ builds.
            // Earlier Win11 builds require specific cumulative updates; use the general page there.
            if(application==null||version.Major!=10||version.Build<22631)return SettingsTarget(version);
            if(String.IsNullOrWhiteSpace(application.RegisteredName))throw new ArgumentException("应用登记名称为空。");
            return "ms-settings:defaultapps?"+(application.PerUser?"registeredAppUser=":"registeredAppMachine=")+Uri.EscapeDataString(application.RegisteredName);
        }
        public static List<DefaultApplicationOption> RegisteredApplications(string extension)
        {
            var format=Find(extension);var list=new List<DefaultApplicationOption>();var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if(Environment.OSVersion.Platform!=PlatformID.Win32NT)return list;
            foreach(var hive in new[]{RegistryHive.CurrentUser,RegistryHive.LocalMachine})foreach(var view in new[]{RegistryView.Registry64,RegistryView.Registry32}){
                try{using(var root=RegistryKey.OpenBaseKey(hive,view))using(var registered=root.OpenSubKey(@"SOFTWARE\RegisteredApplications")){
                    if(registered==null)continue;foreach(string name in registered.GetValueNames())try{
                        string path=registered.GetValue(name) as string;if(String.IsNullOrWhiteSpace(path))continue;
                        using(var capabilities=root.OpenSubKey(path)){
                            if(capabilities==null)continue;using(var associations=capabilities.OpenSubKey(format.Protocol?"URLAssociations":"FileAssociations")){
                                string prog=associations==null?null:associations.GetValue(format.Extension) as string;if(String.IsNullOrWhiteSpace(prog))continue;
                                bool user=hive==RegistryHive.CurrentUser;if(!seen.Add((user?"user:":"machine:")+name))continue;
                                string title=capabilities.GetValue("ApplicationName") as string;if(String.IsNullOrWhiteSpace(title)||title.StartsWith("@",StringComparison.Ordinal))title=name;
                                list.Add(new DefaultApplicationOption{Name=title,RegisteredName=name,ProgId=prog,PerUser=user});
                            }
                        }
                    }catch{} }
                }catch{}
            }
            list.Sort(delegate(DefaultApplicationOption a,DefaultApplicationOption b){return StringComparer.CurrentCultureIgnoreCase.Compare(a.Name,b.Name);});return list;
        }
        public static string Guidance(Version version)
        {
            if(SupportsNativeSelection(version))return "双击格式可打开系统原生选择框，勾选始终使用后确认。恢复时在控制面板的默认程序中逐项指定；旧系统没有统一重置全部默认应用按钮。";
            return "Win10/11：双击格式，在本工具选择已登记的应用，再到系统确认保存。新版 Win11 可直达所选应用页面，旧版本进入默认应用总页。此工具不直接写入受保护的默认关联。";
        }
        public static string ReadApplication(string extension,Func<string,string> reader)
        {
            Find(extension);if(reader==null)throw new ArgumentNullException("reader");
            try{string value=reader(extension);return String.IsNullOrWhiteSpace(value)?"未识别（请到系统核实）":value;}
            catch{return "读取失败（请到系统核实）";}
        }
        public static string SamplePath(string dataDirectory,string extension)
        {
            var format=Find(extension);if(format.Protocol)throw new ArgumentException("协议不使用样例文件。");
            return Path.Combine(Path.Combine(dataDirectory,"AssociationSamples"),"WinForms-关联样例"+format.Extension);
        }
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
        private struct OpenAsInfo
        {
            [MarshalAs(UnmanagedType.LPWStr)]public string File;
            [MarshalAs(UnmanagedType.LPWStr)]public string Class;
            public uint Flags;
        }
        [DllImport("shell32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]
        private static extern int SHOpenWithDialog(IntPtr parent,ref OpenAsInfo info);
        public static bool ChooseLegacy(IntPtr owner,string extension,string dataDirectory,Version version)
        {
            if(!SupportsNativeSelection(version))throw new NotSupportedException("Win10/11 请使用系统默认应用设置；此接口不能保存默认程序。");
            var format=Find(extension);string file=format.Extension;
            if(!format.Protocol){file=SamplePath(dataDirectory,extension);Directory.CreateDirectory(Path.GetDirectoryName(file));if(!File.Exists(file))using(File.Create(file)){};}
            // ALLOW_REGISTRATION | REGISTER_EXT; deliberately omit EXEC (0x4).
            var info=new OpenAsInfo{File=file,Class=null,Flags=(uint)(3|(format.Protocol?0x40:0))};
            int result=SHOpenWithDialog(owner,ref info);
            if(result==unchecked((int)0x800704C7))return false;
            if(result<0)Marshal.ThrowExceptionForHR(result);
            return true;
        }
    }
}
