using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using ITDesktopToolkit;
class DesktopTests
{
    static int passed;
    static void Check(bool ok,string name){if(!ok)throw new Exception(name);passed++;Console.WriteLine("PASS "+name);}
    static string Output(IDictionary<string,string> fields){return "command\r\n返回代码：0\r\n"+String.Join("\n",InspectionService.Serialize(fields).Split('\n').Skip(1).Where(x=>x.Trim().Length>0).Select(x=>"ITCFG:"+x));}
    static void UsbCapabilities()
    {
        var ntfs=new UsbDriveAccess{Root=@"G:\",FileSystem="ntfs"};Check(ntfs.SupportsPermissions&&ntfs.ToString().Contains("ntfs"),"USB NTFS permission capability is case-insensitive and displays its file system");
        foreach(string format in new[]{"exFAT","FAT32","","RAW"}){var drive=new UsbDriveAccess{Root=@"G:\",FileSystem=format};Check(!drive.SupportsPermissions&&drive.PermissionHint.Contains("仅当前电脑")&&drive.PermissionHint.Contains("不会格式化"),"Unsupported USB format explains local-only protection without claiming ACL support: "+format);}
    }
    static void Reports()
    {
        var fields=new Dictionary<string,string>{{"系统/名称","测试系统"},{"系统/架构","64 位"},{"CPU/1/型号","测试 CPU"},{"主板/1/型号","测试主板"},{"网卡/示例/型号","测试网卡"}};
        string report=InspectionService.ConfigurationFromOutput(Output(fields));Check(InspectionService.Parse(report).Count==5&&InspectionService.ConfigurationSummary(report).Contains("CPU：测试 CPU"),"Hardware configuration remains comparable and has a readable summary");
        bool failed=false;try{InspectionService.ConfigurationFromOutput(Output(fields).Replace("返回代码：0","返回代码：1"));}catch(InvalidOperationException){failed=true;}Check(failed,"Failed configuration process cannot return a success report");
        fields.Remove("CPU/1/型号");failed=false;try{InspectionService.ConfigurationFromOutput(Output(fields));}catch(InvalidOperationException){failed=true;}Check(failed,"Missing CPU data cannot masquerade as a complete configuration");
        var compactFields=new Dictionary<string,string>{{"报告/电脑名","排版样例"},{"系统/名称","Windows 11"},{"系统/架构","64 位"},{"CPU/1/型号","示例 CPU"},{"内存/1/厂商","KINGBANK"},{"内存/1/容量","16 GiB"},{"内存/1/配置频率 MHz","3200"},{"内存/1/零件号","not-for-export"},{"内存/2/厂商","KINGBANK"},{"内存/2/容量","16 GiB"},{"内存/2/配置频率 MHz","3200"},{"显卡/1/型号","示例显卡"},{"网卡/示例/型号","示例网卡"},{"网卡/示例/MAC","02:00:00:00:00:01"},{"网络/示例/IPv4","192.0.2.1/24"},{"网络/示例/IPv6","2001:db8::1/64"}};
        string original=InspectionService.Serialize(compactFields),exported=InspectionService.ExportConfiguration(original);var compact=InspectionService.Parse(exported);
        Check(compact["内存"]=="金百达 16G 3200MHz ×2"&&!exported.Contains("not-for-export"),"Export groups identical RAM into one readable brand/capacity/frequency line");
        Check(exported.StartsWith(InspectionService.CompactHeader)&&exported.Contains("CPU：示例 CPU")&&exported.Contains("IPv6 2001:db8::1/64"),"Compact TXT exports readable text and preserves separate address families");
        Check(InspectionService.CompareConfigurations(compactFields,compact).Contains("差异项：0"),"Old structured and new compact reports compare without format-only differences");
        compact["内存"]="金百达 16G 3600MHz ×2";string diff=InspectionService.CompareConfigurations(compactFields,compact);Check(diff.Contains("差异项：1")&&diff.Contains("内存：左")&&diff.Split('\n').Length==2,"A memory change produces one component difference rather than scattered fields");
        bool duplicate=false;try{InspectionService.Parse(InspectionService.CompactHeader+"\n内存：16G\n内存：32G");}catch(FormatException){duplicate=true;}Check(duplicate,"Ambiguous duplicate compact report rows are rejected");
        compactFields["内存/1/厂商"]="Unknown";compactFields["内存/1/配置频率 MHz"]="0";Check(!InspectionService.ExportConfiguration(InspectionService.Serialize(compactFields)).Contains(" 0MHz"),"Unknown memory vendor or frequency is not invented in compact output");
        compactFields["CPU/1/型号"]="示例 CPU\n内存：伪造行";Check(InspectionService.Parse(InspectionService.ExportConfiguration(InspectionService.Serialize(compactFields)))["内存"].Contains("16G"),"Embedded hardware line breaks cannot inject another report field");
        File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"configuration-v054.txt"),exported,new UTF8Encoding(true));
        string dir=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"generated-tests");Directory.CreateDirectory(dir);
        // No system calls: production script is run with CIM/adapter fixtures on Windows.
        string fixture=@"function Get-CimInstance { param($ClassName,$ErrorAction)
 switch($ClassName) {
 'Win32_OperatingSystem' { [pscustomobject]@{Caption='Fixture OS';Version='10.0';BuildNumber='26100';OSArchitecture='64-bit'} }
 'Win32_Processor' { [pscustomobject]@{DeviceID='CPU0';Name='Fixture CPU';NumberOfCores=4;NumberOfLogicalProcessors=8;MaxClockSpeed=3000} }
 'Win32_ComputerSystem' { [pscustomobject]@{Manufacturer='Fixture OEM';Model='Fixture model';TotalPhysicalMemory=16GB;Domain='WORKGROUP'} }
 'Win32_BaseBoard' { [pscustomobject]@{Tag='Board0';Manufacturer='Fixture';Product='Fixture board';Version='1'} }
 'Win32_PhysicalMemory' { [pscustomobject]@{DeviceLocator='DIMM0';BankLabel='BANK0';Manufacturer='Fixture';PartNumber='RAM-TEST';Capacity=16GB;ConfiguredClockSpeed=3200} }
 'Win32_VideoController' { [pscustomobject]@{DeviceID='GPU0';Name='Fixture GPU';DriverVersion='1.0'} }
 'Win32_DiskDrive' { [pscustomobject]@{Index=0;Model='Fixture SSD';Size=1TB;InterfaceType='SCSI'} }
 'Win32_BIOS' { throw 'optional fixture BIOS unavailable' }
 default { throw ('Unexpected CIM query '+$ClassName) }
 }}
function Get-NetAdapter { [pscustomobject]@{Name='Test NIC';InterfaceDescription='Fixture NIC';MacAddress='02-00-00-00-00-01';LinkSpeed='1 Gbps'} }
function Get-NetIPConfiguration { [pscustomobject]@{InterfaceAlias='Test NIC';IPv4Address=@([pscustomobject]@{IPAddress='192.0.2.1';PrefixLength=24});IPv6Address=@([pscustomobject]@{IPAddress='2001:db8::1';PrefixLength=64});IPv4DefaultGateway=[pscustomobject]@{NextHop='192.0.2.254'};DNSServer=[pscustomobject]@{ServerAddresses=@('192.0.2.53')}} }
function Get-Service { throw 'Service query is forbidden in configuration report' }
function Get-SmbShare { throw 'Share query is forbidden in configuration report' }
$lines=@(& { "+InspectionService.ConfigurationScript()+@" })
$fields=@{}
foreach($line in $lines){if($line.StartsWith('ITCFG:')){$cells=$line.Substring(6).Split(""`t"");$key=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($cells[0]));$fields[$key]=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($cells[1]))}}
if($fields['CPU/1/型号'] -ne 'Fixture CPU' -or $fields['内存/1/容量'] -ne '16 GiB' -or $fields['网卡/Test NIC/型号'] -ne 'Fixture NIC' -or -not $fields.ContainsKey('读取状态/Win32_BIOS')){throw 'Hardware field mapping or optional-error reporting failed'}
if(@($fields.Keys | Where-Object {$_ -match '^(服务|共享)/'}).Count -ne 0){throw 'Unexpected service/share fields'}
'PASS trial fixture configuration'
";
        File.WriteAllText(Path.Combine(dir,"configuration-fixture.ps1"),fixture,new UTF8Encoding(true));
    }
    static void Software()
    {
        var data=new SoftwareSnapshot();data.Entries.Add(new SoftwareEntry{Name="Example",Version="1.2",Publisher="Vendor",Scope="当前用户",Architecture="32 位登记区",Location="C:\\Example",Key="Example [CurrentUser/Registry32]"});data.Entries.Add(new SoftwareEntry{Name="Other",Version="2",Publisher="Different",Scope="所有用户",Architecture="64 位登记区",Location="",Key="Other [LocalMachine/Registry64]"});
        Check(SoftwareInventoryService.Filter(data,"vendor").Count==1&&SoftwareInventoryService.Filter(data,"  ").Count==2,"Software filter handles case, publisher and empty search");
        Check(SoftwareInventoryService.Filter(data,"当前用户").Count==1&&data.Entries.Count==2,"Filtered software view never alters cached snapshot");
        Check(InspectionService.CheckRequirements(data.Versions(),"Example\t1.0\nMissing").Contains("[缺失]"),"Structured inventory still supports requirements checking");
        using(var cancel=new CancellationTokenSource()){cancel.Cancel();bool cancelled=false;try{SoftwareInventoryService.Read(cancel.Token);}catch(OperationCanceledException){cancelled=true;}Check(cancelled,"Cancelled software read never enumerates registry");}
        Check(data.Entries[0].Cells().Length==6&&data.Entries[0].Cells()[4].ToString().Contains("登记区"),"Software columns distinguish registration view from actual binary architecture");
    }
    static void Uninstall()
    {
        string directory=@"C:\Windows\System32",vendor=@"C:\Program Files\Example\uninstall.exe";var files=new HashSet<string>(StringComparer.OrdinalIgnoreCase){vendor,directory+@"\msiexec.exe",directory+@"\rundll32.exe",@"C:\Program.exe"};Func<string,bool> exists=files.Contains;
        var plan=SoftwareUninstallService.Build("Example","\""+vendor+"\" --uninstall --name \"test name\"",false,"",false,directory,exists);
        Check(plan.CanStart&&plan.FileName==vendor&&plan.Arguments=="--uninstall --name \"test name\"","Quoted uninstall path and original arguments remain separate");
        plan=SoftwareUninstallService.Build("Example",vendor+" /remove",false,"",false,directory,exists);Check(plan.CanStart&&plan.FileName==vendor,"Unquoted vendor path with spaces does not fall back to Program.exe");
        string code="{12345678-1234-1234-1234-1234567890AB}";plan=SoftwareUninstallService.Build("MSI","ignored",true,code,false,directory,exists);Check(plan.CanStart&&plan.FileName==directory+@"\msiexec.exe"&&plan.Arguments=="/x "+code,"MSI product registration launches system installer in remove mode without silent flags");
        plan=SoftwareUninstallService.Build("MSI","MsiExec.exe /I"+code+" /qn",false,"",false,directory,exists);Check(plan.CanStart&&plan.Arguments=="/x "+code&&!plan.Arguments.Contains("/qn"),"MSI maintenance command becomes an interactive product removal");
        Check(!SoftwareUninstallService.Build("Locked",vendor,false,"",true,directory,exists).CanStart,"NoRemove registration does not become forced removal");
        Check(!SoftwareUninstallService.Build("Missing",@"C:\Missing\uninstall.exe /remove",false,"",false,directory,exists).CanStart,"Missing registered executable never launches a different program");
        Check(!SoftwareUninstallService.Build("Bad","\""+vendor+"\"bad",false,"",false,directory,exists).CanStart&&!SoftwareUninstallService.Build("Bad",vendor+"\n/remove",false,"",false,directory,exists).CanStart,"Malformed quote and control characters are rejected before process creation");
        Check(!SoftwareUninstallService.Build("Bare","uninstall.exe",false,"",false,directory,exists).CanStart,"Uninstaller is not searched in the working directory or PATH");
        Check(!SoftwareUninstallService.Build("None","",false,"",false,directory,exists).CanStart,"Missing uninstall registration remains a settings fallback");
    }
    static int Main(){try{UsbCapabilities();
        Reports();Software();Uninstall();
        Check(RouteInventoryService.MaskPrefix("255.255.255.0")==24&&RouteInventoryService.MaskPrefix("255.255.240.0")==20,"Dotted masks convert to exact prefix lengths");
        Check(RouteInventoryService.MaskPrefix("0.0.0.0")==0&&RouteInventoryService.MaskPrefix("255.255.255.255")==32,"Prefix conversion covers zero and host masks");
        Check(RouteInventoryService.MaskPrefix("255.0.255.0")==null&&RouteInventoryService.PrefixText(null)=="未知","Invalid or absent masks are not replaced by invented prefixes");
        var nic=new RouteAdapterRow{IPv4="192.0.2.1",Prefix="24",IPv6="2001:db8::1",Gateway4="192.0.2.254",Gateway6="fe80::1"};
        Check((string)nic.Cells("IPv4")[3]=="192.0.2.1"&&(string)nic.Cells("IPv4")[4]=="24"&&(string)nic.Cells("IPv4")[5]=="2001:db8::1"&&(string)nic.Cells("IPv6")[6]=="fe80::1","IPv4 and prefix remain separate beside IPv6; gateway follows selected family");
        using(var cancel=new CancellationTokenSource()){cancel.Cancel();bool stopped=false;try{HardwareService.Inventory(new DatabaseService(),cancel.Token);}catch(OperationCanceledException){stopped=true;}Check(stopped,"Cancelled hardware inventory performs no WMI queries");}
        Console.WriteLine("DESKTOP TOTAL PASS "+passed);return 0;
    }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
