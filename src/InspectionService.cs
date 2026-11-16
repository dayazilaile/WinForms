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
        public const string CompactHeader = "# ITDesktopToolkit configuration report v2";
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
                string header=r.ReadLine();bool compact=header==CompactHeader;
                if(header!=Header&&!compact)throw new FormatException("请选择工具箱导出的配置对比报告。");
                string line;
                while ((line = r.ReadLine()) != null)
                {
                    if (line.Length == 0) continue;
                    if(compact)
                    {
                        int split=line.IndexOf('：');if(split<=0)throw new FormatException("紧凑配置报告字段无效。");
                        string name=line.Substring(0,split).Trim();if(name.Length==0||result.ContainsKey(name))throw new FormatException("紧凑报告含空项或重复项。");result.Add(name,line.Substring(split+1).Trim());continue;
                    }
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
        internal static string ConfigurationScript()
        {
            return @"$ErrorActionPreference='Stop';$readWarnings=@{};
function Emit($key,$value) { 'ITCFG:'+ [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes([string]$key)) + ""`t"" + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes([string]$value)) }
function Read-Cim($class) { try { Get-CimInstance $class -ErrorAction Stop } catch { $readWarnings[$class]=$_.Exception.Message } }
function GiB($value) { if($null -eq $value){return '未提供'};([double]$value/1GB).ToString('0.##',[Globalization.CultureInfo]::InvariantCulture)+' GiB' }
Emit '报告/电脑名' $env:COMPUTERNAME
foreach($os in Read-Cim 'Win32_OperatingSystem'){Emit '系统/名称' $os.Caption;Emit '系统/版本' $os.Version;Emit '系统/Build' $os.BuildNumber;Emit '系统/架构' $os.OSArchitecture}
foreach($c in Read-Cim 'Win32_ComputerSystem'){Emit '整机/厂商' $c.Manufacturer;Emit '整机/型号' $c.Model;Emit '整机/内存' (GiB $c.TotalPhysicalMemory);Emit '整机/域或工作组' $c.Domain}
$i=0;foreach($c in (Read-Cim 'Win32_Processor' | Sort-Object DeviceID)){$i++;$k='CPU/'+$i+'/';Emit ($k+'型号') $c.Name;Emit ($k+'核心数') $c.NumberOfCores;Emit ($k+'逻辑处理器') $c.NumberOfLogicalProcessors;Emit ($k+'最大报告频率 MHz') $c.MaxClockSpeed}
$i=0;foreach($c in (Read-Cim 'Win32_BaseBoard' | Sort-Object Tag)){$i++;$k='主板/'+$i+'/';Emit ($k+'厂商') $c.Manufacturer;Emit ($k+'型号') $c.Product;Emit ($k+'版本') $c.Version}
foreach($c in Read-Cim 'Win32_BIOS'){Emit 'BIOS/厂商' $c.Manufacturer;Emit 'BIOS/版本' $c.SMBIOSBIOSVersion}
$i=0;foreach($c in (Read-Cim 'Win32_PhysicalMemory' | Sort-Object DeviceLocator,BankLabel)){$i++;$k='内存/'+$i+'/';Emit ($k+'插槽') $c.DeviceLocator;Emit ($k+'厂商') $c.Manufacturer;Emit ($k+'零件号') ([string]$c.PartNumber).Trim();Emit ($k+'容量') (GiB $c.Capacity);Emit ($k+'配置频率 MHz') $c.ConfiguredClockSpeed}
$i=0;foreach($c in (Read-Cim 'Win32_VideoController' | Sort-Object DeviceID)){$i++;$k='显卡/'+$i+'/';Emit ($k+'型号') $c.Name;Emit ($k+'驱动版本') $c.DriverVersion}
$i=0;foreach($c in (Read-Cim 'Win32_DiskDrive' | Sort-Object Index)){$i++;$k='磁盘/'+$i+'/';Emit ($k+'型号') $c.Model;Emit ($k+'容量') (GiB $c.Size);Emit ($k+'接口') $c.InterfaceType}
foreach($n in Get-NetAdapter){$k='网卡/'+$n.Name+'/';Emit ($k+'型号') $n.InterfaceDescription;Emit ($k+'MAC') $n.MacAddress;Emit ($k+'接口速率') $n.LinkSpeed}
foreach($n in Get-NetIPConfiguration){$k='网络/'+$n.InterfaceAlias+'/';Emit ($k+'IPv4') (($n.IPv4Address|ForEach-Object {$_.IPAddress+'/'+$_.PrefixLength}|Sort-Object)-join ',');Emit ($k+'IPv6') (($n.IPv6Address|ForEach-Object {$_.IPAddress+'/'+$_.PrefixLength}|Sort-Object)-join ',');Emit ($k+'网关') (($n.IPv4DefaultGateway.NextHop|Sort-Object)-join ',');Emit ($k+'DNS') (($n.DNSServer.ServerAddresses)-join ',')}
foreach($name in $readWarnings.Keys){Emit ('读取状态/'+$name) $readWarnings[$name]}
";
        }
        public static string CollectConfiguration()
        {
            return CollectConfiguration(System.Threading.CancellationToken.None);
        }
        public static string CollectConfiguration(System.Threading.CancellationToken cancel)
        {
            return ConfigurationFromOutput(PowerShellService.RunScript(ConfigurationScript(),3,cancel));
        }
        internal static string ConfigurationFromOutput(string raw)
        {
            if(!raw.Replace("\r","").Contains("\n返回代码：0\n"))throw new InvalidOperationException(raw);
            var records=new StringBuilder(Header+"\n");
            foreach(string line in raw.Split('\n'))if(line.StartsWith("ITCFG:",StringComparison.Ordinal))records.AppendLine(line.Substring(6).TrimEnd('\r'));
            var parsed=Parse(records.ToString());
            if(!parsed.ContainsKey("系统/名称")||!parsed.ContainsKey("CPU/1/型号")||String.IsNullOrWhiteSpace(parsed["系统/名称"])||String.IsNullOrWhiteSpace(parsed["CPU/1/型号"]))throw new InvalidOperationException("系统或 CPU 配置未完整读取。\r\n"+raw);
            return Serialize(parsed);
        }
        public static string ExportConfiguration(string current){return ConfigurationReportFormatter.Export(current);}
        public static string ConfigurationSummary(string current)
        {
            string exported=ExportConfiguration(current);return "本机配置报告已采集\r\n\r\n"+exported.Substring(exported.IndexOf('\n')+1);
        }
        public static string CompareConfigurations(IDictionary<string,string> left,IDictionary<string,string> right)
        {
            var a=ConfigurationReportFormatter.ForComparison(left);var b=ConfigurationReportFormatter.ForComparison(right);
            var keys=new SortedSet<string>(a.Keys,StringComparer.Ordinal);keys.UnionWith(b.Keys);var differences=new List<string>();
            foreach(string key in keys){string x,y;bool hasA=a.TryGetValue(key,out x),hasB=b.TryGetValue(key,out y);if(hasA==hasB&&x==y)continue;differences.Add(key+"：左 "+(hasA?x:"（无此项）")+" → 右 "+(hasB?y:"（无此项）"));}
            return "配置对比 · 差异项："+differences.Count+"\r\n"+String.Join("\r\n",differences.ToArray());
        }
        public static Dictionary<string,string> InstalledSoftware()
        {
            return SoftwareInventoryService.Read(System.Threading.CancellationToken.None).Versions();
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
