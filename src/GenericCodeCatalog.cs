using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace WinForms
{
    public sealed class GenericErrorInfo
    {
        public string Group,Code,Name,Explanation,Advice;
        public string Text { get { return "["+Group+"] "+Code+" "+Name+"\r\n"+Explanation+"\r\n建议："+Advice; } }
    }
    public static class GenericCodeCatalog
    {
        private static List<GenericErrorInfo> records=new List<GenericErrorInfo>();
        public static int Count { get { return records.Count; } }
        public static void Reload()
        {
            List<GenericErrorInfo> list=new List<GenericErrorInfo>();
            string file=Path.Combine(Paths.DbDir,"generic_codes.tsv");
            if(File.Exists(file))foreach(string line in File.ReadAllLines(file,Encoding.UTF8))
            {
                if(String.IsNullOrWhiteSpace(line)||line.StartsWith("#"))continue;
                string[] a=line.Split('\t');if(a.Length<5)continue;
                list.Add(new GenericErrorInfo{Group=a[0],Code=a[1],Name=a[2],Explanation=a[3],Advice=a[4]});
            }
            records=list;
        }
        public static List<GenericErrorInfo> Search(string term)
        {
            List<GenericErrorInfo> results=new List<GenericErrorInfo>();
            foreach(GenericErrorInfo x in records)
                if(String.IsNullOrWhiteSpace(term)|| (x.Group+" "+x.Code+" "+x.Name+" "+x.Explanation).IndexOf(term,StringComparison.OrdinalIgnoreCase)>=0)results.Add(x);
            return results;
        }
    }
}
