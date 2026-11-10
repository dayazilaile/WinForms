using System;
using System.Runtime.InteropServices;

namespace ITDesktopToolkit
{
    // Read the CURRENT active plan from powrprof.dll, without parsing language-dependent
    // Chinese/English powercfg.exe text. Values returned by the API are seconds.
    public static class PowerTimeReader
    {
        private static readonly Guid Video = new Guid("7516b95f-f776-4464-8c53-06167f40cc99");
        private static readonly Guid VideoIdle = new Guid("3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e");
        private static readonly Guid Sleep = new Guid("238c9fa8-0aad-41ed-83f4-97be242c8f20");
        private static readonly Guid StandbyIdle = new Guid("29f6c1db-86da-48c5-9fdb-f2b67b1f44da");
        private static readonly Guid HibernateIdle = new Guid("9d7815a6-7ee4-497e-8888-515a05f02364");

        [DllImport("powrprof.dll", ExactSpelling=true)]
        private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr schemeGuid);
        [DllImport("powrprof.dll", ExactSpelling=true)]
        private static extern uint PowerReadACValueIndex(IntPtr root, ref Guid scheme, ref Guid subgroup, ref Guid setting, out uint seconds);
        [DllImport("powrprof.dll", ExactSpelling=true)]
        private static extern uint PowerReadDCValueIndex(IntPtr root, ref Guid scheme, ref Guid subgroup, ref Guid setting, out uint seconds);
        [DllImport("kernel32.dll", ExactSpelling=true)]
        private static extern IntPtr LocalFree(IntPtr memory);

        public static bool TryRead(string setting, out uint acSeconds, out uint dcSeconds, out string error)
        {
            acSeconds = 0; dcSeconds = 0; error = "";
            Guid group, sub;
            if (setting == "monitor") { group = Video; sub = VideoIdle; }
            else if (setting == "standby") { group = Sleep; sub = StandbyIdle; }
            else if (setting == "hibernate") { group = Sleep; sub = HibernateIdle; }
            else { error = "未知电源设置"; return false; }
            IntPtr active = IntPtr.Zero;
            try
            {
                uint rc = PowerGetActiveScheme(IntPtr.Zero, out active);
                if (rc != 0 || active == IntPtr.Zero) { error = "无法读取当前电源计划，Windows 错误 " + rc; return false; }
                Guid scheme = (Guid)Marshal.PtrToStructure(active, typeof(Guid));
                uint a = PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref group, ref sub, out acSeconds);
                uint d = PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref group, ref sub, out dcSeconds);
                if (a != 0 || d != 0) { error = "该电源方案不支持读取此设置：AC=" + a + ", DC=" + d; return false; }
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
            finally { if (active != IntPtr.Zero) LocalFree(active); }
        }

        public static string Describe(uint seconds)
        {
            if (seconds == 0) return "从不";
            if (seconds % 60 == 0) return (seconds / 60).ToString() + " 分钟";
            return (seconds / 60) + " 分 " + (seconds % 60) + " 秒";
        }
    }
}
