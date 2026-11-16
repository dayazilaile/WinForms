using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
namespace ITDesktopToolkit
{
    internal static class ConfigurationReportFormatter
    {
        private static string Line(string value){return (value??"").Replace('\r',' ').Replace('\n',' ').Replace('\t',' ').Trim();}
        private static string Value(IDictionary<string,string> fields,string key){string value;return fields.TryGetValue(key,out value)?Line(value):"";}
        private static string Join(params string[] parts){var values=new List<string>();foreach(var part in parts)if(!String.IsNullOrWhiteSpace(part))values.Add(part.Trim());return String.Join(" ",values.ToArray());}
        private static string Capacity(string value)
        {foreach(string suffix in new[]{"GiB","GB"})if(value.EndsWith(suffix,StringComparison.OrdinalIgnoreCase))return value.Substring(0,value.Length-suffix.Length).Trim()+"G";return value;}
        private static string Positive(string value,string suffix){double number;return Double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out number)&&number>0?value+suffix:"";}
        private static string Brand(string value)
        {
            value=HardwareService.CleanSmbios(value);
            if(value.Equals("KINGBANK",StringComparison.OrdinalIgnoreCase))return "金百达";
            if(value.Equals("Kingston",StringComparison.OrdinalIgnoreCase))return "金士顿";
            if(value.Equals("Samsung",StringComparison.OrdinalIgnoreCase))return "三星";
            if(value.Equals("Corsair",StringComparison.OrdinalIgnoreCase))return "海盗船";
            return value;
        }
        private static SortedDictionary<string,Dictionary<string,string>> Groups(IDictionary<string,string> fields,string category)
        {
            var result=new SortedDictionary<string,Dictionary<string,string>>(StringComparer.Ordinal);string prefix=category+"/";
            foreach(var pair in fields)if(pair.Key.StartsWith(prefix,StringComparison.Ordinal))
            {
                int split=pair.Key.LastIndexOf('/');if(split<=prefix.Length)continue;
                string key=pair.Key.Substring(prefix.Length,split-prefix.Length);Dictionary<string,string> group;
                if(!result.TryGetValue(key,out group)){group=new Dictionary<string,string>(StringComparer.Ordinal);result.Add(key,group);}
                group[pair.Key.Substring(split+1)]=Line(pair.Value);
            }
            return result;
        }
        private static string Combine(IEnumerable<string> items)
        {
            var order=new List<string>();var counts=new Dictionary<string,int>(StringComparer.Ordinal);
            foreach(string item in items)if(!String.IsNullOrWhiteSpace(item)){if(!counts.ContainsKey(item)){counts[item]=0;order.Add(item);}counts[item]++;}
            var output=new List<string>();foreach(string item in order)output.Add(item+(counts[item]>1?" ×"+counts[item].ToString(CultureInfo.InvariantCulture):""));return String.Join(" + ",output.ToArray());
        }
        internal static List<KeyValuePair<string,string>> Rows(IDictionary<string,string> fields)
        {
            var rows=new List<KeyValuePair<string,string>>();Action<string,string> add=delegate(string key,string value){if(!String.IsNullOrWhiteSpace(value))rows.Add(new KeyValuePair<string,string>(key,Line(value)));};
            add("电脑名",Value(fields,"报告/电脑名"));
            add("系统",Join(Value(fields,"系统/名称"),Value(fields,"系统/版本"),Value(fields,"系统/Build")==""?"":"Build "+Value(fields,"系统/Build"),Value(fields,"系统/架构")));
            add("整机",Join(Brand(Value(fields,"整机/厂商")),Value(fields,"整机/型号")));
            var values=new List<string>();
            foreach(var item in Groups(fields,"CPU").Values){string cores=Positive(Value(item,"核心数"),"核"),threads=Positive(Value(item,"逻辑处理器"),"线程");values.Add(Join(Value(item,"型号"),cores+(cores!=""&&threads!=""?"/":"")+threads));}add("CPU",Combine(values));
            values.Clear();foreach(var item in Groups(fields,"主板").Values)values.Add(Join(Brand(Value(item,"厂商")),Value(item,"型号")));add("主板",Combine(values));
            values.Clear();foreach(var item in Groups(fields,"内存").Values)values.Add(Join(Brand(Value(item,"厂商")),Capacity(Value(item,"容量")),Positive(Value(item,"配置频率 MHz"),"MHz")));
            add("内存",values.Count>0?Combine(values):Capacity(Value(fields,"整机/内存")));
            values.Clear();foreach(var item in Groups(fields,"显卡").Values)values.Add(Value(item,"型号"));add("显卡",Combine(values));
            values.Clear();foreach(var item in Groups(fields,"磁盘").Values)values.Add(Join(Value(item,"型号"),Capacity(Value(item,"容量"))));add("磁盘",Combine(values));
            var adapters=Groups(fields,"网卡");var networks=Groups(fields,"网络");var aliases=new SortedSet<string>(adapters.Keys,StringComparer.Ordinal);aliases.UnionWith(networks.Keys);int index=0;
            foreach(string alias in aliases)
            {
                Dictionary<string,string> adapter,network;if(!adapters.TryGetValue(alias,out adapter))adapter=new Dictionary<string,string>();if(!networks.TryGetValue(alias,out network))network=new Dictionary<string,string>();
                string mac=Value(adapter,"MAC"),ipv4=Value(network,"IPv4"),ipv6=Value(network,"IPv6");
                var parts=new List<string>();parts.Add(Join(alias,Value(adapter,"型号")));if(mac!="")parts.Add("MAC "+mac);if(ipv4!="")parts.Add("IPv4 "+ipv4);if(ipv6!="")parts.Add("IPv6 "+ipv6);
                add("网卡"+(++index).ToString(CultureInfo.InvariantCulture),String.Join(" | ",parts.ToArray()));
            }
            values.Clear();foreach(var pair in fields)if(pair.Key.StartsWith("读取状态/",StringComparison.Ordinal))values.Add(pair.Key.Substring(5)+"："+Line(pair.Value));add("读取提示",String.Join("；",values.ToArray()));
            if(rows.Count==0)foreach(string key in new SortedSet<string>(fields.Keys,StringComparer.Ordinal))add(key,fields[key]);
            return rows;
        }
        internal static string Export(string current)
        {
            var text=new StringBuilder(InspectionService.CompactHeader+"\r\n");
            foreach(var row in Rows(InspectionService.Parse(current))){if(row.Key.IndexOf('：')>=0)throw new FormatException("配置项名称不能含分隔符。");text.AppendLine(Line(row.Key)+"："+Line(row.Value));}
            return text.ToString();
        }
        internal static Dictionary<string,string> ForComparison(IDictionary<string,string> fields)
        {var result=new Dictionary<string,string>(StringComparer.Ordinal);foreach(var row in Rows(fields))result.Add(row.Key,row.Value);return result;}
    }
}
