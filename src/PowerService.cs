using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ITDesktopToolkit
{
    public static class PowerService
    {
        private const string PowerKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Power";
        public static string CurrentPlan() { return CommandService.Run("powercfg.exe", "/getactivescheme", 2); }
        public static string AvailablePlans() { return CommandService.Run("powercfg.exe", "/list", 2); }
        public static string HibernateState()
        {
            try { using (RegistryKey r = Registry.LocalMachine.OpenSubKey(PowerKey)) { object x = r == null ? null : r.GetValue("HibernateEnabled"); return x == null ? "未知" : (Convert.ToInt32(x) != 0 ? "已启用" : "已禁用"); } }
            catch { return "无法读取"; }
        }
        public static string FastStartupState()
        {
            try { using (RegistryKey r = Registry.LocalMachine.OpenSubKey(PowerKey)) { object x = r == null ? null : r.GetValue("HiberbootEnabled"); return x == null ? "系统默认/未知" : (Convert.ToInt32(x) != 0 ? "已启用（配置值）" : "已禁用"); } }
            catch { return "无法读取"; }
        }
        public static string Status()
        {
            return "【当前电源计划】\r\n" + CurrentPlan() + "\r\n【可用电源计划】\r\n" + AvailablePlans() + "\r\n休眠：" + HibernateState() + "\r\n快速启动：" + FastStartupState() + "\r\n注意：现代待机设备、组策略及 OEM 软件可能限制选项。";
        }
        public static string SetPlan(string planGuid)
        {
            if (!CommandService.IsAdministrator()) return "请使用管理员权限。";
            if (planGuid != "381b4222-f694-41f0-9685-ff5bb260df2e" && planGuid != "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c" && planGuid != "e9a42b02-d5df-448d-aa00-03f14749eb61") return "未知的电源方案。";
            string prior = CurrentPlan();
            File.AppendAllText(Path.Combine(Paths.UserDir, "power_changes.txt"), DateTime.Now + " 原计划：\r\n" + prior + "\r\n", Encoding.UTF8);
            return CommandService.Run("powercfg.exe", "/setactive " + planGuid, 2) + "\r\n" + CurrentPlan();
        }
        public static string RestorePreviousPowerPlan()
        {
            if(!CommandService.IsAdministrator())return "需要管理员权限。";
            string f=Path.Combine(Paths.UserDir,"power_changes.txt");
            if(!File.Exists(f))return "没有找到本工具箱记录的电源计划备份。";
            string all=File.ReadAllText(f,Encoding.UTF8);
            MatchCollection matches=Regex.Matches(all,@"原计划：[\s\S]*?([0-9a-fA-F]{8}-[0-9a-fA-F-]{27,36})");
            if(matches.Count==0)return "备份中未发现原电源计划 GUID。";
            string guid=matches[matches.Count-1].Groups[1].Value;
            return "目标原电源计划："+guid+"\r\n"+CommandService.Run("powercfg.exe","/setactive "+guid,2)+"\r\n"+CurrentPlan();
        }
        public static string SetTimeout(string setting, int ac, int dc)
        {
            if (ac < 0 || ac > 100000 || dc < 0 || dc > 100000) return "时间值超出范围。";
            if (setting != "monitor" && setting != "standby" && setting != "hibernate") return "不支持的电源参数。";
            File.AppendAllText(Path.Combine(Paths.UserDir,"power_changes.txt"), DateTime.Now+" 变更前电源详细参数：\r\n"+CommandService.Run("powercfg.exe","/query SCHEME_CURRENT",2)+"\r\n",Encoding.UTF8);
            return "接电源：\r\n" + CommandService.Run("powercfg.exe", "/change " + setting + "-timeout-ac " + ac, 2) + "\r\n使用电池：\r\n" + CommandService.Run("powercfg.exe", "/change " + setting + "-timeout-dc " + dc, 2);
        }
        public static string ToggleHibernate(bool enabled)
        {
            if (!CommandService.IsAdministrator()) return "需要管理员权限。";
            string old = HibernateState();
            File.AppendAllText(Path.Combine(Paths.UserDir, "power_changes.txt"), DateTime.Now + " 休眠原状态：" + old + "\r\n", Encoding.UTF8);
            return CommandService.Run("powercfg.exe", "/hibernate " + (enabled ? "on" : "off"), 3) + "\r\n重新读取：" + HibernateState();
        }
        public static string ToggleFastStartup(bool enabled)
        {
            if (!CommandService.IsAdministrator()) return "需要管理员权限。";
            string old = FastStartupState();
            File.AppendAllText(Path.Combine(Paths.UserDir, "power_changes.txt"), DateTime.Now + " 快速启动原状态：" + old + "\r\n", Encoding.UTF8);
            using (RegistryKey k = Registry.LocalMachine.CreateSubKey(PowerKey)) k.SetValue("HiberbootEnabled", enabled ? 1 : 0, RegistryValueKind.DWord);
            return "写入配置，重新读取：" + FastStartupState() + "\r\n注意：快速启动依赖休眠功能，可能需要重启生效。";
        }
    }
}
