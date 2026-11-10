using System;

namespace ITDesktopToolkit
{
    // 专供硬件清单 / 网络报告使用。先排除虚拟设备，再按名称和适配器类型判断。
    // 只把能辨别为有线或无线的物理网卡写入清单，不根据 Microsoft 的回环接口推断硬件。
    public static class NicClassifier
    {
        public static string Classify(string name, string connection, string adapterType, string pnpId)
        {
            string text = ((name ?? "") + " " + (connection ?? "")).ToLowerInvariant();
            string id = (pnpId ?? "").ToUpperInvariant();
            string[] excluded = {
                "bluetooth", "personal area network", "pan network", "loopback", "loop-back", "回环", "环回",
                "virtual", "virtualbox", "vmware", "hyper-v", "vethernet", "tunnel", "隧道", "miniport",
                "wi-fi direct", "wifi direct", "wlan direct", "host-only", "teredo", "isatap", "tap-windows",
                "wireguard", "vpn", "ppp", "wan miniport", "npcap", "packet capture", "bridge",
                "usb tethering", "remote ndis", "rndis", "pseudo-interface"
            };
            foreach (string bad in excluded) if (text.Contains(bad)) return "";
            // WMI PhysicalAdapter=True 仍可能返回逻辑网卡。排除软件枚举实例。
            if (id.Length > 0 && !id.StartsWith("PCI\\") && !id.StartsWith("USB\\") &&
                !id.StartsWith("PCMCIA\\") && !id.StartsWith("ACPI\\")) return "";
            if (Has(text, "wireless", "wi-fi", "wifi", "wlan", "802.11", "无线", "killer(r) wi-fi")) return "无线网卡";
            if (Has(text, "ethernet", "以太网", "本地连接", "gbe", "gigabit", "2.5g", "10g", "pcie fe", "lan controller")) return "有线网卡";
            // 特殊 OEM 型号没有常见关键词，WMI 返回 Ethernet AdapterType 时按有线处理。
            if ((adapterType ?? "").IndexOf("Ethernet", StringComparison.OrdinalIgnoreCase) >= 0 &&
                (id.StartsWith("PCI\\") || id.StartsWith("USB\\"))) return "有线网卡";
            return "";
        }
        private static bool Has(string source, params string[] options)
        {
            foreach (string key in options) if (source.Contains(key)) return true;
            return false;
        }
    }
}
