using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace ITDesktopToolkit
{
    public sealed class StartupItem
    {
        public string Name, Source, Command, RunKey, ApprovalKey, ValueName;
        public RegistryHive Hive;
        public bool Enabled;
        public bool IsFolder;
        public string Status { get { return Enabled ? "已启用" : "已禁用"; } }
    }
    public static class StartupService
    {
        private const string BaseRun = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ApprovalRun = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        private const string ApprovalRun32 = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32";
        private const string ApprovalFolder = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";
        public static List<StartupItem> Read()
        {
            List<StartupItem> list = new List<StartupItem>();
            ReadRun(list, Registry.CurrentUser, RegistryHive.CurrentUser, BaseRun, ApprovalRun, "当前用户 Run");
            ReadRun(list, Registry.LocalMachine, RegistryHive.LocalMachine, BaseRun, ApprovalRun, "所有用户 Run");
            ReadRun(list, Registry.LocalMachine, RegistryHive.LocalMachine, @"Software\Wow6432Node\Microsoft\Windows\CurrentVersion\Run", ApprovalRun32, "所有用户 Run32");
            ReadFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.Startup), "当前用户启动文件夹", RegistryHive.CurrentUser);
            ReadFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "所有用户启动文件夹", RegistryHive.LocalMachine);
            return list;
        }
        private static void ReadRun(List<StartupItem> list, RegistryKey hive, RegistryHive which, string run, string approval, string caption)
        {
            try
            {
                using (RegistryKey r = hive.OpenSubKey(run))
                {
                    if (r == null) return;
                    foreach (string name in r.GetValueNames())
                    {
                        string command = Convert.ToString(r.GetValue(name, ""));
                        list.Add(new StartupItem { Name = name, Command = command, Source = caption, RunKey = run, ApprovalKey = approval,
                            ValueName = name, Hive = which, Enabled = ReadEnabled(which, approval, name) });
                    }
                }
            }
            catch (Exception ex) { Paths.Log("Startup scan " + caption + ": " + ex.Message); }
        }
        private static void ReadFolder(List<StartupItem> list, string dir, string caption, RegistryHive which)
        {
            try
            {
                if (!Directory.Exists(dir)) return;
                foreach (string p in Directory.GetFiles(dir))
                {
                    string name = Path.GetFileName(p);
                    list.Add(new StartupItem { Name = name, Command = p, Source = caption, IsFolder = true,
                        ValueName = name, ApprovalKey = ApprovalFolder, Hive=which, Enabled = ReadEnabled(which, ApprovalFolder, name) });
                }
            }
            catch (Exception ex) { Paths.Log("Startup folder scan: " + ex.Message); }
        }
        private static bool ReadEnabled(RegistryHive hive, string path, string name)
        {
            try
            {
                using (RegistryKey key = (hive == RegistryHive.CurrentUser ? Registry.CurrentUser : Registry.LocalMachine).OpenSubKey(path))
                {
                    byte[] value = key == null ? null : key.GetValue(name) as byte[];
                    return value == null || value.Length == 0 || value[0] != 3;
                }
            }
            catch { return true; }
        }
        public static string SetEnabled(StartupItem item, bool enabled)
        {
            if (item.Hive == RegistryHive.LocalMachine && !CommandService.IsAdministrator())
                return "所有用户启动项需要管理员权限。";
            byte[] original = null;
            using (RegistryKey r = (item.Hive == RegistryHive.CurrentUser ? Registry.CurrentUser : Registry.LocalMachine).OpenSubKey(item.ApprovalKey))
                original = r == null ? null : r.GetValue(item.ValueName) as byte[];
            string log = Path.Combine(Paths.MachineDir, "startup_changes.tsv");
            string old = original == null ? "(无值)" : Convert.ToBase64String(original);
            File.AppendAllText(log, OperationAudit.Stamp("修改启动项 / "+item.Name) + DateTimeOffset.Now.ToString("o") + "\t" + item.Source + "\t" + item.ValueName.Replace("\t", " ") + "\t" + old + "\t" + enabled + Environment.NewLine);
            byte[] value = new byte[12];
            value[0] = enabled ? (byte)2 : (byte)3;
            using (RegistryKey r = (item.Hive == RegistryHive.CurrentUser ? Registry.CurrentUser : Registry.LocalMachine).CreateSubKey(item.ApprovalKey)) r.SetValue(item.ValueName, value, RegistryValueKind.Binary);
            bool actual = ReadEnabled(item.Hive, item.ApprovalKey, item.ValueName);
            return actual == enabled ? "已写入并重新读取：" + item.Name + " → " + (actual ? "已启用" : "已禁用") : "写入后状态不一致，请使用 Windows 启动设置核实。";
        }
    }
}
