using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ITDesktopToolkit
{
    public static class EventService
    {
        private static readonly Regex StopCodeRegex = new Regex(@"\b0x[0-9a-fA-F]{8,16}\b", RegexOptions.Compiled);

        public static FaultScanResult Scan(int days, DatabaseService db)
        {
            FaultScanResult output = new FaultScanResult();
            output.Days = days;
            DateTime cutoff = DateTime.Now.AddDays(-days);
            ReadLog("System", cutoff, db, output);
            ReadLog("Application", cutoff, db, output);
            output.Events.Sort(delegate(FaultEntry a, FaultEntry b) { return b.Time.CompareTo(a.Time); });
            try
            {
                string p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Minidump");
                if (Directory.Exists(p))
                {
                    foreach (string file in Directory.GetFiles(p, "*.dmp"))
                    {
                        try
                        {
                            FileInfo f = new FileInfo(file);
                            if (f.LastWriteTime < cutoff) continue;
                            output.DumpFiles.Add(f.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss") + " | " + f.Length + " bytes | " + file);
                        }
                        catch (Exception ex) { output.Warnings.Add("读取 DMP 元数据失败：" + ex.Message); }
                    }
                }
                string kernel = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "MEMORY.DMP");
                if (File.Exists(kernel))
                {
                    FileInfo f = new FileInfo(kernel);
                    if (f.LastWriteTime >= cutoff)
                        output.DumpFiles.Add(f.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss") + " | " + f.Length + " bytes | " + kernel);
                }
            }
            catch (Exception ex) { output.Warnings.Add("读取转储文件列表失败：" + ex.Message); }
            output.DumpFiles.Sort(StringComparer.OrdinalIgnoreCase);
            return output;
        }

        private static void ReadLog(string logName, DateTime cutoff, DatabaseService db, FaultScanResult output)
        {
            try
            {
                using (EventLog log = new EventLog(logName))
                {
                    EventLogEntryCollection entries = log.Entries;
                    int inspected = 0;
                    int found = 0;
                    for (int i = entries.Count - 1; i >= 0; --i)
                    {
                        if (inspected++ >= 60000)
                        {
                            output.Warnings.Add(logName + " 日志条目过多，本次最多检查最近 60000 条，可能不完整。");
                            break;
                        }
                        EventLogEntry e;
                        try { e = entries[i]; }
                        catch { continue; }
                        if (e.TimeGenerated < cutoff) break;
                        string source = e.Source == null ? "" : e.Source;
                        int id = (int)(e.InstanceId & 65535L);
                        string kind = Classify(logName, source, id);
                        if (kind == null) continue;
                        if (found++ >= 600)
                        {
                            output.Warnings.Add(logName + " 相关事件过多，仅显示最近 600 条。");
                            break;
                        }
                        FaultEntry x = new FaultEntry();
                        x.Time = e.TimeGenerated;
                        x.LogName = logName;
                        x.Source = source;
                        x.EventId = id;
                        x.RecordId = e.Index;
                        x.Kind = kind;
                        string replacements = "";
                        try { if (e.ReplacementStrings != null) replacements = String.Join("；", e.ReplacementStrings); } catch { }
                        try { x.Message = e.Message; }
                        catch (Exception ex) { x.Message = "Windows 无法格式化该事件说明（可能缺少对应消息资源）：" + ex.Message; }
                        if (String.IsNullOrWhiteSpace(x.Message) || x.Message.Contains("无法找到源") || x.Message.Contains("description for Event ID") || x.Message.Contains("消息 DLL"))
                            x.Message = "事件说明不可用；以下为事件原始数据：\r\n" + replacements + "\r\n" + x.Message;
                        else if (!String.IsNullOrEmpty(replacements)) x.Message += "\r\n【事件原始字段】\r\n" + replacements;
                        CatalogEntry ce = FaultCatalog.Find(logName, source, id);
                        if (ce != null) x.Advice = ce.Description + "\r\n" + ce.Advice;
                        if (kind == "蓝屏记录")
                        {
                            Match m = StopCodeRegex.Match(replacements + " " + x.Message);
                            if (m.Success) ApplyBugcheckCode(x, m.Value, db);
                        }
                        else if (kind == "意外重启")
                        {
                            string code = ReadKernelPower41Bugcheck(e, x.Message);
                            if (!String.IsNullOrEmpty(code))
                            {
                                x.HasBugcheckEvidence = true;
                                ApplyBugcheckCode(x, code, db);
                            }
                        }
                        output.Events.Add(x);
                    }
                }
            }
            catch (Exception ex)
            {
                output.Warnings.Add("无法读取 " + logName + " 日志：" + ex.Message);
                Paths.Log("Event log failed: " + logName + " " + ex);
            }
        }

        private static void ApplyBugcheckCode(FaultEntry entry, string code, DatabaseService db)
        {
            entry.StopCode = DatabaseService.NormalizeCode(code);
            if (String.IsNullOrEmpty(entry.StopCode)) return;
            BugcheckRule rule = db.FindBugcheck(entry.StopCode);
            if (rule == null)
            {
                entry.CodeName = "本地数据库暂无记录";
                return;
            }
            entry.CodeName = rule.Name;
            entry.Advice = rule.Description + "\r\n" + rule.Advice;
        }

        // Kernel-Power 41 的 EventData.BugcheckCode 为十进制整数；非零表示系统记录了停止错误。
        // 不用 41/6008 的存在与否推断蓝屏。读取不到该字段时宁可不标红。
        private static string ReadKernelPower41Bugcheck(EventLogEntry record, string message)
        {
            string value = "";
            Match named = Regex.Match(message ?? "", @"\bBugcheckCode\s*[=:：]?\s*(0x[0-9a-fA-F]+|[0-9]+)\b", RegexOptions.IgnoreCase);
            if (named.Success) value = named.Groups[1].Value;
            if (String.IsNullOrEmpty(value))
            {
                try
                {
                    string[] data = record.ReplacementStrings;
                    // Windows 事件 41 的常见 EventData 顺序：第 7 个值为 BugcheckCode。
                    if (data != null && data.Length >= 10) value = data[6];
                }
                catch { }
            }
            if (String.IsNullOrWhiteSpace(value)) return "";
            value = value.Trim();
            uint number;
            bool ok;
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                ok = uint.TryParse(value.Substring(2), System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out number);
            else
                ok = uint.TryParse(value, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out number);
            return ok && number != 0 ? "0x" + number.ToString("X8") : "";
        }

        private static string Classify(string log, string source, int id)
        {
            string s = source.ToLowerInvariant();
            if (log == "System")
            {
                if (id == 1001 && (s.Contains("bugcheck") || s.Contains("wer-systemerrorreporting"))) return "蓝屏记录";
                if (id == 41 && s.Contains("kernel-power")) return "意外重启";
                if (id == 6008 && s.Contains("eventlog")) return "异常关机";
                if (id == 1074 && s.Contains("user32")) return "正常关机请求";
                if (id == 6005 && s.Contains("eventlog")) return "系统启动";
                if (id == 6006 && s.Contains("eventlog")) return "正常关机";
                if (s.Contains("service control manager") && (id == 7000 || id == 7001 || id == 7009 || id == 7011 || id == 7023 || id == 7031 || id == 7034)) return "服务异常";
                if (s.Contains("windowsupdateclient") && id == 20) return "更新失败";
                if (s.Contains("dns-client") && id == 1014) return "网络异常";
                if (s.Contains("whea-logger") && (id == 1 || id == 17 || id == 18 || id == 19 || id == 20 || id == 46)) return "硬件异常";
                if ((s == "disk" || s.Contains("ntfs") || s.Contains("storahci") || s.Contains("stornvme") || s.Contains("storport")) &&
                    (id == 7 || id == 11 || id == 15 || id == 51 || id == 55 || id == 129 || id == 153 || id == 157)) return "磁盘异常";
            }
            if (log == "Application")
            {
                if (id == 1000 && s.Contains("application error")) return "应用崩溃";
                if (id == 1026 && s.Contains(".net runtime")) return ".NET 异常";
                if (id == 1001 && s.Contains("windows error reporting")) return "应用错误报告";
                if (id == 1002 && s.Contains("application hang")) return "应用无响应";
            }
            CatalogEntry known = FaultCatalog.Find(log, source, id);
            if (known != null)
            {
                if (known.Group == "硬件") return "硬件异常";
                if (known.Group == "存储") return "磁盘异常";
                if (known.Group == "服务") return "服务异常";
                if (known.Group == "更新") return "更新失败";
                if (known.Group == "网络") return "网络异常";
                if (known.Group == "打印") return "打印异常";
                if (known.Group == "程序") return "应用异常";
            }
            return null;
        }

        public static string BuildReport(FaultScanResult result)
        {
            StringBuilder b = new StringBuilder();
            b.AppendLine("IT 桌面运维工具箱 · 故障检测报告");
            b.AppendLine("导出时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            b.AppendLine("计算机名称：" + Environment.MachineName);
            b.AppendLine("检测范围：最近 " + result.Days + " 天（每个日志最多检查 60000 条、提取 600 条相关事件）");
            b.AppendLine("蓝屏事件：" + result.BlueScreens);
            b.AppendLine("带蓝屏停止代码的意外重启事件：" + result.BlueScreenRelatedRestarts);
            b.AppendLine("异常关机事件与意外重启事件（可能重复指向同一次故障）：" + result.UnexpectedShutdowns);
            b.AppendLine("硬件错误事件：" + result.HardwareErrors);
            b.AppendLine("检测到的 DMP 文件：" + result.DumpFiles.Count);
            b.AppendLine("红色判定：BugCheck 1001 或 Kernel-Power 41 带非零 BugcheckCode；41/6008 无此证据不能单独视为蓝屏。");
            b.AppendLine("说明：Kernel-Power 41/6008 常发生于后续启动记录，不代表真正故障时刻。此报告未分析 DMP 调用栈。");
            b.AppendLine(new string('=', 65));
            b.AppendLine("【转储文件列表】");
            foreach (string d in result.DumpFiles) b.AppendLine(d);
            if (result.DumpFiles.Count == 0) b.AppendLine("未发现可访问的常用位置转储文件。");
            b.AppendLine("\r\n【相关事件】");
            foreach (FaultEntry e in result.Events)
            {
                b.AppendLine(new string('-', 65));
                b.AppendLine(e.Details());
            }
            b.AppendLine("\r\n【检测局限性与告警】");
            if (result.Warnings.Count == 0) b.AppendLine("无额外读取告警。");
            foreach (string w in result.Warnings) b.AppendLine(w);
            return b.ToString();
        }
    }
}
