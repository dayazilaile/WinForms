namespace ITDesktopToolkit
{
    public static partial class AutomationService
    {
        private const string NetworkAuditHelper=@"$script:itNetworkAuditSession=[guid]::NewGuid().ToString('N')
$script:itNetworkLogId=''
$script:itNetworkLogOperation=''
$script:itNetworkClock=$null
function Get-ITNetworkIdentity {
 $uuid='';$serial='';$macs=@();$facts=@()
 if($null -eq $script:itAuditHardware){$script:itAuditHardware=@('','');try{$script:itAuditHardware[0]=[string](Get-CimInstance Win32_ComputerSystemProduct -ErrorAction Stop).UUID}catch{};try{$script:itAuditHardware[1]=[string](Get-CimInstance Win32_BIOS -ErrorAction Stop).SerialNumber}catch{}}
 $uuid=$script:itAuditHardware[0];$serial=$script:itAuditHardware[1]
 $uuid=$uuid.Trim().ToUpperInvariant();$guid=[guid]::Empty
 if(-not [guid]::TryParse($uuid,[ref]$guid) -or $guid -eq [guid]::Empty -or $uuid -eq 'FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF'){$uuid=''}else{$uuid=$guid.ToString('D').ToUpperInvariant()}
 $serial=$serial.Trim();if($serial -match '^(?i:default string|system serial number|to be filled by o.e.m.|unknown|none|not specified|0|n/a)$'){$serial=''}
 try{foreach($n in [Net.NetworkInformation.NetworkInterface]::GetAllNetworkInterfaces()){
  if($n.NetworkInterfaceType -in @('Loopback','Tunnel')){continue}
  $bytes=$n.GetPhysicalAddress().GetAddressBytes();if($bytes.Length -ne 6){continue};$mac=[BitConverter]::ToString($bytes).Replace('-',':');if($mac -in @('00:00:00:00:00:00','FF:FF:FF:FF:FF:FF')){continue};$macs+=,$mac
  $ip='未读取';$dns='未读取';$gateway='未读取';$dhcp='未读取';try{$p=$n.GetIPProperties();$ip=(@($p.UnicastAddresses|ForEach-Object {$value=$_.Address.ToString();if($_.Address.AddressFamily -eq 'InterNetwork' -and $null -ne $_.IPv4Mask){$value+='/'+$_.IPv4Mask.ToString()};$value}) -join ',');$dns=(@($p.DnsAddresses|ForEach-Object {$_.ToString()}) -join ',');$gateway=(@($p.GatewayAddresses|ForEach-Object {$_.Address.ToString()}) -join ',');try{$v4=$p.GetIPv4Properties();if($null -ne $v4){$dhcp=if($v4.IsDhcpEnabled){'自动'}else{'静态'}}}catch{}}catch{}
  $facts+=('网卡='+$n.Name+';ID='+$n.Id+';MAC='+$mac+';类型='+$n.NetworkInterfaceType+';状态='+$n.OperationalStatus+';IP='+$ip+';DHCP='+$dhcp+';网关='+$gateway+';DNS='+$dns)
 }}catch{}
 $device='SESSION-'+$script:itNetworkAuditSession;$source='仅本次会话，硬件标识未读取';$basis=''
 if($uuid){$source='硬件UUID';$basis='uuid:'+$uuid}elseif($serial){$source='BIOS序列号（需结合MAC核对）';$basis='serial:'+$serial}elseif($macs.Count){$source='MAC集合（更换网卡后可能变化）';$basis='mac:'+(@($macs|Sort-Object -Unique) -join ';')}
 if($basis){$sha=[Security.Cryptography.SHA256]::Create();try{$device='DEV-'+[BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($basis))).Replace('-','').Substring(0,24)}finally{$sha.Dispose()}}
 $permission='未读取';try{$identity=[Security.Principal.WindowsIdentity]::GetCurrent();$principal=New-Object Security.Principal.WindowsPrincipal($identity);$permission=if($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){'管理员'}else{'普通用户'}}catch{}
 return [pscustomobject]@{HostName=[Environment]::MachineName;Device=$device;Source=$source;Uuid=$(if($uuid){$uuid}else{'未读取'});Serial=$(if($serial){$serial}else{'未读取'});Facts=$(if($facts.Count){$facts -join ' | '}else{'未读取到有效网卡MAC/IP'});Account=([Environment]::UserDomainName+'\'+[Environment]::UserName);Permission=$permission;System=([Environment]::OSVersion.VersionString+' / '+$(if([Environment]::Is64BitOperatingSystem){'64位'}else{'32位'}))}
}
function Convert-ITAuditCell([object]$value) {
 $s=[string]$value;$s=[regex]::Replace($s,'(?i)(password|passwd|pwd|密码)\s*[:=]\s*[^;\r\n]*','$1=[已省略]');if($s.Length -gt 16000){$s=$s.Substring(0,16000)+'[截断]'}
 if($s.Length -gt 0 -and '=+-@'.IndexOf($s[0]) -ge 0){$s=""'""+$s}
 return $s.Replace('\','\\').Replace(""`t"",'\t').Replace(""`r"",'\r').Replace(""`n"",'\n')
}
function Write-ITNetworkAudit([string]$phase,[string]$result,[string]$detail) {
 try {
  $root=[IO.Path]::GetDirectoryName($env:IT_TOOLKIT_BAT);if([string]::IsNullOrEmpty($root)){throw '无法确定 BAT 所在目录'}
  $folder=Join-Path $root 'Logs';[void][IO.Directory]::CreateDirectory($folder);$file=Join-Path $folder 'network-operations-v1.tsv'
  $id=Get-ITNetworkIdentity;$elapsed=if($null -ne $script:itNetworkClock){[long]$script:itNetworkClock.ElapsedMilliseconds}else{0}
  $cells=@([DateTimeOffset]::Now.ToString('yyyy-MM-ddTHH:mm:ss.fffzzz'),$id.HostName,$id.Device,$id.Source,$id.Uuid,$id.Serial,$id.Facts,$id.Account,$id.Permission,$id.System,'0.5.22',$script:itNetworkAuditSession,$script:itNetworkLogId,'',$script:itNetworkLogOperation,$phase,$result,$elapsed,$detail)
  $header='时间','主机名','设备标识','标识依据','硬件UUID','BIOS序列号','网卡_MAC_IP','运行账户','权限','系统与架构','软件版本','会话ID','操作ID','上级操作ID','操作','阶段','结果','耗时毫秒','详情'
  $stream=New-Object IO.FileStream($file,[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::Write,[IO.FileShare]::Read)
  try{$empty=$stream.Length -eq 0;[void]$stream.Seek(0,[IO.SeekOrigin]::End);$writer=New-Object IO.StreamWriter($stream,(New-Object Text.UTF8Encoding($true)));try{if($empty){$writer.WriteLine(($header -join ""`t""))};$writer.WriteLine((@($cells|ForEach-Object {Convert-ITAuditCell $_}) -join ""`t""))}finally{$writer.Dispose()}}finally{$stream.Dispose()}
 } catch {$script:itAbortWorkflow=$true;throw ('便携网络日志写入失败，已停止后续动作：'+$_.Exception.Message)}
}
function Write-ITNetworkEvent([string]$detail) {Write-ITNetworkAudit '过程' '执行中' $detail}
";
    }
}
