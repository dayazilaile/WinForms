using System;
using System.Collections.Generic;
using System.Net;
namespace WinForms
{
    public sealed class RandomIpv4Options
    {
        public string Nic,Network,Base,Gateway;public int Prefix;public bool Finish;public int[] Hosts;
    }
    public static partial class AutomationService
    {
        private static uint IpNumber(IPAddress ip){byte[] b=ip.GetAddressBytes();return ((uint)b[0]<<24)|((uint)b[1]<<16)|((uint)b[2]<<8)|b[3];}
        private static IPAddress UnicastIp(string value)
        {
            IPAddress ip=IPAddress.Parse(CheckIp(value));byte first=ip.GetAddressBytes()[0];if(first==0||first==127||first>=224)throw new ArgumentException("需要可配置的单播 IPv4 地址。");return ip;
        }
        public static RandomIpv4Options RandomIpv4(string nic,string network,string details)
        {
            string name=CheckNic(nic);foreach(char c in name)if(Char.IsControl(c))throw new ArgumentException("网卡名不能含控制字符。");
            string[] fields=(network??"").Trim().Split('/');int prefix=24;
            if(fields.Length>2||(fields.Length==2&&(!Int32.TryParse(fields[1],System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out prefix)||prefix<8||prefix>30)))throw new ArgumentException("请输入前三段或 IPv4/CIDR，例如 192.168.1.0/24，掩码位数支持 8–30。");
            string raw=fields[0];if(raw.Split('.').Length==3)raw+=".0";IPAddress baseIp=UnicastIp(raw);byte[] parts=baseIp.GetAddressBytes();uint mask=UInt32.MaxValue<<(32-prefix),subnet=IpNumber(baseIp)&mask,broadcast=subnet|~mask;
            string[] last=(details??"").Split('|');if(last.Length<1||last.Length>2)throw new ArgumentException("网关/完成方式格式无效。");IPAddress gateway=UnicastIp(last[0]);uint gw=IpNumber(gateway);if((gw&mask)!=subnet||gw==subnet||gw==broadcast)throw new ArgumentException("网关必须是所填网段内的有效主机地址。");
            string mode=last.Length==1?"继续流程":last[1];if(mode!="继续流程"&&mode!="结束脚本")throw new ArgumentException("完成方式只能是继续流程或结束脚本。");
            string first=parts[0]+"."+parts[1]+"."+parts[2];var hosts=new List<int>();for(int i=1;i<=253;i++){if(i==100)continue;uint candidate=(IpNumber(baseIp)&0xffffff00u)|(uint)i;if((candidate&mask)==subnet&&candidate!=subnet&&candidate!=broadcast&&candidate!=gw)hosts.Add(i);}
            if(hosts.Count==0)throw new ArgumentException("该网段与网关下没有符合末段范围的候选地址。");
            return new RandomIpv4Options{Nic=name,Network=first+"."+(parts[3]&(byte)mask)+"/"+prefix,Base=first,Gateway=gateway.ToString(),Prefix=prefix,Finish=mode=="结束脚本",Hosts=hosts.ToArray()};
        }
        private const string RandomIpv4Helper=@"function Resolve-RandomIpNic([string]$name) {
 if($name -in @('自动有线','自动无线')) {
  $list=@(Get-NetAdapter -Physical -ErrorAction Stop | Where-Object { $_.Status -eq 'Up' -and $(if($name -eq '自动无线'){($_.NdisPhysicalMedium -eq 9) -or ($_.InterfaceDescription -match 'Wireless|Wi-Fi|WLAN|802\.11')}else{-not (($_.NdisPhysicalMedium -eq 9) -or ($_.InterfaceDescription -match 'Wireless|Wi-Fi|WLAN|802\.11'))}) })
 } else {$list=@(Get-NetAdapter -ErrorAction Stop | Where-Object {[string]::Equals($_.Name,$name,[StringComparison]::OrdinalIgnoreCase)})}
 if($list.Count -ne 1 -or $list[0].Status -ne 'Up'){throw '必须唯一选中一块已连接网卡；请检查网线/无线连接并填写准确网卡名。'}
 return $list[0]
}
function RandomIp-Mask([int]$prefix) {
 $bits=('1'*$prefix).PadRight(32,'0');return (@(0..3|ForEach-Object {[Convert]::ToInt32($bits.Substring($_*8,8),2)}) -join '.')
}
function Probe-RandomIp([string]$ip,[string]$source) {
 if([string]::IsNullOrEmpty($source)){return ''}
 if(-not ('ITRandomIPv4Arp' -as [type])) {
  Add-Type -TypeDefinition @'
using System;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
public static class ITRandomIPv4Arp {
 [DllImport(""iphlpapi.dll"", ExactSpelling=true)] private static extern uint SendARP(uint dst,uint src,[Out] byte[] mac,ref uint len);
 public static string Probe(string ip,string source) {
  var work=Task.Factory.StartNew<string>(delegate {
   byte[] bytes=new byte[6];uint count=6;
   uint code=SendARP(BitConverter.ToUInt32(IPAddress.Parse(ip).GetAddressBytes(),0),BitConverter.ToUInt32(IPAddress.Parse(source).GetAddressBytes(),0),bytes,ref count);
   if(code!=0||count!=6)return """";
   string value=BitConverter.ToString(bytes).Replace(""-"","""");
   return value==""000000000000""||value==""FFFFFFFFFFFF""?"""":value;
  });
  if(!work.Wait(800))return """";
  return work.Result;
 }
}
'@
 }
 return [ITRandomIPv4Arp]::Probe($ip,$source)
}
function Set-RandomIpAddress([string]$nic,[string]$address,[int]$prefix,[string]$gateway) {
 $ipv4Mask=RandomIp-Mask $prefix
 & netsh.exe interface ipv4 set address name=$nic source=static address=$address mask=$ipv4Mask gateway=$gateway store=persistent | Out-Host
 if($LASTEXITCODE -ne 0){throw ('设置 IPv4 失败，netsh 返回 '+$LASTEXITCODE)}
}
function Restore-RandomIp($saved,[string]$candidate,[string]$gateway) {
 if($saved.Dhcp -eq 'Enabled') {
  & netsh.exe interface ipv4 set address ""name=$($saved.Name)"" source=dhcp store=persistent | Out-Host
  if($LASTEXITCODE -ne 0){throw '恢复原 DHCP 模式失败'}
 } elseif($saved.Addresses.Count -eq 1) {
  $old=$saved.Addresses[0];$oldGateway=if($saved.Routes.Count -eq 1){$saved.Routes[0].NextHop}else{'none'}
  Set-RandomIpAddress $saved.Name $old.IPAddress $old.PrefixLength $oldGateway
 } else {
  $rows=@(Get-NetIPAddress -AddressFamily IPv4 -ErrorAction Stop | Where-Object {$_.InterfaceIndex -eq $saved.Index -and $_.IPAddress -eq $candidate})
  foreach($row in $rows){Remove-NetIPAddress -InterfaceIndex $saved.Index -IPAddress $candidate -Confirm:$false -ErrorAction Stop}
  $routes=@(Get-NetRoute -AddressFamily IPv4 -ErrorAction Stop | Where-Object {$_.InterfaceIndex -eq $saved.Index -and $_.DestinationPrefix -eq '0.0.0.0/0' -and $_.NextHop -eq $gateway})
  foreach($route in $routes){Remove-NetRoute -InputObject $route -Confirm:$false -ErrorAction Stop}
 }
}
function Watch-RandomIp([int]$index,[string]$address) {
 $clock=[Diagnostics.Stopwatch]::StartNew()
 do {
  $adapter=@(Get-NetAdapter -ErrorAction Stop | Where-Object {$_.ifIndex -eq $index})
  if($adapter.Count -ne 1 -or $adapter[0].Status -ne 'Up'){throw '冲突观察期间网卡断开，无法确认地址可用'}
  $rows=@(Get-NetIPAddress -AddressFamily IPv4 -ErrorAction Stop | Where-Object {$_.InterfaceIndex -eq $index -and $_.IPAddress -eq $address})
  if(@($rows|Where-Object {$_.AddressState -eq 'Duplicate'}).Count -gt 0){return $false}
  Start-Sleep -Milliseconds 250
 } while($clock.Elapsed.TotalSeconds -lt 10)
 $rows=@(Get-NetIPAddress -AddressFamily IPv4 -ErrorAction Stop | Where-Object {$_.InterfaceIndex -eq $index -and $_.IPAddress -eq $address})
 return $rows.Count -eq 1 -and $rows[0].AddressState -eq 'Preferred'
}
function Invoke-RandomIPv4([string]$name,[string]$base,[int]$prefix,[string]$gateway,[int[]]$hosts) {
 $changed=$false;$success=$false;$saved=$null;$candidate=''
 try {
  $nic=Resolve-RandomIpNic $name;$index=[int]$nic.ifIndex
  $mac=([string]$nic.MacAddress -replace '[:-]','').ToUpperInvariant()
  if($mac -notmatch '^[0-9A-F]{12}$' -or $mac -in @('000000000000','FFFFFFFFFFFF')){throw '无法读取所选网卡有效 MAC，未修改地址'}
  $interfaces=@(Get-NetIPInterface -InterfaceIndex $index -AddressFamily IPv4 -ErrorAction Stop)
  if($interfaces.Count -ne 1 -or $interfaces[0].Dhcp -notin @('Enabled','Disabled') -or $interfaces[0].DadTransmits -lt 1){throw '无法确认网卡 DHCP/DAD 状态或 IPv4 冲突检测已关闭，未修改地址'}
  $initial=@(Get-NetIPAddress -AddressFamily IPv4 -ErrorAction Stop | Where-Object {$_.InterfaceIndex -eq $index})
  $routes=@(Get-NetRoute -AddressFamily IPv4 -ErrorAction Stop | Where-Object {$_.InterfaceIndex -eq $index -and $_.DestinationPrefix -eq '0.0.0.0/0'})
  if($initial.Count -gt 1 -or $routes.Count -gt 1){throw '本动作只修改单 IPv4/至多一条默认路由的网卡，未改动多地址或复杂路由配置'}
  $saved=[pscustomobject]@{Name=$nic.Name;Index=$index;Dhcp=[string]$interfaces[0].Dhcp;Addresses=$initial;Routes=$routes}
  $others=@(Get-NetIPAddress -AddressFamily IPv4 -ErrorAction Stop | Where-Object {$_.InterfaceIndex -ne $index} | ForEach-Object {$_.IPAddress})
  $pool=New-Object 'Collections.Generic.List[int]'
  foreach($hostNumber in $hosts){$ip=$base+'.'+$hostNumber;if($ip -ne $gateway -and $ip -notin $others){$pool.Add($hostNumber)}}
  while($pool.Count -gt 0) {
   $pick=Get-Random -Minimum 0 -Maximum $pool.Count;$candidate=$base+'.'+$pool[$pick];$pool.RemoveAt($pick)
   $current=@(Get-NetIPAddress -AddressFamily IPv4 -ErrorAction Stop | Where-Object {$_.InterfaceIndex -eq $index -and $_.AddressState -eq 'Preferred'} | Select-Object -First 1)
   $source=if($current.Count -gt 0){[string]$current[0].IPAddress}else{''}
   $peer=Probe-RandomIp $candidate $source
   $neighbors=@(Get-NetNeighbor -ErrorAction Stop | Where-Object {$_.InterfaceIndex -eq $index -and $_.IPAddress -eq $candidate -and $_.State -notin @('Incomplete','Unreachable')})
   if(($peer.Length -gt 0 -and $peer -ne $mac) -or @($neighbors | Where-Object {($_.LinkLayerAddress -replace '[:-]','').ToUpperInvariant() -match '^[0-9A-F]{12}$' -and ($_.LinkLayerAddress -replace '[:-]','').ToUpperInvariant() -notin @($mac,'000000000000','FFFFFFFFFFFF')}).Count -gt 0){Write-Host ('地址已有 ARP/邻居证据，换一个：'+$candidate);if(Get-Command Write-ITNetworkEvent -ErrorAction SilentlyContinue){Write-ITNetworkEvent ('ARP占用，跳过 '+$candidate)};continue}
   Write-Host ('尝试 '+$candidate+'/'+$prefix+'，网关 '+$gateway+'；观察冲突 10 秒。')
   if(Get-Command Write-ITNetworkEvent -ErrorAction SilentlyContinue){Write-ITNetworkEvent ('尝试地址='+$candidate+';前缀='+$prefix+';网关='+$gateway)}
   $changed=$true;Set-RandomIpAddress $nic.Name $candidate $prefix $gateway
   if(Watch-RandomIp $index $candidate) {
    $configured=@(Get-NetIPInterface -InterfaceIndex $index -AddressFamily IPv4 -ErrorAction Stop)
    $actualRoutes=@(Get-NetRoute -AddressFamily IPv4 -ErrorAction Stop | Where-Object {$_.InterfaceIndex -eq $index -and $_.DestinationPrefix -eq '0.0.0.0/0'} | Where-Object {$_.NextHop -eq $gateway})
    $actualIp=@(Get-NetIPAddress -AddressFamily IPv4 -ErrorAction Stop | Where-Object {$_.InterfaceIndex -eq $index -and $_.IPAddress -eq $candidate})
    if($configured.Count -ne 1 -or $configured[0].Dhcp -ne 'Disabled' -or $actualRoutes.Count -ne 1 -or $actualIp.Count -ne 1 -or $actualIp[0].PrefixLength -ne $prefix){throw '地址观察通过，但静态模式或默认网关未确认生效'}
    $success=$true;Write-Host ('已设置 '+$candidate+'；10 秒内未检测到冲突（不保证关机/未响应设备无重复地址）。');return $candidate
   }
   Write-Host ('检测到重复或地址未成为 Preferred，重新抽取：'+$candidate)
   if(Get-Command Write-ITNetworkEvent -ErrorAction SilentlyContinue){Write-ITNetworkEvent ('重复/未就绪，换号 '+$candidate)}
   Start-Sleep -Milliseconds (Get-Random -Minimum 250 -Maximum 1251)
  }
  throw '允许地址池已尝试完或均被本机其他网卡占用，未找到通过检测的地址。'
 } catch {
  $original=$_.Exception.Message;$script:itAbortWorkflow=$true
  if($changed -and -not $success){try{Restore-RandomIp $saved $candidate $gateway;Write-Host '已请求恢复原 IPv4 模式/地址，DNS 未改动。'}catch{throw ($original+'；恢复原配置也失败：'+$_.Exception.Message)};if(Get-Command Write-ITNetworkEvent -ErrorAction SilentlyContinue){try{Write-ITNetworkEvent '已请求恢复原IPv4配置，结果需核对'}catch{Write-Warning $_.Exception.Message}}}
  throw $original
 }
}
";
    }
}
