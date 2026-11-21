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
    static void RejectBaseline(Action action,string name){try{action();}catch(FormatException){Check(true,name);return;}throw new Exception("Expected invalid baseline: "+name);}
    static void Baseline()
    {
        var rows=new List<SoftwareBaselineEntry>{new SoftwareBaselineEntry{Name=" Editor 中文 ",Publisher="Vendor"},new SoftwareBaselineEntry{Name="editor 中文",Publisher="vendor"},new SoftwareBaselineEntry{Name="Other",Publisher=""}};
        var clean=SoftwareBaselineService.Checked(rows);Check(clean.Count==2&&clean[0].Name=="Editor 中文","Standard list trims and deduplicates full names and publishers case-insensitively");
        clean[0].Name="changed";Check(rows[0].Name==" Editor 中文 ","Standard draft normalization never mutates original rules");
        string text=SoftwareBaselineService.Serialize(rows);var loaded=SoftwareBaselineService.Parse("\uFEFF"+text);Check(loaded.Count==2&&loaded[0].Publisher=="Vendor"&&loaded[1].Publisher=="","Chinese readable standard list round-trips with optional blank publisher");
        var data=new SoftwareSnapshot();var allowed=new SoftwareEntry{Name="EDITOR 中文",Publisher="vendor",Version="999",RegistryName="allowed"};var duplicate=new SoftwareEntry{Name="editor 中文",Publisher="Vendor",Version="1",RegistryName="second-view"};var extra=new SoftwareEntry{Name="Editor 中文 Helper",Publisher="Vendor",HasUninstaller=true,RegistryName="extra"};var other=new SoftwareEntry{Name="Other",Publisher="Any vendor"};
        data.Entries.AddRange(new[]{allowed,duplicate,extra,other});var compared=SoftwareBaselineService.Compare(data,loaded);
        Check(compared.Extra.Count==1&&Object.ReferenceEquals(compared.Extra[0],extra)&&compared.Missing.Count==0,"Comparison preserves exact uninstall row identity, distinguishes helper names and ignores versions/registration duplicates");
        allowed.Publisher="different";duplicate.Publisher="different";compared=SoftwareBaselineService.Compare(data,loaded);Check(compared.Extra.Count==3&&compared.Missing.Count==1&&compared.Missing[0].Name=="Editor 中文","A changed publisher does not silently satisfy a publisher-restricted standard");
        compared=SoftwareBaselineService.Compare(new SoftwareSnapshot(),loaded);Check(compared.Extra.Count==0&&compared.Missing.Count==2,"Missing software remains a standard rule, never a fabricated uninstall registration");
        var wildcard=new[]{new SoftwareBaselineEntry{Name="editor 中文",Publisher=""}};Check(SoftwareBaselineService.Compare(data,wildcard).Extra.Count==2,"Blank publisher permits any publisher for that exact software name only");
        RejectBaseline(()=>SoftwareBaselineService.Checked(new SoftwareBaselineEntry[0]),"Empty standard cannot classify every installed item as extra");
        RejectBaseline(()=>SoftwareBaselineService.Checked(new[]{new SoftwareBaselineEntry{Name="   "}}),"Blank software names are rejected");
        RejectBaseline(()=>SoftwareBaselineService.Checked(new[]{new SoftwareBaselineEntry{Name="name\tforged"}}),"Control characters cannot inject standard list columns");
        RejectBaseline(()=>SoftwareBaselineService.Parse("Google Chrome\t120.0"),"Old keyword/minimum-version lists cannot be confused with standard allowlists");
        RejectBaseline(()=>SoftwareBaselineService.Parse(SoftwareBaselineService.Header+"\n软件名称\t发布者\nApp\tVendor\tcommand"),"Unrecognized extra fields cannot provide commands in the standard list");
        RejectBaseline(()=>SoftwareBaselineService.Checked(Enumerable.Range(0,SoftwareBaselineService.Maximum+1).Select(i=>new SoftwareBaselineEntry{Name="Item "+i})),"Oversized standard lists are rejected");
        RejectBaseline(()=>SoftwareBaselineService.Parse(new string('a',4*1024*1024+1)),"Standard text size is bounded before parsing");
        string directory=Path.Combine(Path.GetTempPath(),"baseline-test-"+Guid.NewGuid());Directory.CreateDirectory(directory);string file=Path.Combine(directory,"中文标准.tsv");
        try{
            Check(SoftwareBaselineService.Load(file)==null,"Missing standard file is distinct from an empty standard");
            SoftwareBaselineService.Save(file,loaded);Check(SoftwareBaselineService.Load(file).Count==2,"Standard file persists independently from the installed software snapshot");
            byte[] original=File.ReadAllBytes(file);RejectBaseline(()=>SoftwareBaselineService.Save(file,new SoftwareBaselineEntry[0]),"Invalid replacement is rejected before overwriting the saved standard");Check(original.SequenceEqual(File.ReadAllBytes(file))&&Directory.GetFiles(directory).Length==1,"Failed standard save preserves old bytes and leaves no temporary file");
            SoftwareBaselineService.Save(file,wildcard);Check(SoftwareBaselineService.Load(file).Single().Name=="editor 中文"&&Directory.GetFiles(directory).Length==1,"Valid replacement is atomic and leaves no staging files");
        }finally{Directory.Delete(directory,true);}
        var partial=new SoftwareSnapshot();partial.Warnings.Add("Registry view denied");partial.Entries.Add(extra);compared=SoftwareBaselineService.Compare(partial,loaded);Check(compared.Extra.Single()==extra&&partial.Warnings.Count==1,"Partial inventory retains warnings and only compares observed installed records");
    }
    static void CleanupScripts()
    {
        var standard=new[]{new SoftwareBaselineEntry{Name="Standard's 中文",Publisher="Vendor"}};
        string confirm=SoftwareCleanupScriptService.MakeScript(standard,false),automatic=SoftwareCleanupScriptService.MakeScript(standard,true);
        Check(confirm.Contains("Name='Standard''s 中文'")&&confirm.Contains("Invoke-SWCleanup $standard $false")&&automatic.Contains("Invoke-SWCleanup $standard $true"),"Cleanup exports preserve Chinese/quoted standard data and selectable execution mode");
        Check(automatic.Contains("Read-SWInventory")&&automatic.Contains("Read-SWRecord $row.Hive")&&automatic.Contains("Test-SWSameRecord")&&automatic.Contains("$remaining=@(Find-SWExtras"),"Cleanup uses target registrations, revalidates before launch and checks remaining registrations afterward");
        Check(!automatic.Contains("Out-File")&&!automatic.Contains("WriteAll")&&!automatic.Contains("Export-Csv")&&!automatic.Contains("Start-Transcript")&&automatic.Contains("Show-SWResult"),"Cleanup reports via popups without writing its own result files or logs");
        Check(automatic.Contains("$row.NoRemove")&&automatic.Contains("火绒")&&automatic.Contains("Quiet")&&automatic.Contains("WaitForExit(600000)")&&!automatic.Contains(".Kill()"),"Protected items and unsupported quiet uninstall are skipped and installer timeout never force-kills a process");
        string directory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"generated-tests");Directory.CreateDirectory(directory);string file=Path.Combine(directory,"cleanup-export-test.bat");SoftwareCleanupScriptService.ExportBat(file,standard,true);string bat=File.ReadAllText(file);string marker=":__SOFTWARE_CLEANUP_V1__";
        Check(System.Text.Encoding.Unicode.GetString(Convert.FromBase64String(bat.Substring(bat.LastIndexOf(marker)+marker.Length).Trim()))==automatic&&!bat.Contains("-WindowStyle")&&!bat.Contains("-EncodedCommand")&&!bat.Contains("-ExecutionPolicy")&&automatic.Contains("-Verb RunAs"),"Single BAT contains exact workflow and standard UAC without hidden launch or execution-policy overrides");
        RejectBaseline(()=>SoftwareCleanupScriptService.MakeScript(new SoftwareBaselineEntry[0],true),"No cleanup script can be generated from an empty standard");
        byte[] original=File.ReadAllBytes(file);RejectBaseline(()=>SoftwareCleanupScriptService.ExportBat(file,new SoftwareBaselineEntry[0],true),"Invalid cleanup export is rejected before replacing an existing BAT");Check(original.SequenceEqual(File.ReadAllBytes(file))&&Directory.GetFiles(directory,"software-cleanup-*.tmp").Length==0,"Cleanup export preserves old bytes on validation failure and leaves no staging file");
        string core=(string)typeof(SoftwareCleanupScriptService).GetField("CoreScript",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static).GetRawConstantValue();
        string fixture=@"$ErrorActionPreference='Stop'
__CORE__
$script:notice='';$script:noticeOk=$false;$script:confirmCount=0;$script:approved=$true;$script:starts=0;$script:changeRecord=$false;$script:keepRecord=$false;$script:readFailure=$false;$script:timeout=$false;$script:admin=$true;$script:stages=0
function Show-SWResult([string]$text,[bool]$ok){$script:notice=$text;$script:noticeOk=$ok}
function Confirm-SWRemoval([string]$text){$script:confirmCount++;return $script:approved}
function Test-SWAdmin {return $script:admin}
function Get-SWInstaller {return 'C:\Windows\System32\msiexec.exe'}
function Test-SWFile([string]$file){return $file -in @('C:\Apps\remove.exe','C:\Program Files\Vendor\remove.exe','C:\Program.exe','C:\Program.exe Files\Vendor\remove.exe','C:\Windows\System32\msiexec.exe','C:\Windows\System32\cmd.exe')}
function Read-SWInventory([bool]$machineOnly=$false) {if($script:readFailure){throw 'fixture incomplete registry read'};if($machineOnly){return @($script:inventory|Where-Object {$_.Hive -eq 'LocalMachine'})};return @($script:inventory)}
function Read-SWRecord([string]$hive,[string]$view,[string]$keyName){$found=@($script:inventory|Where-Object {$_.Key -ceq $keyName});if($found.Count -eq 0){return $null};if($script:changeRecord){$copy=$found[0].PSObject.Copy();$copy.Publisher='changed';return $copy};return $found[0]}
function Start-SWRemoval($plan){$script:starts++;if($script:timeout){$script:inventory=@($script:inventory|Where-Object {$_.Name -eq 'Standard'});throw (New-Object TimeoutException 'fixture installer still running')};if(-not $script:keepRecord){$script:inventory=@($script:inventory|Where-Object {$_.Name -eq 'Standard'})};return 0}
function New-TestRow([string]$name,[string]$quiet='""C:\Apps\remove.exe"" /quiet') {return [pscustomobject]@{Name=$name;Publisher='Vendor';Hive='LocalMachine';View='Registry64';Key=$name;Command='""C:\Apps\remove.exe"" /remove';Quiet=$quiet;Msi=$false;NoRemove=$false;System=$false;Parent='';Release=''}}
function Assert-True([bool]$value,[string]$name){if(-not $value){throw ('fixture assertion: '+$name)};Write-Output ('PASS '+$name)}
$rules=@([pscustomobject]@{Name='Standard';Publisher='Vendor'})
$standard=New-TestRow 'Standard';$extra=New-TestRow 'Extra';$script:inventory=@($standard,$extra)
$result=Invoke-SWCleanup $rules $true
Assert-True ($result -and $script:noticeOk -and $script:starts -eq 1 -and $script:confirmCount -eq 0) 'automatic mode uses registered quiet removal without asking'
$script:starts=0;$script:approved=$false;$script:inventory=@($standard,(New-TestRow 'Interactive' ''))
$result=Invoke-SWCleanup $rules $false
Assert-True (-not $result -and $script:starts -eq 0 -and $script:confirmCount -eq 1 -and $script:notice.Contains('已取消')) 'confirm cancellation starts nothing'
$script:approved=$true;$result=Invoke-SWCleanup $rules $false
Assert-True ($result -and $script:starts -eq 1 -and $script:confirmCount -eq 2) 'confirm mode supports registered interactive uninstall'
$script:starts=0;$script:inventory=@($standard,(New-TestRow 'No quiet' ''));$result=Invoke-SWCleanup $rules $true
Assert-True (-not $result -and $script:starts -eq 0 -and $script:notice.Contains('静默')) 'automatic mode does not guess unsupported quiet switches'
foreach($field in @('NoRemove','System','Parent','Release','Name')) {
 $row=New-TestRow 'Extra';switch($field){'NoRemove'{$row.NoRemove=$true};'System'{$row.System=$true};'Parent'{$row.Parent='parent'};'Release'{$row.Release='Security Update'};'Hive'{$row.Hive='CurrentUser'};'Name'{$row.Name='火绒安全软件'}}
 $script:inventory=@($standard,$row);$script:starts=0;$result=Invoke-SWCleanup $rules $true
 Assert-True (-not $result -and $script:starts -eq 0) ('protected item is skipped: '+$field)
}
$script:inventory=@($standard,$extra);$script:changeRecord=$true;$script:starts=0;$result=Invoke-SWCleanup $rules $true
Assert-True (-not $result -and $script:starts -eq 0 -and $script:notice.Contains('登记已经变化')) 'changed registry identity is never executed'
$script:changeRecord=$false;$script:keepRecord=$true;$result=Invoke-SWCleanup $rules $true
Assert-True (-not $result -and -not $script:noticeOk -and $script:starts -eq 1) 'zero exit alone is not reported as removed while registration remains'
$script:timeout=$true;$script:keepRecord=$false;$script:starts=0;$script:inventory=@($standard,$extra);$result=Invoke-SWCleanup $rules $true
Assert-True (-not $result -and -not $script:noticeOk -and $script:starts -eq 1) 'installer timeout cannot claim completion even after its registration disappears'
$script:timeout=$false
$script:keepRecord=$false;$script:starts=0;$script:readFailure=$true;$result=Invoke-SWCleanup $rules $true
Assert-True (-not $result -and $script:starts -eq 0) 'incomplete inventory aborts before removal'
$script:readFailure=$false;$result=Invoke-SWCleanup @() $true
Assert-True (-not $result -and $script:starts -eq 0) 'empty standard cannot authorize mass removal'
$script:inventory=@($standard);$result=Invoke-SWCleanup $rules $true
Assert-True ($result -and $script:starts -eq 0 -and $script:notice.Contains('没有标准')) 'already matching computer completes without uninstall'
$msi=New-TestRow 'MSI';$msi.Msi=$true;$msi.Key='{12345678-1234-1234-1234-1234567890AB}'
$plan=Get-SWPlan $msi $true
Assert-True ($plan.File -eq 'C:\Windows\System32\msiexec.exe' -and $plan.Arguments.Contains('/qn') -and $plan.Arguments.Contains('/norestart')) 'MSI automatic plan uses product identity and suppresses reboot'
$plan=Get-SWPlan $msi $false
Assert-True (-not $plan.Arguments.Contains('/qn')) 'MSI confirm plan retains interactive removal'
foreach($command in @('uninstall.exe','C:\Program.exe Files\Vendor\remove.exe /remove','""C:\Apps\remove.exe""bad','""C:\Windows\System32\cmd.exe"" /c anything')) {
 $failed=$false;try{[void](Parse-SWCommand $command)}catch{$failed=$true};Assert-True $failed ('unsafe/ambiguous command is rejected: '+$command)
}
$plan=Parse-SWCommand '""C:\Program Files\Vendor\remove.exe"" /remove ""literal & data""'
Assert-True ($plan.File -eq 'C:\Program Files\Vendor\remove.exe' -and $plan.Arguments.Contains('literal & data')) 'quoted EXE and arguments remain separate without a command shell'
$rows=@((New-TestRow 'Standard'),(New-TestRow 'Standard Helper'));$rows[0].Publisher='OTHER'
Assert-True (@(Find-SWExtras $rows $rules).Count -eq 2) 'comparison uses exact complete names and publishers'
$loose=@([pscustomobject]@{Name='standard';Publisher=''})
Assert-True (@(Find-SWExtras $rows $loose).Count -eq 1) 'optional publisher and name case match the saved standard semantics'
$script:admin=$false;$script:starts=0;$script:inventory=@($standard,(New-TestRow 'UserExtra'));$script:inventory[1].Hive='CurrentUser'
$result=Invoke-SWCleanup $rules $true
Assert-True ($result -and $script:starts -eq 1) 'current-user software is removed in original non-admin account'
function Start-SWMachineStage {$script:stages++;$script:inventory=@($script:inventory|Where-Object {$_.Name -eq 'Standard'});return 0}
$script:starts=0;$script:inventory=@($standard,(New-TestRow 'MachineExtra'));$result=Invoke-SWCleanup $rules $true
Assert-True ($result -and $script:starts -eq 0 -and $script:stages -eq 1) 'non-admin parent delegates machine removal through standard UAC stage'
$script:inventory=@($standard,(New-TestRow 'UserExtra'));$script:inventory[1].Hive='CurrentUser';$script:admin=$true;$script:starts=0;$script:notice='unchanged'
$result=Invoke-SWCleanup $rules $true $true
Assert-True ($result -and $script:starts -eq 0 -and $script:notice -eq 'unchanged') 'elevated machine stage never removes another admin user profile or displays duplicate completion popup'
function Start-SWMachineStage {$script:inventory=@($standard);return 2}
$script:admin=$false;$script:inventory=@($standard,(New-TestRow 'MachineExtra'));$result=Invoke-SWCleanup $rules $true
Assert-True (-not $result -and -not $script:noticeOk -and $script:notice.Contains('重启')) 'machine-stage restart request is propagated to the single final popup'
function Start-SWMachineStage {throw 'fixture UAC cancelled'}
$script:inventory=@($standard,(New-TestRow 'MachineExtra'));$result=Invoke-SWCleanup $rules $true
Assert-True (-not $result -and $script:notice.Contains('管理员阶段')) 'cancelled UAC cannot be reported as successful cleanup'
foreach($name in @('NVIDIA Graphics Driver','Intel(R) Graphics','Microsoft .NET Runtime','360安全卫士')){Assert-True ((Get-SWProtection (New-TestRow $name)).Length -gt 0) ('common protected component: '+$name)}
foreach($name in @('Paint.NET','360浏览器')){Assert-True ((Get-SWProtection (New-TestRow $name)).Length -eq 0) ('ordinary software is not protected by an overly broad name fragment: '+$name)}
Write-Output 'PASS software cleanup fixture'
".Replace("__CORE__",core);File.WriteAllText(Path.Combine(directory,"software-cleanup-fixture.ps1"),fixture,new UTF8Encoding(true));
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
        Reports();Software();Baseline();CleanupScripts();Uninstall();
        Check(RouteInventoryService.MaskPrefix("255.255.255.0")==24&&RouteInventoryService.MaskPrefix("255.255.240.0")==20,"Dotted masks convert to exact prefix lengths");
        Check(RouteInventoryService.MaskPrefix("0.0.0.0")==0&&RouteInventoryService.MaskPrefix("255.255.255.255")==32,"Prefix conversion covers zero and host masks");
        Check(RouteInventoryService.MaskPrefix("255.0.255.0")==null&&RouteInventoryService.PrefixText(null)=="未知","Invalid or absent masks are not replaced by invented prefixes");
        var nic=new RouteAdapterRow{IPv4="192.0.2.1",Prefix="24",IPv6="2001:db8::1",Gateway4="192.0.2.254",Gateway6="fe80::1"};
        Check((string)nic.Cells("IPv4")[3]=="192.0.2.1"&&(string)nic.Cells("IPv4")[4]=="24"&&(string)nic.Cells("IPv4")[5]=="2001:db8::1"&&(string)nic.Cells("IPv6")[6]=="fe80::1","IPv4 and prefix remain separate beside IPv6; gateway follows selected family");
        using(var cancel=new CancellationTokenSource()){cancel.Cancel();bool stopped=false;try{HardwareService.Inventory(new DatabaseService(),cancel.Token);}catch(OperationCanceledException){stopped=true;}Check(stopped,"Cancelled hardware inventory performs no WMI queries");}
        Console.WriteLine("DESKTOP TOTAL PASS "+passed);return 0;
    }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
