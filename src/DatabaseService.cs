using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ITDesktopToolkit
{
    public sealed class HardwareRule
    {
        public string Type, Keyword, Brand, Label, Note;
    }
    public sealed class BugcheckRule
    {
        public string Name, Description, Advice;
    }
    public sealed class DatabaseService
    {
        public readonly IdCatalogService Ids = new IdCatalogService();
        private readonly List<HardwareRule> hardwareRules = new List<HardwareRule>();
        private readonly Dictionary<string, BugcheckRule> bugcheckRules = new Dictionary<string, BugcheckRule>(StringComparer.OrdinalIgnoreCase);
        public int HardwareRuleCount { get { return hardwareRules.Count; } }
        public int BugcheckRuleCount { get { return bugcheckRules.Count; } }
        public string HardwarePath { get { return Path.Combine(Paths.DbDir, "hardware_models.tsv"); } }
        public string BugcheckPath { get { return Path.Combine(Paths.DbDir, "bugcheck_codes.tsv"); } }
        public string HardwareSupplementPath { get { return Path.Combine(Paths.DbDir, "hardware_models_v039.tsv"); } }
        private void LoadHardwareRules(string file)
        {
            if (!File.Exists(file)) return;
            foreach (string line in File.ReadAllLines(file, Encoding.UTF8))
            {
                if (String.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                string[] x = line.Split('\t');
                if (x.Length < 5 || x[0].Trim().Length == 0 || x[1].Trim().Length == 0) continue;
                hardwareRules.Add(new HardwareRule { Type = x[0].Trim(), Keyword = x[1].Trim(), Brand = x[2].Trim(), Label = x[3].Trim(), Note = x[4].Trim() });
            }
        }
        public void Reload()
        {
            hardwareRules.Clear();
            bugcheckRules.Clear();
            Ids.Reload();
            // Load user-editable original rules first. The additive v039 file lets older
            // build folders receive new monitor/vendor mappings without overwriting local edits.
            LoadHardwareRules(HardwarePath);
            LoadHardwareRules(HardwareSupplementPath);
            if (File.Exists(BugcheckPath))
            {
                foreach (string line in File.ReadAllLines(BugcheckPath, Encoding.UTF8))
                {
                    if (String.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                    string[] x = line.Split('\t');
                    if (x.Length < 4) continue;
                    string code = NormalizeCode(x[0]);
                    if (code.Length == 0) continue;
                    bugcheckRules[code] = new BugcheckRule { Name = x[1], Description = x[2], Advice = x[3] };
                }
            }
        }
        public void MatchHardware(HardwareItem item)
        {
            Ids.MatchHardware(item);
            item.DatabaseKey = "";
            item.DatabaseBrand = "";
            item.DatabaseLabel = "";
            item.DatabaseNote = "";
            string combined = item.Name + "\n" + item.Manufacturer + "\n" + item.Model + "\n" + item.Identifier;
            HardwareRule selected = null;
            foreach (HardwareRule r in hardwareRules)
            {
                if (!r.Type.Equals(item.Type, StringComparison.OrdinalIgnoreCase) && r.Type != "*") continue;
                if (combined.IndexOf(r.Keyword, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (selected == null || r.Keyword.Length > selected.Keyword.Length) selected = r;
            }
            if (selected != null)
            {
                item.DatabaseBrand = selected.Brand;
                item.DatabaseLabel = selected.Label;
                item.DatabaseNote = selected.Note;
                item.DatabaseKey = selected.Keyword;
            }
        }
        public BugcheckRule FindBugcheck(string code)
        {
            BugcheckRule r;
            bugcheckRules.TryGetValue(NormalizeCode(code), out r);
            return r;
        }
        public static string NormalizeCode(string code)
        {
            if (String.IsNullOrWhiteSpace(code)) return "";
            string s = code.Trim().ToUpperInvariant();
            if (s.StartsWith("0X")) s = s.Substring(2);
            long n;
            if (!long.TryParse(s, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out n)) return "";
            return "0x" + n.ToString("X8");
        }
        public void AddHardwareRule(string type, string keyword, string brand, string label, string note)
        {
            string[] parts = new string[] { type, keyword, brand, label, note };
            foreach (string p in parts)
                if (p.Contains("\n") || p.Contains("\r") || p.Contains("\t")) throw new ArgumentException("字段不能包含换行或 Tab。");
            if (String.IsNullOrWhiteSpace(type) || String.IsNullOrWhiteSpace(keyword))
                throw new ArgumentException("类别和关键词必填。");
            Directory.CreateDirectory(Paths.DbDir);
            File.AppendAllText(HardwarePath, String.Join("\t", parts) + Environment.NewLine, new UTF8Encoding(false));
            Reload();
        }
    }
}
