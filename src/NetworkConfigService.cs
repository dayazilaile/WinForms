using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ITDesktopToolkit
{
    public sealed class NicSetting
    {
        public string Name, Id, Description, Mac, Kind, IPv4, Mask, Gateway, Dns;
        public bool Dhcp, AutoDns;
        public override string ToString() { return Name + "（" + Kind + " · " + Description + "）"; }
    }
    public static class NetworkConfigService
    {
        public static List<NicSetting> GetAdapters()
        {
            List<NicSetting> results = new List<NicSetting>();
            foreach (NetworkInterface a in NetworkInterface.GetAllNetworkInterfaces())
            {
                string type = a.NetworkInterfaceType.ToString();
                string description = a.Description ?? "";
                string name = a.Name ?? "";
                string kind = "";
                if (a.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) kind = "无线网卡";
                else if (a.NetworkInterfaceType == NetworkInterfaceType.Ethernet || a.NetworkInterfaceType == NetworkInterfaceType.GigabitEthernet || a.NetworkInterfaceType == NetworkInterfaceType.FastEthernetFx || a.NetworkInterfaceType == NetworkInterfaceType.FastEthernetT) kind = "有线网卡";
                if (kind.Length == 0) continue;
                string low = (name + " " + description).ToLowerInvariant();
                if (Regex.IsMatch(low, @"virtual|vmware|vbox|hyper-v|bluetooth|loopback|vpn|tunnel|tap-|wireguard|tailscale|zerotier|虚拟|蓝牙")) continue;
                IPInterfaceProperties ip;
                try { ip = a.GetIPProperties(); } catch { continue; }
                NicSetting n = new NicSetting();
                n.Name = name; n.Id = a.Id; n.Description = description; n.Kind = kind;
                n.Mac = a.GetPhysicalAddress().ToString();
                IPv4InterfaceProperties ipv4 = null;
                try { ipv4 = ip.GetIPv4Properties(); } catch { }
                n.Dhcp = ipv4 != null && ipv4.IsDhcpEnabled;
                n.AutoDns = true;
                try { using(RegistryKey k=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{"+a.Id.Trim('{','}')+"}")) {
                    string manual=k==null?"":Convert.ToString(k.GetValue("NameServer", ""));
                    n.AutoDns = String.IsNullOrWhiteSpace(manual);
                }} catch {}
                List<string> ips = new List<string>(), masks = new List<string>(), dns = new List<string>(), gw = new List<string>();
                foreach (UnicastIPAddressInformation u in ip.UnicastAddresses) if (u.Address.AddressFamily == AddressFamily.InterNetwork)
                {
                    ips.Add(u.Address.ToString()); masks.Add(u.IPv4Mask == null ? "" : u.IPv4Mask.ToString());
                }
                foreach (GatewayIPAddressInformation g in ip.GatewayAddresses) if (g.Address.AddressFamily == AddressFamily.InterNetwork) gw.Add(g.Address.ToString());
                foreach (IPAddress d in ip.DnsAddresses) if (d.AddressFamily == AddressFamily.InterNetwork) dns.Add(d.ToString());
                n.IPv4 = String.Join(", ", ips); n.Mask = String.Join(", ", masks); n.Gateway = String.Join(", ", gw); n.Dns = String.Join(", ", dns);
                results.Add(n);
            }
            return results;
        }
        public static bool IsIPv4(string value)
        {
            IPAddress addr;
            return IPAddress.TryParse(value, out addr) && addr.AddressFamily == AddressFamily.InterNetwork && Regex.IsMatch(value, @"^\d{1,3}(\.\d{1,3}){3}$");
        }
        // V0.3.7: Do not treat a launched netsh process as a successful IP change.
        // Preserve exit code, verify the actual IPv4/DHCP state, and stop on failure.
        private static int RunNetshChecked(string args, StringBuilder report)
        {
            StringBuilder output = new StringBuilder();
            object sync = new object();
            using (Process p = new Process())
            {
                p.StartInfo = CommandService.CreateCommand("netsh.exe", args);
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.RedirectStandardError = true;
                p.StartInfo.CreateNoWindow = true;
                try
                {
                    p.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) {
                        if (e.Data != null) lock(sync) { if (output.Length < 60000) output.AppendLine(e.Data); }
                    };
                    p.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) {
                        if (e.Data != null) lock(sync) { if (output.Length < 60000) output.AppendLine("[stderr] " + e.Data); }
                    };
                    CommandService.NotifyProcess(p.StartInfo.FileName);p.Start();
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    if (!p.WaitForExit(90000))
                    {
                        try { p.Kill(); } catch { }
                        report.AppendLine("命令超时（90 秒），已尝试停止 netsh：" + args);
                        return -1;
                    }
                    p.WaitForExit(); // flush async output handlers
                    report.AppendLine("命令：netsh.exe " + args);
                    report.AppendLine("退出码：" + p.ExitCode);
                    lock(sync) report.AppendLine(output.ToString());
                    return p.ExitCode;
                }
                catch(Exception ex)
                {
                    report.AppendLine("无法执行 netsh：" + ex.Message);
                    return -1;
                }
            }
        }
        private static bool ConfirmActualIPv4(string adapterId, bool requestedDhcp, string expectedIp, out NicSetting observed)
        {
            observed = null;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                try
                {
                    observed = GetAdapters().Find(delegate(NicSetting n) {
                        return String.Equals(n.Id, adapterId, StringComparison.OrdinalIgnoreCase);
                    });
                    if (observed != null && observed.Dhcp == requestedDhcp)
                    {
                        if (requestedDhcp) return true;
                        foreach (string value in (observed.IPv4 ?? "").Split(','))
                            if (String.Equals(value.Trim(), expectedIp, StringComparison.OrdinalIgnoreCase)) return true;
                    }
                }
                catch { /* A transient adapter refresh is retried. */ }
                if (attempt < 5) Thread.Sleep(400);
            }
            return false;
        }
        private static void AppendActual(StringBuilder report, NicSetting actual)
        {
            report.AppendLine("\r\n【重新读取的实际状态】");
            if (actual == null) { report.AppendLine("网卡暂时不可读取；请在 Windows 网络设置中核对。 "); return; }
            report.AppendLine("IP 分配方式：" + (actual.Dhcp ? "DHCP（自动获取）" : "静态（关闭 DHCP）"));
            report.AppendLine("IPv4：" + actual.IPv4);
            report.AppendLine("掩码：" + actual.Mask);
            report.AppendLine("网关：" + actual.Gateway);
            report.AppendLine("DNS：" + actual.Dns);
        }
        public static string ApplyIPv4(NicSetting nic, bool dhcp, string ip, string mask, string gateway, bool autoDns, string dns1, string dns2)
        {
            using(var audit=OperationAudit.Begin("手动配置IPv4", "网卡="+(nic==null?"未选择":nic.Name)+";目标IPv4="+(dhcp?"DHCP":ip)+";掩码="+mask+";网关="+gateway+";DNS="+(autoDns?"自动":dns1+","+dns2)))
            {if(!audit.StartWritten){audit.End("未执行","无法保存操作日志");return "【配置结果】失败：软件目录日志不可写，未修改网络配置。";}try{string result=ApplyIPv4Core(nic,dhcp,ip,mask,gateway,autoDns,dns1,dns2);audit.End(OperationAudit.Outcome(result));return result;}catch(Exception ex){audit.End("失败",ex.Message);throw;}}
        }
        private static string ApplyIPv4Core(NicSetting nic, bool dhcp, string ip, string mask, string gateway, bool autoDns, string dns1, string dns2)
        {
            if (!CommandService.IsAdministrator()) return "【配置结果】失败：需要以管理员权限启动工具箱。未执行修改。";
            if (nic == null || String.IsNullOrWhiteSpace(nic.Name) || String.IsNullOrWhiteSpace(nic.Id)) return "【配置结果】失败：未选中有效网卡。";
            if (nic.Name.Contains("\"")) return "【配置结果】失败：网卡名称包含特殊引号，拒绝执行。";
            ip=(ip??"").Trim();mask=(mask??"").Trim();gateway=(gateway??"").Trim();dns1=(dns1??"").Trim();dns2=(dns2??"").Trim();
            if (!dhcp && (!IsIPv4(ip) || !IsIPv4(mask) || (gateway.Length>0 && !IsIPv4(gateway)))) return "【配置结果】失败：IPv4、掩码或网关格式有误。未执行修改。";
            if (!autoDns && (!IsIPv4(dns1) || (dns2.Length>0 && !IsIPv4(dns2)))) return "【配置结果】失败：DNS 格式不正确。未执行修改。";
            // Refresh before backup so we do not preserve a stale UI snapshot.
            NicSetting before = null;
            try { before = GetAdapters().Find(delegate(NicSetting x) { return String.Equals(x.Id,nic.Id,StringComparison.OrdinalIgnoreCase); }); }
            catch(Exception ex) { return "【配置结果】失败：读取原始网卡状态失败："+ex.Message; }
            if (before == null) return "【配置结果】失败：找不到原来的网卡（可能已断开或重命名）。未执行修改。";
            string backup = Path.Combine(Paths.MachineDir, "network_before_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".txt");
            try
            {
                Directory.CreateDirectory(Paths.MachineDir);
                File.WriteAllText(backup, OperationAudit.Stamp("手动配置IPv4 / 修改前备份")+"修改时间：" + DateTimeOffset.Now.ToString("o") + "\r\n主机名："+Environment.MachineName+"\r\n设备标识："+OperationAudit.Capture().DeviceId+"\r\nMAC："+before.Mac+ "\r\n网卡：" + before.Name + "\r\nID：" + before.Id + "\r\nDHCP：" + before.Dhcp + "\r\nIP：" + before.IPv4 + "\r\n掩码：" + before.Mask + "\r\n网关：" + before.Gateway + "\r\nDNS：" + before.Dns + "\r\nDNS自动：" + before.AutoDns + "\r\n", Encoding.UTF8);
            }
            catch(Exception ex) { return "【配置结果】失败：无法保存修改前备份，已中止："+ex.Message; }
            StringBuilder report = new StringBuilder();
            report.AppendLine("目标网卡：" + before.Name + "（" + before.Id + "）");
            report.AppendLine("原始配置备份：" + backup);
            string q = "\"" + before.Name + "\"";
            string addrArgs = dhcp
                ? "interface ipv4 set address name=" + q + " source=dhcp"
                : "interface ipv4 set address name=" + q + " source=static address=" + ip + " mask=" + mask +
                  " gateway=" + (gateway.Length==0 ? "none" : gateway) + (gateway.Length==0 ? "" : " gwmetric=1") + " store=persistent";
            if (RunNetshChecked(addrArgs, report) != 0)
            {
                NicSetting afterFailure;
                ConfirmActualIPv4(before.Id,dhcp,ip,out afterFailure);
                report.AppendLine("【配置结果】失败：设置 IPv4 的 netsh 命令报错；未继续修改 DNS。请查看上面的退出码和错误。 ");
                AppendActual(report, afterFailure);
                return report.ToString();
            }
            NicSetting afterAddress;
            if (!ConfirmActualIPv4(before.Id,dhcp,ip,out afterAddress))
            {
                report.AppendLine("【配置结果】失败：命令退出码为 0，但实际 IP/DHCP 状态不符合请求；已停止后续 DNS 操作。 ");
                AppendActual(report, afterAddress);
                report.AppendLine("可能是网卡配置尚未应用，或被管理策略、网络管理软件覆盖。没有自动切换回 DHCP。 ");
                return report.ToString();
            }
            string dnsArgs = autoDns
                ? "interface ipv4 set dnsservers name=" + q + " source=dhcp"
                : "interface ipv4 set dnsservers name=" + q + " source=static address=" + dns1 + " register=primary validate=no";
            bool dnsOk = RunNetshChecked(dnsArgs, report) == 0;
            if (dnsOk && !autoDns && dns2.Length>0)
                dnsOk = RunNetshChecked("interface ipv4 add dnsservers name=" + q + " address=" + dns2 + " index=2 validate=no", report) == 0;
            NicSetting after;
            bool addressStillCorrect = ConfirmActualIPv4(before.Id,dhcp,ip,out after);
            if (!addressStillCorrect)
                report.AppendLine("【配置结果】失败：设置 DNS 后实际 IP/DHCP 状态发生变化，请检查系统网络管理策略。 ");
            else if (!dnsOk)
                report.AppendLine("【配置结果】部分完成：IP 分配方式已核实，DNS 设置失败。请查看上述错误；不会自动改成 DHCP。 ");
            else
                report.AppendLine("【配置结果】成功：已按请求设置 IP，并重新验证实际 IP/DHCP 状态。 ");
            AppendActual(report,after);
            report.AppendLine("注意：可能导致远程连接中断；备份仅保存原始参数，不代表操作失败后自动恢复。 ");
            return report.ToString();
        }
        public static string RestoreLatest(NicSetting nic)
        {
            if (!CommandService.IsAdministrator()) return "需要管理员权限。";
            string[] paths=Directory.GetFiles(Paths.MachineDir, "network_before_*.txt");
            Array.Sort(paths, StringComparer.OrdinalIgnoreCase);
            Array.Reverse(paths);
            foreach(string path in paths)
            {
                string[] lines=File.ReadAllLines(path, Encoding.UTF8);
                Dictionary<string,string> d=new Dictionary<string,string>();
                foreach(string line in lines)
                {
                    int idx=line.IndexOf('：');
                    if(idx>0)d[line.Substring(0,idx)]=line.Substring(idx+1).Trim();
                }
                if(!d.ContainsKey("ID") || d["ID"]!=nic.Id)continue;
                bool dhcp=d.ContainsKey("DHCP") && d["DHCP"].Equals("True",StringComparison.OrdinalIgnoreCase);
                bool autodns=d.ContainsKey("DNS自动") && d["DNS自动"].Equals("True",StringComparison.OrdinalIgnoreCase);
                string ip=d.ContainsKey("IP")?d["IP"].Split(',')[0].Trim():"";
                string mask=d.ContainsKey("掩码")?d["掩码"].Split(',')[0].Trim():"";
                string gw=d.ContainsKey("网关")?d["网关"].Split(',')[0].Trim():"";
                string[] dns=d.ContainsKey("DNS")?d["DNS"].Split(','):new string[0];
                string dns1=dns.Length>0?dns[0].Trim():"",dns2=dns.Length>1?dns[1].Trim():"";
                // 旧版本备份若未记录 DNS 自动模式则不要根据服务器地址擅自猜测。
                if(!d.ContainsKey("DNS自动")) return "此备份未保存 DNS 分配模式，无法保证安全恢复："+path;
                return "使用备份："+path+"\r\n"+ApplyIPv4(nic,dhcp,ip,mask,gw,autodns,dns1,dns2);
            }
            return "没有找到该网卡的历史配置备份。";
        }
        public static string ApplyMac(NicSetting nic,string mac,bool reset)
        {
            using(var audit=OperationAudit.Begin("修改网卡MAC","网卡="+(nic==null?"未选择":nic.Name)+";目标="+(reset?"恢复默认MAC":mac)))
            {if(!audit.StartWritten){audit.End("未执行","无法保存操作日志");return "软件目录日志不可写，未修改MAC。";}try{string output=ApplyMacCore(nic,mac,reset);audit.End(OperationAudit.Outcome(output));return output;}catch(Exception ex){audit.End("失败",ex.Message);throw;}}
        }
        private static string ApplyMacCore(NicSetting nic, string mac, bool reset)
        {
            if (!CommandService.IsAdministrator()) return "需要管理员权限。";
            string clean = Regex.Replace(mac ?? "", "[-:]", "").ToUpperInvariant();
            if (!reset)
            {
                if (!Regex.IsMatch(clean, "^[0-9A-F]{12}$")) return "MAC 必须为 12 位十六进制字符。";
                int head = Convert.ToInt32(clean.Substring(0, 2), 16);
                if ((head & 1) != 0 || clean == "000000000000" || clean == "FFFFFFFFFFFF") return "只能填写单播 MAC，不能是组播、全零或广播地址。";
            }
            string name = nic.Name.Replace("'", "''");
            string cmd = reset ? "Reset-NetAdapterAdvancedProperty -Name '" + name + "' -RegistryKeyword 'NetworkAddress' -NoRestart -ErrorAction Stop" :
                "Set-NetAdapter -Name '" + name + "' -MacAddress '" + clean + "' -Confirm:$false -ErrorAction Stop";
            string script = "$ErrorActionPreference='Stop'; " + cmd + "; Disable-NetAdapter -Name '" + name + "' -Confirm:$false -ErrorAction Stop; Start-Sleep -Seconds 2; Enable-NetAdapter -Name '" + name + "' -Confirm:$false -ErrorAction Stop; Get-NetAdapter -Name '" + name + "' | Select-Object Name,MacAddress,Status | Format-List | Out-String";
            return PowerShellService.RunScript(script, 3);
        }
    }
}
