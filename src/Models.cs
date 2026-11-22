using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ITDesktopToolkit
{
    public sealed class HardwareItem
    {
        public string Type = "";
        public string Name = "";
        public string Manufacturer = "";
        public string Model = "";
        public string Identifier = "";
        public string Specs = "";
        public string DatabaseBrand = "";
        public string DatabaseLabel = "";
        public string DatabaseNote = "";
        public string DatabaseSource = "";
        public string DatabaseMatchMode = "";
        public string DatabaseKey = "";
        public string IdVendor = "";
        public string IdDevice = "";
        public string IdMatchKey = "";

        // 0813 等 JEDEC/SMBIOS 数字厂商代号不等于零售品牌；保留在详情中供人工核实。
        private static bool IsUnknownBrand(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return true;
            string s = value.Trim();
            if (s.Equals("unknown", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("to be filled by o.e.m.", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("default string", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("system manufacturer", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("not specified", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("none", StringComparison.OrdinalIgnoreCase)) return true;
            if (s.Length == 4 || s.Length == 6 || s.Length == 8)
            {
                bool allHex = true;
                foreach (char c in s) if (!Uri.IsHexDigit(c)) { allHex = false; break; }
                if (allHex) return true;
            }
            return false;
        }
        public string DisplayBrand
        {
            get
            {
                string value = !String.IsNullOrWhiteSpace(DatabaseBrand) ? DatabaseBrand : Manufacturer;
                if (IsUnknownBrand(value) && !String.IsNullOrWhiteSpace(IdVendor)) value = IdVendor;
                return IsUnknownBrand(value) ? "未识别" : value.Trim();
            }
        }
        public string DisplayModel
        {
            get
            {
                if (Type == "内存" && String.IsNullOrWhiteSpace(Model)) return "内存零件号未提供";
                string m = String.IsNullOrWhiteSpace(Model) ? Name : Model;
                if (String.IsNullOrWhiteSpace(m) || m.Trim() == "0" || m.Trim() == "00000000" ||
                    m.Trim().Equals("Default string", StringComparison.OrdinalIgnoreCase)) return "未提供型号";
                return m.Trim();
            }
        }
        public string MatchState
        {
            get { return !String.IsNullOrEmpty(DatabaseKey) ? (DatabaseMatchMode=="exact"?"型号名称匹配":DatabaseMatchMode=="prefix"?"家族前缀匹配":"关键词匹配") :
                !String.IsNullOrEmpty(IdDevice)?"PCI/USB设备ID":!String.IsNullOrEmpty(IdVendor)?"PCI/USB厂商ID":"系统检测"; }
        }
        public string Details()
        {
            StringBuilder b = new StringBuilder();
            b.AppendLine("类别：" + Type);
            b.AppendLine("设备名称（系统读取）：" + Name);
            b.AppendLine("原始厂商（系统读取）：" + Manufacturer);
            b.AppendLine("原始型号 / 零件号（系统读取）：" + (String.IsNullOrWhiteSpace(Model) ? "未提供" : Model));
            b.AppendLine("列表展示的品牌/厂商：" + DisplayBrand);
            b.AppendLine("列表展示的型号：" + DisplayModel);
            b.AppendLine("硬件标识：" + Identifier);
            b.AppendLine("系统参数：" + Specs);
            if (!String.IsNullOrWhiteSpace(IdVendor) || !String.IsNullOrWhiteSpace(IdDevice))
            {
                b.AppendLine();
                b.AppendLine("PCI/USB 编号库匹配：" + IdMatchKey);
                b.AppendLine("编号库厂商：" + (String.IsNullOrEmpty(IdVendor) ? "无记录" : IdVendor));
                b.AppendLine("编号库设备名：" + (String.IsNullOrEmpty(IdDevice) ? "无记录" : IdDevice));
            }
            b.AppendLine();
            b.AppendLine("本地数据库匹配：" + (String.IsNullOrEmpty(DatabaseKey) ? "无记录" : "已匹配"));
            if (!String.IsNullOrEmpty(DatabaseKey))
            {
                b.AppendLine("匹配关键词：" + DatabaseKey);
                b.AppendLine("数据库品牌：" + DatabaseBrand);
                b.AppendLine("数据库补充名称：" + DatabaseLabel);
                b.AppendLine("数据库备注：" + DatabaseNote);
                if(!String.IsNullOrWhiteSpace(DatabaseSource))b.AppendLine("规则来源："+DatabaseSource);
            }
            b.AppendLine();
            b.AppendLine("注意：品牌可能是 SMBIOS 报告的模组厂商，而不是零售品牌；本地库只是型号关键词推断。");
            b.AppendLine("零件号为空时，单靠 Windows 标准 WMI 无法保证推断内存条商品品牌。");
            b.AppendLine("关键词匹配不是官方精确认证，芯片厂商不一定等于板卡/整机品牌。");
            return b.ToString();
        }
    }

    public sealed class FaultEntry
    {
        public DateTime Time;
        public string Kind = "";
        public string Source = "";
        public string LogName = "";
        public int EventId;
        public long RecordId;
        public string Message = "";
        public string StopCode = "";
        public string CodeName = "";
        public string Advice = "";
        // 仅在有 BugCheck 1001，或 Kernel-Power 41 记录了非零 BugcheckCode 时标红。
        // 41/6008 本身不能证明发生蓝屏，更不能证明故障原因。
        public bool HasBugcheckEvidence = false;
        public bool IsBlueScreenRelated { get { return Kind == "蓝屏记录" || HasBugcheckEvidence; } }
        public string DisplayKind { get { return HasBugcheckEvidence && Kind == "意外重启" ? "蓝屏关联重启" : Kind; } }
        public string Summary
        {
            get
            {
                string m = Message.Replace('\r', ' ').Replace('\n', ' ').Trim();
                if (m.Length > 125) m = m.Substring(0, 125) + "...";
                return !String.IsNullOrEmpty(StopCode) ? StopCode + " " + CodeName + " · " + m : m;
            }
        }
        public string Details()
        {
            return "时间：" + Time.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n" +
                "类型：" + DisplayKind + "\r\n" + "日志：" + LogName + "\r\n" +
                "事件来源：" + Source + "\r\n" + "事件 ID：" + EventId + "\r\n" +
                (RecordId > 0 ? "日志记录编号：" + RecordId + "\r\n" : "") +
                (!String.IsNullOrEmpty(StopCode) ? "停止代码：" + StopCode + "\r\n名称：" + CodeName + "\r\n" : "") +
                (IsBlueScreenRelated ? "蓝屏标记：有直接蓝屏记录或非零 BugcheckCode。\r\n" :
                    (Kind == "异常关机" || Kind == "意外重启" ? "蓝屏标记：此事件没有直接蓝屏证据，请结合其他日志判断。\r\n" : "")) +
                "\r\n事件原文：\r\n" + Message + "\r\n" +
                (!String.IsNullOrEmpty(Advice) ? "\r\n离线建议（仅初步方向）：\r\n" + Advice + "\r\n" : "") +
                "\r\n提醒：事件相关性不等于因果关系；Kernel-Power 41 不能单独证明电源或硬件故障。";
        }
    }

    public sealed class FaultScanResult
    {
        public List<FaultEntry> Events = new List<FaultEntry>();
        public List<string> DumpFiles = new List<string>();
        public List<string> Warnings = new List<string>();
        public int Days;
        public int BlueScreens { get { return CountKind("蓝屏记录"); } }
        public int BlueScreenRelatedRestarts
        {
            get
            {
                int n = 0;
                foreach (FaultEntry e in Events) if (e.Kind == "意外重启" && e.HasBugcheckEvidence) n++;
                return n;
            }
        }
        public int UnexpectedShutdowns
        {
            get
            {
                List<DateTime> times=new List<DateTime>();
                foreach (FaultEntry e in Events)
                {
                    if (e.Kind != "异常关机" && e.Kind != "意外重启") continue;
                    bool grouped=false;
                    foreach(DateTime t in times) if(Math.Abs((t-e.Time).TotalMinutes)<10) {grouped=true;break;}
                    if(!grouped)times.Add(e.Time);
                }
                return times.Count;
            }
        }
        public int HardwareErrors { get { return CountKind("硬件异常"); } }
        public int CountKind(string kind)
        {
            int n = 0;
            foreach (FaultEntry e in Events) if (e.Kind == kind) n++;
            return n;
        }
    }

    public static class Paths
    {
        public static string BaseDir { get { return AppDomain.CurrentDomain.BaseDirectory; } }
        public static string DbDir { get { return Path.Combine(BaseDir, "Database"); } }
        public static string UserDir
        {
            get
            {
                string p = Path.Combine(BaseDir, "Data");
                Directory.CreateDirectory(p);
                return p;
            }
        }
        public static string MachineDir
        {
            get{string name=System.Text.RegularExpressions.Regex.Replace(Environment.MachineName,@"[^A-Za-z0-9_.-]","_");string path=Path.Combine(UserDir,"Computers",name);Directory.CreateDirectory(path);return path;}
        }
        public static string LastLogError="";
        public static void Log(string message)
        {OperationAudit.Record("程序诊断","信息",message);}
    }
}
