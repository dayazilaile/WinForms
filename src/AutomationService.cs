using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Net;
using System.Net.Sockets;

namespace ITDesktopToolkit
{
    public sealed class AutoStep
    {
        public string Type, A, B, C;
        public override string ToString() { return Type + "  " + A + (String.IsNullOrWhiteSpace(B) ? "" : "  |  " + B) + (String.IsNullOrWhiteSpace(C) ? "" : "  |  " + C); }
    }
    // Both choices export a standalone .bat. The GUI editor is shared; only the
    // PowerShell input implementation differs. No toolkit installation on target PC.
    public enum BatInputEngine { SendInput, Legacy }

    // Shared script settings; editor and exported BAT use the same workflow.
    public sealed class AutoWorkflowSettings
    {
        public BatInputEngine Engine = BatInputEngine.SendInput;
        public bool HideConsole = false;
        public bool DeleteOnSuccess = true;
        public bool RunAsAdministrator = false;
        public bool DiagnosticMode = true;
        public int StartupDelaySeconds = 3;
    }

    public static partial class AutomationService
    {
        public static readonly string[] Types = { "运行程序", "等待毫秒", "随机等待", "等待窗口", "激活窗口", "移动鼠标", "鼠标左击", "鼠标双击", "鼠标右击", "按键/组合键", "输入文字", "滚轮", "设置 DNS", "设置默认网关", "设置静态 IPv4", "随机设置 IPv4", "IP及DNS切换 DHCP", "删除指定路径文件", "清空当前用户回收站", "格式化指定卷", "如果文件存在", "如果网络可达", "如果系统版本", "否则", "结束条件", "重试开始", "重试结束", "运行程序并等待", "检查文件存在" };
        public static string Guide(string type)
        {
            switch(type)
            {
                case "如果文件存在": return "填写文件完整路径，在两个分支栏添加动作；保存自动补齐否则和结束条件。";
                case "如果网络可达": return "填写单个 IP 或主机名，在两个分支栏添加动作；保存自动补齐结构。Ping 无响应也可能是 ICMP 被禁用。";
                case "如果系统版本": return "填写 Windows Build 下限（如22000）和可空的上限，在两个分支栏添加动作；保存自动补齐结构。";
                case "否则": return "位于条件块内；条件不满足时执行后续动作。每个条件只能有一个否则。";
                case "结束条件": return "结束最近的条件分支，不需要参数。";
                case "重试开始": return "参数1=最多尝试次数（1-10，默认3）；参数2=重试间隔秒（1-300，默认2）。失败会重新执行整个块，需确认操作可重复。";
                case "重试结束": return "闭合最近的重试块；格式化/删除动作不能放入重试块。";
                case "运行程序并等待": return "参数1=程序完整路径；参数2=参数（可空）；参数3=超时秒（1-86400，默认60）；非零退出码或超时视为失败。";
                case "检查文件存在": return "参数1=完整文件路径；文件不存在即失败，可结合重试块使用。";
                case "运行程序": return "参数1=程序完整路径；参数2=启动参数（可空）；参数3=工作目录（可空）";
                case "随机等待": return "填写最短和最长等待秒数（0–3600 的整数，含上下限），每次执行时重新随机抽取，允许重复；例如 1 到 20 秒。";
                case "等待毫秒": return "参数1=毫秒，可点击下方 1/2/3/4/5/10 秒快捷按钮，也支持手动填写例如 1500。";
                case "等待窗口": return "参数1=窗口标题关键词；参数2=超时秒数（1–120，默认120）。超时终止后续动作并按末尾删除选项清理 BAT。仅等待，不会激活窗口；按回车前请再加『激活窗口』动作。";
                case "激活窗口": return "参数1=窗口标题（可用『选择窗口』自动填写）；参数2=目标进程名（可空，推荐填写，可减少同名误选）。执行时尝试恢复最小化并激活窗口。";
                case "移动鼠标": return "参数1=X 坐标；参数2=Y 坐标，支持多屏负坐标";
                case "按键/组合键": return "参数1=TAB、SPACE、ENTER（回车）、ESC、F1-F12、CTRL+C、ALT+TAB、WIN+R 等";
                case "输入文字": return "参数1=要输入的文本，支持中文；发送至当前焦点窗口。传统兼容模式使用临时剪贴板粘贴，尽量恢复旧内容。";
                case "滚轮": return "参数1=滚动档数，正向上、负向下";
                case "设置 DNS": return "参数1=网卡名称或 自动有线/自动无线；参数2=首选DNS或 自动；参数3=备用DNS（可空）。需要管理员。";
                case "设置默认网关": return "参数1=网卡名称或 自动有线/自动无线；参数2=新的IPv4网关；参数3=路由跃点数（默认25）。要求静态IPv4。";
                case "随机设置 IPv4": return "指定网卡、网段/CIDR和网关；末段从 1–99、101–253 随机抽取，排除网关与其他网卡地址。设置后观察 10 秒，冲突换号；仅代表当时未检测到，不保证离线设备无重复。需要管理员。";
                case "设置静态 IPv4": return "参数1=网卡名称或 自动有线/自动无线；参数2=IPv4地址；参数3=子网掩码|网关，例如 255.255.255.0|192.168.1.1。";
                case "IP及DNS切换 DHCP": return "参数1=网卡名称或 自动有线/自动无线；参数2/3留空。将IP和DNS都改为自动获取。";
                case "删除指定路径文件": return "参数1=选择或填写文件的完整绝对路径（支持本地盘/UNC），参数2/3留空；只删除一个文件，不删除目录或匹配通配符，不进入回收站。";
                case "清空当前用户回收站": return "无需参数。永久清除 BAT 实际运行账户在所有驱动器的回收站内容；以其他管理员账户提权时会清理该管理员的回收站。";
                case "删除桌面指定文件": return "参数1=完整文件名（包含扩展名，例如 错误通知.docx、错误快捷方式.lnk）；参数2=当前用户桌面 / 公共桌面 / 两者；参数3留空。只按精确文件名删除，不支持文件夹、路径或通配符。删除不进入回收站。";
                case "格式化指定卷": return "参数1=D/E等明确盘符（仅D-Z）；参数2=NTFS或exFAT（默认NTFS）；参数3=新卷标（可空）。添加动作时会先确认一次；执行到此步骤即尝试清空目标卷，并自动阻止系统/启动盘和非固定盘。";
                default: return "不需要参数。";
            }
        }
        public static void Save(string path, List<AutoStep> steps)
        {
            Save(path, steps, new AutoWorkflowSettings());
        }
        // Backwards compatible for V0.2.4 integrations.
        public static void Save(string path, List<AutoStep> steps, bool deleteOnSuccess)
        {
            AutoWorkflowSettings options = new AutoWorkflowSettings();
            options.DeleteOnSuccess = deleteOnSuccess;
            Save(path, steps, options);
        }
        private static void CheckOptions(List<AutoStep> steps, AutoWorkflowSettings options)
        {
            if (steps == null) throw new ArgumentNullException("steps");
            if (options == null) throw new ArgumentNullException("options");
            ValidateFlow(steps);
            if (options.StartupDelaySeconds < 0 || options.StartupDelaySeconds > 3600)
                throw new ArgumentException("启动延时只能在 0 至 3600 秒之间。");
            if (HasFormatStep(steps) && options.HideConsole)
                throw new ArgumentException("流程含格式化磁盘动作时必须显示运行窗口，不能隐藏。该限制用于避免静默格式化。");
        }
        public static void Save(string path, List<AutoStep> steps, AutoWorkflowSettings options)
        {
            CheckOptions(steps, options);
            StringBuilder b = new StringBuilder("# ITToolbox Workflow V3 | UTF-8 | fields Base64\r\n");
            b.AppendLine("# EXPORT_ENGINE=" + (options.Engine == BatInputEngine.Legacy ? "Legacy" : "SendInput"));
            b.AppendLine("# HIDE_CONSOLE=" + (options.HideConsole ? "1" : "0"));
            b.AppendLine("# RUN_AS_ADMIN=" + (options.RunAsAdministrator ? "1" : "0"));
            b.AppendLine("# DIAGNOSTIC_MODE=" + (options.DiagnosticMode ? "1" : "0"));
            b.AppendLine("# STARTUP_DELAY_SECONDS=" + options.StartupDelaySeconds);
            b.AppendLine("# DELETE_ON_SUCCESS=" + (options.DeleteOnSuccess ? "15" : "0"));
            foreach (AutoStep x in steps) b.AppendLine(Convert.ToBase64String(Encoding.UTF8.GetBytes(x.Type ?? "")) + "\t" +
                Convert.ToBase64String(Encoding.UTF8.GetBytes(x.A ?? "")) + "\t" + Convert.ToBase64String(Encoding.UTF8.GetBytes(x.B ?? "")) + "\t" + Convert.ToBase64String(Encoding.UTF8.GetBytes(x.C ?? "")));
            File.WriteAllText(path, b.ToString(), new UTF8Encoding(true));
        }
        public static List<AutoStep> Load(string path)
        {
            AutoWorkflowSettings ignored;
            return Load(path, out ignored);
        }
        public static List<AutoStep> Load(string path, out bool deleteOnSuccess)
        {
            AutoWorkflowSettings options;
            List<AutoStep> result = Load(path, out options);
            deleteOnSuccess = options.DeleteOnSuccess;
            return result;
        }
        public static List<AutoStep> Load(string path, out AutoWorkflowSettings options)
        {
            options = new AutoWorkflowSettings();
            List<AutoStep> items = new List<AutoStep>();
            foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
            {
                if (line.StartsWith("# DELETE_ON_SUCCESS=", StringComparison.Ordinal))
                    options.DeleteOnSuccess = line.Substring("# DELETE_ON_SUCCESS=".Length).Trim() == "15";
                else if (line.StartsWith("# HIDE_CONSOLE=", StringComparison.Ordinal))
                    options.HideConsole = line.Substring("# HIDE_CONSOLE=".Length).Trim() == "1";
                else if (line.StartsWith("# RUN_AS_ADMIN=", StringComparison.Ordinal))
                    options.RunAsAdministrator = line.Substring("# RUN_AS_ADMIN=".Length).Trim() == "1";
                else if (line.StartsWith("# DIAGNOSTIC_MODE=", StringComparison.Ordinal))
                    options.DiagnosticMode = line.Substring("# DIAGNOSTIC_MODE=".Length).Trim() == "1";
                else if (line.StartsWith("# EXPORT_ENGINE=", StringComparison.Ordinal))
                    options.Engine = line.Substring("# EXPORT_ENGINE=".Length).Trim().Equals("Legacy", StringComparison.OrdinalIgnoreCase) ? BatInputEngine.Legacy : BatInputEngine.SendInput;
                else if (line.StartsWith("# STARTUP_DELAY_SECONDS=", StringComparison.Ordinal))
                {
                    int delay;
                    if (!Int32.TryParse(line.Substring("# STARTUP_DELAY_SECONDS=".Length).Trim(), out delay))
                        throw new FormatException("启动延迟字段无效。");
                    options.StartupDelaySeconds = delay;
                }
                else if (!line.StartsWith("#") && !String.IsNullOrWhiteSpace(line))
                {
                    string[] p = line.Split('\t');
                    if (p.Length != 4) throw new FormatException("工作流文件格式不正确。");
                    string actionType=Encoding.UTF8.GetString(Convert.FromBase64String(p[0]));
                    if(actionType == "格式化指定卷（人工确认）") actionType = "格式化指定卷";
                    items.Add(new AutoStep { Type=actionType, A=Encoding.UTF8.GetString(Convert.FromBase64String(p[1])), B=Encoding.UTF8.GetString(Convert.FromBase64String(p[2])), C=Encoding.UTF8.GetString(Convert.FromBase64String(p[3])) });
                }
            }
            CheckOptions(items,options);
            return items;
        }
        public static bool HasFormatStep(List<AutoStep> steps)
        {
            return steps != null && steps.Exists(delegate(AutoStep x) { return x != null && (x.Type == "格式化指定卷" || x.Type == "格式化指定卷（人工确认）"); });
        }
        // Restrict cleanup to ONE exact file name within explicitly selected desktop folders.
        // User-supplied absolute/relative paths, directory names and wildcards are rejected.
        public static string CheckDesktopFileName(string value)
        {
            string name=(value??"").Trim();
            if(name.Length==0 || name.Length>255 || name=="." || name==".." ||
               name.EndsWith(".",StringComparison.Ordinal) ||
               name.IndexOfAny(new char[]{'\\','/',':','*','?','"','<','>','|','\0'})>=0 ||
               name.Equals("desktop.ini",StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("请填写一个完整文件名（如 错误文件.docx）；不允许路径、通配符、文件夹、desktop.ini 或特殊文件名。");
            return name;
        }
        public static string CheckDesktopScope(string value)
        {
            string scope=(value??"").Trim();
            if(scope=="当前用户桌面" || scope=="公共桌面" || scope=="两者") return scope;
            throw new ArgumentException("桌面范围只能是：当前用户桌面、公共桌面、两者。");
        }
        public static string CheckDeleteFilePath(string value)
        {
            string raw=(value??"").Trim();
            if(raw.Length==0 || raw.IndexOfAny(new char[]{'*','?','"','<','>','|','\0','\r','\n'})>=0)
                throw new ArgumentException("请选择一个具体文件的完整路径，不允许通配符或特殊字符。");
            // Require an absolute drive path (C:\...) or UNC path (\\server\share\file).
            if(!System.Text.RegularExpressions.Regex.IsMatch(raw,@"^(?:[A-Za-z]:\\|\\\\[^\\]+\\[^\\]+\\).+"))
                throw new ArgumentException("请填写绝对文件路径，例如 D:\\Temp\\test.txt 或 \\\\\\server\\share\\test.txt。");
            string full=System.IO.Path.GetFullPath(raw);
            if(full.EndsWith("\\",StringComparison.Ordinal) || full.EndsWith("/",StringComparison.Ordinal))
                throw new ArgumentException("只允许选择文件，不能选择目录。");
            string root=System.IO.Path.GetPathRoot(full);
            if(String.Equals(full.TrimEnd('\\'),root.TrimEnd('\\'),StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("禁止删除磁盘根目录。");
            return full;
        }
        private static string FormatLetter(string value)
        {
            string s = (value ?? "").Trim().ToUpperInvariant();
            if (!System.Text.RegularExpressions.Regex.IsMatch(s, @"^[D-Z]:?\\?$"))
                throw new ArgumentException("格式化盘符仅允许 D: 至 Z: 的明确本地卷，不允许 C:、根路径或多个盘符。 ");
            return s.Substring(0, 1);
        }
        private static string FormatFileSystem(string value)
        {
            string s = String.IsNullOrWhiteSpace(value) ? "NTFS" : value.Trim().ToUpperInvariant();
            if (s != "NTFS" && s != "EXFAT") throw new ArgumentException("格式化仅支持 NTFS 或 exFAT。 ");
            return s;
        }
        private static string FormatLabel(string value)
        {
            string s = (value ?? "").Trim();
            if (s.Length > 15 || s.IndexOfAny(new char[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|' }) >= 0)
                throw new ArgumentException("卷标最长 15 字符，不能包含文件系统不支持的字符。 ");
            return s;
        }
        private static string P(string x) { return PowerShellService.Quote(x); }
        public static int[] RandomWaitBounds(string minimum,string maximum)
        {
            int low,high;
            if(!Int32.TryParse((minimum??"").Trim(),System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out low)||!Int32.TryParse((maximum??"").Trim(),System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out high)||low<0||high>3600||low>high)
                throw new ArgumentException("随机等待请填写 0–3600 的整数秒，最短时间不能大于最长时间。");
            return new[]{low,high};
        }
        private static int PositiveInt(string value, int fallback, int max)
        {
            int n;
            if (String.IsNullOrWhiteSpace(value)) return fallback;
            if (!Int32.TryParse(value, out n) || n < 0 || n > max) throw new ArgumentException("数值参数无效：" + value);
            return n;
        }
        private static string CheckIp(string value)
        {
            string s=(value ?? "").Trim();
            if (!System.Text.RegularExpressions.Regex.IsMatch(s, @"^([0-9]{1,3}\.){3}[0-9]{1,3}$"))
                throw new ArgumentException("IPv4地址格式不正确："+s);
            IPAddress ip; if(!IPAddress.TryParse(s,out ip) || ip.AddressFamily != AddressFamily.InterNetwork)
                throw new ArgumentException("不是有效的IPv4地址："+s);
            foreach(string piece in s.Split('.'))if(Int32.Parse(piece)>255)throw new ArgumentException("IPv4地址超出范围："+s);
            return s;
        }
        private static string CheckNic(string value)
        {
            string s=(value??"").Trim();
            if(String.IsNullOrWhiteSpace(s)||s.Length>128)throw new ArgumentException("网卡名称为空或过长。支持'自动有线'或'自动无线'。");
            return s;
        }
        private static int Coordinate(string value)
        {
            int n; if (!Int32.TryParse(value, out n) || n < -50000 || n > 50000) throw new ArgumentException("坐标值无效：" + value);
            return n;
        }
        private static string KeyCodes(string text)
        {
            Dictionary<string,int> m = new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase) {
                {"CTRL",17},{"CONTROL",17},{"ALT",18},{"SHIFT",16},{"WIN",91},{"WINDOWS",91},
                {"TAB",9},{"制表",9},{"SPACE",32},{"空格",32},{"ENTER",13},{"RETURN",13},{"回车",13},{"ESC",27},{"ESCAPE",27},{"退出",27},
                {"UP",38},{"DOWN",40},{"LEFT",37},{"RIGHT",39},{"BACKSPACE",8},{"DELETE",46},{"HOME",36},{"END",35},
                {"PAGEUP",33},{"PAGEDOWN",34},{"INSERT",45},
                {"NUMPAD0",96},{"NUMPAD1",97},{"NUMPAD2",98},{"NUMPAD3",99},{"NUMPAD4",100},
                {"NUMPAD5",101},{"NUMPAD6",102},{"NUMPAD7",103},{"NUMPAD8",104},{"NUMPAD9",105}
            };
            List<int> codes = new List<int>();
            foreach (string item in (text ?? "").ToUpperInvariant().Split('+'))
            {
                string s = item.Trim(); int v;
                if (m.TryGetValue(s, out v)) codes.Add(v);
                else if (s.Length == 1 && ((s[0] >= 'A' && s[0] <= 'Z') || (s[0] >= '0' && s[0] <= '9'))) codes.Add((int)s[0]);
                else if (s.StartsWith("F") && Int32.TryParse(s.Substring(1), out v) && v>=1 && v<=12) codes.Add(111+v);
                else throw new ArgumentException("不支持的键盘按键：" + s);
            }
            if (codes.Count == 0) throw new ArgumentException("未提供按键。");
            return "[System.UInt16[]]@(" + String.Join(",", codes) + ")";
        }
        private static string WinApiBridge = @"
using System;
using System.Runtime.InteropServices;
public static class ITAutomationInput {
 [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public UNION U; }
 [StructLayout(LayoutKind.Explicit)] public struct UNION { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
 [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int dx,dy; public uint mouseData, dwFlags,time; public IntPtr extra; }
 [StructLayout(LayoutKind.Sequential)] public struct KEYBDINPUT { public ushort vk,scan; public uint flags,time; public IntPtr extra; }
 [DllImport(""user32.dll"")] public static extern bool SetCursorPos(int x,int y);
 [DllImport(""user32.dll"")] public static extern bool SetProcessDPIAware();
 [DllImport(""user32.dll"")] private static extern uint SendInput(uint n, INPUT[] p, int size);
 private static void Send(INPUT i) { var a=new INPUT[]{i}; if(SendInput(1,a,Marshal.SizeOf(typeof(INPUT)))!=1) throw new Exception(""SendInput was blocked or failed""); }
 public static void Mouse(uint flags, int data) { var i=new INPUT(); i.type=0; i.U.mi=new MOUSEINPUT(); i.U.mi.dwFlags=flags; i.U.mi.mouseData=(uint)data; Send(i); }
 public static void Key(ushort vk, bool down) { var i=new INPUT(); i.type=1; i.U.ki=new KEYBDINPUT(); i.U.ki.vk=vk; i.U.ki.flags=down?0u:2u; Send(i); }
 public static void Hotkey(ushort[] keys) { for(int k=0;k<keys.Length;k++) Key(keys[k],true); for(int k=keys.Length-1;k>=0;k--) Key(keys[k],false); }
 public static void Text(string value) { foreach(char c in value) { var i=new INPUT(); i.type=1; i.U.ki=new KEYBDINPUT(); i.U.ki.scan=(ushort)c; i.U.ki.flags=4; Send(i); i.U.ki.flags=6; Send(i); } }
} ";
        private static string LegacyWinApiBridge = @"
using System;
using System.Runtime.InteropServices;
public static class ITAutomationLegacy {
 [DllImport(""user32.dll"")] public static extern bool SetCursorPos(int x, int y);
 [DllImport(""user32.dll"")] public static extern bool SetProcessDPIAware();
 [DllImport(""user32.dll"")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
 [DllImport(""user32.dll"")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
 public static void Mouse(uint flags, int data) { mouse_event(flags, 0, 0, unchecked((uint)data), UIntPtr.Zero); }
 public static void Key(byte vk, bool down) { keybd_event(vk, 0, down?0u:2u, UIntPtr.Zero); }
 public static void Hotkey(ushort[] keys) {
  for(int k=0;k<keys.Length;k++) { if(keys[k]>255) throw new ArgumentException(""VK out of range""); Key((byte)keys[k],true); }
  for(int k=keys.Length-1;k>=0;k--) Key((byte)keys[k],false);
 }
} ";

        // Cursor movement is isolated from the already-tested key and click bridges.
        // Uses physical pixels to match the editor's coordinate capture on mixed-DPI PCs.
        // No forced elevation: Windows may still block input on an elevated/secure desktop.
        private static string CursorWinApiBridge = @"
using System;
using System.Runtime.InteropServices;
public static class ITAutomationCursor {
 [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X; public int Y; }
 [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public UNION U; }
 [StructLayout(LayoutKind.Explicit)] public struct UNION { [FieldOffset(0)] public MOUSEINPUT mi; }
 [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr extra; }
 [DllImport(""user32.dll"", SetLastError=true)] private static extern bool SetPhysicalCursorPos(int x, int y);
 [DllImport(""user32.dll"", SetLastError=true)] private static extern bool SetCursorPos(int x, int y);
 [DllImport(""user32.dll"", SetLastError=true)] private static extern bool GetPhysicalCursorPos(out POINT p);
 [DllImport(""user32.dll"")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
 [DllImport(""user32.dll"")] private static extern int GetSystemMetrics(int index);
 [DllImport(""user32.dll"", SetLastError=true)] private static extern uint SendInput(uint count, INPUT[] items, int size);
 private static int Normalized(int value, int origin, int span) {
  double target = ((double)value - origin) * 65535.0 / (span - 1);
  return (int)Math.Max(0, Math.Min(65535, Math.Round(target)));
 }
 private static void Verify(int x,int y) {
  POINT actual;
  if(!GetPhysicalCursorPos(out actual)) throw new InvalidOperationException(""无法核对鼠标物理坐标，Win32=""+Marshal.GetLastWin32Error());
  if(Math.Abs((long)actual.X-x)>3 || Math.Abs((long)actual.Y-y)>3)
   throw new InvalidOperationException(""鼠标未到达目标物理坐标，可能存在光标锁定、显示器布局变化或桌面权限限制。实际(""+actual.X+"",""+actual.Y+"")，目标(""+x+"",""+y+"")"");
 }
 public static void MoveTo(int x, int y) {
  IntPtr previous=IntPtr.Zero;
  try {
   try { previous=SetThreadDpiAwarenessContext(new IntPtr(-4)); if(previous==IntPtr.Zero)previous=SetThreadDpiAwarenessContext(new IntPtr(-3)); } catch(EntryPointNotFoundException) {}
   MovePhysical(x,y,previous!=IntPtr.Zero);
  } finally { if(previous!=IntPtr.Zero)SetThreadDpiAwarenessContext(previous); }
 }
 private static void MovePhysical(int x,int y,bool physicalContext) {
  int physicalErr = -1, cursorErr = -1, sendErr = -1;
  try { if(SetPhysicalCursorPos(x,y)) { Verify(x,y); return; } physicalErr=Marshal.GetLastWin32Error(); }
  catch(EntryPointNotFoundException) { physicalErr=-2; }
  if(!physicalContext)throw new InvalidOperationException(""物理坐标移动失败且无法启用逐显示器 DPI 上下文，未回退到可能偏移的逻辑坐标。Win32=""+physicalErr);
  if(SetCursorPos(x,y)) { Verify(x,y); return; }
  cursorErr=Marshal.GetLastWin32Error();
  // The virtual desktop includes secondary monitors with negative coordinates.
  int left=GetSystemMetrics(76), top=GetSystemMetrics(77);
  int width=GetSystemMetrics(78), height=GetSystemMetrics(79);
  if(width<=1 || height<=1) throw new InvalidOperationException(""无法读取显示器坐标范围，Win32=""+cursorErr);
  if(x<left || y<top || (long)x >= (long)left+width || (long)y >= (long)top+height)
   throw new ArgumentOutOfRangeException(""坐标"",""坐标超出当前显示器范围。目标(""+x+"",""+y+"")，桌面(""+left+"",""+top+"",""+width+"",""+height+"")"");
  INPUT data=new INPUT(); data.type=0;
  data.U.mi=new MOUSEINPUT();
  data.U.mi.dx=Normalized(x,left,width);
  data.U.mi.dy=Normalized(y,top,height);
  data.U.mi.dwFlags=0x0001u | 0x4000u | 0x8000u;
  INPUT[] inputs=new INPUT[]{data};
  if(SendInput(1,inputs,Marshal.SizeOf(typeof(INPUT)))==1) {
   System.Threading.Thread.Sleep(40);
   Verify(x,y);
   return;
  }
  sendErr=Marshal.GetLastWin32Error();
  throw new InvalidOperationException(""鼠标移动失败；SetPhysicalCursorPos 错误=""+physicalErr+
    ""，SetCursorPos 错误=""+cursorErr+""，SendInput 错误=""+sendErr+
    ""。请确认在交互式桌面运行、目标程序权限不高于 BAT、未处于 UAC 安全桌面。"");
 }
} ";

        // Windows PowerShell 5.1 / .NET Framework clipboard-based text entry.
        // Legacy key/mouse events remain keybd_event/mouse_event; only the text
        // insertion route changes to Ctrl+V to avoid Chinese IME composition.
        // Uses clipboard in memory only; creates no temporary files.
        // STA is required and is supplied by the generated BAT (-STA).
        private static string LegacyTextHelper = @"
Add-Type -AssemblyName System.Windows.Forms
function Send-LegacyText([string]$value) {
 if([string]::IsNullOrEmpty($value)) { return }
 $savedObject=$null
 $savedText=$null
 $hasSavedText=$false
 $ready=$false
 for($attempt=0; $attempt -lt 8; $attempt++) {
  try {
   $savedObject=[System.Windows.Forms.Clipboard]::GetDataObject()
   if($null -ne $savedObject -and $savedObject.GetDataPresent([System.Windows.Forms.DataFormats]::UnicodeText)) {
    $savedText=[string]$savedObject.GetData([System.Windows.Forms.DataFormats]::UnicodeText)
    $hasSavedText=$true
   }
   [System.Windows.Forms.Clipboard]::SetText($value, [System.Windows.Forms.TextDataFormat]::UnicodeText)
   $ready=$true
   break
  } catch { Start-Sleep -Milliseconds 100 }
 }
 if(-not $ready) { throw '剪贴板无法写入，可能被其他程序占用。未发送文本。' }
 try {
  Start-Sleep -Milliseconds 100
  [ITAutomationLegacy]::Hotkey([System.UInt16[]]@(17,86))
  # Allow the foreground application to process Ctrl+V before restoration.
  Start-Sleep -Milliseconds 450
 } finally {
  $restored=$false
  for($attempt=0; $attempt -lt 8; $attempt++) {
   try {
    if($null -ne $savedObject) {
     [System.Windows.Forms.Clipboard]::SetDataObject($savedObject,$true)
    } else {
     [System.Windows.Forms.Clipboard]::Clear()
    }
    $restored=$true
    break
   } catch { Start-Sleep -Milliseconds 100 }
  }
  if(-not $restored -and $hasSavedText) {
   try { [System.Windows.Forms.Clipboard]::SetText($savedText); $restored=$true } catch {}
  }
  if(-not $restored) {
   Write-Warning '原剪贴板未能恢复，请检查剪贴板内容。'
  }
 }
}
";

        // Diagnostic helper prints ONLY to the active console (no files/network).
        // Allows technician to compare DPI, virtual screen, ClipCursor, focus and
        // integrity between lab PCs while keeping existing SendInput intact.
        private static string DiagnosticWinApiBridge = @"
using System;
using System.Text;
using System.Runtime.InteropServices;
using System.Diagnostics;
public static class ITAutomationDiag {
 [StructLayout(LayoutKind.Sequential)] public struct PT { public int X,Y; }
 [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left,Top,Right,Bottom; }
 [DllImport(""user32.dll"")] private static extern int GetSystemMetrics(int n);
 [DllImport(""user32.dll"",SetLastError=true)] private static extern bool GetClipCursor(out RECT r);
 [DllImport(""user32.dll"",SetLastError=true)] private static extern bool GetPhysicalCursorPos(out PT p);
 [DllImport(""user32.dll"")] private static extern IntPtr GetForegroundWindow();
 [DllImport(""user32.dll"",CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr h,StringBuilder b,int n);
 [DllImport(""user32.dll"")] private static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
 [DllImport(""user32.dll"")] private static extern uint GetDpiForSystem();
 public static void ScreenInfo() {
  Console.WriteLine(""[DIAG] OS=""+Environment.OSVersion+"" 64bit=""+Environment.Is64BitOperatingSystem);
  Console.WriteLine(""[DIAG] primary=""+GetSystemMetrics(0)+""x""+GetSystemMetrics(1)+"" virtual=""+GetSystemMetrics(76)+"",""+GetSystemMetrics(77)+"" ""+GetSystemMetrics(78)+""x""+GetSystemMetrics(79));
  try { Console.WriteLine(""[DIAG] systemDPI=""+GetDpiForSystem()); } catch(EntryPointNotFoundException) { Console.WriteLine(""[DIAG] systemDPI=n/a""); }
  RECT r; if(GetClipCursor(out r)) Console.WriteLine(""[DIAG] mouseClip=(""+r.Left+"",""+r.Top+"")-(""+r.Right+"",""+r.Bottom+"")"");
  else Console.WriteLine(""[DIAG] mouseClip=unavailable Win32=""+Marshal.GetLastWin32Error());
  PT pt; if(GetPhysicalCursorPos(out pt)) Console.WriteLine(""[DIAG] currentPhysicalMouse=(""+pt.X+"",""+pt.Y+"")"");
  else Console.WriteLine(""[DIAG] currentPhysicalMouse=unavailable Win32=""+Marshal.GetLastWin32Error());
  try { var id=System.Security.Principal.WindowsIdentity.GetCurrent(); var pr=new System.Security.Principal.WindowsPrincipal(id); Console.WriteLine(""[DIAG] adminToken=""+pr.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator)); } catch(Exception e) { Console.WriteLine(""[DIAG] adminToken=unknown ""+e.GetType().Name); }
 }
 public static void FocusInfo() {
  IntPtr h=GetForegroundWindow(); uint pid=0;
  if(h!=IntPtr.Zero) GetWindowThreadProcessId(h,out pid);
  StringBuilder b=new StringBuilder(260); if(h!=IntPtr.Zero) GetWindowText(h,b,b.Capacity);
  string proc=""unknown""; try { if(pid!=0) proc=Process.GetProcessById((int)pid).ProcessName; }catch { proc=""unknown""; }
  Console.WriteLine(""[DIAG] Foreground HWND=""+h+"" PID=""+pid+"" EXE=""+proc+"" TITLE=""+b.ToString());
 }
} ";

        // Read-only checks run in an in-process PowerShell instance, with a bounded wait.
        // A timed-out native read may resist cancellation; never synchronously Dispose it.
        // The failed workflow exits its own PowerShell host, releasing remaining resources.
        private const string BoundedCheckHelper = @"
function Invoke-ITBoundedCheck([string]$label, [scriptblock]$test, [int]$seconds=120) {
 $worker=$null; $pending=$null; $finished=$false
 try {
  $worker=[PowerShell]::Create()
  [void]$worker.AddScript('$ErrorActionPreference=''Stop''; '+$test.ToString())
  $pending=$worker.BeginInvoke()
  if(-not $pending.AsyncWaitHandle.WaitOne($seconds*1000)) { throw ($label+'超时（'+$seconds+'秒），已停止后续动作。') }
  $finished=$true
  $values=@($worker.EndInvoke($pending))
  if($worker.HadErrors) { throw ($label+'判断失败：'+($worker.Streams.Error | Out-String)) }
  if($values.Count -ne 1 -or $values[0] -isnot [bool]) { throw ($label+'未返回明确的是/否结果。') }
  return [bool]$values[0]
 } catch {
  $script:itAbortWorkflow=$true
  throw
 } finally {
  if($null -ne $worker) {
   if($finished -or $null -eq $pending) { $worker.Dispose() }
   else { try { [void]$worker.BeginStop($null,$null) } catch {} }
  }
 }
}
";
        public static string MakeScript(List<AutoStep> steps)
        {
            return MakeScript(steps, BatInputEngine.SendInput);
        }
        public static string MakeScript(List<AutoStep> steps, BatInputEngine engine)
        {
            return MakeScript(steps, engine, 3, true);
        }
        public static string MakeScript(List<AutoStep> steps, BatInputEngine engine, int startupDelaySeconds)
        {
            return MakeScript(steps, engine, startupDelaySeconds, true);
        }
        public static string MakeScript(List<AutoStep> steps, BatInputEngine engine, int startupDelaySeconds, bool diagnostic)
        {
            if (startupDelaySeconds < 0 || startupDelaySeconds > 3600) throw new ArgumentException("启动延时超出范围。");
            ValidateFlow(steps);
            if (steps.Count == 0) throw new ArgumentException("至少添加一个步骤。 ");
            StringBuilder b = new StringBuilder();
            b.AppendLine("$ErrorActionPreference='Stop'");
            b.AppendLine("$script:itAbortWorkflow=$false");
            if(steps.Exists(delegate(AutoStep x){return AutomationBlockService.IsCondition(x.Type)||x.Type=="等待窗口";})) b.AppendLine(BoundedCheckHelper);
            b.AppendLine("try {");
            string apiClass = engine == BatInputEngine.Legacy ? "ITAutomationLegacy" : "ITAutomationInput";
            bool needsInput=steps.Exists(delegate(AutoStep x){return x!=null&&(x.Type=="移动鼠标"||x.Type=="鼠标左击"||x.Type=="鼠标双击"||x.Type=="鼠标右击"||x.Type=="按键/组合键"||x.Type=="输入文字"||x.Type=="滚轮");});
            if(needsInput||diagnostic){
                b.AppendLine("Add-Type -TypeDefinition @'\r\n" + (engine == BatInputEngine.Legacy ? LegacyWinApiBridge : WinApiBridge) + "\r\n'@");
                b.AppendLine("try { [void][" + apiClass + "]::SetProcessDPIAware() } catch {}");
            }
            bool hasMouseMove = steps.Exists(delegate(AutoStep x) { return x != null && x.Type == "移动鼠标"; });
            if (hasMouseMove)
                b.AppendLine("Add-Type -TypeDefinition @'\r\n" + CursorWinApiBridge + "\r\n'@");
            if (diagnostic) {
                b.AppendLine("Add-Type -TypeDefinition @'\r\n" + DiagnosticWinApiBridge + "\r\n'@");
                b.AppendLine("[ITAutomationDiag]::ScreenInfo()");
                b.AppendLine("[ITAutomationDiag]::FocusInfo()");
            }
            // V0.3.1: Load the optional window bridge ONLY when a window-activation
            // step exists. A simple Enter/Tab hotkey workflow must have the same
            // PowerShell startup path as V0.2.8, which the user has tested.
            bool hasWindowActivation = steps.Exists(delegate(AutoStep x) { return x != null && x.Type == "激活窗口"; });
            if (hasWindowActivation)
            {
                b.AppendLine("$shell=New-Object -ComObject WScript.Shell");
                b.AppendLine("Add-Type -TypeDefinition @'\r\nusing System; using System.Runtime.InteropServices; public static class ITAutomationWindow { [DllImport(\"user32.dll\")] public static extern bool IsIconic(IntPtr h); [DllImport(\"user32.dll\")] public static extern bool ShowWindowAsync(IntPtr h, int cmd); [DllImport(\"user32.dll\")] public static extern bool SetForegroundWindow(IntPtr h); }\r\n'@");
            }

            // Network steps use Windows built-in PowerShell NetTCPIP/DnsClient cmdlets.
            // Fail closed if an auto selector has zero or multiple physical matches.
            bool hasNetwork = steps.Exists(delegate(AutoStep x){return x.Type=="设置 DNS"||x.Type=="设置默认网关"||x.Type=="设置静态 IPv4"||x.Type=="随机设置 IPv4"||x.Type=="IP及DNS切换 DHCP";});
            if(hasNetwork)
            {
                b.AppendLine(@"function Assert-Admin { $id=[Security.Principal.WindowsIdentity]::GetCurrent(); $p=New-Object Security.Principal.WindowsPrincipal($id); if(-not $p.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw '网络配置需要管理员权限，请右键以管理员身份运行 BAT。'} }");
                b.AppendLine(@"function Target-Nic([string]$name) { if($name -ne '自动有线' -and $name -ne '自动无线') { $n=Get-NetAdapter -Name $name -ErrorAction Stop; if($n.Status -eq 'Disabled'){throw '目标网卡已禁用'}; return $n.Name }; $all=@(Get-NetAdapter -Physical -ErrorAction Stop | Where-Object {$_.Status -ne 'Disabled'}); $list=@($all|Where-Object { $wifi=($_.NdisPhysicalMedium -eq 9) -or ($_.InterfaceDescription -match 'Wireless|Wi-Fi|WLAN|802\.11'); if($name -eq '自动无线'){$wifi}else{-not $wifi} }); if($list.Count -ne 1){throw ('自动筛选网卡结果为 '+$list.Count+' 个，未执行修改。请改填明确的网卡名称。')}; return $list[0].Name }");
                b.AppendLine(@"function Backup-Net { $file=Join-Path $env:TEMP ('ITToolkit_IPV4_Before_'+$env:COMPUTERNAME+'_'+(Get-Date -Format yyyyMMdd_HHmmss_fff)+'.txt'); (& netsh.exe interface ipv4 dump) | Out-File -FilePath $file -Encoding Unicode; Write-Host ('旧IPv4配置备份：'+$file) }");
                b.AppendLine("Assert-Admin");
            }
            if(steps.Exists(delegate(AutoStep x){return x.Type=="随机设置 IPv4";}))b.AppendLine(RandomIpv4Helper);
            if (HasFormatStep(steps))
            {
                b.AppendLine(@"function Invoke-FormatStep([string]$drive, [string]$filesystem, [string]$newLabel) {");
                b.AppendLine(@" $identity=[Security.Principal.WindowsIdentity]::GetCurrent(); $principal=New-Object Security.Principal.WindowsPrincipal($identity)");
                b.AppendLine(@" if(-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw '格式化卷需要以管理员身份运行 BAT。' }");
                b.AppendLine(@" if($drive -notmatch '^[D-Z]$') { throw '只允许明确的 D-Z 本地卷盘符。' }");
                b.AppendLine(@" $systemDrive=$env:SystemDrive.TrimEnd(':').ToUpperInvariant(); if($drive -eq $systemDrive) { throw '不能格式化系统盘。' }");
                b.AppendLine(@" $part=Get-Partition -DriveLetter $drive -ErrorAction Stop; if($part.IsBoot -or $part.IsSystem) { throw '卷标识为启动卷或系统卷，操作已阻止。' }");
                b.AppendLine(@" $vol=Get-Volume -DriveLetter $drive -ErrorAction Stop; if($vol.DriveType -ne 'Fixed') { throw '仅支持本地固定磁盘卷，外接可移动卷请使用 Windows 磁盘管理。' }");
                b.AppendLine(@" if($vol.FileSystem -notin @('NTFS','ReFS','exFAT','FAT32')) { throw '卷文件系统不受支持或尚未就绪，已停止。' }");
                b.AppendLine(@" if(Get-Command Get-BitLockerVolume -ErrorAction SilentlyContinue) { $bl=Get-BitLockerVolume -MountPoint ($drive+':') -ErrorAction SilentlyContinue; if($bl -and $bl.ProtectionStatus -eq 'On') { throw 'BitLocker 保护中的卷禁止在脚本中格式化。' } }");
                b.AppendLine(@" $displaySize=[Math]::Round($vol.Size/1GB,2); Write-Host ('目标卷：'+$drive+':  卷标：'+$vol.FileSystemLabel+'  容量：'+$displaySize+' GiB  文件系统：'+$vol.FileSystem) -ForegroundColor Yellow");
                b.AppendLine(@" Write-Host '警告：格式化会删除该卷的数据。请核实盘符、分区和备份；没有自动恢复能力。' -ForegroundColor Red");
                b.AppendLine(@" $args=@{DriveLetter=$drive; FileSystem=$filesystem; Confirm=$false; ErrorAction='Stop'}; if(-not [string]::IsNullOrWhiteSpace($newLabel)) { $args['NewFileSystemLabel']=$newLabel }");
                b.AppendLine(@" Format-Volume @args | Format-List DriveLetter,FileSystem,FileSystemLabel,HealthStatus | Out-String | Write-Host");
                b.AppendLine("}");
            }
            // Desktop cleanup is independent of keyboard/mouse input and permissions.
            // Paths are resolved at RUN time on the target PC, including redirected Desktop folders.
            if (steps.Exists(delegate(AutoStep x) { return x != null && x.Type == "删除桌面指定文件"; }))
            {
                b.AppendLine(@"function Remove-DesktopExactFile([string]$fileName, [string]$scope) {");
                b.AppendLine(@" $dirs=@()");
                b.AppendLine(@" if($scope -eq '当前用户桌面' -or $scope -eq '两者') { $dirs+= [Environment]::GetFolderPath('DesktopDirectory') }");
                b.AppendLine(@" if($scope -eq '公共桌面' -or $scope -eq '两者') { $dirs+= [Environment]::GetFolderPath('CommonDesktopDirectory') }");
                b.AppendLine(@" foreach($dir in ($dirs | Select-Object -Unique)) {");
                b.AppendLine(@"   if([string]::IsNullOrWhiteSpace($dir) -or -not [IO.Directory]::Exists($dir)) { throw ('无法访问桌面目录：'+$dir) }");
                b.AppendLine(@"   $target=[IO.Path]::Combine($dir,$fileName)");
                b.AppendLine(@"   if(Test-Path -LiteralPath $target -PathType Container) { throw ('目标是文件夹，为避免误删已停止：'+$target) }");
                b.AppendLine(@"   if(-not (Test-Path -LiteralPath $target -PathType Leaf)) { Write-Host ('[SKIP] 文件不存在：'+$target); continue }");
                b.AppendLine(@"   Remove-Item -LiteralPath $target -Force -ErrorAction Stop");
                b.AppendLine(@"   if(Test-Path -LiteralPath $target) { throw ('删除后目标仍存在：'+$target) }");
                b.AppendLine(@"   Write-Host ('[OK] 已删除：'+$target) -ForegroundColor Green");
                b.AppendLine(@" }");
                b.AppendLine(@"}");
            }
            if (steps.Exists(delegate(AutoStep x) { return x != null && x.Type == "删除指定路径文件"; }))
            {
                b.AppendLine(@"function Remove-SelectedExactFile([string]$target) {");
                b.AppendLine(@" if([string]::IsNullOrWhiteSpace($target) -or $target.IndexOfAny([char[]]@('*','?')) -ge 0){throw '文件路径无效'}");
                b.AppendLine(@" if(Test-Path -LiteralPath $target -PathType Container){throw ('目标是目录，已拒绝删除：'+$target)}");
                b.AppendLine(@" if(-not(Test-Path -LiteralPath $target -PathType Leaf)){Write-Host ('[SKIP] 文件不存在：'+$target);return}");
                b.AppendLine(@" Remove-Item -LiteralPath $target -Force -ErrorAction Stop");
                b.AppendLine(@" if(Test-Path -LiteralPath $target){throw ('删除后仍存在：'+$target)}");
                b.AppendLine(@" Write-Host ('[OK] 删除指定文件：'+$target) -ForegroundColor Green");
                b.AppendLine(@"}");
            }
            if (engine == BatInputEngine.Legacy && steps.Exists(delegate(AutoStep x) { return x != null && x.Type == "输入文字"; }))
                b.AppendLine(LegacyTextHelper);
            b.AppendLine("Write-Host " + P(engine == BatInputEngine.Legacy ? "传统兼容模式：mouse_event + keybd_event；文字使用剪贴板粘贴（避免中文输入法拦截）" : "新版模式：SendInput + Unicode 键盘输入"));
            b.AppendLine("Write-Host " + P("IT Toolkit: 启动前等待 " + startupDelaySeconds + " 秒。可在可见控制台按 Ctrl+C 中止。"));
            if (startupDelaySeconds > 0) b.AppendLine("Start-Sleep -Seconds " + startupDelaySeconds);
            var flowStack=new Stack<FlowBlock>();
            for (int i=0;i<steps.Count;i++)
            {
                AutoStep s = steps[i];
                if(EmitFlow(s,b,flowStack,i))continue;
                b.AppendLine("Write-Host " + P("步骤 " + (i+1) + "/" + steps.Count + " - " + s.Type));
                if (diagnostic && (s.Type == "按键/组合键" || s.Type == "输入文字" || s.Type == "移动鼠标" || s.Type == "鼠标左击" || s.Type == "鼠标双击" || s.Type == "鼠标右击"))
                    b.AppendLine("[ITAutomationDiag]::FocusInfo()");
                switch(s.Type)
                {
                    case "检查文件存在":
                        b.AppendLine("if(-not(Test-Path -LiteralPath "+P(FlowValue(s.A))+" -PathType Leaf)){throw '所需文件不存在'}");break;
                    case "运行程序并等待":
                        b.AppendLine("$itProc=Start-Process -FilePath "+P(FlowValue(s.A))+(String.IsNullOrWhiteSpace(s.B)?"":" -ArgumentList "+P(s.B))+" -PassThru -ErrorAction Stop; if(-not $itProc.WaitForExit("+(FlowInt(s.C,60,86400)*1000)+")){try{$itProc.Kill()}catch{};throw '程序执行超时，已尝试结束主进程'}; if($itProc.ExitCode -ne 0){throw ('程序返回失败码：'+$itProc.ExitCode)}");break;
                    case "运行程序":
                        if (String.IsNullOrWhiteSpace(s.A)) throw new ArgumentException("运行程序路径为空。");
                        string directory = String.IsNullOrWhiteSpace(s.C) ? System.IO.Path.GetDirectoryName(s.A) : s.C;
                        b.AppendLine("$p=Start-Process -FilePath " + P(s.A) + (String.IsNullOrWhiteSpace(s.B) ? "" : " -ArgumentList " + P(s.B)) +
                            (String.IsNullOrWhiteSpace(directory) ? "" : " -WorkingDirectory " + P(directory)) + " -PassThru -ErrorAction Stop");
                        break;
                    case "随机等待":
                        int[] seconds=RandomWaitBounds(s.A,s.B);string randomWait="$itRandomWaitSeconds"+i;
                        b.AppendLine(randomWait+" = Get-Random -Minimum "+seconds[0]+" -Maximum "+(seconds[1]+1));
                        b.AppendLine("Write-Host ('本次随机等待 ' + "+randomWait+" + ' 秒')");
                        b.AppendLine("Start-Sleep -Seconds "+randomWait);break;
                    case "等待毫秒": b.AppendLine("Start-Sleep -Milliseconds " + PositiveInt(s.A,1000,3600000)); break;
                    case "等待窗口":
                        int windowTimeout=FlowInt(s.B,120,120);
                        b.AppendLine("$found=Invoke-ITBoundedCheck '等待窗口' { $deadline=(Get-Date).AddSeconds("+windowTimeout+"); while((Get-Date) -lt $deadline){if(Get-Process | Where-Object {$_.MainWindowTitle -like ('*'+"+P(FlowValue(s.A))+"+'*')} | Select-Object -First 1){return $true}; Start-Sleep -Milliseconds 250}; return $false } "+windowTimeout+"; if(-not $found){$script:itAbortWorkflow=$true; throw '窗口超时未出现，已停止后续动作。'}");
                        break;
                    case "激活窗口":
                    {
                        if(String.IsNullOrWhiteSpace(s.A))throw new ArgumentException("请设置目标窗口标题。 ");
                        string proc=String.IsNullOrWhiteSpace(s.B)?"":s.B.Trim();
                        if(proc.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))proc=proc.Substring(0,proc.Length-4);
                        b.AppendLine("$winTitle="+P(s.A)+"; $winProcess="+P(proc)+"; "+
                            "$matches=@(Get-Process -ErrorAction SilentlyContinue | Where-Object { "+
                            "$_.MainWindowHandle -ne [IntPtr]::Zero -and $_.MainWindowTitle -like ('*'+$winTitle+'*') -and "+
                            "([string]::IsNullOrEmpty($winProcess) -or $_.ProcessName -eq $winProcess) }); "+
                            "if($matches.Count -gt 1){throw '匹配到多个窗口，请在编辑器中指定更精确的窗口标题和进程名。'}; "+
                            "if($matches.Count -eq 1){$handle=$matches[0].MainWindowHandle; "+
                            "if([ITAutomationWindow]::IsIconic($handle)){[void][ITAutomationWindow]::ShowWindowAsync($handle,9); Start-Sleep -Milliseconds 250}; "+
                            "if(-not [ITAutomationWindow]::SetForegroundWindow($handle)){ "+
                            "if(-not $shell.AppActivate([int]$matches[0].Id)){throw 'Windows 阻止窗口激活'} } "+
                            "}else{if(-not $shell.AppActivate($winTitle)){throw '找不到目标窗口或无法激活'}}; Start-Sleep -Milliseconds 450");
                        break;
                    }
                    case "移动鼠标": b.AppendLine("[ITAutomationCursor]::MoveTo(" + Coordinate(s.A) + "," + Coordinate(s.B) + ")"); break;
                    case "鼠标左击": b.AppendLine("[" + apiClass + "]::Mouse(2,0); [" + apiClass + "]::Mouse(4,0)"); break;
                    case "鼠标双击": b.AppendLine("[" + apiClass + "]::Mouse(2,0); [" + apiClass + "]::Mouse(4,0); Start-Sleep -Milliseconds 80; [" + apiClass + "]::Mouse(2,0); [" + apiClass + "]::Mouse(4,0)"); break;
                    case "鼠标右击": b.AppendLine("[" + apiClass + "]::Mouse(8,0); [" + apiClass + "]::Mouse(16,0)"); break;
                    case "按键/组合键": b.AppendLine("[" + apiClass + "]::Hotkey(" + KeyCodes(s.A) + "); Start-Sleep -Milliseconds 100"); break;
                    case "输入文字": b.AppendLine(engine == BatInputEngine.Legacy ? "Send-LegacyText " + P(s.A) : "[ITAutomationInput]::Text(" + P(s.A) + ")"); break;
                    case "设置 DNS":
                    {
                        string nic=CheckNic(s.A);
                        string primary=(s.B??"").Trim();
                        string secondary=(s.C??"").Trim();
                        if(primary.Equals("自动",StringComparison.OrdinalIgnoreCase)||primary.Equals("DHCP",StringComparison.OrdinalIgnoreCase))
                        {
                            if(secondary.Length>0)throw new ArgumentException("自动DNS模式下备用DNS必须为空。 ");
                            b.AppendLine("$nic=Target-Nic " + P(nic) + "; Backup-Net; Set-DnsClientServerAddress -InterfaceAlias $nic -ResetServerAddresses -ErrorAction Stop");
                        }
                        else
                        {
                            string dns=CheckIp(primary);
                            string servers="@("+P(dns)+(secondary.Length>0?","+P(CheckIp(secondary)):"")+")";
                            b.AppendLine("$nic=Target-Nic " + P(nic) + "; Backup-Net; Set-DnsClientServerAddress -InterfaceAlias $nic -ServerAddresses " + servers + " -ErrorAction Stop");
                        }
                        b.AppendLine("Get-DnsClientServerAddress -InterfaceAlias $nic -AddressFamily IPv4 | Format-List | Out-String | Write-Host");
                        break;
                    }
                    case "随机设置 IPv4":
                    {
                        var randomIp=RandomIpv4(s.A,s.B,s.C);b.AppendLine("$itRandomAddress=Invoke-RandomIPv4 "+P(randomIp.Nic)+" "+P(randomIp.Base)+" "+randomIp.Prefix+" "+P(randomIp.Gateway)+" ([int[]]@("+String.Join(",",Array.ConvertAll(randomIp.Hosts,delegate(int n){return n.ToString(System.Globalization.CultureInfo.InvariantCulture); }))+"))");
                        if(randomIp.Finish)b.AppendLine("Write-Host 'All steps complete.'; return");break;
                    }
                    case "设置静态 IPv4":
                    {
                        string nic=CheckNic(s.A),address=CheckIp(s.B);
                        string[] parts=(s.C??"").Split('|');
                        if(parts.Length!=2)throw new ArgumentException("参数3必须填写：子网掩码|网关。");
                        string mask=CheckIp(parts[0]),gateway=CheckIp(parts[1]);
                        b.AppendLine("$nic=Target-Nic " + P(nic) + "; Backup-Net; & netsh.exe interface ipv4 set address name=$nic source=static address=" + P(address) + " mask=" + P(mask) + " gateway=" + P(gateway) + " store=persistent");
                        b.AppendLine("if($LASTEXITCODE -ne 0){throw ('netsh设置静态IPv4失败，退出码：'+$LASTEXITCODE)}; $verified=$false; for($try=0;$try -lt 6;$try++){ $current=Get-NetIPInterface -InterfaceAlias $nic -AddressFamily IPv4 -ErrorAction Stop; $addresses=@(Get-NetIPAddress -InterfaceAlias $nic -AddressFamily IPv4 -ErrorAction SilentlyContinue | Where-Object {$_.IPAddress -eq " + P(address) + "}); if($current.Dhcp -eq 'Disabled' -and $addresses.Count -gt 0){$verified=$true;break}; Start-Sleep -Milliseconds 400 }; if(-not $verified){throw '静态IPv4命令返回成功，但实际网卡仍为DHCP或目标IP未生效，已停止后续步骤'}; Get-NetIPConfiguration -InterfaceAlias $nic | Format-List | Out-String | Write-Host");
                        break;
                    }
                    case "IP及DNS切换 DHCP":
                    {
                        string nic=CheckNic(s.A);
                        b.AppendLine("$nic=Target-Nic " + P(nic) + "; Backup-Net; & netsh.exe interface ipv4 set address name=$nic source=dhcp");
                        b.AppendLine("if($LASTEXITCODE -ne 0){throw '启用DHCP失败'}; Set-DnsClientServerAddress -InterfaceAlias $nic -ResetServerAddresses -ErrorAction Stop; Get-NetIPConfiguration -InterfaceAlias $nic | Format-List | Out-String | Write-Host");
                        break;
                    }
                    case "设置默认网关":
                    {
                        string nic=CheckNic(s.A),gateway=CheckIp(s.B);
                        int metric=PositiveInt(s.C,25,9999);if(metric<1||metric>9999)throw new ArgumentException("路由跃点数必须在1～9999之间。 ");
                        b.AppendLine("$nic=Target-Nic " + P(nic) + "; if((Get-NetIPInterface -InterfaceAlias $nic -AddressFamily IPv4).Dhcp -ne 'Disabled'){throw '当前为DHCP网卡，单独改网关可能被自动覆盖；请先设置静态IPv4。'}; Backup-Net");
                        b.AppendLine("$old=@(Get-NetRoute -InterfaceAlias $nic -DestinationPrefix '0.0.0.0/0' -ErrorAction SilentlyContinue); if($old.Count -eq 0){throw '该接口没有默认路由，未自动创建'}; if(@($old|Where-Object {$_.NextHop -eq " + P(gateway) + "}).Count -gt 0){Write-Host '默认网关已经是目标值，无需修改'}else{ New-NetRoute -InterfaceAlias $nic -DestinationPrefix '0.0.0.0/0' -NextHop " + P(gateway) + " -RouteMetric " + metric + " -Confirm:$false -ErrorAction Stop | Out-Null; foreach($r in $old){Remove-NetRoute -InputObject $r -Confirm:$false -ErrorAction Stop} }");
                        b.AppendLine("Get-NetRoute -InterfaceAlias $nic -DestinationPrefix '0.0.0.0/0' | Format-Table | Out-String | Write-Host");
                        break;
                    }
                    case "删除指定路径文件":
                    {
                        string targetPath=CheckDeleteFilePath(s.A);
                        b.AppendLine("Remove-SelectedExactFile " + P(targetPath));
                        break;
                    }
                    case "清空当前用户回收站":
                        b.AppendLine("Clear-RecycleBin -Force -Confirm:$false -ErrorAction Stop; Write-Host '[OK] 已清空当前运行账户的回收站' -ForegroundColor Green");
                        break;
                    case "删除桌面指定文件":
                    {
                        string fileName=CheckDesktopFileName(s.A);
                        string scope=CheckDesktopScope(s.B);
                        b.AppendLine("Remove-DesktopExactFile " + P(fileName) + " " + P(scope));
                        break;
                    }
                    case "格式化指定卷":
                    {
                        string letter=FormatLetter(s.A);
                        string fs=FormatFileSystem(s.B);
                        string label=FormatLabel(s.C);
                        b.AppendLine("Invoke-FormatStep " + P(letter) + " " + P(fs) + " " + P(label));
                        break;
                    }
                    case "滚轮":
                        int count=Coordinate(s.A);
                        if (count < -100 || count > 100) throw new ArgumentException("单次滚轮档数最大 100。");
                        b.AppendLine("[" + apiClass + "]::Mouse(2048," + (count*120) + ")"); break;
                    default: throw new ArgumentException("未知步骤："+s.Type);
                }
            }
            b.AppendLine("Write-Host 'All steps complete.'");
            b.AppendLine("} catch { Write-Host ('FAILED: ' + $_.Exception.Message) -ForegroundColor Red; throw }");
            return b.ToString();
        }
        private static string VisiblePowerShellCommand(string script,string switches)
        {
            // Launcher text is controlled ASCII, without cmd.exe expansions or quote interpolation.
            // User paths and workflow data are supplied through environment variables / the payload.
            foreach(char c in script)if(c>127||c=='"'||c=='%'||c=='\r'||c=='\n')throw new InvalidOperationException("启动命令包含未允许的字符。");
            return "powershell.exe -NoLogo -NoProfile "+switches+" -Command \""+script+"\"";
        }
        public static void ExportBat(string target, List<AutoStep> steps)
        {
            ExportBat(target, steps, new AutoWorkflowSettings());
        }
        public static void ExportBat(string target, List<AutoStep> steps, BatInputEngine engine)
        {
            AutoWorkflowSettings options = new AutoWorkflowSettings(); options.Engine = engine;
            ExportBat(target, steps, options);
        }
        public static void ExportBat(string target, List<AutoStep> steps, BatInputEngine engine, bool deleteOnSuccess)
        {
            AutoWorkflowSettings options = new AutoWorkflowSettings();
            options.Engine = engine; options.DeleteOnSuccess = deleteOnSuccess;
            ExportBat(target, steps, options);
        }
        public static void ExportBat(string target, List<AutoStep> steps, AutoWorkflowSettings options)
        {
            CheckOptions(steps, options);
            string powershell = MakeScript(steps, options.Engine, options.StartupDelaySeconds, options.DiagnosticMode);
            StringBuilder text = BatScriptPackage.Begin();
            if (options.RunAsAdministrator)
            {
                // Standard ShellExecute RunAs, with visible source rather than an encoded command.
                // The BAT path is passed as environment data; it is never inserted into PowerShell code.
                string elevate="$ErrorActionPreference='Stop'; try { $identity=[Security.Principal.WindowsIdentity]::GetCurrent(); $principal=New-Object -TypeName System.Security.Principal.WindowsPrincipal -ArgumentList @($identity); if($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){ exit 0 }; $target=$env:IT_TOOLKIT_BAT; if(-not [IO.File]::Exists($target)){ throw 'Cannot locate BAT file for UAC restart' }; Start-Process -FilePath $target -WorkingDirectory ([IO.Path]::GetDirectoryName($target)) -Verb RunAs -ErrorAction Stop | Out-Null; exit 42 } catch { Write-Host ('UAC elevation cancelled or failed: '+$_.Exception.Message) -ForegroundColor Red; exit 1 }";
                text.AppendLine("rem Standard Windows UAC; existing administrator sessions are reused");
                text.AppendLine(VisiblePowerShellCommand(elevate,"-NonInteractive"));
                text.AppendLine("set \"IT_TOOLKIT_UAC_RC=%ERRORLEVEL%\"");
                text.AppendLine("if \"%IT_TOOLKIT_UAC_RC%\"==\"42\" exit /b 0");
                text.AppendLine("if not \"%IT_TOOLKIT_UAC_RC%\"==\"0\" goto :__ITTOOLKIT_UAC_FAILED__");
            }
            if (options.HideConsole)
            {
                // Keep optional hidden execution, but make the complete bootstrap directly readable.
                string runner="$ErrorActionPreference='Stop'; $f=$env:IT_TOOLKIT_BAT; $rc=0; try { $t=[IO.File]::ReadAllText($f); $mark=':__ITTOOLKIT_PAYLOAD_V1__'; $ix=$t.LastIndexOf($mark); if($ix -lt 0){throw 'BAT payload marker missing'}; $x=$t.Substring($ix+$mark.Length).Trim(); $s=[Text.Encoding]::Unicode.GetString([Convert]::FromBase64String($x)); & ([ScriptBlock]::Create($s)) *> $null; if(-not $?){$rc=1} } catch {$rc=1}";
                if(options.DeleteOnSuccess)runner+=" finally { Start-Sleep -Seconds 2; try {Remove-Item -LiteralPath $f -Force -ErrorAction Stop} catch {$rc=1} }";
                runner+="; exit $rc";
                text.AppendLine("rem Optional hidden worker; the main workflow remains the original Unicode payload");
                text.AppendLine("start \"\" "+VisiblePowerShellCommand(runner,"-STA -WindowStyle Hidden"));
                text.AppendLine("exit /b %ERRORLEVEL%");
            }
            else
            {
                BatScriptPackage.AppendForeground(text);
                text.AppendLine("set \"RC=%ERRORLEVEL%\"");
                text.AppendLine("echo Automation exit code: %RC%");
                if(options.DeleteOnSuccess)
                {
                    // cmd reads this entire block before deleting the running BAT, so exit
                    // remains available after deletion. No background or hidden cleanup process.
                    text.AppendLine("rem Clean up only this BAT in the current visible cmd session");
                    text.AppendLine("(");
                    text.AppendLine("del /f /q \"%IT_TOOLKIT_BAT%\" >nul 2>&1");
                    text.AppendLine("if exist \"%IT_TOOLKIT_BAT%\" echo [WARNING] BAT cleanup failed; remove this file manually.");
                    text.AppendLine("exit /b %RC%");
                    text.AppendLine(")");
                }
                else
                {
                    text.AppendLine("pause");
                }
                text.AppendLine("exit /b %RC%");
            }
            if (options.RunAsAdministrator)
            {
                text.AppendLine(":__ITTOOLKIT_UAC_FAILED__");
                text.AppendLine("echo [ERROR] BAT was not launched with administrator rights. UAC may have been cancelled.");
                text.AppendLine("pause");
                text.AppendLine("exit /b 1");
            }
            BatScriptPackage.Write(target,text,powershell,false);
        }
    }
}
