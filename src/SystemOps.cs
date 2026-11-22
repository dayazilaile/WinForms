using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace ITDesktopToolkit
{
    public static class SystemOps
    {
        private const string RdpKey = @"SYSTEM\CurrentControlSet\Control\Terminal Server\WinStations\RDP-Tcp";
        public static int RdpPort()
        {
            using (RegistryKey k = Registry.LocalMachine.OpenSubKey(RdpKey))
            {
                object value = k == null ? null : k.GetValue("PortNumber");
                return value == null ? 3389 : Convert.ToInt32(value);
            }
        }
        public static string RdpStatus()
        {
            string enabled = "未知";
            using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Terminal Server"))
                if (k != null) enabled = Convert.ToInt32(k.GetValue("fDenyTSConnections", 1)) == 0 ? "允许远程桌面（配置）" : "未允许远程桌面（配置）";
            return "远程桌面状态：" + enabled + "\r\nRDP 注册表端口：" + RdpPort() + "\r\n实际监听与防火墙规则需单独验证。修改端口通常需要重启远程桌面相关服务或系统。";
        }
        public static string SetRdpPort(int port)
        {
            if (!CommandService.IsAdministrator()) return "需要管理员权限。";
            if (port < 1 || port > 65535) return "端口必须在 1～65535 之间。0 不是有效的 RDP 侦听端口。";
            int old = RdpPort();
            if (old != port)
            {
                foreach(System.Net.IPEndPoint endpoint in System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners())
                    if(endpoint.Port==port)return "目标 TCP 端口当前被其它服务监听，未修改。请先检查端口冲突。";
            }
            if (old != port)
            {
                foreach (System.Net.IPEndPoint endpoint in System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners())
                    if (endpoint.Port == port) return "目标 UDP 端口已被其它服务监听，未修改。请检查冲突。";
            }
            File.AppendAllText(Path.Combine(Paths.MachineDir, "rdp_port_changes.tsv"), OperationAudit.Stamp("修改远程桌面端口") + DateTimeOffset.Now.ToString("o") + "\t" + old + "\t" + port + Environment.NewLine, Encoding.UTF8);
            using (RegistryKey k = Registry.LocalMachine.OpenSubKey(RdpKey, true))
            {
                if (k == null) throw new InvalidOperationException("系统没有 RDP-Tcp 配置。可能不支持远程桌面主机。");
                k.SetValue("PortNumber", port, RegistryValueKind.DWord);
            }
            return "旧端口：" + old + "；新端口：" + RdpPort() + "\r\n" +
                "已保存原端口到本地历史文件。尚未自动变更防火墙，也未重启服务。\r\n请在防火墙允许新的 TCP/UDP 端口，确认网络连通并重启系统后验证监听；远程维护前务必准备本地恢复方式。";
        }
        public static string AddRdpFirewallRules(int port)
        {
            if (!CommandService.IsAdministrator()) return "需要管理员权限。";
            if (port < 1 || port > 65535) return "端口必须在 1～65535 之间。";
            // Dedicated Private/Domain profile rules only. Do not expose RDP on public networks.
            string script = "$ErrorActionPreference='Stop'; $p=" + port + "; " +
                "$name='ITToolkit-RDP-'+$p; " +
                "if(-not(Get-NetFirewallRule -Name ($name+'-TCP') -ErrorAction SilentlyContinue)){" +
                "New-NetFirewallRule -Name ($name+'-TCP') -DisplayName ('IT Toolkit RDP TCP '+$p) -Direction Inbound -Protocol TCP -LocalPort $p -Action Allow -Profile Private,Domain -ErrorAction Stop | Out-Null};" +
                "if(-not(Get-NetFirewallRule -Name ($name+'-UDP') -ErrorAction SilentlyContinue)){" +
                "New-NetFirewallRule -Name ($name+'-UDP') -DisplayName ('IT Toolkit RDP UDP '+$p) -Direction Inbound -Protocol UDP -LocalPort $p -Action Allow -Profile Private,Domain -ErrorAction Stop | Out-Null};" +
                "Get-NetFirewallRule -Name ($name+'-*') -ErrorAction Stop | Select-Object DisplayName,Enabled,Profile,Direction,Action | Format-Table -AutoSize | Out-String";
            return PowerShellService.RunScript(script,2);
        }
        private static string UpdateBackup { get { return Path.Combine(Paths.MachineDir, "windows_update_original_start.txt"); } }
        public static string UpdateStatus()
        {
            string state = "未知";
            try { using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\wuauserv"))
            { if (k != null) state = Convert.ToString(k.GetValue("Start", "未知")); } } catch { }
            return "Windows Update 服务启动类型注册表值 (Start)：" + state + "\r\n" + CommandService.Run("sc.exe", "query wuauserv", 2) +
                "\r\n说明：Windows 维护和组策略可能修改服务状态，本工具不会尝试绕过系统保护或强制阻止补丁安装。";
        }
        public static string ToggleUpdates(bool disable)
        {
            if (!CommandService.IsAdministrator()) return "需要管理员权限。";
            string key = @"SYSTEM\CurrentControlSet\Services\wuauserv";
            int initial;
            using (RegistryKey k = Registry.LocalMachine.OpenSubKey(key))
                initial = Convert.ToInt32(k.GetValue("Start", 3));
            if (disable)
            {
                if (!File.Exists(UpdateBackup)) File.WriteAllText(UpdateBackup, initial.ToString(), Encoding.ASCII);
                string stop = CommandService.Run("sc.exe", "stop wuauserv", 2);
                string config = CommandService.Run("sc.exe", "config wuauserv start= disabled", 2);
                return "停止服务：\r\n" + stop + "\r\n禁用启动：\r\n" + config + "\r\n" + UpdateStatus();
            }
            int original = 3;
            if (File.Exists(UpdateBackup)) Int32.TryParse(File.ReadAllText(UpdateBackup).Trim(), out original);
            if (original < 0 || original > 4) original = 3;
            string start = original == 2 ? "auto" : original == 4 ? "disabled" : "demand";
            string result = CommandService.Run("sc.exe", "config wuauserv start= " + start, 2);
            // 已设置的原始启动类型若是延迟自动/触发启动，则仍应通过 Windows 设置继续核对。
            return "恢复原记录启动类型：" + original + "\r\n" + result + "\r\n" + UpdateStatus();
        }
        public static string SecurityStatus() { return SecurityDiagnostics.Inspect(); }
        public static string SecurityChange(bool disable) { return SecurityDiagnostics.Change(disable); }
    }
}
