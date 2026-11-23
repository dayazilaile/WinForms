using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace WinForms
{
    public sealed class SoftwareBaselineEntry
    {
        public string Name="",Publisher="";
    }
    public sealed class SoftwareBaselineComparison
    {
        public readonly List<SoftwareEntry> Extra=new List<SoftwareEntry>();
        public readonly List<SoftwareBaselineEntry> Missing=new List<SoftwareBaselineEntry>();
    }
    public static class SoftwareBaselineService
    {
        public const string Header="# WinForms Software Baseline V1";
        internal const string LegacyHeader="# ITDesktopToolkit Software Baseline V1";
        public const int Maximum=10000;
        public static string DefaultPath { get { return Path.Combine(Paths.UserDir,"software-baseline-v1.tsv"); } }
        private static string Field(string value,bool required)
        {
            string text=(value??"").Trim();
            if((required&&text.Length==0)||text.Length>1000)throw new FormatException("软件名称不能为空，名称和发布者分别不能超过 1000 字符。");
            foreach(char c in text)if(Char.IsControl(c))throw new FormatException("软件名称和发布者不能含制表符、换行或其他控制字符。");
            return text;
        }
        private static string Key(string name,string publisher){return (name??"").Trim()+"\0"+(publisher??"").Trim();}
        public static List<SoftwareBaselineEntry> Checked(IEnumerable<SoftwareBaselineEntry> entries)
        {
            if(entries==null)throw new ArgumentNullException("entries");
            var result=new List<SoftwareBaselineEntry>();var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);int count=0;
            foreach(var item in entries){if(++count>Maximum)throw new FormatException("标准清单最多 "+Maximum+" 行。");if(item==null)throw new FormatException("标准清单项目不能为空。");var row=new SoftwareBaselineEntry{Name=Field(item.Name,true),Publisher=Field(item.Publisher,false)};if(seen.Add(Key(row.Name,row.Publisher)))result.Add(row);}
            if(result.Count==0)throw new FormatException("标准清单至少保留一项软件；空清单不能用于判断多出软件。");
            result.Sort(delegate(SoftwareBaselineEntry a,SoftwareBaselineEntry b){int n=StringComparer.OrdinalIgnoreCase.Compare(a.Name,b.Name);return n!=0?n:StringComparer.OrdinalIgnoreCase.Compare(a.Publisher,b.Publisher);});return result;
        }
        public static List<SoftwareBaselineEntry> Capture(SoftwareSnapshot snapshot)
        {
            if(snapshot==null)throw new ArgumentNullException("snapshot");var rows=new List<SoftwareBaselineEntry>();
            foreach(var item in snapshot.Entries)rows.Add(new SoftwareBaselineEntry{Name=item.Name,Publisher=item.Publisher});return Checked(rows);
        }
        public static string Serialize(IEnumerable<SoftwareBaselineEntry> entries)
        {
            var b=new StringBuilder(Header+"\r\n软件名称\t发布者\r\n");foreach(var row in Checked(entries))b.Append(row.Name).Append('\t').Append(row.Publisher).Append("\r\n");
            string text=b.ToString();if(Encoding.UTF8.GetByteCount(text)>4*1024*1024)throw new FormatException("标准清单不能超过 4 MiB。");return text;
        }
        public static List<SoftwareBaselineEntry> Parse(string text)
        {
            if(text==null||Encoding.UTF8.GetByteCount(text)>4*1024*1024)throw new FormatException("标准清单不能超过 4 MiB。");
            string[] lines=text.TrimStart('\uFEFF').Replace("\r\n","\n").Split('\n');
            if(lines.Length<2||(lines[0]!=Header&&lines[0]!=LegacyHeader)||lines[1]!="软件名称\t发布者")throw new FormatException("不是软件标准清单，请使用“标准清单 → 导出标准清单”生成的文件。原关键词/最低版本清单请从“版本检查”导入。");
            var rows=new List<SoftwareBaselineEntry>();for(int i=2;i<lines.Length;i++){if(String.IsNullOrWhiteSpace(lines[i]))continue;string[] cells=lines[i].Split('\t');if(cells.Length!=2)throw new FormatException("标准清单第 "+(i+1)+" 行应为：软件完整名称<Tab>发布者（可空）。");rows.Add(new SoftwareBaselineEntry{Name=cells[0],Publisher=cells[1]});if(rows.Count>Maximum)throw new FormatException("标准清单最多 "+Maximum+" 行。");}return Checked(rows);
        }
        public static List<SoftwareBaselineEntry> Load(string path)
        {
            if(!File.Exists(path))return null;if(new FileInfo(path).Length>4*1024*1024)throw new IOException("标准清单文件超过 4 MiB。");return Parse(File.ReadAllText(path,new UTF8Encoding(false,true)));
        }
        public static void Save(string path,IEnumerable<SoftwareBaselineEntry> entries)
        {
            string text=Serialize(entries),folder=Path.GetDirectoryName(Path.GetFullPath(path));Directory.CreateDirectory(folder);string temporary=Path.Combine(folder,"software-baseline-"+Guid.NewGuid().ToString("N")+".tmp");
            try{File.WriteAllText(temporary,text,new UTF8Encoding(true));if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);OperationAudit.Record("保存软件标准清单","已保存","文件="+path);}finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
        public static SoftwareBaselineComparison Compare(SoftwareSnapshot snapshot,IEnumerable<SoftwareBaselineEntry> entries)
        {
            if(snapshot==null)throw new ArgumentNullException("snapshot");var rules=Checked(entries);var exact=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var matched=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var result=new SoftwareBaselineComparison();
            foreach(var rule in rules){exact.Add(Key(rule.Name,rule.Publisher));if(rule.Publisher.Length==0)names.Add(rule.Name);}
            foreach(var item in snapshot.Entries){string name=(item.Name??"").Trim(),key=Key(name,item.Publisher);bool allowed=false;if(exact.Contains(key)){matched.Add(key);allowed=true;}if(names.Contains(name)){matched.Add(Key(name,""));allowed=true;}if(!allowed)result.Extra.Add(item);}
            foreach(var rule in rules)if(!matched.Contains(Key(rule.Name,rule.Publisher)))result.Missing.Add(rule);return result;
        }
    }
}
