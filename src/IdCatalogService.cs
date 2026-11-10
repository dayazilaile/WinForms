using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ITDesktopToolkit
{
    // 可选的 pci.ids / usb.ids 离线查找；不请求网络、不改写用户的本地数据库。
    // ID 识别的是总线注册厂商/设备系列，不意味着板卡零售品牌或笔记本整机品牌。
    public sealed class IdCatalogService
    {
        private readonly Dictionary<string, string> pciVendors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> pciDevices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> usbVendors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> usbDevices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public int PciVendorCount { get { return pciVendors.Count; } }
        public int PciDeviceCount { get { return pciDevices.Count; } }
        public int UsbVendorCount { get { return usbVendors.Count; } }
        public int UsbDeviceCount { get { return usbDevices.Count; } }

        private static bool IsHex4(string value)
        {
            if (value.Length != 4) return false;
            foreach (char c in value)
                if (!Uri.IsHexDigit(c)) return false;
            return true;
        }
        private static void LoadFile(string path, Dictionary<string, string> vendors, Dictionary<string, string> devices)
        {
            if (!File.Exists(path)) return;
            string currentVendor = "";
            foreach (string line in File.ReadLines(path, Encoding.UTF8))
            {
                if (String.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                bool deviceLine = line[0] == '\t' && line.Length > 1 && line[1] != '\t';
                bool vendorLine = line[0] != '\t' && line[0] != ' ';
                if (!vendorLine && !deviceLine) continue;
                string l = line.Trim();
                if (l.Length < 6 || !Char.IsWhiteSpace(l[4])) continue;
                string id = l.Substring(0, 4);
                if (!IsHex4(id)) continue;
                string name = l.Substring(5).Trim();
                if (name.Length == 0) continue;
                if (vendorLine)
                {
                    currentVendor = id.ToUpperInvariant();
                    vendors[currentVendor] = name;
                }
                else if (currentVendor.Length == 4)
                    devices[currentVendor + ":" + id.ToUpperInvariant()] = name;
            }
        }
        public void Reload()
        {
            pciVendors.Clear(); pciDevices.Clear(); usbVendors.Clear(); usbDevices.Clear();
            try { LoadFile(Path.Combine(Paths.DbDir, "pci.ids"), pciVendors, pciDevices); }
            catch (Exception ex) { Paths.Log("PCI ID database unavailable: " + ex.Message); }
            try { LoadFile(Path.Combine(Paths.DbDir, "usb.ids"), usbVendors, usbDevices); }
            catch (Exception ex) { Paths.Log("USB ID database unavailable: " + ex.Message); }
        }
        public void MatchHardware(HardwareItem item)
        {
            item.IdVendor = ""; item.IdDevice = ""; item.IdMatchKey = "";
            string raw = item.Identifier ?? "";
            Match pci = Regex.Match(raw, @"VEN_([0-9A-Fa-f]{4})&DEV_([0-9A-Fa-f]{4})", RegexOptions.IgnoreCase);
            if (pci.Success)
            {
                string vendor = pci.Groups[1].Value.ToUpperInvariant();
                string device = pci.Groups[2].Value.ToUpperInvariant();
                item.IdMatchKey = "PCI " + vendor + ":" + device;
                pciVendors.TryGetValue(vendor, out item.IdVendor);
                pciDevices.TryGetValue(vendor + ":" + device, out item.IdDevice);
                return;
            }
            Match usb = Regex.Match(raw, @"VID_([0-9A-Fa-f]{4})&PID_([0-9A-Fa-f]{4})", RegexOptions.IgnoreCase);
            if (usb.Success)
            {
                string vendor = usb.Groups[1].Value.ToUpperInvariant();
                string device = usb.Groups[2].Value.ToUpperInvariant();
                item.IdMatchKey = "USB " + vendor + ":" + device;
                usbVendors.TryGetValue(vendor, out item.IdVendor);
                usbDevices.TryGetValue(vendor + ":" + device, out item.IdDevice);
            }
        }
    }
}
