using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WinForms
{
    // Text Services Framework (TSF): enumerates installed/enabled IME profiles,
    // including third-party IMEs which may not appear in Get-WinUserLanguageList.
    // Read-only: no activation and no changes to Windows settings here.
    internal static class InputMethodTsf
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct TF_LANGUAGEPROFILE
        {
            public Guid clsid;
            public ushort langid;
            public Guid catid;
            public int fActive;
            public Guid guidProfile;
        }

        [ComImport, Guid("1F02B6C5-7842-4EE6-8A0B-9A24183A95CA"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ITfInputProcessorProfiles
        {
            void Register();
            void Unregister();
            void AddLanguageProfile();
            void RemoveLanguageProfile();
            void EnumInputProcessorInfo();
            void GetDefaultLanguageProfile();
            void SetDefaultLanguageProfile();
            void ActivateLanguageProfile();
            void GetActiveLanguageProfile();
            [PreserveSig]
            int GetLanguageProfileDescription(ref Guid clsid, ushort langid, ref Guid profile,
                                              out IntPtr description);
            void GetCurrentLanguage();
            void ChangeCurrentLanguage();
            [PreserveSig]
            int GetLanguageList(out IntPtr langids, out uint count);
            [PreserveSig]
            int EnumLanguageProfiles(ushort langid, out IEnumTfLanguageProfiles enumerator);
            void EnableLanguageProfile();
            [PreserveSig]
            int IsEnabledLanguageProfile(ref Guid clsid, ushort langid, ref Guid profile,
                                         out int enabled);
            void EnableLanguageProfileByDefault();
            void SubstituteKeyboardLayout();
        }

        [ComImport, Guid("3D61BF11-AC5F-42C8-A4CB-931BCC28C744"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IEnumTfLanguageProfiles
        {
            void Clone(out IEnumTfLanguageProfiles copy);
            [PreserveSig]
            int Next(uint count, [Out, MarshalAs(UnmanagedType.LPArray, SizeConst = 1)]
                     TF_LANGUAGEPROFILE[] profiles, out uint fetched);
            void Reset();
            void Skip(uint count);
        }

        [DllImport("msctf.dll", ExactSpelling = true)]
        private static extern int TF_CreateInputProcessorProfiles(out ITfInputProcessorProfiles profiles);

        public static void AddEnabledProfiles(List<InputMethodEntry> entries, List<string> diagnostics)
        {
            ITfInputProcessorProfiles service = null;
            IntPtr langids = IntPtr.Zero;
            try
            {
                int hr = TF_CreateInputProcessorProfiles(out service);
                if (hr != 0 || service == null)
                {
                    diagnostics.Add("TSF 初始化失败：HRESULT=0x" + hr.ToString("X8"));
                    return;
                }

                uint count;
                hr = service.GetLanguageList(out langids, out count);
                if (hr != 0)
                {
                    diagnostics.Add("TSF 获取语言列表失败：HRESULT=0x" + hr.ToString("X8"));
                    return;
                }
                int n = (int)Math.Min(count, 256U);
                for (int i = 0; i < n; i++)
                {
                    ushort langid = unchecked((ushort)Marshal.ReadInt16(langids, i * 2));
                    IEnumTfLanguageProfiles cursor = null;
                    try
                    {
                        if (service.EnumLanguageProfiles(langid, out cursor) != 0 || cursor == null)
                            continue;
                        TF_LANGUAGEPROFILE[] buffer = new TF_LANGUAGEPROFILE[1];
                        uint fetched;
                        int scanned = 0;
                        while (scanned++ < 128 && cursor.Next(1, buffer, out fetched) == 0 && fetched == 1)
                        {
                            TF_LANGUAGEPROFILE profile = buffer[0];
                            // Only list enabled keyboard input processors; unrelated TSF
                            // text services are not user-selectable default keyboards.
                            if (profile.catid != new Guid("34745C63-B2F0-4784-8B67-5E12C8701A31"))
                                continue;
                            int enabled;
                            if (service.IsEnabledLanguageProfile(ref profile.clsid, profile.langid,
                                ref profile.guidProfile, out enabled) != 0 || enabled == 0)
                                continue;
                            string tip = profile.langid.ToString("X4") + ":" +
                                         profile.clsid.ToString("B").ToUpperInvariant() +
                                         profile.guidProfile.ToString("B").ToUpperInvariant();
                            IntPtr description = IntPtr.Zero;
                            string name = "";
                            try
                            {
                                if (service.GetLanguageProfileDescription(ref profile.clsid,
                                    profile.langid, ref profile.guidProfile, out description) == 0 &&
                                    description != IntPtr.Zero)
                                    name = Marshal.PtrToStringBSTR(description) ?? "";
                            }
                            finally
                            {
                                if (description != IntPtr.Zero) Marshal.FreeBSTR(description);
                            }
                            bool named = !String.IsNullOrWhiteSpace(name);
                            if (!named) name = "已启用的 TSF 输入法";
                            string language;
                            try { language = CultureInfo.GetCultureInfo(profile.langid).NativeName; }
                            catch { language = "0x" + profile.langid.ToString("X4"); }
                            InputMethodService.AddOrUpdate(entries, tip, language + " · " + name,
                                                           language, named);
                        }
                    }
                    catch (Exception ex) { diagnostics.Add("TSF 语言 " + langid.ToString("X4") + "：" + ex.Message); }
                    finally
                    {
                        if (cursor != null) Marshal.ReleaseComObject(cursor);
                    }
                }
                diagnostics.Add("TSF 枚举完成。");
            }
            catch (Exception ex) { diagnostics.Add("TSF 读取不可用：" + ex.Message); }
            finally
            {
                if (langids != IntPtr.Zero) Marshal.FreeCoTaskMem(langids);
                if (service != null) Marshal.ReleaseComObject(service);
            }
        }

        // .NET InputLanguage exposes installed keyboard-layout friendly names;
        // TSF-only IMEs are supplied by the API above.
        public static void AddWindowsLayouts(List<InputMethodEntry> entries, List<string> diagnostics)
        {
            try
            {
                foreach (InputLanguage input in InputLanguage.InstalledInputLanguages)
                {
                    if (input == null || input.Culture == null) continue;
                    // HKL handles are not InputMethodTips. Do not synthesize
                    // selectable IDs from them: the override setter rejects them.
                    // Only enrich existing Windows-language-list keyboard entries.
                    string langPrefix = input.Culture.LCID.ToString("X4") + ":";
                    string layoutSuffix = unchecked((ushort)input.Handle.ToInt64()).ToString("X4");
                    string name = input.LayoutName;
                    if (String.IsNullOrWhiteSpace(name)) continue;
                    foreach (InputMethodEntry entry in entries)
                    {
                        if (entry.Id.StartsWith(langPrefix, StringComparison.OrdinalIgnoreCase) &&
                            entry.Id.Length == 13 && entry.Id.EndsWith(layoutSuffix, StringComparison.OrdinalIgnoreCase) &&
                            (entry.Display.Contains("已安装输入法") || entry.Display.Contains("名称未注册")))
                        {
                            entry.Display = input.Culture.NativeName + " · " + name;
                        }
                    }
                }
            }
            catch (Exception ex) { diagnostics.Add("Windows 键盘布局读取不可用：" + ex.Message); }
        }
    }
}
