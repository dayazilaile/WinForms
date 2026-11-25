using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using WinForms;
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
        string directory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"generated-tests");Directory.CreateDirectory(directory);string file=Path.Combine(directory,"cleanup-export-test.bat");SoftwareCleanupScriptService.ExportBat(file,standard,true);string bat=File.ReadAllText(file);string marker=":__ITTOOLKIT_PAYLOAD_V1__";
        Check(System.Text.Encoding.Unicode.GetString(Convert.FromBase64String(bat.Substring(bat.LastIndexOf(marker)+marker.Length).Trim()))==automatic&&!bat.Contains("-WindowStyle")&&!bat.Contains("-EncodedCommand")&&!bat.Contains("-ExecutionPolicy")&&automatic.Contains("-Verb RunAs"),"Single BAT contains exact workflow and standard UAC without hidden launch or execution-policy overrides");
        string autoFile=Path.Combine(directory,"cleanup-shared-writer-auto.bat");AutomationService.ExportBat(autoFile,new List<AutoStep>{new AutoStep{Type="等待毫秒",A="1"}},new AutoWorkflowSettings{DeleteOnSuccess=false,RunAsAdministrator=false,DiagnosticMode=false,StartupDelaySeconds=0});string autoBat=File.ReadAllText(autoFile);string autoCommand=autoBat.Split('\n').Single(line=>line.StartsWith("powershell.exe -NoLogo -NoProfile -STA -Command",StringComparison.Ordinal)).TrimEnd('\r');Check(bat.Replace("\r\n","\n").StartsWith("@echo off\nsetlocal DisableDelayedExpansion\nset \"IT_TOOLKIT_BAT=%~f0\"\n")&&bat.Contains(autoCommand)&&bat.Contains(":__ITTOOLKIT_PAYLOAD_V1__"),"Software cleanup and automation share the exact tested BAT header, visible launcher and payload marker");
        RejectBaseline(()=>SoftwareCleanupScriptService.MakeScript(new SoftwareBaselineEntry[0],true),"No cleanup script can be generated from an empty standard");
        byte[] original=File.ReadAllBytes(file);RejectBaseline(()=>SoftwareCleanupScriptService.ExportBat(file,new SoftwareBaselineEntry[0],true),"Invalid cleanup export is rejected before replacing an existing BAT");Check(original.SequenceEqual(File.ReadAllBytes(file))&&Directory.GetFiles(directory,"it-bat-*.tmp").Length==0,"Cleanup export preserves old bytes on validation failure and leaves no staging file");
        string core=SoftwareCleanupScriptService.ReadTemplate(SoftwareCleanupScriptService.TemplatePath);
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
    static void TemplateIntegrity()
    {
        string original=SoftwareCleanupScriptService.ReadTemplate(SoftwareCleanupScriptService.TemplatePath);Check(original.Contains("function Invoke-SWCleanup")&&original.Contains("function Start-SWMachineStage"),"Reviewable cleanup template is deployed beside the application and loads only as export input");
        string folder=Path.Combine(Path.GetTempPath(),"cleanup-template-"+Guid.NewGuid());Directory.CreateDirectory(folder);string file=Path.Combine(folder,"SoftwareCleanup.ps1");
        try{
            File.WriteAllText(file,original.Replace("\n","\r\n"),new UTF8Encoding(true));Check(SoftwareCleanupScriptService.ReadTemplate(file)==original,"Template integrity tolerates UTF-8 BOM and Windows line endings without changing script meaning");
            File.AppendAllText(file,"# unexpected change");bool rejected=false;try{SoftwareCleanupScriptService.ReadTemplate(file);}catch(InvalidDataException){rejected=true;}Check(rejected,"Modified or mismatched cleanup template cannot generate an uninstall script");
            File.Delete(file);rejected=false;try{SoftwareCleanupScriptService.ReadTemplate(file);}catch(IOException){rejected=true;}Check(rejected,"Missing template produces a clear export failure rather than executing a fallback payload");
            File.WriteAllText(file,new string('x',4*1024*1024+1));rejected=false;try{SoftwareCleanupScriptService.ReadTemplate(file);}catch(InvalidDataException){rejected=true;}Check(rejected,"Oversized template is rejected before text parsing");
        }finally{Directory.Delete(folder,true);}
        byte[] assembly=File.ReadAllBytes(typeof(SoftwareCleanupScriptService).Assembly.Location);byte[] marker=Encoding.Unicode.GetBytes("function Read-SWInventory");bool embedded=false;for(int i=0;i<=assembly.Length-marker.Length&&!embedded;i++){int j=0;for(;j<marker.Length&&assembly[i+j]==marker[j];j++){}embedded=j==marker.Length;}Check(!embedded,"Production EXE no longer embeds the bulk-uninstall implementation as a managed string");
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
        foreach(string runner in new[]{"cmd.exe","powershell.exe","rundll32.exe"})Check(!SoftwareUninstallService.Build("Scripted", "\""+directory+"\\"+runner+"\" /c something",false,"",false,directory,delegate{return true;}).CanStart,"Interactive uninstall refuses generic command runners: "+runner);
        Check(!SoftwareUninstallService.Build("MSI", "\""+directory+"\\msiexec.exe\" /uninstall other.msi",false,"",false,directory,delegate{return true;}).CanStart,"MSI removal needs a registered product GUID, not an arbitrary installer argument");
        Check(!SoftwareUninstallService.Build("None","",false,"",false,directory,exists).CanStart,"Missing uninstall registration remains a settings fallback");
    }
    static void Compatibility()
    {
        Check(WindowsCompatibility.StartupProblem(new Version(6,1),"Service Pack 1",528040)=="","Win7 SP1 with .NET 4.8 satisfies the desktop baseline");
        Check(WindowsCompatibility.StartupProblem(new Version(6,1),"",528040).Contains("SP1"),"Win7 / 2008 R2 without SP1 is rejected clearly");
        Check(WindowsCompatibility.StartupProblem(new Version(6,0),"Service Pack 2",528040).Length>0,"Unsupported pre-Win7 systems are not claimed compatible");
        Check(WindowsCompatibility.StartupProblem(new Version(10,0),"",378388).Contains("4.5"),"Older .NET is reported before constructing the async UI");
        Check(WindowsCompatibility.StartupProblem(new Version(6,1),"Service Pack 1",378389)=="","Verified .NET 4.5 baseline is accepted on Win7 SP1");
        Check(WindowsCompatibility.StartupProblem(new Version(10,0),"",461808)=="",".NET 4.7.2 is no longer incorrectly rejected by a mandatory 4.8 gate");
        Check(WindowsCompatibility.HighestRelease(0,528040)==528040&&WindowsCompatibility.HighestRelease(528040,378389)==528040,"Runtime detection takes the highest valid release across both registry views");
        foreach(var v in new[]{new Version(6,2),new Version(6,3),new Version(10,0)})Check(WindowsCompatibility.StartupProblem(v,"",533320)=="","Server-era desktop baseline accepted with newer .NET: "+v);
        string target="ms-settings:appsfeatures",args="";WindowsCompatibility.ManagementTarget(ref target,ref args,true);Check(target=="appwiz.cpl","Legacy software management opens Programs and Features");
        target="ms-settings:windowsupdate";WindowsCompatibility.ManagementTarget(ref target,ref args,true);Check(target=="control.exe"&&args.Contains("WindowsUpdate"),"Win7 update shortcut uses its real control panel");
        target="ms-settings:appsfeatures";WindowsCompatibility.ManagementTarget(ref target,ref args,false);Check(target=="ms-settings:appsfeatures","Current Windows keeps its normal Settings URI");
        Check(Path.GetFullPath(Paths.UserDir)==Path.GetFullPath(Path.Combine(Paths.BaseDir,"Data")),"Portable settings stay under the executable directory, independent of working directory");
        Check(SoftwareBaselineService.DefaultPath.StartsWith(Paths.UserDir+Path.DirectorySeparatorChar)&&Paths.MachineDir.StartsWith(Paths.UserDir+Path.DirectorySeparatorChar),"Software standards and per-computer rollback data share the portable tree");
        Paths.Log("portable-storage-fixture");Check(File.Exists(OperationAudit.LogPath),"Application logs are written beside the EXE without AppData fallback");
        string networkScript=AutomationService.MakeScript(new List<AutoStep>{new AutoStep{Type="设置静态 IPv4",A="Ethernet",B="192.0.2.9",C="255.255.255.0|192.0.2.1"}},BatInputEngine.SendInput,0,false);Check(!networkScript.Contains("$env:TEMP")&&networkScript.Contains("GetDirectoryName($env:IT_TOOLKIT_BAT)")&&networkScript.Contains("Out-File -LiteralPath"),"Exported network backup uses BAT directory and preserves literal paths");
        string system=Path.Combine(Path.GetTempPath(),"trusted-system"),windows=Path.Combine(Path.GetTempPath(),"trusted-windows");
        Check(SystemCommandPath.ResolveWindows("powershell.exe",system,windows)==Path.Combine(system,"WindowsPowerShell","v1.0","powershell.exe"),"PowerShell is pinned to the Windows installation, not PATH");
        Check(SystemCommandPath.ResolveWindows("netsh.exe",system,windows)==Path.Combine(system,"netsh.exe")&&SystemCommandPath.ResolveWindows("explorer.exe",system,windows)==Path.Combine(windows,"explorer.exe"),"System tools resolve to their real Windows directories");
        var start=CommandService.CreateCommand("netsh.exe","interface ipv4 show config");Check(!start.UseShellExecute&&start.RedirectStandardError&&start.RedirectStandardOutput,"Noninteractive management commands avoid cmd.exe chaining and capture failure output");
        string script="Write-Output '中文 O''Reilly &100%! ` $ ()'; Write-Output \"double-quote\"; Write-Output 'C:\\Space Folder\\'; if(1 -eq 1){Write-Output 'PASS direct command'}";
        string command=PowerShellService.Arguments(script);Check(command.Contains("-Command ")&&!command.Contains("-EncodedCommand")&&!command.Contains("ExecutionPolicy")&&!command.Contains("WindowStyle"),"Management PowerShell uses directly reviewable commands without encoded or policy-bypass switches");
        bool rejected=false;try{PowerShellService.Arguments(new string('a',31000));}catch(ArgumentException){rejected=true;}Check(rejected,"Oversized direct commands stop before process creation");
        string shell=Environment.GetEnvironmentVariable("IT_TEST_POWERSHELL");if(Environment.OSVersion.Platform==PlatformID.Win32NT)shell=SystemCommandPath.Resolve("powershell.exe");
        if(!String.IsNullOrEmpty(shell)){string output=CommandService.Run(shell,command,1);Check(output.Contains("返回代码：0")&&output.Contains("PASS direct command")&&output.Contains("O'Reilly &100%! ` $ ()")&&output.Replace("\r", "").Split('\n').Contains("double-quote")&&output.Contains("C:\\Space Folder\\"),"Actual PowerShell direct-argument round trip preserves quotes, symbols and backslashes");}
        byte[] row=new byte[28];Array.Copy(BitConverter.GetBytes(1u),row,4);Array.Copy(BitConverter.GetBytes(7),0,row,4,4);Array.Copy(BitConverter.GetBytes(6u),0,row,8,4);for(int i=0;i<6;i++)row[12+i]=(byte)(i+1);row[20]=192;row[22]=2;row[23]=9;Array.Copy(BitConverter.GetBytes(3u),0,row,24,4);
        var neighbors=LegacyNetworkReader.ParseNeighbors(row,new Dictionary<int,string>{{7,"Legacy NIC"}});Check(neighbors.Count==1&&(string)neighbors[0][1]=="192.0.2.9"&&(string)neighbors[0][2]=="01:02:03:04:05:06"&&(string)neighbors[0][3]=="动态缓存","Win7 ARP ABI maps interface, address and MAC without localized command parsing");
        rejected=false;try{LegacyNetworkReader.ParseNeighbors(new byte[]{1,0,0,0},new Dictionary<int,string>());}catch(FormatException){rejected=true;}Check(rejected,"Truncated native ARP rows are rejected before traversal");
        string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"generated-tests");Directory.CreateDirectory(folder);
        string backupFunction=System.Text.RegularExpressions.Regex.Match(networkScript,@"function Backup-Net[^\r\n]+").Value;
        string backupFixture=@"$ErrorActionPreference='Stop'
function netsh.exe {$global:LASTEXITCODE=0;'# harmless fixture network dump'}
function Get-ITNetworkIdentity {return [pscustomobject]@{HostName='FixtureHost';Device='FixtureDevice';Uuid='FixtureUUID';Serial='FixtureSerial';Facts='MAC=02:00:00:00:00:07'}}
__BACKUP__
$folder=Join-Path $PSScriptRoot 'portable backup [test]'
[void][IO.Directory]::CreateDirectory($folder)
try {
 $env:IT_TOOLKIT_BAT=Join-Path $folder '网络 O''Reilly.bat'
 Backup-Net
 $files=@([IO.Directory]::GetFiles((Join-Path $folder 'Logs')))
 if($files.Count -ne 1 -or [IO.File]::ReadAllText($files[0]) -notmatch 'harmless fixture'){throw 'Backup not stored beside BAT'}
 'PASS portable network backup fixture'
}finally{[IO.Directory]::Delete($folder,$true)}
".Replace("__BACKUP__",backupFunction);File.WriteAllText(Path.Combine(folder,"portable-network-backup-fixture.ps1"),backupFixture,new UTF8Encoding(true));
        string fixture=@"$ErrorActionPreference='Stop'
function Get-Command {param($Name,$ErrorAction) return $null}
function Get-WmiObject {param($Class,$ErrorAction)
 switch($Class){
 'Win32_OperatingSystem'{[pscustomobject]@{Caption='Legacy OS';Version='6.1';BuildNumber='7601';OSArchitecture='64-bit'}}
 'Win32_Processor'{[pscustomobject]@{DeviceID='CPU0';Name='Legacy CPU'}}
 'Win32_PhysicalMemory'{[pscustomobject]@{DeviceLocator='DIMM0';Capacity=4GB;Speed=1600}}
 'Win32_NetworkAdapter'{[pscustomobject]@{NetConnectionID='Legacy NIC';Name='Legacy adapter';MACAddress='02:00:00:00:00:07';Speed=1000000000}}
 'Win32_NetworkAdapterConfiguration'{[pscustomobject]@{IPEnabled=$true;Description='Legacy adapter';IPAddress=@('192.0.2.9','2001:db8::9');IPSubnet=@('255.255.255.0','64');DefaultIPGateway=@('192.0.2.1');DNSServerSearchOrder=@('192.0.2.53')}}
 }
}
$data=@(& { __CONFIG__ })
function Has([string]$key,[string]$value){$line='ITCFG:'+ [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($key)) + ""`t"" + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($value));if($data -notcontains $line){throw ('Missing legacy field '+$key)}}
Has '系统/名称' 'Legacy OS'
Has 'CPU/1/型号' 'Legacy CPU'
Has '内存/1/配置频率 MHz' '1600'
Has '网卡/Legacy NIC/MAC' '02:00:00:00:00:07'
Has '网卡/Legacy NIC/接口速率' '1000 Mb'
Has '网络/Legacy adapter/IPv4' '192.0.2.9/255.255.255.0'
Has '网络/Legacy adapter/IPv6' '2001:db8::9'
'PASS legacy configuration fixture'
".Replace("__CONFIG__",InspectionService.ConfigurationScript());File.WriteAllText(Path.Combine(folder,"legacy-configuration-fixture.ps1"),fixture,new UTF8Encoding(true));
    }
    static void AuditLog()
    {
        Check(OperationAudit.ValidUuid("00000000-0000-0000-0000-000000000000")==""&&OperationAudit.ValidUuid("FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF")=="","Placeholder hardware UUIDs are not represented as device identity");
        Check(OperationAudit.ValidSerial("System Serial Number")==""&&OperationAudit.ValidSerial(" unknown ")=="","Generic OEM serial placeholders are not treated as identifiers");
        string source,first=OperationAudit.DeviceKey("12345678-1234-1234-1234-1234567890ab","A",new[]{"02:00:00:00:00:01"},out source),renamed=OperationAudit.DeviceKey("12345678-1234-1234-1234-1234567890AB","B",new[]{"02:00:00:00:00:99"},out source);
        Check(first==renamed&&source=="硬件UUID","Hardware-based ID remains stable when MAC or other labels change");
        Check(first!=OperationAudit.DeviceKey("22345678-1234-1234-1234-1234567890AB","A",new[]{"02:00:00:00:00:01"},out source),"Different UUIDs distinguish computers even if their host names were cloned");
        string a=OperationAudit.DeviceKey("","",new[]{"02:00:00:00:00:02","02:00:00:00:00:01"},out source),b=OperationAudit.DeviceKey("","",new[]{"02:00:00:00:00:01","02:00:00:00:00:02","02:00:00:00:00:01"},out source);Check(a==b&&source.Contains("MAC"),"MAC fallback is explicit and independent of adapter order or duplicate rows");
        string missing=OperationAudit.DeviceKey("","",new string[0],out source);Check(missing.StartsWith("SESSION-")&&source.Contains("仅本次会话"),"Missing identifiers are labelled as session-only instead of invented hardware");
        var who=new AuditIdentity{Host="教室-PC01",DeviceId=first,IdentitySource="硬件UUID",Uuid="12345678-1234-1234-1234-1234567890AB",Serial="SN-TEST",Adapters="Ethernet;MAC=02:00:00:00:00:07;IP=192.0.2.9 | WLAN;MAC=02:00:00:00:00:08",Account="LAB\\Teacher",Permission="管理员",System="Fixture Windows / 64位"};
        string row=OperationAudit.Format(who,new DateTimeOffset(2026,11,22,10,2,3,TimeSpan.FromHours(8)),"operation-id","parent-id","改IP","结束","失败",123,"password=fixture-secret; note\tsecond\nforged line");var cells=row.TrimEnd('\r','\n').Split('\t');
        Check(cells.Length==19&&cells[0].Contains("+08:00")&&cells[1]==who.Host&&cells[6].Contains("00:07")&&cells[6].Contains("00:08")&&cells[12]=="operation-id"&&cells[13]=="parent-id","Each audit row carries timezone, host, both adapters and correlated operation IDs");
        Check(!row.Contains("fixture-secret")&&cells[18].Contains("\\t")&&cells[18].Contains("\\n")&&row.Count(c=>c=='\n')==1,"Secrets and embedded row separators cannot become exposed or forged log entries");
        Check(OperationAudit.Cell("=1+2")=="'=1+2"&&OperationAudit.Cell("@SUM(1)").StartsWith("'"),"Spreadsheet import treats untrusted formula-like fields as text");
        string dir=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"audit-write-fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        try{string file=Path.Combine(dir,"Logs","events.tsv");var jobs=Enumerable.Range(0,32).Select(i=>System.Threading.Tasks.Task.Run(delegate{OperationAudit.Append(file,OperationAudit.Format(who,DateTimeOffset.Now,"id-"+i,"","operation","结束","已返回",i,""));})).ToArray();System.Threading.Tasks.Task.WaitAll(jobs);var lines=File.ReadAllLines(file);Check(lines.Length==33&&lines.Count(l=>l==OperationAudit.Header)==1&&lines.Skip(1).All(l=>l.Split('\t').Length==19),"Concurrent operations produce exactly one header and complete independent rows");}finally{Directory.Delete(dir,true);}
        string parentId,childId;using(var parent=OperationAudit.Begin("audit-parent-fixture")){parentId=parent.Id;childId=System.Threading.Tasks.Task.Run(delegate{using(var child=OperationAudit.Begin("audit-child-fixture")){Check(child.ParentId==parentId,"Async child commands keep their parent operation ID");child.End("测试结束");return child.Id;}}).GetAwaiter().GetResult();parent.End("测试结束");}
        var records=File.ReadAllLines(OperationAudit.LogPath).Select(l=>l.Split('\t')).Where(c=>c.Length==19&&c[12]==childId).ToArray();Check(records.Length==2&&records.All(c=>c[13]==parentId)&&records[0][15]=="开始"&&records[1][15]=="结束","Start and end records remain correlated across worker threads");
        string siblingParent;string[] siblings;using(var parent=OperationAudit.Begin("audit-concurrent-parent")){siblingParent=parent.Id;var jobs=Enumerable.Range(0,8).Select(i=>System.Threading.Tasks.Task.Run(async delegate{using(var child=OperationAudit.Begin("audit-sibling-"+i)){await System.Threading.Tasks.Task.Delay(8-i);OperationAudit.Record("audit-sibling-record","测试","index="+i);child.End("测试结束");return child.Id;}})).ToArray();siblings=System.Threading.Tasks.Task.WhenAll(jobs).GetAwaiter().GetResult();parent.End("测试结束");}
        var concurrentRows=File.ReadAllLines(OperationAudit.LogPath).Select(l=>l.Split('\t')).Where(c=>c.Length==19&&siblings.Contains(c[12])).ToArray();Check(siblings.Distinct().Count()==8&&siblings.All(id=>concurrentRows.Count(c=>c[12]==id)==3)&&concurrentRows.All(c=>c[13]==siblingParent),"Parallel async operations retain distinct IDs and the correct parent on the .NET 4.5 context path");
        Check(OperationAudit.Outcome("返回代码：7")=="失败或未完成"&&OperationAudit.Outcome("返回代码：0")!="成功","Process exit is distinguished from verified business success");
        Check(OperationAudit.LogPath.StartsWith(Paths.BaseDir)&&!OperationAudit.LogPath.Contains("AppData"),"Structured audit remains in the portable program directory");

    }
    static void DefaultAssociations()
    {
        var formats=DefaultAppsService.Formats;
        Check(formats.Select(x=>x.Extension).Distinct(StringComparer.OrdinalIgnoreCase).Count()==formats.Count,"Association catalogue contains no duplicate formats");
        Check(new[]{".doc",".docx",".xls",".xlsx",".ppt",".pptx",".zip",".rar",".7z",".mp3",".flac",".mp4",".mkv",".pdf",".jpg","http","https","mailto"}.All(x=>formats.Any(f=>f.Extension==x)),"Office, archives, audio, video, images, PDF and web protocols covered");
        Check(DefaultAppsService.Find(".DOCX").Extension==".docx","Format selection is canonical and case insensitive");
        Check(DefaultAppsService.SettingsTarget(new Version(6,1))=="control.exe"&&DefaultAppsService.SettingsTarget(new Version(6,3))=="control.exe"&&DefaultAppsService.SettingsTarget(new Version(10,0))=="ms-settings:defaultapps","Default application entry respects legacy Windows and modern settings");
        var option=new DefaultApplicationOption{Name="Demo",RegisteredName="Player & 100%",PerUser=true};
        Check(DefaultAppsService.ApplicationSettingsTarget(new Version(10,0,22631),option)=="ms-settings:defaultapps?registeredAppUser=Player%20%26%20100%25","Supported Win11 app links escape registered names without command interpolation");
        option.PerUser=false;Check(DefaultAppsService.ApplicationSettingsTarget(new Version(10,0,26100),option).Contains("registeredAppMachine="),"Machine and per-user application registrations use distinct supported links");
        Check(DefaultAppsService.ApplicationSettingsTarget(new Version(10,0,19045),option)=="ms-settings:defaultapps"&&DefaultAppsService.ApplicationSettingsTarget(new Version(10,0,22000),option)=="ms-settings:defaultapps","Older Windows use the general page instead of assuming unsupported deep links");
        bool modern=false;try{DefaultAppsService.ChooseLegacy(IntPtr.Zero,".pdf","unused",new Version(10,0));}catch(NotSupportedException){modern=true;}Check(modern,"Modern Windows never invokes the legacy API as a default setter");
        Check(DefaultAppsService.ReadApplication(".pdf",delegate{return null;}).Contains("未识别")&&DefaultAppsService.ReadApplication(".pdf",delegate{throw new IOException();}).Contains("读取失败"),"Unreadable association is not presented as a default or a hijack");
        string dir=Path.Combine(Path.GetTempPath(),"association-policy-"+Guid.NewGuid());Directory.CreateDirectory(dir);
        try{
            Check(DefaultAppsService.SamplePath(dir,".PDF")==Path.Combine(Path.Combine(dir,"AssociationSamples"),"WinForms-关联样例.pdf"),"Legacy samples stay in the supplied portable data directory");
            string sample=DefaultAppsService.PrepareOpenWithSample(dir,".PDF");Check(File.Exists(sample)&&new FileInfo(sample).Length==0,"Native open-with creates a harmless empty portable sample");
            Check(DefaultAppsService.PrepareOpenWithSample(dir,".pdf")==sample,"Native open-with reuses only a still-empty known sample");
            File.WriteAllText(sample,"user content");bool protectedSample=false;try{DefaultAppsService.PrepareOpenWithSample(dir,".pdf");}catch(IOException){protectedSample=true;}Check(protectedSample&&File.ReadAllText(sample)=="user content","Modified sample is neither opened nor overwritten");
            bool noProtocol=false;try{DefaultAppsService.PrepareOpenWithSample(dir,"https");}catch(ArgumentException){noProtocol=true;}Check(noProtocol,"Protocols cannot be fabricated as files or launched to dummy network URLs");
            var native=typeof(DefaultAppsService).GetNestedType("ShellExecuteInfo",System.Reflection.BindingFlags.NonPublic);Check(System.Runtime.InteropServices.Marshal.SizeOf(native)==(IntPtr.Size==8?112:60),"Shell invocation uses the exact native structure size on both architectures");
        }finally{Directory.Delete(dir,true);}
    }
    static void BrandingCompatibility()
    {
        var fields=new Dictionary<string,string>{{"系统/名称","Legacy OS"},{"CPU/1/型号","Legacy CPU"}};string report=InspectionService.Serialize(fields);Check(report.StartsWith("# WinForms configuration report")&&InspectionService.Parse(report.Replace(InspectionService.Header,InspectionService.LegacyHeader))["CPU/1/型号"]=="Legacy CPU","New configuration report uses WinForms and still reads the original report header");
        string compact=InspectionService.ExportConfiguration(report);Check(InspectionService.Parse(compact.Replace(InspectionService.CompactHeader,InspectionService.LegacyCompactHeader)).Count>0,"Compact reports exported under the original brand remain readable");
        string standard=SoftwareBaselineService.Serialize(new[]{new SoftwareBaselineEntry{Name="Editor",Publisher="Vendor"}});Check(standard.StartsWith("# WinForms Software Baseline")&&SoftwareBaselineService.Parse(standard.Replace(SoftwareBaselineService.Header,SoftwareBaselineService.LegacyHeader)).Single().Name=="Editor","Renamed software standards preserve original-brand imports");
        var assembly=typeof(ProductInfo).Assembly;Check(assembly.GetName().Name=="WinForms"&&ProductInfo.Version=="0.6.5"&&((System.Reflection.AssemblyProductAttribute)Attribute.GetCustomAttribute(assembly,typeof(System.Reflection.AssemblyProductAttribute))).Product=="WinForms","Window product metadata and renamed executable agree on WinForms 0.6.5");
    }
    static int Main(){try{UsbCapabilities();
        Reports();Software();Baseline();CleanupScripts();TemplateIntegrity();Uninstall();Compatibility();AuditLog();BrandingCompatibility();DefaultAssociations();
        Check(RouteInventoryService.MaskPrefix("255.255.255.0")==24&&RouteInventoryService.MaskPrefix("255.255.240.0")==20,"Dotted masks convert to exact prefix lengths");
        Check(RouteInventoryService.MaskPrefix("0.0.0.0")==0&&RouteInventoryService.MaskPrefix("255.255.255.255")==32,"Prefix conversion covers zero and host masks");
        Check(RouteInventoryService.MaskPrefix("255.0.255.0")==null&&RouteInventoryService.PrefixText(null)=="未知","Invalid or absent masks are not replaced by invented prefixes");
        var nic=new RouteAdapterRow{IPv4="192.0.2.1",Prefix="24",IPv6="2001:db8::1",Gateway4="192.0.2.254",Gateway6="fe80::1"};
        Check((string)nic.Cells("IPv4")[3]=="192.0.2.1"&&(string)nic.Cells("IPv4")[4]=="24"&&(string)nic.Cells("IPv4")[5]=="2001:db8::1"&&(string)nic.Cells("IPv6")[6]=="fe80::1","IPv4 and prefix remain separate beside IPv6; gateway follows selected family");
        using(var cancel=new CancellationTokenSource()){cancel.Cancel();bool stopped=false;try{HardwareService.Inventory(new DatabaseService(),cancel.Token);}catch(OperationCanceledException){stopped=true;}Check(stopped,"Cancelled hardware inventory performs no WMI queries");}
        Console.WriteLine("DESKTOP TOTAL PASS "+passed);return 0;
    }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
