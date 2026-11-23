using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace WinForms
{
    public sealed class InputMethodEntry
    {
        public string Id, Display, Language;
        public override string ToString() { return Display; }
    }

    public sealed class InputMethodSnapshot
    {
        public readonly List<InputMethodEntry> Entries = new List<InputMethodEntry>();
        public string OverrideTip = "";
        public string PreferredTip = "";
        public string Diagnostic = "";
    }

    public static class InputMethodService
    {
        internal static void AddOrUpdate(List<InputMethodEntry> entries, string id,
                                         string label, string language, bool nativeName)
        {
            if (String.IsNullOrWhiteSpace(id)) return;
            foreach (InputMethodEntry entry in entries)
            {
                if (!entry.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) continue;
                // Prefer native TSF profile names over registry/PowerShell placeholders.
                if (nativeName || entry.Display.Contains("名称未注册") || entry.Display.Contains("已安装输入法"))
                    entry.Display = label;
                return;
            }
            entries.Add(new InputMethodEntry { Id = id, Display = label, Language = language });
        }

        public static InputMethodSnapshot Read()
        {
            InputMethodSnapshot result = new InputMethodSnapshot();
            List<string> diagnostics = new List<string>();
            try { diagnostics.Add("当前运行账户：" + System.Security.Principal.WindowsIdentity.GetCurrent().Name +
                                  "；输入法读取与设置只针对该账户。若通过另一个管理员账户提权，请改用目标用户运行工具箱。"); }
            catch { }

            // PowerShell supplies the user language ordering and the *override* setting.
            // A missing override is normal, not a failure to detect installed IMEs.
            string script = @"
$ErrorActionPreference='Stop'
try {
 $override=Get-WinDefaultInputMethodOverride -ErrorAction Stop
 $tip=if($null -ne $override){ [string]$override.InputMethodTip }else{''}
 Write-Output ('OVERRIDE|'+$tip)
} catch { Write-Output ('WARNING|默认覆盖值读取失败：'+$_.Exception.Message) }
try {
 $list=Get-WinUserLanguageList -ErrorAction Stop
 foreach($lang in $list){
  $language=[string]$lang.LocalizedName
  foreach($tip in $lang.InputMethodTips){
   $tip=[string]$tip
   if(-not $tip){continue}
   $label=''
   if($tip -eq '0409:00000409'){$label='美式键盘 (US)'}
   elseif($tip -eq '0804:00000804'){$label='中文简体键盘'}
   elseif($tip -like '0804:{81D4E9C9-*'){$label='微软拼音'}
   elseif($tip -like '0804:{6A498709-*'){$label='微软五笔'}
   else{$label='已安装输入法或键盘'}
   # Registry fallback when the native TSF description is unavailable.
   if($label -eq '已安装输入法或键盘' -and
      $tip -match '^[0-9a-fA-F]{4}:(\{[0-9a-fA-F-]{36}\})(\{[0-9a-fA-F-]{36}\})$'){
    $clsid=$matches[1]; $profile=$matches[2]; $lcid=$tip.Substring(0,4)
    foreach($key in @(
      ('Registry::HKEY_CURRENT_USER\SOFTWARE\Microsoft\CTF\TIP\'+$clsid+'\LanguageProfile\0x0000'+$lcid+'\'+$profile),
      ('Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\CTF\TIP\'+$clsid+'\LanguageProfile\0x0000'+$lcid+'\'+$profile)
    )){
     try{
      $properties=Get-ItemProperty -LiteralPath $key -ErrorAction Stop
      foreach($property in @('Description','DisplayName','LocalizedName')){
       $value=[string]$properties.$property
       if($value -and -not $value.StartsWith('@')){$label=$value;break}
      }
      if($label -ne '已安装输入法或键盘'){break}
     }catch{}
    }
   }
   $text=($language+' · '+$label)
   $base64=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($text))
   Write-Output ('IM|'+$tip+'|'+$base64)
  }
 }
} catch { Write-Output ('WARNING|用户语言列表读取失败：'+$_.Exception.Message) }
";
            string raw = PowerShellService.RunScript(script, 2);
            diagnostics.Add("【PowerShell 用户语言列表与覆盖设置】\r\n" + raw);
            foreach (string line in raw.Split(new char[] {'\r','\n'}, StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.StartsWith("OVERRIDE|", StringComparison.Ordinal))
                    result.OverrideTip = line.Substring(9).Trim();
                else if (line.StartsWith("IM|", StringComparison.Ordinal))
                {
                    string[] parts = line.Split('|');
                    if (parts.Length != 3) continue;
                    try
                    {
                        string label = Encoding.UTF8.GetString(Convert.FromBase64String(parts[2]));
                        AddOrUpdate(result.Entries, parts[1], label, label, false);
                        if (String.IsNullOrEmpty(result.PreferredTip)) result.PreferredTip = parts[1];
                    }
                    catch (Exception ex) { diagnostics.Add("无法解析输入法项目：" + ex.Message); }
                }
                else if (line.StartsWith("WARNING|", StringComparison.Ordinal))
                    diagnostics.Add(line.Substring(8));
            }

            // Query modern Text Services Framework to pick up third-party IMEs
            // (Sogou, QQ Pinyin, etc.) and their registered profile descriptions.
            InputMethodTsf.AddEnabledProfiles(result.Entries, diagnostics);
            InputMethodTsf.AddWindowsLayouts(result.Entries, diagnostics);
            if (!String.IsNullOrWhiteSpace(result.OverrideTip) &&
                !Contains(result.Entries, result.OverrideTip))
                diagnostics.Add("提示：系统覆盖值不在已枚举列表中。可能已卸载、属于另一用户或 TSF 读取受限。");
            if (result.Entries.Count == 0)
                diagnostics.Add("没有取得任何可用输入法。请查看上面的详细错误，或通过 Windows 语言设置核对。");
            result.Diagnostic = String.Join("\r\n", diagnostics.ToArray());
            return result;
        }

        private static bool Contains(List<InputMethodEntry> entries, string id)
        {
            foreach (InputMethodEntry entry in entries)
                if (entry.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static string Set(string id)
        {
            if (String.IsNullOrWhiteSpace(id) || !Regex.IsMatch(id, @"^[0-9a-fA-F]{4}:(?:[0-9a-fA-F]{8}|\{[0-9a-fA-F-]{36}\}\{[0-9a-fA-F-]{36}\})$"))
                throw new ArgumentException("输入法标识无效");
            string script = "$ErrorActionPreference='Stop'; " +
                "Set-WinDefaultInputMethodOverride -InputTip " + PowerShellService.Quote(id) + " -ErrorAction Stop; " +
                "$new=Get-WinDefaultInputMethodOverride -ErrorAction Stop; $actual=if($new){[string]$new.InputMethodTip}else{''}; " +
                "if($actual -ine " + PowerShellService.Quote(id) + "){ throw ('设置未生效；Windows 返回的覆盖值：'+$actual) }; " +
                "'VERIFY|'+$actual";
            return PowerShellService.RunScript(script, 2);
        }
    }
}
