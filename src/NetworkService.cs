using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace ITDesktopToolkit
{
    public static class NetworkService
    {
        public static string GetReport()
        {
            StringBuilder b = new StringBuilder();
            b.AppendLine("【本机网络详细信息】");
            b.AppendLine("读取时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            b.AppendLine("计算机名：" + Environment.MachineName);
            b.AppendLine();
            int count = 0;
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Ppp) continue;
                string kind = NicClassifier.Classify(ni.Description, ni.Name,
                    ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? "Wi-Fi" : "Ethernet", "");
                if (kind.Length == 0) continue;
                count++;
                b.AppendLine(new string('-', 68));
                b.AppendLine("分类：" + kind);
                b.AppendLine("名称：" + ni.Name);
                b.AppendLine("描述：" + ni.Description);
                b.AppendLine("类型：" + ni.NetworkInterfaceType + " | 状态：" + ni.OperationalStatus);
                b.AppendLine("网卡 ID：" + ni.Id);
                b.AppendLine("MAC：" + ni.GetPhysicalAddress());
                b.AppendLine("报告链路速度：" + (ni.Speed / 1000000.0).ToString("0.##") + " Mbps");
                try
                {
                    IPInterfaceProperties ip = ni.GetIPProperties();
                    foreach (UnicastIPAddressInformation u in ip.UnicastAddresses)
                        b.AppendLine("IP：" + u.Address + (u.IPv4Mask == null ? "" : " / 掩码 " + u.IPv4Mask));
                    foreach (GatewayIPAddressInformation g in ip.GatewayAddresses) b.AppendLine("网关：" + g.Address);
                    foreach (IPAddress dns in ip.DnsAddresses) b.AppendLine("DNS：" + dns);
                    foreach (IPAddress server in ip.DhcpServerAddresses) b.AppendLine("DHCP 服务器：" + server);
                    IPv4InterfaceProperties v4 = ip.GetIPv4Properties();
                    if (v4 != null) b.AppendLine("DHCP 自动配置：" + (v4.IsDhcpEnabled ? "启用" : "关闭"));
                }
                catch (Exception ex) { b.AppendLine("网络属性读取失败：" + ex.Message); }
                b.AppendLine();
            }
            if (count == 0) b.AppendLine("没有找到可识别的有线/无线网卡。可能是驱动缺失、设备禁用或系统未上报信息。");
            b.AppendLine("已显示 " + count + " 个有线/无线适配器；隐藏回环、蓝牙 PAN、虚拟、VPN 和隧道接口。");
            b.AppendLine("注意：网卡 Up 不代表能访问网关或互联网；某些特殊 OEM 网卡名称可能无法分类。");
            return b.ToString();
        }
        public static string PingHost(string target)
        {
            StringBuilder b = new StringBuilder("【Ping 检测】\r\n目标：" + target + "\r\n次数：4，单次超时：1500 ms\r\n\r\n");
            int ok = 0;
            long totalMs = 0;
            long minMs = long.MaxValue;
            long maxMs = 0;
            using (Ping p = new Ping())
            {
                for (int i = 0; i < 4; i++)
                {
                    try
                    {
                        PingReply r = p.Send(target, 1500);
                        if (r.Status == IPStatus.Success)
                        {
                            ok++;
                            long ms = r.RoundtripTime;
                            totalMs += ms;
                            if (ms < minMs) minMs = ms;
                            if (ms > maxMs) maxMs = ms;
                            string latency = ms == 0 ? "<1 ms（系统毫秒精度）" : ms + " ms";
                            b.AppendLine("第 " + (i + 1) + " 次：成功 | 延迟 " + latency + " | 回应 IP " + r.Address);
                        }
                        else b.AppendLine("第 " + (i + 1) + " 次：" + r.Status + " | 无有效延迟");
                    }
                    catch (Exception ex) { b.AppendLine("第 " + (i + 1) + " 次失败：" + ex.Message); }
                }
            }
            b.AppendLine();
            b.AppendLine("【Ping 统计】");
            b.AppendLine("发送 4，收到 " + ok + "，丢失 " + (4 - ok) + "，丢包率 " + ((4 - ok) * 25) + "%");
            if (ok > 0)
            {
                b.AppendLine("最低延迟：" + (minMs == 0 ? "<1 ms" : minMs + " ms"));
                b.AppendLine("平均延迟：" + (totalMs == 0 ? "<1 ms" : (totalMs / (double)ok).ToString("0.0") + " ms"));
                b.AppendLine("最高延迟：" + (maxMs == 0 ? "<1 ms" : maxMs + " ms"));
            }
            else b.AppendLine("无成功响应，无法计算 RTT 延迟。");
            b.AppendLine("提醒：Ping 延迟是 ICMP 往返时间（RTT），不是网速；被禁 Ping 不一定表示无法访问网站。");
            return b.ToString();
        }
        public static string LookupDns(string host)
        {
            StringBuilder b = new StringBuilder("DNS 查询：" + host + "\r\n");
            try
            {
                IPAddress[] addrs = Dns.GetHostAddresses(host);
                foreach (IPAddress a in addrs) b.AppendLine("解析地址：" + a);
                if (addrs.Length == 0) b.AppendLine("未返回 A/AAAA 地址。");
            }
            catch (Exception ex) { b.AppendLine("解析失败：" + ex.Message); }
            return b.ToString();
        }
        public static string TestTcp(string host, int port)
        {
            using (TcpClient c = new TcpClient())
            {
                try
                {
                    IAsyncResult a = c.BeginConnect(host, port, null, null);
                    using (a.AsyncWaitHandle)
                    {
                        if (!a.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(3)))
                            return "TCP " + host + ":" + port + " 连接超时（3 秒）；可能是防火墙、网络问题或服务未监听。";
                        c.EndConnect(a);
                        return "TCP " + host + ":" + port + " 连接成功。";
                    }
                }
                catch (Exception ex) { return "TCP " + host + ":" + port + " 连接失败：" + ex.Message; }
            }
        }
    }
}
