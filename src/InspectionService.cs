using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace ITDesktopToolkit
{
    // Reports contain keys and values, never executable content. No Win32_Product queries.
    public static class InspectionService
    {
        public const string Header = "# ITDesktopToolkit configuration report v1";
        private static string Encode(string s) { return Convert.ToBase64String(Encoding.UTF8.GetBytes(s ?? "")); }
        public static string Serialize(IDictionary<string,string> values)
        {
            StringBuilder b = new StringBuilder(Header + "\r\n");
            foreach (string key in new SortedSet<string>(values.Keys, StringComparer.Ordinal))
                b.AppendLine(Encode(key) + "\t" + Encode(values[key]));
            return b.ToString();
        }
        public static Dictionary<string,string> Parse(string text)
        {
            var result = new Dictionary<string,string>(StringComparer.Ordinal);
            using (StringReader r = new StringReader(text.TrimStart('\uFEFF')))
            {
                if (r.ReadLine() != Header) throw new FormatException("请选择工具箱导出的配置对比报告。");
                string line;
                while ((line = r.ReadLine()) != null)
                {
                    if (line.Length == 0) continue;
                    string[] p = line.Split('\t');
                    if (p.Length != 2) throw new FormatException("报告字段无效。");
                    string key = Encoding.UTF8.GetString(Convert.FromBase64String(p[0]));
                    if (result.ContainsKey(key)) throw new FormatException("报告含重复字段：" + key);
                    result.Add(key, Encoding.UTF8.GetString(Convert.FromBase64String(p[1])));
                }
            }
            return result;
        }
        public static string Compare(IDictionary<string,string> a, IDictionary<string,string> b)
        {
            StringBuilder report = new StringBuilder("配置对比（左侧报告 → 右侧报告）\r\n");
            int changed = 0;
            var keys = new SortedSet<string>(a.Keys, StringComparer.Ordinal); keys.UnionWith(b.Keys);
            foreach (string k in keys)
            {
                string x, y; bool hasA=a.TryGetValue(k,out x), hasB=b.TryGetValue(k,out y);
                if (hasA == hasB && x == y) continue;
                changed++; report.AppendLine("\r\n[" + k + "]\r\n左：" + (hasA?x:"（无此项）") + "\r\n右：" + (hasB?y:"（无此项）"));
            }
            return report.Insert(0,"差异项：" + changed + "\r\n").ToString();
        }
        public static string CollectConfiguration()
        {
            string script = @"$ErrorActionPreference='Stop';
function Emit($key,$value) { 'ITCFG:'+ [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes([string]$key)) + ""`t"" + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes([string]$value)) }
Emit '报告/电脑名' $env:COMPUTERNAME
$os=Get-CimInstance Win32_OperatingSystem; Emit '系统/名称' $os.Caption; Emit '系统/版本' $os.Version; Emit '系统/架构' $os.OSArchitecture
foreach($n in Get-NetAdapter){$k='网卡/'+$n.Name+'/'; Emit ($k+'型号') $n.InterfaceDescription; Emit ($k+'状态') $n.Status; Emit ($k+'MAC') $n.MacAddress}
foreach($n in Get-NetIPConfiguration){$k='网络/'+$n.InterfaceAlias+'/'; Emit ($k+'IPv4') (($n.IPv4Address|ForEach-Object {$_.IPAddress+'/'+$_.PrefixLength}|Sort-Object)-join ','); Emit ($k+'网关') (($n.IPv4DefaultGateway.NextHop|Sort-Object)-join ','); Emit ($k+'DNS') (($n.DNSServer.ServerAddresses)-join ',')}
foreach($n in Get-NetConnectionProfile){Emit ('网络/'+$n.InterfaceAlias+'/类别') $n.NetworkCategory}
foreach($name in @('Spooler','LanmanServer','LanmanWorkstation','Dnscache','Dhcp','FDResPub')){$s=Get-Service $name; Emit ('服务/'+$name) ($s.Status.ToString()+'/'+$s.StartType)}
$c=Get-SmbClientConfiguration; $s=Get-SmbServerConfiguration; Emit '共享/客户端Guest' $c.EnableInsecureGuestLogons; Emit '共享/客户端强制签名' $c.RequireSecuritySignature; Emit '共享/服务端强制签名' $s.RequireSecuritySignature; Emit '共享/服务端加密' $s.EncryptData
foreach($s in Get-SmbShare | Where-Object {-not $_.Special}) {Emit ('共享目录/'+$s.Name) $s.Path}
";
            string raw = PowerShellService.RunScript(script, 3);
            if (!raw.Contains("返回代码：0")) throw new InvalidOperationException(raw);
            StringBuilder records = new StringBuilder(Header + "\n");
            foreach (string line in raw.Split('\n')) if(line.StartsWith("ITCFG:",StringComparison.Ordinal)) records.AppendLine(line.Substring(6).TrimEnd('\r'));
            var parsed = Parse(records.ToString());
            if(parsed.Count < 4) throw new InvalidOperationException("未收到完整配置报告。\r\n"+raw);
            return Serialize(parsed);
        }
        public static Dictionary<string,string> InstalledSoftware()
        {
            var apps = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            foreach (RegistryHive hive in new RegistryHive[]{RegistryHive.LocalMachine,RegistryHive.CurrentUser})
            foreach (RegistryView view in new RegistryView[]{RegistryView.Registry32,RegistryView.Registry64})
            {
                if(view==RegistryView.Registry64 && !Environment.Is64BitOperatingSystem) continue;
                using (RegistryKey root=RegistryKey.OpenBaseKey(hive,view))
                using (RegistryKey uninstall=root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
                {
                    if(uninstall==null)continue;
                    foreach(string name in uninstall.GetSubKeyNames()) using(RegistryKey k=uninstall.OpenSubKey(name))
                    {
                        if(k==null)continue; string display=Convert.ToString(k.GetValue("DisplayName"));
                        if(String.IsNullOrWhiteSpace(display))continue;
                        string version=Convert.ToString(k.GetValue("DisplayVersion"));
                        apps[display+" ["+hive+"/"+view+"]"]=version;
                    }
                }
            }
            return apps;
        }
        public static string SoftwareReport(string requirements)
        {
            var apps=InstalledSoftware(); StringBuilder b=new StringBuilder("软件清单（注册表登记项，不调用 MSI 修复）\r\n");
            foreach(string name in new SortedSet<string>(apps.Keys,StringComparer.OrdinalIgnoreCase)) b.AppendLine(name+"\t"+apps[name]);
            b.AppendLine("\r\n运行环境：");
            using(RegistryKey k=Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full")) b.AppendLine(".NET Framework v4 Release="+(k==null?"未登记":Convert.ToString(k.GetValue("Release","未登记"))));
            b.AppendLine("VC++ 运行库：请查看上方 Microsoft Visual C++ 各年份及 x86/x64 登记项。软件清单不涵盖所有便携软件及其他用户的应用。");
            if(!String.IsNullOrWhiteSpace(requirements)) b.AppendLine("\r\n自定义清单检查：\r\n"+CheckRequirements(apps,requirements));
            return b.ToString();
        }
        private static Version NormalizeVersion(Version v){return new Version(v.Major,v.Minor,Math.Max(0,v.Build),Math.Max(0,v.Revision));}
        public static string CheckRequirements(IDictionary<string,string> apps,string requirements)
        {
            StringBuilder b=new StringBuilder();
            foreach(string row in requirements.Replace("\r","").Split('\n'))
            {
                if(String.IsNullOrWhiteSpace(row)||row.StartsWith("#"))continue;
                string[] p=row.Split('\t'); if(p.Length>2||String.IsNullOrWhiteSpace(p[0]))throw new FormatException("清单每行：软件名称关键词<Tab>最低版本（可空）。");
                Version minimum=null;
                if(p.Length==2&&!String.IsNullOrWhiteSpace(p[1])&&!Version.TryParse(p[1].Trim(),out minimum))throw new FormatException("最低版本格式无效："+p[1]);
                bool found=false,good=false,unknown=false;
                foreach(var app in apps) if(app.Key.IndexOf(p[0].Trim(),StringComparison.OrdinalIgnoreCase)>=0)
                {
                    found=true; Version installed;
                    if(minimum==null)good=true;
                    else if(!Version.TryParse(app.Value.Trim(),out installed))unknown=true;
                    else if(NormalizeVersion(installed)>=NormalizeVersion(minimum))good=true;
                }
                b.AppendLine((!found?"[缺失]":good?"[符合]":unknown?"[版本需人工核对]":"[版本偏低]")+" "+row);
            }
            return b.ToString();
        }
    }
}
