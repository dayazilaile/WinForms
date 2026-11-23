using System;
using System.Collections.Generic;
using System.Globalization;
using System.Management;
using System.Text;
using System.Windows.Forms;

namespace WinForms
{
    // Read-only monitor inventory. EDID is device-reported data, not a retail SKU lookup.
    // Never correlate multiple displays by WMI iteration order; it is not guaranteed.
    public static class MonitorService
    {
        private sealed class Extra
        {
            public int WidthCm;
            public int HeightCm;
            public string Connection = "";
        }

        private static List<ManagementObject> Query(string scope, string sql)
        {
            List<ManagementObject> rows = new List<ManagementObject>();
            try
            {
                using (ManagementObjectSearcher search = new ManagementObjectSearcher(scope, sql))
                using (ManagementObjectCollection results = search.Get())
                {
                    foreach (ManagementObject row in results) rows.Add(row);
                }
            }
            catch (Exception ex) { Paths.Log("Monitor WMI unavailable: " + sql + " / " + ex.Message); }
            return rows;
        }
        private static object Value(ManagementObject m, string name)
        {
            try { return m[name]; }
            catch { return null; }
        }
        private static string Str(ManagementObject m, string name)
        {
            object v = Value(m, name);
            return v == null ? "" : Convert.ToString(v, CultureInfo.InvariantCulture).Trim();
        }
        private static bool Active(ManagementObject m)
        {
            object v = Value(m, "Active");
            try { return v == null || Convert.ToBoolean(v, CultureInfo.InvariantCulture); }
            catch { return true; }
        }
        private static int Int(ManagementObject m, string name)
        {
            try
            {
                object v = Value(m, name);
                return v == null ? 0 : Convert.ToInt32(v, CultureInfo.InvariantCulture);
            }
            catch { return 0; }
        }
        // WmiMonitorID string values are uint16 arrays with an optional trailing 0.
        private static string Decode(object input)
        {
            Array data = input as Array;
            if (data == null) return "";
            StringBuilder b = new StringBuilder();
            foreach (object obj in data)
            {
                int value;
                try { value = Convert.ToInt32(obj, CultureInfo.InvariantCulture); }
                catch { break; }
                if (value == 0) break;
                if (value >= 32 && value <= 0xFFFD && value != 0x7F)
                    b.Append((char)value);
            }
            return b.ToString().Trim();
        }
        public static string BrandFromEdid(string id)
        {
            // Common VESA/EISA manufacturer IDs. Unlisted codes stay visible as IDs, not guessed brands.
            switch ((id ?? "").Trim().ToUpperInvariant())
            {
                case "XMI": return "小米 Xiaomi";
                case "DEL": return "戴尔 Dell";
                case "SAM": return "三星 Samsung";
                case "GSM": return "LG";
                case "AOC": return "冠捷 AOC";
                case "ACR": return "宏碁 Acer";
                case "AUO": return "友达 AUO（面板厂商）";
                case "BOE": return "京东方 BOE（面板厂商）";
                case "BNQ": return "明基 BenQ";
                case "CMN": return "奇美 / 群创（面板厂商）";
                case "CSO": return "华星光电 CSOT（面板厂商）";
                case "HWP": case "HPN": return "惠普 HP";
                case "LEN": return "联想 Lenovo";
                case "PHL": return "飞利浦 Philips";
                case "VSC": return "优派 ViewSonic";
                case "SNY": return "索尼 Sony";
                case "APP": return "苹果 Apple";
                case "ASU": return "华硕 ASUS";
                case "MSI": return "微星 MSI";
                case "GBT": return "技嘉 GIGABYTE";
                case "IVM": return "艺卓 EIZO";
                case "NEC": return "NEC";
                case "FUS": return "富士通 Fujitsu";
                case "HIT": return "日立 Hitachi";
                case "SEC": return "三星显示 Samsung Display（面板厂商）";
                case "SHL": return "夏普 Sharp";
                case "QDS": return "奇美 / 群创 QDS（面板厂商）";
                case "HKC": return "惠科 HKC";
                case "KTC": return "康冠 KTC";
                case "HSD": return "瀚宇彩晶 HannStar（面板厂商）";
                case "SDC": return "三星显示 Samsung Display（面板厂商）";
                case "CHD": return "奇美 Chimei（面板厂商）";
                case "EPI": return "Envision";
                case "GVT": return "Great Wall / 长城";
                case "LPL": return "LG Display（面板厂商）";
                default: return "";
            }
        }
        private static string Connection(uint id)
        {
            switch (id)
            {
                case 0: return "VGA (HD15)";
                case 1: return "S-Video";
                case 2: return "Composite";
                case 3: return "Component";
                case 4: return "DVI";
                case 5: return "HDMI";
                case 6: return "LVDS（内置）";
                case 8: return "D-JPN";
                case 9: return "SDI";
                case 10: return "DisplayPort";
                case 11: return "eDP（内置）";
                case 12: return "UDI";
                case 13: return "UDI（内置）";
                case 14: return "SDTV Dongle";
                case 15: return "Miracast";
                case 16: return "间接有线显示";
                case 17: return "虚拟显示";
                case 18: return "USB-C DP 隧道（系统报告）";
                case UInt32.MaxValue: return "其他 / 未知";
                default: return "未识别的连接代码 " + id;
            }
        }
        private static Dictionary<string, Extra> ReadExtras()
        {
            Dictionary<string, Extra> map = new Dictionary<string, Extra>(StringComparer.OrdinalIgnoreCase);
            foreach (ManagementObject m in Query(@"\\.\root\wmi", "SELECT InstanceName,Active,MaxHorizontalImageSize,MaxVerticalImageSize FROM WmiMonitorBasicDisplayParams"))
            {
                if (!Active(m)) continue;
                string key = Str(m, "InstanceName");
                if (key.Length == 0) continue;
                Extra e;
                if (!map.TryGetValue(key, out e)) { e = new Extra(); map[key] = e; }
                e.WidthCm = Int(m, "MaxHorizontalImageSize");
                e.HeightCm = Int(m, "MaxVerticalImageSize");
            }
            foreach (ManagementObject m in Query(@"\\.\root\wmi", "SELECT InstanceName,Active,VideoOutputTechnology FROM WmiMonitorConnectionParams"))
            {
                if (!Active(m)) continue;
                string key = Str(m, "InstanceName");
                if (key.Length == 0) continue;
                Extra e;
                if (!map.TryGetValue(key, out e)) { e = new Extra(); map[key] = e; }
                try
                {
                    object v = Value(m, "VideoOutputTechnology");
                    if (v != null) e.Connection = Connection(Convert.ToUInt32(v, CultureInfo.InvariantCulture));
                }
                catch { }
            }
            return map;
        }
        private static string Size(Extra e)
        {
            if (e == null || e.WidthCm < 1 || e.HeightCm < 1) return "EDID 未提供有效尺寸";
            double diagonal = Math.Sqrt((double)e.WidthCm * e.WidthCm + (double)e.HeightCm * e.HeightCm) / 2.54;
            return e.WidthCm + " × " + e.HeightCm + " cm（对角约 " + diagonal.ToString("0.#", CultureInfo.InvariantCulture) + " 英寸，EDID 估算）";
        }
        private static string ScreenSpecs(Screen screen)
        {
            return screen.DeviceName + "；当前桌面分辨率：" + screen.Bounds.Width + " × " + screen.Bounds.Height +
                "；主屏：" + (screen.Primary ? "是" : "否") + "；坐标：(" + screen.Bounds.X + "," + screen.Bounds.Y + ")";
        }
        public static List<HardwareItem> ReadMonitors()
        {
            List<HardwareItem> list = new List<HardwareItem>();
            Dictionary<string, Extra> extras = ReadExtras();
            HashSet<string> found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ManagementObject m in Query(@"\\.\root\wmi", "SELECT Active,InstanceName,ManufacturerName,ProductCodeID,UserFriendlyName,SerialNumberID,YearOfManufacture,WeekOfManufacture FROM WmiMonitorID"))
            {
                if (!Active(m)) continue;
                string instance = Str(m, "InstanceName");
                if (instance.Length == 0 || !found.Add(instance)) continue;
                string code = Decode(Value(m, "ManufacturerName"));
                string brand = BrandFromEdid(code);
                string model = Decode(Value(m, "UserFriendlyName"));
                string product = Decode(Value(m, "ProductCodeID"));
                string serial = Decode(Value(m, "SerialNumberID"));
                Extra e;
                extras.TryGetValue(instance, out e);
                string name = model.Length == 0 ? "显示器（型号未提供）" : model;
                string specs = "识别来源：EDID / WmiMonitorID；厂商代码：" + (code.Length == 0 ? "未提供" : code) +
                    "；产品代码：" + (product.Length == 0 ? "未提供" : product) +
                    "；显示尺寸：" + Size(e) +
                    "；连接方式：" + (e == null || e.Connection.Length == 0 ? "未报告" : e.Connection) +
                    "；出厂年份：" + (Int(m, "YearOfManufacture") > 1990 ? Str(m, "YearOfManufacture") : "未报告") +
                    "；序列号：" + (serial.Length == 0 ? "EDID 未报告" : "已读取（末四位 " + (serial.Length > 4 ? serial.Substring(serial.Length - 4) : serial) + "）");
                list.Add(new HardwareItem {
                    Type = "显示器", Name = name, Model = model.Length == 0 ? "型号未提供（EDID 产品码 " + (product.Length == 0 ? "未知" : product) + "）" : model,
                    Manufacturer = brand.Length == 0 ? (code.Length == 0 ? "EDID 未提供厂商" : "EDID 厂商代码 " + code) : brand,
                    Identifier = instance, Specs = specs
                });
            }
            // Only when EDID is unavailable, use the installed active monitor device entries.
            if (list.Count == 0)
            {
                foreach (ManagementObject m in Query(@"\\.\root\cimv2", "SELECT Name,Manufacturer,PNPDeviceID,ConfigManagerErrorCode FROM Win32_PnPEntity WHERE PNPClass='Monitor'"))
                {
                    string id = Str(m, "PNPDeviceID");
                    if (id.Length == 0 || Int(m, "ConfigManagerErrorCode") != 0 || !found.Add(id)) continue;
                    string name = Str(m, "Name");
                    string vendor = Str(m, "Manufacturer");
                    string[] fields = id.Split('\\');
                    string monitorCode = fields.Length > 1 && fields[1].Length >= 3 ? fields[1].Substring(0, 3) : "";
                    string brand = BrandFromEdid(monitorCode);
                    if (brand.Length == 0 && vendor.Length > 0 && vendor.IndexOf("Standard", StringComparison.OrdinalIgnoreCase) < 0 && vendor.IndexOf("标准", StringComparison.OrdinalIgnoreCase) < 0)
                        brand = vendor;
                    list.Add(new HardwareItem { Type = "显示器", Name = name.Length == 0 ? "PnP 显示器" : name,
                        Manufacturer = brand.Length == 0 ? "未识别（PnP）" : brand,
                        Model = name.Length == 0 ? "型号未提供（PnP）" : name,
                        Identifier = id, Specs = "识别来源：Win32_PnPEntity；EDID 读取失败或不可用，型号及连接方式可能不完整；设备管理器报告正常" });
                }
            }
            Screen[] screens;
            try { screens = Screen.AllScreens; }
            catch (Exception ex) { Paths.Log("Screen.AllScreens failed: " + ex.Message); screens = new Screen[0]; }
            if (list.Count == 0 && screens.Length > 0)
            {
                list.Add(new HardwareItem { Type = "显示器", Name = "显示器型号未识别",
                    Manufacturer = "未识别", Model = "型号未知（仅 Windows 显示输出）",
                    Specs = "无法读取活动 EDID 和正常 PnP 显示器，分辨率仍可由 Windows 枚举；不是确定的硬件商品型号。" });
            }
            if (screens.Length == 1 && list.Count == 1)
            {
                list[0].Specs += "；" + ScreenSpecs(screens[0]);
            }
            else
            {
                foreach (Screen screen in screens)
                {
                    list.Add(new HardwareItem { Type = "显示输出", Name = screen.DeviceName,
                        Manufacturer = "Windows", Model = screen.Bounds.Width + " × " + screen.Bounds.Height,
                        Identifier = screen.DeviceName, Specs = "识别来源：Screen.AllScreens；" + ScreenSpecs(screen) +
                            "；多屏不按扫描顺序推断对应的 EDID 设备" });
                }
            }
            if (list.Count == 0)
                list.Add(new HardwareItem { Type = "显示器", Name = "未能获取显示器", Manufacturer = "未识别",
                    Model = "未识别", Specs = "EDID、PnP 和屏幕枚举均不可用；常见于远程会话、虚拟显示器或驱动未提供资料。" });
            return list;
        }
        public static string Overview()
        {
            StringBuilder b = new StringBuilder();
            b.AppendLine("【显示设备】");
            foreach (HardwareItem item in ReadMonitors())
                b.AppendLine(item.Type + "：" + item.Manufacturer + " · " + item.DisplayModel + " | " + item.Specs);
            return b.ToString().TrimEnd();
        }
    }
}
