using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace ITDesktopToolkit
{
    public sealed class SwitchRule
    {
        public string Category, Name, Path, ValueName, Help;
        public int EnabledValue, DisabledValue;
        public bool Caution;
        public SwitchRule(string category, string name, string path, string valueName,
            int enabledValue, int disabledValue, string help, bool caution)
        {
            Category = category; Name = name; Path = path; ValueName = valueName;
            EnabledValue = enabledValue; DisabledValue = disabledValue;
            Help = help; Caution = caution;
        }
        public int? ReadValue()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Path, false))
                {
                    if (k == null) return null;
                    object raw = k.GetValue(ValueName, null);
                    return raw == null ? (int?)null : Convert.ToInt32(raw);
                }
            }
            catch (Exception ex) { Paths.Log("Switch read failed: " + Name + " " + ex.Message); return null; }
        }
        public string State()
        {
            int? value = ReadValue();
            if (!value.HasValue) return "未设置 / 系统默认";
            if (value.Value == EnabledValue) return "开启";
            if (value.Value == DisabledValue) return "关闭";
            return "未知值：" + value.Value;
        }
        public string Recommendation()
        {
            // This is a recommendation, not the observed state. No blanket
            // performance claim is made for personalization options.
            if (Name == "显示文件扩展名") return "建议开启";
            if (Name == "显示受保护系统文件") return "建议关闭";
            if (Name == "文件夹显示缩略图") return "可按需开启";
            if (Name == "显示隐藏文件") return "排障时按需";
            return "个人偏好";
        }
        public string Explain()
        {
            int? current = ReadValue();
            return "功能：" + Name + "\r\n分类：" + Category + "\r\n注册表：HKCU\\" + Path +
                "\\" + ValueName + "\r\n当前值：" + (current.HasValue ? current.Value.ToString() : "未设置") +
                "\r\n当前状态：" + State() + "\r\n建议：" + Recommendation() + "\r\n\r\n作用：" + Help +
                (Caution ? "\r\n\r\n警告：开启后可能暴露受保护文件，避免随意删除。" : "") +
                "\r\n\r\n限制：此处显示 HKCU 注册表值，组策略/MDM、系统版本或软件本身可能覆盖实际效果。";
        }
        public void Write(bool enabled)
        {
            int desired = enabled ? EnabledValue : DisabledValue;
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Path))
            {
                if (k == null) throw new InvalidOperationException("无法打开当前用户注册表项。");
                k.SetValue(ValueName, desired, RegistryValueKind.DWord);
            }
            int? actual = ReadValue();
            if (!actual.HasValue || actual.Value != desired)
                throw new InvalidOperationException("注册表写入后校验不一致，可能被权限或策略阻止。");
        }
    }

    public static class SwitchService
    {
        public static readonly List<SwitchRule> Rules = new List<SwitchRule> {
            new SwitchRule("资源管理器", "显示文件扩展名", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "HideFileExt", 0, 1,
                "显示例如 .txt / .exe 文件后缀。", false),
            new SwitchRule("资源管理器", "显示隐藏文件", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Hidden", 1, 2,
                "显示具有隐藏属性的文件和文件夹。", false),
            new SwitchRule("资源管理器", "显示受保护系统文件", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowSuperHidden", 1, 0,
                "显示被 Windows 标记为受保护的操作系统文件，适用于专业排障。", true),
            new SwitchRule("资源管理器", "文件夹显示缩略图", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "IconsOnly", 0, 1,
                "显示图片/视频缩略图而不是一直显示图标。", false),
            new SwitchRule("资源管理器", "项目选择复选框", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "AutoCheckSelect", 1, 0,
                "文件列表中的多选复选框；部分系统需要重开资源管理器窗口。", false),
            new SwitchRule("资源管理器", "独立进程打开文件夹", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "SeparateProcess", 1, 0,
                "使用单独进程承载部分文件夹窗口，可能增加内存开销。", false),
            new SwitchRule("资源管理器", "文件夹内容工具提示", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowInfoTip", 1, 0,
                "显示文件夹/文件悬停说明。", false),
            new SwitchRule("资源管理器", "压缩或加密文件显示颜色", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowCompColor", 1, 0,
                "在支持的文件夹视图中使用不同颜色标注 NTFS 压缩或加密项目。", false),
            new SwitchRule("资源管理器", "窗口标题栏显示完整路径", @"Software\Microsoft\Windows\CurrentVersion\Explorer\CabinetState", "FullPath", 1, 0,
                "在支持的资源管理器版本中展示完整路径；Win11 新版文件管理器可能忽略此项。", false),
            new SwitchRule("个性化", "应用深色模式", @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 0, 1,
                "切换支持深色模式的 Windows 应用外观，部分程序需重新打开。", false),
            new SwitchRule("个性化", "系统深色模式", @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", 0, 1,
                "切换开始菜单、任务栏等系统外观（取决于 Windows 版本）。", false),
            new SwitchRule("剪贴板", "剪贴板历史记录", @"Software\Microsoft\Clipboard", "EnableClipboardHistory", 1, 0,
                "Win+V 剪贴板历史功能；受组织策略约束。", false),
            new SwitchRule("窗口操作", "启用摇动窗口最小化", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "DisallowShaking", 0, 1,
                "通过摇动标题栏最小化其他窗口；系统版本和多任务设置可能影响实际生效。", false),
            new SwitchRule("任务栏", "任务栏动画", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarAnimations", 1, 0,
                "控制部分任务栏动画；Win11 可能不完全遵守旧注册表值。", false),
            new SwitchRule("资源管理器", "文件选中半透明效果", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ListviewAlphaSelect", 1, 0,
                "控制传统文件列表的半透明选中效果；适用性依系统版本变化。", false),
            new SwitchRule("桌面", "图标文字阴影", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ListviewShadow", 1, 0,
                "控制桌面图标标签阴影。", false),
            new SwitchRule("资源管理器", "显示状态栏", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowStatusBar", 1, 0,
                "显示文件资源管理器状态栏；部分 Win11 布局可能忽略。", false),
            new SwitchRule("资源管理器", "导航栏展开至当前文件夹", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "NavPaneExpandToCurrentFolder", 1, 0,
                "让左侧导航栏同步展开当前文件夹。", false),
            new SwitchRule("资源管理器", "默认打开此电脑", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "LaunchTo", 1, 2,
                "控制文件资源管理器启动位置（此电脑/快速访问）；新版本 Windows 可能有不同设置。", false)
        };
        public static string Report()
        {
            System.Text.StringBuilder s = new System.Text.StringBuilder();
            s.AppendLine("【Windows 功能开关 · 当前用户设置】");
            s.AppendLine("检测时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            s.AppendLine("本页不是安全策略绕过工具。未设置时不推断当前最终生效状态。\r\n");
            foreach (SwitchRule r in Rules) s.AppendLine("[" + r.Category + "] " + r.Name + "：" + r.State());
            s.AppendLine("\r\n【限制】");
            s.AppendLine("注册表写入成功只表示配置值被保存，受组策略、MDM 和系统版本影响，实际 UI 可能不会立即改变。");
            s.AppendLine("网络、共享、RDP、防火墙、BitLocker 等功能只打开 Windows 官方管理页面，不做不安全的直接切换。");
            return s.ToString();
        }
    }
}
