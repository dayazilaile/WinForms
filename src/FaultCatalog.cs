using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ITDesktopToolkit
{
    public sealed class CatalogEntry
    {
        public string Log, Source, Code, Group, Description, Advice;
    }
    public static class FaultCatalog
    {
        private static List<CatalogEntry> entries = new List<CatalogEntry>();
        public static int Count { get { return entries.Count; } }
        public static void Reload()
        {
            List<CatalogEntry> result = new List<CatalogEntry>();
            string f = Path.Combine(Paths.DbDir, "fault_codes.tsv");
            if (File.Exists(f)) foreach (string line in File.ReadAllLines(f, Encoding.UTF8))
            {
                if (String.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                string[] x = line.Split('\t');
                if (x.Length < 6) continue;
                result.Add(new CatalogEntry { Log=x[0],Source=x[1],Code=x[2],Group=x[3],Description=x[4],Advice=x[5] });
            }
            entries = result;
        }
        public static CatalogEntry Find(string log, string source, int code)
        {
            foreach (CatalogEntry e in entries)
                if (e.Log.Equals(log, StringComparison.OrdinalIgnoreCase) && e.Code == code.ToString() &&
                    source.IndexOf(e.Source, StringComparison.OrdinalIgnoreCase) >= 0) return e;
            return null;
        }
    }
}
