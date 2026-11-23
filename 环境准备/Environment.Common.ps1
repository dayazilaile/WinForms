# Compatible with Windows PowerShell 2.0. Loading this file performs no installation.
function Get-PreparationPlatform {
    param([version]$Version, [int]$ServicePack, [int]$ProductType, [string]$Architecture)
    $supported = $false; $wmf = ''; $reason = ''
    $server = $ProductType -eq 2 -or $ProductType -eq 3
    if ($ProductType -ne 1 -and -not $server) {
        $reason = '无法识别系统类型，停止选择安装包。'
    } elseif ($Architecture -ne 'x86' -and $Architecture -ne 'x64') {
        $reason = '此脚本仅处理 x86/x64；ARM 等平台请使用系统自带组件或微软对应安装器。'
    } elseif ($server -and $Architecture -ne 'x64') {
        $reason = '此脚本不处理 32 位 Server。'
    } elseif ($Version.Major -eq 6 -and $Version.Minor -eq 1 -and $ServicePack -ge 1) {
        $supported = $true; $wmf = 'Win7AndW2K8R2-KB3191566-' + $Architecture + '.msu'
    } elseif ($Version.Major -eq 6 -and $Version.Minor -eq 2 -and $server) {
        $supported = $true; $wmf = 'W2K12-KB3191565-x64.msu'
    } elseif ($Version.Major -eq 6 -and $Version.Minor -eq 3) {
        $supported = $true; $wmf = 'Win8.1AndW2K12R2-KB3191564-' + $Architecture + '.msu'
    } elseif ($Version.Major -eq 10 -and $Version.Build -ge 14393) {
        $supported = $true
    } elseif ($Version.Major -eq 10) {
        $reason = '早期 Win10 不支持本助手推荐的 .NET 4.8，请先更新到 1607 或更高版本。'
    } else {
        $reason = '需要 Win7 SP1、Win8.1、Win10/11 或对应的受支持 Server；Win7/2008 R2 未装 SP1 时先补 SP1。'
    }
    New-Object PSObject -Property @{ Supported=$supported; WmfPackage=$wmf; Reason=$reason }
}

function Get-PreparationPlan {
    param($Platform, [int]$NetRelease, [version]$PowerShellVersion, [bool]$PendingRestart)
    $needNet = $NetRelease -lt 528040
    $needWmf = $PowerShellVersion -lt [version]'5.1'
    $reason = $Platform.Reason
    if ($PendingRestart) { $reason = '检测到系统有待完成的重启；请先自行重启，再运行本脚本。' }
    New-Object PSObject -Property @{
        NeedNet=$needNet; NeedWmf=$needWmf
        CanInstall=($Platform.Supported -and -not $PendingRestart)
        WmfManual=($needWmf -and -not $Platform.WmfPackage)
        Reason=$reason
    }
}

function Get-PreparationNetRelease {
    $highest = 0
    $reg = Join-Path $env:WINDIR 'System32\reg.exe'
    foreach ($view in @('/reg:64', '/reg:32', '')) {
        $registryArgs = @('query', 'HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', '/v', 'Release')
        if ($view) { $registryArgs += $view }
        $savedPreference = $ErrorActionPreference
        try { $ErrorActionPreference = 'Continue'; $rows = @(& $reg @registryArgs 2>$null) }
        finally { $ErrorActionPreference = $savedPreference }
        foreach ($row in $rows) {
            if ($row -match '^\s*Release\s+REG_DWORD\s+(0x[0-9a-fA-F]+|[0-9]+)\s*$') {
                $value = $matches[1]
                if ($value.StartsWith('0x')) { $number = [Convert]::ToInt32($value.Substring(2), 16) }
                else { $number = [Convert]::ToInt32($value, 10) }
                if ($number -gt $highest) { $highest = $number }
            }
        }
    }
    return $highest
}

function Test-PreparationPendingRestart {
    foreach ($key in @(
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending',
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired'
    )) {
        if (Test-Path -LiteralPath $key -ErrorAction Stop) { return $true }
    }
    $session = Get-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager' -ErrorAction Stop
    return @($session.PendingFileRenameOperations | Where-Object { $_ }).Count -gt 0
}

function Test-PreparationAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    try {
        $principal = New-Object Security.Principal.WindowsPrincipal($identity)
        return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    } finally { $identity.Dispose() }
}

function Get-PreparationInstaller {
    param([string]$Directory, [string]$Name)
    if ($Name -ne [IO.Path]::GetFileName($Name)) { throw '安装包名不能含有目录。' }
    $path = Join-Path $Directory $Name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw ('缺少安装包：' + $path + '。请按 README 从微软官网下载。') }
    if ([IO.Path]::GetExtension($Name) -eq '.exe') {
        # FilePath is supported by PS 2.0; escape wildcard characters in the folder name.
        $signature = Get-AuthenticodeSignature -FilePath ([Management.Automation.WildcardPattern]::Escape($path)) -ErrorAction Stop
        if ($signature.Status -ne 'Valid' -or -not $signature.SignerCertificate -or
            $signature.SignerCertificate.Subject -notmatch '(^|,\s*)O=Microsoft Corporation(,|$)') {
            throw '安装包未通过微软有效签名验证。停止安装，请核对官方来源、系统时间、根证书及 SHA-2 更新。'
        }
    } elseif ([IO.Path]::GetExtension($Name) -ne '.msu') { throw '只支持指定的 EXE/MSU 安装包。' }
    # MSU authentication and applicability are enforced by the Windows Update Standalone Installer.
    return [IO.Path]::GetFullPath($path)
}

function Get-PreparationWusa {
    $native = Join-Path $env:WINDIR 'Sysnative\wusa.exe'
    if (Test-Path -LiteralPath $native -PathType Leaf) { return $native }
    return (Join-Path $env:WINDIR 'System32\wusa.exe')
}

function ConvertTo-PreparationCell {
    param($Value)
    return ([string]$Value -replace '[\t\r\n]', ' ')
}

function Write-PreparationLog {
    param([string]$Path, $Computer, [string]$Operation, [string]$Result)
    $row = @((Get-Date).ToString('yyyy-MM-ddTHH:mm:ss.fffzzz'), $Computer.HostName,
        $Computer.Mac, $Computer.IP, $Computer.OS, $Computer.User, $Operation, $Result)
    $cells = @(); foreach ($value in $row) { $cells += (ConvertTo-PreparationCell $value) }
    $encoding = New-Object Text.UTF8Encoding($true)
    if (-not (Test-Path -LiteralPath $Path)) {
        [IO.File]::WriteAllText($Path, "时间`t主机名`tMAC`tIP`t系统`t账户`t操作`t结果`r`n", $encoding)
    }
    [IO.File]::AppendAllText($Path, ([string]::Join("`t", [string[]]$cells) + "`r`n"), $encoding)
}

function Invoke-PreparationInstaller {
    param([string]$Path, [string]$LogPath, $Computer)
    Write-PreparationLog $LogPath $Computer ('安装 ' + [IO.Path]::GetFileName($Path)) '开始'
    if ([IO.Path]::GetExtension($Path) -eq '.msu') {
        $executable = Get-PreparationWusa
        $arguments = '"' + $Path + '" /quiet /norestart'
    } else {
        $executable = $Path
        $detail = Join-Path (Split-Path $LogPath -Parent) ('dotnet-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.html')
        $arguments = '/passive /norestart /log "' + $detail + '"'
    }
    # No hidden window, execution-policy change, self-elevation or forced restart.
    try {
        $process = Start-Process -FilePath $executable -ArgumentList $arguments -Wait -PassThru -ErrorAction Stop
        try { $code = $process.ExitCode } finally { $process.Dispose() }
    } catch {
        Write-PreparationLog $LogPath $Computer ('安装 ' + [IO.Path]::GetFileName($Path)) ('启动失败：' + $_.Exception.Message)
        throw
    }
    Write-PreparationLog $LogPath $Computer ('安装 ' + [IO.Path]::GetFileName($Path)) ('退出码=' + $code)
    if ($code -eq 0 -or $code -eq 2359302) { return 0 }
    if ($code -eq 3010 -or $code -eq 1641) { return 3010 }
    throw ('安装未成功，退出码=' + $code + '。请核对适用系统、SP1、SHA-2/服务堆栈更新与系统更新服务，不会继续安装其他组件。')
}
