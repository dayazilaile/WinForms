# Windows PowerShell 2.0 compatible bootstrap; no automatic policy changes.
param([switch]$Install)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
try {
    . (Join-Path $root 'Environment.Common.ps1')
    $os = Get-WmiObject -Class Win32_OperatingSystem -ErrorAction Stop
    $architecture = $env:PROCESSOR_ARCHITECTURE
    if ($env:PROCESSOR_ARCHITEW6432) { $architecture = $env:PROCESSOR_ARCHITEW6432 }
    if ($architecture -eq 'AMD64') { $architecture = 'x64' }
    $platform = Get-PreparationPlatform ([version]$os.Version) ([int]$os.ServicePackMajorVersion) ([int]$os.ProductType) $architecture
    $net = Get-PreparationNetRelease
    $ps = $PSVersionTable.PSVersion
    $pending = Test-PreparationPendingRestart
    $plan = Get-PreparationPlan $platform $net $ps $pending
    Write-Host '=== WinForms 环境准备 ==='
    Write-Host ('系统：' + $os.Caption + ' ' + $os.Version + ' SP' + $os.ServicePackMajorVersion + ' / ' + $architecture)
    Write-Host ('.NET Release：' + $net + '；主程序最低 378389（4.5），建议 528040（4.8）')
    if ($net -eq 0) { Write-Host '未读取到 Release；可能未安装，也可能注册表读取受限，不能据此断言版本。' }
    Write-Host ('Windows PowerShell：' + $ps + '；生成的自动化 BAT 需要 5.1')
    Write-Host ('待重启：' + $pending)
    if ($plan.Reason) { Write-Host $plan.Reason }
    if ($plan.WmfManual) { Write-Host '此系统不适用旧版 WMF 安装包。请通过系统更新/组件修复补齐 Windows PowerShell 5.1，不能用 PowerShell 7 替代。' }
    if (-not $Install) {
        Write-Host '仅检测；未安装、未改设置、未保存日志。安装方法见本文件夹 README.md。'
        if (-not $platform.Supported -or $net -lt 378389 -or $plan.NeedWmf) { exit 5 }
        exit 0
    }
    if (-not $plan.CanInstall) { throw ('暂不能安装：' + $plan.Reason) }
    if (-not (Test-PreparationAdministrator)) { throw '请关闭此窗口，右键“02_安装缺少组件.bat”，选择“以管理员身份运行”。' }
    if (-not $plan.NeedNet -and -not $plan.NeedWmf) { Write-Host '已具备建议的 .NET 与 PowerShell 环境，无需安装。'; exit 0 }
    $name = ''; $label = ''
    if ($plan.NeedNet) { $name = 'ndp48-x86-x64-allos-enu.exe'; $label = '.NET Framework 4.8（已装 4.5+ 时属于推荐升级）' }
    elseif (-not $plan.WmfManual) { $name = $platform.WmfPackage; $label = 'Windows Management Framework 5.1' }
    else { throw '请按上述提示修复系统组件，本脚本不会给此系统安装不匹配的 WMF 包。' }
    $package = Get-PreparationInstaller (Join-Path $root '安装包') $name
    Write-Host ('本次将安装：' + $label)
    Write-Host ('安装包：' + $package)
    Write-Host 'WMF 可能影响旧管理软件，尤其是 Server 上的管理服务；请先核对微软兼容说明。'
    Write-Host '每次只安装一个组件。不自动重启、不修改安全策略。安装会写入系统；微软安装器可能产生自己的系统缓存与日志。'
    $answer = Read-Host '确认安装请输入 Y，其余输入取消'
    if ($answer -ne 'Y') { Write-Host '已取消。'; exit 0 }
    $computer = New-Object PSObject -Property @{
        HostName=[Environment]::MachineName; Mac='未读取'; IP='未读取'
        OS=($os.Caption + ' ' + $os.Version); User=([Environment]::UserDomainName + '\' + [Environment]::UserName)
    }
    try {
        $adapters = @(Get-WmiObject -Class Win32_NetworkAdapterConfiguration -Filter 'IPEnabled=True' -ErrorAction Stop)
        $macs = @(); $ips = @()
        foreach ($adapter in $adapters) { if ($adapter.MACAddress) { $macs += $adapter.MACAddress }; $ips += @($adapter.IPAddress) }
        $computer.Mac = [string]::Join('; ', [string[]]$macs); $computer.IP = [string]::Join('; ', [string[]]$ips)
    } catch { Write-Host '未能读取网卡身份，日志仍会记录主机名及账户。' }
    $logDir = Join-Path $root 'Logs'
    [void][IO.Directory]::CreateDirectory($logDir)
    # A failed portable log write aborts BEFORE starting an installer. No fallback to the PC's profile.
    $code = Invoke-PreparationInstaller $package (Join-Path $logDir 'environment-install.tsv') $computer
    if ($code -eq 3010) { Write-Host '安装器要求重启。请自行重启，再重新检测/安装下一组件。'; exit 3010 }
    Write-Host '安装器已结束，请重新运行检测；如仍提示缺少组件，请自行重启后再检查。安装 WMF 后必须新开 PowerShell 会话。'
    exit 0
} catch {
    Write-Host ('[错误] ' + $_.Exception.Message) -ForegroundColor Red
    exit 1
}
