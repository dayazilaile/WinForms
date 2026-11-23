using System;
using System.IO;
using System.Text;

namespace WinForms
{
    // 系统安全状态是多来源信息。某项接口不存在不能被推断为“已关闭”。
    // 不安装、删除或修补 Defender / Windows Security / Security Center 组件。
    public static class SecurityDiagnostics
    {
        private static readonly string InspectionScript = @"
$ErrorActionPreference='Continue'
Write-Output '【Windows 防护状态 / 组件可用性】'
Write-Output ('查询时间：' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))
foreach($name in @('WinDefend','SecurityHealthService','wscsvc')) {
    try {
        $service=Get-Service -Name $name -ErrorAction Stop
        Write-Output ($name + ' 服务：' + $service.Status)
    } catch { Write-Output ($name + ' 服务：未安装或无权读取（不代表未启用防护）') }
}
if(Get-Command Get-MpComputerStatus -ErrorAction SilentlyContinue) {
    try {
        $d=Get-MpComputerStatus -ErrorAction Stop
        if($null -ne $d) {
            Write-Output ('Defender 运行模式：' + $d.AMRunningMode)
            Write-Output ('Defender 实时保护：' + $d.RealTimeProtectionEnabled)
            Write-Output ('Defender 反病毒启用：' + $d.AntivirusEnabled)
            Write-Output ('Defender 篡改防护：' + $d.IsTamperProtected)
        } else { Write-Output 'Defender：命令未返回数据，状态未知' }
    } catch { Write-Output ('Defender：状态接口不可用（' + $_.Exception.Message + '）') }
} else { Write-Output 'Defender：未找到 Get-MpComputerStatus 命令，跳过该检测' }
Write-Output ''
Write-Output '【Windows 安全中心登记的杀毒产品（只读）】'
try {
    $av=@(Get-CimInstance -Namespace root/SecurityCenter2 -ClassName AntiVirusProduct -ErrorAction Stop)
    if($av.Count -eq 0) { Write-Output '未返回杀毒产品；不能据此断言没有安全软件。' }
    foreach($product in $av) {
        Write-Output ('产品：' + $product.displayName + ' | 状态原始码：' + $product.productState)
    }
} catch { Write-Output ('安全中心 WMI 信息不可用：' + $_.Exception.Message) }
Write-Output ''
Write-Output '【Windows 防火墙】'
if(Get-Command Get-NetFirewallProfile -ErrorAction SilentlyContinue) {
    try {
        foreach($profile in @(Get-NetFirewallProfile -ErrorAction Stop)) {
            Write-Output ($profile.Name + '：' + $(if($profile.Enabled){'开启'}else{'关闭'}))
        }
    } catch { Write-Output ('防火墙状态无法读取：' + $_.Exception.Message) }
} else {
    Write-Output '缺少防火墙 PowerShell 管理命令，使用 netsh 读取：'
    try { netsh advfirewall show allprofiles } catch { Write-Output ('netsh 无法运行：' + $_.Exception.Message) }
}
Write-Output ''
Write-Output '说明：缺失命令/服务可能与系统版本、第三方杀毒软件、组织策略或组件损坏有关。本工具只读查询，不会为此增删系统组件。'
";

        public static string Inspect()
        {
            return PowerShellService.RunScript(InspectionScript, 2);
        }

        public static string Change(bool disable)
        {
            if (!CommandService.IsAdministrator()) return "需要管理员权限，未执行。";
            string backupFile = Path.Combine(Paths.MachineDir, "security_snapshot.clixml");
            string escapedBackup = backupFile.Replace("'", "''");
            // 备份使用与旧版一致的 Mp/Firewall 属性结构，保留兼容性。
            // 缺失 Defender cmdlet 时只处理能检测且能备份的项目；不以注册表绕过防护。
            string script = @"
$ErrorActionPreference='Stop'
$bak='__BACKUP_PATH__'
$hasMp=(Get-Command Get-MpPreference -ErrorAction SilentlyContinue) -and (Get-Command Set-MpPreference -ErrorAction SilentlyContinue)
$hasFw=(Get-Command Get-NetFirewallProfile -ErrorAction SilentlyContinue) -and (Get-Command Set-NetFirewallProfile -ErrorAction SilentlyContinue)
Write-Output '【操作前检测】'
Write-Output ('Defender 管理命令：' + $(if($hasMp){'可用'}else{'不可用，跳过'}))
Write-Output ('Windows 防火墙管理命令：' + $(if($hasFw){'可用'}else{'不可用，跳过'}))

if('__ACTION__' -eq 'disable') {
    if(-not $hasMp -and -not $hasFw) {
        Write-Output '未找到可管理的防护组件：未执行任何修改。'
        return
    }
    if(-not(Test-Path -LiteralPath $bak)) {
        $mp=$null; $fw=@()
        if($hasMp) { try { $mp=Get-MpPreference -ErrorAction Stop } catch { Write-Output ('Defender 备份读取失败：'+$_.Exception.Message); $hasMp=$false } }
        if($hasFw) { try { $fw=@(Get-NetFirewallProfile -ErrorAction Stop | Select-Object Name,Enabled) } catch { Write-Output ('防火墙备份读取失败：'+$_.Exception.Message); $hasFw=$false } }
        if(-not $hasMp -and -not $hasFw) { Write-Output '原始配置无法读取，已停止修改。';return }
        try {
            [pscustomobject]@{Mp=$mp;Firewall=$fw} | Export-Clixml -LiteralPath $bak -ErrorAction Stop
            Write-Output ('初始状态备份完成：'+$bak)
        } catch { Write-Output ('无法保存原始配置，已停止修改：'+$_.Exception.Message); return }
    } else { Write-Output '已有原始状态备份：不会覆盖。' }

    if($hasMp) {
        try { Set-MpPreference -DisableRealtimeMonitoring $true -ErrorAction Stop; Write-Output 'Defender 实时保护：已提交关闭请求' }
        catch { Write-Output ('Defender 实时保护：请求失败：'+$_.Exception.Message) }
        try { Set-MpPreference -MAPSReporting Disabled -ErrorAction Stop; Write-Output '云报告：已提交关闭请求' }
        catch { Write-Output ('云报告：请求失败：'+$_.Exception.Message) }
        try { Set-MpPreference -SubmitSamplesConsent NeverSend -ErrorAction Stop; Write-Output '样本提交：已提交关闭请求' }
        catch { Write-Output ('样本提交：请求失败：'+$_.Exception.Message) }
    } else { Write-Output 'Defender 功能不完整或不可管理：安全跳过，不尝试绕过保护。' }
    if($hasFw) {
        try { Set-NetFirewallProfile -Profile Domain,Private,Public -Enabled False -ErrorAction Stop; Write-Output 'Windows 防火墙：已提交关闭请求' }
        catch { Write-Output ('防火墙：请求失败：'+$_.Exception.Message) }
    }
} else {
    if(-not(Test-Path -LiteralPath $bak)) {
        Write-Output '未找到本工具保存的原始备份；为避免覆盖用户原配置，未进行猜测式恢复。'
        Write-Output '请使用 Windows 安全中心/防火墙设置恢复，或先确认原始状态。'
        return
    }
    try { $original=Import-Clixml -LiteralPath $bak -ErrorAction Stop }
    catch { Write-Output ('备份损坏或不可读，已停止：'+$_.Exception.Message);return }
    if($null -ne $original.Mp) {
        if($hasMp) {
            try { Set-MpPreference -DisableRealtimeMonitoring ([bool]$original.Mp.DisableRealtimeMonitoring) -ErrorAction Stop; Write-Output 'Defender 实时保护：已按备份请求恢复' }
            catch { Write-Output ('Defender 恢复失败：'+$_.Exception.Message) }
            try { Set-MpPreference -MAPSReporting $original.Mp.MAPSReporting -ErrorAction Stop; Write-Output '云报告：已按备份请求恢复' }
            catch { Write-Output ('云报告恢复失败：'+$_.Exception.Message) }
            try { Set-MpPreference -SubmitSamplesConsent $original.Mp.SubmitSamplesConsent -ErrorAction Stop; Write-Output '样本提交：已按备份请求恢复' }
            catch { Write-Output ('样本提交恢复失败：'+$_.Exception.Message) }
        } else { Write-Output 'Defender 命令不可用，未能恢复该部分；请在系统界面检查。' }
    }
    if($null -ne $original.Firewall -and @($original.Firewall).Count -gt 0) {
        if($hasFw) {
            foreach($f in @($original.Firewall)) {
                try { Set-NetFirewallProfile -Profile $f.Name -Enabled ([bool]$f.Enabled) -ErrorAction Stop; Write-Output ($f.Name + ' 防火墙：已按备份请求恢复') }
                catch { Write-Output ($f.Name + ' 防火墙恢复失败：'+$_.Exception.Message) }
            }
        } else { Write-Output '防火墙管理命令不可用，未能恢复该部分；请在系统界面检查。' }
    }
    Write-Output ('备份继续保留：'+$bak)
}
Write-Output '说明：命令提交成功不等于保护状态已变化。下一段会重新读取实际状态。'
";
            script = script.Replace("__BACKUP_PATH__", escapedBackup)
                           .Replace("__ACTION__", disable ? "disable" : "restore");
            string actionResult = PowerShellService.RunScript(script, 3);
            string current = Inspect();
            return "【" + (disable ? "关闭保护" : "恢复保护") + "：操作与反馈】\r\n" + actionResult +
                "\r\n【执行后的状态检查】\r\n" + current +
                "\r\n注意：防护真实状态以复查为准，‘不可用’不是‘已关闭’。未移除安全中心、Defender 服务或组件。";
        }
    }
}
