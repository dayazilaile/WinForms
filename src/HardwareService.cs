using System;
using System.Collections.Generic;
using System.Management;
using System.Text;
using System.Threading;

namespace ITDesktopToolkit
{
    public static class HardwareService
    {
        private static IEnumerable<ManagementObject> Query(string query,CancellationToken cancel=default(CancellationToken))
        {
            cancel.ThrowIfCancellationRequested();
            ManagementObjectSearcher searcher=null;ManagementObjectCollection results=null;
            try{searcher=new ManagementObjectSearcher(query);results=searcher.Get();}
            catch(Exception ex){Paths.Log("WMI query failed: "+query+" "+ex.Message);}
            if(results==null){if(searcher!=null)searcher.Dispose();yield break;}
            try{foreach(ManagementObject row in results)using(row){cancel.ThrowIfCancellationRequested();yield return row;}}
            finally{results.Dispose();searcher.Dispose();}
        }
        private static string P(ManagementObject m, string key)
        {
            try { object o = m[key]; return o == null ? "" : Convert.ToString(o); }
            catch { return ""; }
        }
        private static string GB(string bytes)
        {
            decimal n;
            if (Decimal.TryParse(bytes, out n)) return (n / (1024m * 1024m * 1024m)).ToString("0.##") + " GiB";
            return bytes;
        }
        private static string MemoryKind(string value)
        {
            switch (value)
            {
                case "20": return "DDR";
                case "21": return "DDR2";
                case "24": return "DDR3";
                case "26": return "DDR4";
                case "27": return "LPDDR";
                case "28": return "LPDDR2";
                case "29": return "LPDDR3";
                case "30": return "LPDDR4";
                case "34": return "DDR5";
                case "35": return "LPDDR5";
                default: return "未知类型（SMBIOS " + (String.IsNullOrWhiteSpace(value) ? "未提供" : value) + "）";
            }
        }
        private static string MemoryFormFactor(string value)
        {
            switch (value)
            {
                case "8": return "DIMM";
                case "12": return "SODIMM";
                default: return "其他或未知（" + value + "）";
            }
        }
        // 设备商经常用 Default string / To Be Filled By O.E.M. 作 SMBIOS 占位值。
        // 这些不是实际产品型号，不应与商品名称拼接。
        public static string CleanSmbios(string value)
        {
            string s = (value ?? "").Trim();
            if (s.Length == 0) return "";
            string l = s.ToLowerInvariant();
            if (l == "default string" || l == "default" || l == "system product name" ||
                l == "system version" || l == "to be filled by o.e.m." || l == "to be filled by oem" ||
                l == "o.e.m." || l == "oem" || l == "none" || l == "unknown" ||
                l == "not applicable" || l == "not specified" || l == "n/a" || l == "0") return "";
            return s;
        }
        public static string Friendly(string s)
        {
            return String.IsNullOrWhiteSpace(s) ? "未提供" : s;
        }
        private static string ComputerSystemModel()
        {
            foreach (ManagementObject m in Query("SELECT Manufacturer,Model FROM Win32_ComputerSystem"))
            {
                string v = CleanSmbios(P(m,"Model"));
                if (v.Length > 0) return v;
            }
            return "";
        }
        private static string ManufacturerName(string raw)
        {
            // 只有确切的品牌映射才中文化，不凭主板 / 芯片供应商反推整机品牌。
            string cleaned = CleanSmbios(raw);
            if (cleaned.Equals("MECHREVO", StringComparison.OrdinalIgnoreCase)) return "MECHREVO（机械革命）";
            return cleaned;
        }
        public static string GetOverview()
        {
            StringBuilder b = new StringBuilder();
            b.AppendLine("【本机系统信息】");
            b.AppendLine("计算机名：" + Environment.MachineName);
            b.AppendLine("登录用户：" + Environment.UserName);
            b.AppendLine("架构：" + (Environment.Is64BitOperatingSystem ? "64 位" : "32 位"));
            b.AppendLine("当前时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            foreach (ManagementObject m in Query("SELECT Caption,Version,BuildNumber,OSArchitecture,InstallDate,LastBootUpTime,FreePhysicalMemory,TotalVisibleMemorySize FROM Win32_OperatingSystem"))
            {
                b.AppendLine("操作系统：" + P(m, "Caption"));
                b.AppendLine("系统版本：" + P(m, "Version") + " / Build " + P(m, "BuildNumber"));
                b.AppendLine("系统架构：" + P(m, "OSArchitecture"));
                string boot = P(m, "LastBootUpTime");
                try
                {
                    DateTime d = ManagementDateTimeConverter.ToDateTime(boot);
                    b.AppendLine("上次启动：" + d.ToString("yyyy-MM-dd HH:mm:ss"));
                    TimeSpan uptime = DateTime.Now - d;
                    b.AppendLine("已运行：" + (int)uptime.TotalDays + " 天 " + uptime.Hours + " 小时 " + uptime.Minutes + " 分钟");
                }
                catch { b.AppendLine("启动时间：无法解析"); }
                decimal free, total;
                if (Decimal.TryParse(P(m, "FreePhysicalMemory"), out free) && Decimal.TryParse(P(m, "TotalVisibleMemorySize"), out total) && total > 0)
                    b.AppendLine("物理内存（系统报告）：总计 " + (total / 1048576m).ToString("0.##") + " GiB，可用 " + (free / 1048576m).ToString("0.##") + " GiB");
            }
            b.AppendLine();
            b.AppendLine("【整机信息】");
            foreach (ManagementObject m in Query("SELECT Manufacturer,Model,TotalPhysicalMemory,Domain FROM Win32_ComputerSystem"))
            {
                b.AppendLine("厂商：" + Friendly(ManufacturerName(P(m, "Manufacturer"))));
                b.AppendLine("型号：" + Friendly(CleanSmbios(P(m, "Model"))));
                b.AppendLine("内存：" + GB(P(m, "TotalPhysicalMemory")));
                b.AppendLine("域/工作组：" + P(m, "Domain"));
            }
            foreach (ManagementObject m in Query("SELECT Name,NumberOfCores,NumberOfLogicalProcessors,MaxClockSpeed FROM Win32_Processor"))
                b.AppendLine("CPU：" + P(m, "Name") + " | " + P(m, "NumberOfCores") + " 核 / " + P(m, "NumberOfLogicalProcessors") + " 线程 | 最大报告频率 " + P(m, "MaxClockSpeed") + " MHz");
            foreach (ManagementObject m in Query("SELECT Manufacturer,Product,Version FROM Win32_BaseBoard"))
                b.AppendLine("主板：" + P(m, "Manufacturer") + " " + P(m, "Product") + " | 版本 " + P(m, "Version"));
            foreach (ManagementObject m in Query("SELECT Manufacturer,SMBIOSBIOSVersion,ReleaseDate FROM Win32_BIOS"))
                b.AppendLine("BIOS：" + P(m, "Manufacturer") + " " + P(m, "SMBIOSBIOSVersion"));
            b.AppendLine();
            b.AppendLine(MonitorService.Overview());
            b.AppendLine();
            b.AppendLine("【磁盘分区】");
            foreach (ManagementObject m in Query("SELECT DeviceID,VolumeName,Size,FreeSpace,DriveType FROM Win32_LogicalDisk WHERE DriveType=3"))
                b.AppendLine(P(m, "DeviceID") + " " + P(m, "VolumeName") + " | 总计 " + GB(P(m, "Size")) + " | 可用 " + GB(P(m, "FreeSpace")));
            b.AppendLine();
            b.AppendLine("提示：部分 OEM 设备的 WMI 型号不精确；硬件详情页可以显示原始硬件 ID 和本地数据库匹配结果。");
            return b.ToString();
        }

        private static string DeviceErrorName(string code)
        {
            switch (code)
            {
                case "10": return "设备无法启动";
                case "12": return "资源不足";
                case "14": return "需要重新启动";
                case "22": return "设备已禁用";
                case "28": return "未安装驱动程序";
                case "31": return "无法加载所需驱动";
                case "32": return "驱动服务已禁用";
                case "43": return "Windows 已停止该设备";
                case "45": return "设备未连接（若报告）";
                default: return "请查看设备管理器错误详情";
            }
        }
        public static List<HardwareItem> Inventory(DatabaseService db){return Inventory(db,CancellationToken.None);}
        public static List<HardwareItem> Inventory(DatabaseService db,CancellationToken cancel)
        {
            List<HardwareItem> list = new List<HardwareItem>();
            foreach (ManagementObject m in Query("SELECT Name,Manufacturer,ProcessorId,SocketDesignation,NumberOfCores,NumberOfLogicalProcessors,MaxClockSpeed FROM Win32_Processor",cancel))
            {
                list.Add(new HardwareItem { Type = "CPU", Name = P(m,"Name"), Manufacturer = P(m,"Manufacturer"), Model = P(m,"Name"), Identifier = P(m,"ProcessorId"), Specs = P(m,"NumberOfCores") + " 核 / " + P(m,"NumberOfLogicalProcessors") + " 线程，接口报告：" + P(m,"SocketDesignation") + "，最大频率：" + P(m,"MaxClockSpeed") + " MHz" });
            }
            foreach (ManagementObject m in Query("SELECT Manufacturer,Product,Version,SerialNumber FROM Win32_BaseBoard",cancel))
            {
                list.Add(new HardwareItem { Type = "主板", Name = P(m,"Product"), Manufacturer = P(m,"Manufacturer"), Model = P(m,"Product"), Identifier = "", Specs = "PCB/固件报告版本：" + P(m,"Version") + "；序列号请在系统硬件信息工具中查看" });
            }
            string computerModel = ComputerSystemModel();
            foreach (ManagementObject m in Query("SELECT Vendor,Name,Version FROM Win32_ComputerSystemProduct",cancel))
            {
                string productName = CleanSmbios(P(m, "Name"));
                string version = CleanSmbios(P(m, "Version"));
                string finalModel = productName.Length > 0 ? productName : computerModel;
                if (finalModel.Length == 0) finalModel = "未识别整机型号";
                list.Add(new HardwareItem {
                    Type = "整机", Name = finalModel,
                    Manufacturer = ManufacturerName(P(m, "Vendor")),
                    Model = finalModel,
                    Specs = "Win32_ComputerSystem.Model：" + Friendly(computerModel) +
                        "；产品 Name：" + Friendly(productName) +
                        "；有效 Version：" + Friendly(version) +
                        "；已自动剔除 Default string 等 SMBIOS 占位值"
                });
            }
            int index = 0;
            foreach (ManagementObject m in Query("SELECT Manufacturer,PartNumber,Capacity,Speed,ConfiguredClockSpeed,DeviceLocator,BankLabel,FormFactor,SMBIOSMemoryType FROM Win32_PhysicalMemory",cancel))
            {
                index++;
                string vendor = P(m, "Manufacturer").Trim();
                string part = P(m, "PartNumber").Trim();
                string locator = P(m, "DeviceLocator");
                string bank = P(m, "BankLabel");
                string kind = MemoryKind(P(m, "SMBIOSMemoryType"));
                string cap = GB(P(m, "Capacity"));
                string rated = P(m, "Speed");
                string configured = P(m, "ConfiguredClockSpeed");
                list.Add(new HardwareItem {
                    Type = "内存",
                    Name = "内存 " + index + " / " + cap + " " + kind + " / " + (String.IsNullOrWhiteSpace(locator) ? "插槽未知" : locator),
                    Manufacturer = vendor,
                    Model = part,
                    Identifier = "插槽：" + locator + "；内存组：" + bank,
                    Specs = "容量：" + cap + "；类型：" + kind + "；模块规格：" + MemoryFormFactor(P(m, "FormFactor")) +
                        "；SPD/SMBIOS 标称速率：" + (String.IsNullOrWhiteSpace(rated) ? "未知" : rated + " MT/s") +
                        "；当前配置速率：" + (String.IsNullOrWhiteSpace(configured) ? "未知" : configured + " MT/s") +
                        "；原始制造商：" + (String.IsNullOrWhiteSpace(vendor) ? "未提供" : vendor) +
                        "；零件号：" + (String.IsNullOrWhiteSpace(part) ? "未提供" : part)
                });
            }
            foreach (ManagementObject m in Query("SELECT Model,Manufacturer,Size,InterfaceType,PNPDeviceID,SerialNumber FROM Win32_DiskDrive",cancel))
            {
                list.Add(new HardwareItem { Type = "硬盘", Name = P(m,"Model"), Manufacturer = P(m,"Manufacturer"), Model = P(m,"Model"), Identifier = P(m,"PNPDeviceID"), Specs = "容量：" + GB(P(m,"Size")) + "；接口报告：" + P(m,"InterfaceType") + "；SMART 未在本版检测" });
            }
            foreach (ManagementObject m in Query("SELECT Name,AdapterCompatibility,DriverVersion,PNPDeviceID,AdapterRAM FROM Win32_VideoController",cancel))
            {
                list.Add(new HardwareItem { Type = "显卡", Name = P(m,"Name"), Manufacturer = P(m,"AdapterCompatibility"), Model = P(m,"Name"), Identifier = P(m,"PNPDeviceID"), Specs = "驱动版本：" + P(m,"DriverVersion") + "；显存字段：" + P(m,"AdapterRAM") + " 字节（WMI 值可能截断）" });
            }
            list.AddRange(MonitorService.ReadMonitors());
            foreach (ManagementObject m in Query("SELECT Name,Manufacturer,PNPDeviceID,MACAddress,Speed,NetConnectionStatus,NetConnectionID,AdapterType,ServiceName FROM Win32_NetworkAdapter WHERE PhysicalAdapter=True",cancel))
            {
                string kind = NicClassifier.Classify(P(m,"Name"), P(m,"NetConnectionID"), P(m,"AdapterType"), P(m,"PNPDeviceID"));
                if (kind.Length == 0) continue; // 不在硬件列表展示回环、蓝牙 PAN、虚拟、隧道等网络接口。
                list.Add(new HardwareItem { Type = kind, Name = P(m,"Name"), Manufacturer = P(m,"Manufacturer"),
                    Model = P(m,"Name"), Identifier = P(m,"PNPDeviceID"),
                    Specs = "连接名称：" + P(m,"NetConnectionID") + "；MAC：" + P(m,"MACAddress") +
                    "；系统速度：" + P(m,"Speed") + " bps；连接状态代码：" + P(m,"NetConnectionStatus") });
            }
            foreach (ManagementObject m in Query("SELECT Name,Manufacturer,PNPDeviceID,Status,ConfigManagerErrorCode FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0",cancel))
            {
                string error = P(m, "ConfigManagerErrorCode");
                bool disabled = error == "22";
                list.Add(new HardwareItem { Type = disabled ? "已禁用设备" : "异常设备",
                    Name = P(m,"Name"), Manufacturer = P(m,"Manufacturer"), Model = P(m,"Name"),
                    Identifier = P(m,"PNPDeviceID"),
                    Specs = "设备管理器代码：" + error + "（" + DeviceErrorName(error) + "）；状态：" + P(m,"Status") +
                        (disabled ? "；注意：设备可能由用户主动禁用" : "；请在设备管理器核实问题") });
            }
            // 普通 USB 外设不再重复列入详细硬件清单；有错误的 USB 设备仍会显示在异常设备中。
            foreach (HardwareItem item in list) db.MatchHardware(item);
            return list;
        }
    }
}
