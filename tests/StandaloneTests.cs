using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ITDesktopToolkit;
class StandaloneTests
{
    static int passed;
    static void Check(bool value,string name){if(!value)throw new Exception(name);passed++;Console.WriteLine("PASS "+name);}
    static void Reject(Action work,string name){try{work();}catch(ArgumentException){Check(true,name);return;}throw new Exception("Expected rejection: "+name);}
    static AutoStep S(string t,string a="",string b="",string c=""){return new AutoStep{Type=t,A=a,B=b,C=c};}
    static void Flow()
    {
        var steps=new List<AutoStep>{S("如果文件存在",@"D:\a'b.txt"),S("如果网络可达","127.0.0.1"),S("等待毫秒","1"),S("否则"),S("等待毫秒","2"),S("结束条件"),S("否则"),S("如果系统版本","22000","26100"),S("等待毫秒","3"),S("结束条件"),S("结束条件"),S("重试开始","2","1"),S("检查文件存在",@"D:\ready.txt"),S("运行程序并等待","cmd.exe","/c exit 0","5"),S("重试结束")};
        string script=AutomationService.MakeScript(steps,BatInputEngine.SendInput,0,false);
        Check(script.Contains("a''b.txt")&&script.Contains("$itWindowsBuild -ge 22000")&&script.Contains("$itProc.ExitCode")&&script.Contains("$itRetry11"),"Nested conditions, quoting, retry and exit-code propagation");
        Reject(()=>AutomationService.ValidateFlow(new List<AutoStep>{S("否则")}),"Unmatched else");
        Reject(()=>AutomationService.ValidateFlow(new List<AutoStep>{S("如果文件存在","x")}),"Unclosed block");
        Reject(()=>AutomationService.ValidateFlow(new List<AutoStep>{S("重试开始"),S("结束条件")}),"Mismatched block");
        Reject(()=>AutomationService.ValidateFlow(new List<AutoStep>{S("重试开始","0"),S("重试结束")}),"Zero retries rejected");
        Reject(()=>AutomationService.ValidateFlow(new List<AutoStep>{S("重试开始"),S("删除指定路径文件",@"D:\x.txt"),S("重试结束")}),"Destructive auto retry rejected");
        Reject(()=>AutomationService.ValidateFlow(new List<AutoStep>{S("重试开始"),S("清空当前用户回收站"),S("重试结束")}),"Recycle Bin cleanup cannot be retried");
        Check(AutomationService.MakeScript(new List<AutoStep>{S("清空当前用户回收站")},BatInputEngine.SendInput,0,false).Contains("Clear-RecycleBin -Force"),"Recycle Bin action emitted for BAT workflow");
        Reject(()=>AutomationService.ValidateFlow(new List<AutoStep>{S("如果网络可达","a,b"),S("结束条件")}),"Multiple hosts rejected in single-host condition");
        var deep=new List<AutoStep>();for(int i=0;i<9;i++)deep.Add(S("如果文件存在","x"));Reject(()=>AutomationService.ValidateFlow(deep),"Excessive nesting rejected");
        string tmp=Path.GetTempFileName();try{AutomationService.Save(tmp,steps);AutoWorkflowSettings settings;var restored=AutomationService.Load(tmp,out settings);Check(restored.Count==steps.Count&&restored[0].A==steps[0].A,"Workflow save/load with new types");}finally{File.Delete(tmp);}
        var folder=Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),"generated-tests");Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder,"flow.ps1"),script,new System.Text.UTF8Encoding(true));
        string marker=Path.Combine(folder,"ready.txt");File.WriteAllText(marker,"fixture");
        var success=new List<AutoStep>{S("如果文件存在",marker),S("等待毫秒","1"),S("否则"),S("检查文件存在",marker+".missing"),S("结束条件"),S("重试开始","2","1"),S("检查文件存在",marker),S("重试结束")};
        File.WriteAllText(Path.Combine(folder,"success.ps1"),AutomationService.MakeScript(success,BatInputEngine.SendInput,0,false),new System.Text.UTF8Encoding(true));
        File.WriteAllText(Path.Combine(folder,"failure.ps1"),AutomationService.MakeScript(new List<AutoStep>{S("重试开始","2","1"),S("检查文件存在",marker+".missing"),S("重试结束")},BatInputEngine.SendInput,0,false),new System.Text.UTF8Encoding(true));
        File.WriteAllText(Path.Combine(folder,"legacy-flow.ps1"),AutomationService.MakeScript(steps,BatInputEngine.Legacy,0,false),new System.Text.UTF8Encoding(true));
    }
    static void LauncherReview()
    {
        string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"generated-tests");Directory.CreateDirectory(folder);string temporary=Path.Combine(Path.GetTempPath(),"launcher-review-"+Guid.NewGuid());Directory.CreateDirectory(temporary);string bootstrap="";bool visible=true,payloads=true,flags=true,length=true,foreground=true,cleanupFlags=true;string cleanup="";
        try{
            int count=0;foreach(var engine in new[]{BatInputEngine.SendInput,BatInputEngine.Legacy})foreach(bool hide in new[]{false,true})foreach(bool admin in new[]{false,true})foreach(bool remove in new[]{false,true})foreach(bool diagnostic in new[]{false,true}){
                var options=new AutoWorkflowSettings{Engine=engine,HideConsole=hide,RunAsAdministrator=admin,DeleteOnSuccess=remove,DiagnosticMode=diagnostic,StartupDelaySeconds=3};var actions=new List<AutoStep>{S("移动鼠标","1887","25")};string file=Path.Combine(temporary,"sample O'Reilly &100%-"+(count++)+".bat");AutomationService.ExportBat(file,actions,options);string bat=File.ReadAllText(file);
                visible&=!bat.Contains("-EncodedCommand")&&!bat.Contains("-ExecutionPolicy");flags&=bat.Contains("-Verb RunAs")==admin&&bat.Contains("rem Optional hidden worker")==hide&&bat.Contains("Remove-Item -LiteralPath $f")== (hide&&remove);
                if(!hide){foreground&=!bat.Contains("-WindowStyle")&&!bat.Contains("start \"\"")&&!bat.Contains("Start-Sleep -Seconds 2;");cleanupFlags&=bat.Contains("del /f /q \"%IT_TOOLKIT_BAT%\"")==remove;if(remove){string normalized=bat.Replace("\r\n","\n");int from=normalized.IndexOf("rem Clean up only this BAT",StringComparison.Ordinal);int end=normalized.IndexOf("\n)\n",from,StringComparison.Ordinal)+3;cleanup=normalized.Substring(from,end-from);cleanupFlags&=cleanup.Contains("(\ndel /f /q")&&cleanup.Contains("[WARNING]")&&cleanup.Contains("exit /b %RC%\n)");}}
                string marker=":__ITTOOLKIT_PAYLOAD_V1__";string body=bat.Substring(bat.LastIndexOf(marker,StringComparison.Ordinal)+marker.Length).Trim();payloads&=System.Text.Encoding.Unicode.GetString(Convert.FromBase64String(body))==AutomationService.MakeScript(actions,engine,3,diagnostic);
                int launcher=0;foreach(string line in bat.Split('\n'))if(line.StartsWith("powershell.exe",StringComparison.Ordinal)||line.StartsWith("start ",StringComparison.Ordinal)){length&=line.Length<8191;int start=line.IndexOf("-Command \"",StringComparison.Ordinal)+10;string command=line.Substring(start).TrimEnd('\r');command=command.Substring(0,command.Length-1);File.WriteAllText(Path.Combine(folder,"launcher-parse-"+count+"-"+(launcher++)+".ps1"),command,new System.Text.UTF8Encoding(true));if(line.Contains("-NonInteractive -Command")&&!line.StartsWith("start ",StringComparison.Ordinal))bootstrap=command;}
            }
            Check(visible,"All 32 option combinations expose their startup commands without EncodedCommand or policy overrides");Check(payloads,"Single BAT Unicode payloads preserve the exact generated workflow in all modes");Check(flags,"Automatic UAC, hidden execution and optional deletion remain selectable");Check(length,"Visible startup commands stay below cmd.exe command-length limits");Check(Directory.GetFiles(temporary).Length==32,"Export still produces exactly one BAT per workflow and no companion file");
        }finally{Directory.Delete(temporary,true);}
        Check(foreground,"Visible workflows never launch a hidden or detached cleanup process in any option combination");
        Check(cleanupFlags,"Visible self-deletion is optional, exact-path only, warns on failure and preserves exit code inside a pre-parsed block");
        // Inert Windows fixtures exercise only the actual generated cleanup block, never workflow actions.
        foreach(int code in new[]{0,7})File.WriteAllText(Path.Combine(folder,"visible-cleanup-"+code+".bat"),"@echo off\r\nsetlocal DisableDelayedExpansion\r\nset \"IT_TOOLKIT_BAT=%~f0\"\r\nset \"RC="+code+"\"\r\n"+cleanup.Replace("\n","\r\n"),new System.Text.UTF8Encoding(false));
        Check(bootstrap.Contains("Start-Process -FilePath $target")&&bootstrap.Contains("-Verb RunAs")&&bootstrap.Contains("IsInRole")&&!bootstrap.Contains("cmdArgs"),"UAC uses the standard shell verb and an environment-supplied file path without building nested cmd arguments");
        string wait=AutomationService.MakeScript(new List<AutoStep>{S("等待毫秒","1"),S("随机等待","1","20")},BatInputEngine.SendInput,0,false);Check(!wait.Contains("Add-Type")&&!wait.Contains("WScript.Shell"),"Non-input waits with diagnostics off do not compile unused input bridges or create COM objects");
        string mouse=AutomationService.MakeScript(new List<AutoStep>{S("移动鼠标","1887","25")},BatInputEngine.SendInput,0,true);Check(mouse.Contains("ITAutomationCursor")&&mouse.Contains("SetProcessDPIAware()")&&mouse.Contains("ITAutomationDiag")&&!mouse.Contains("WScript.Shell"),"Mouse, physical-DPI behavior and diagnostics remain present without unused window-activation COM");
        string window=AutomationService.MakeScript(new List<AutoStep>{S("激活窗口","测试窗口")},BatInputEngine.SendInput,0,false);Check(window.Contains("New-Object -ComObject WScript.Shell")&&window.Contains("AppActivate"),"Window activation retains its required COM interface");
        string legacy=AutomationService.MakeScript(new List<AutoStep>{S("输入文字","中文样例")},BatInputEngine.Legacy,0,false);Check(legacy.Contains("ITAutomationLegacy")&&legacy.Contains("Send-LegacyText")&&legacy.Contains("Clipboard"),"Legacy Chinese input and clipboard restoration stay available");
        string role="$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)";
        string preamble=@"$ErrorActionPreference='Stop'
$directory=Join-Path ([IO.Path]::GetTempPath()) ('uac-fixture-'+[Guid]::NewGuid())
[void][IO.Directory]::CreateDirectory($directory)
$env:IT_TOOLKIT_BAT=Join-Path $directory '合法 O''Reilly &100%.bat'
[IO.File]::WriteAllText($env:IT_TOOLKIT_BAT,'@rem inert fixture; never executed')
";
        string request=@"function Start-Process { param($FilePath,$WorkingDirectory,$Verb,$ErrorAction) if($FilePath -cne $env:IT_TOOLKIT_BAT -or $Verb -ne 'RunAs' -or $WorkingDirectory -cne $directory){throw 'path/verb mismatch'};Write-Host 'PASS mock standard UAC request' }
";
        string cancel=@"function Start-Process { param($FilePath,$WorkingDirectory,$Verb,$ErrorAction) throw 'mock cancellation' }
";
        string already=@"function Start-Process { throw 'unexpected re-elevation' }
";
        foreach(var item in new[]{new[]{"request",request,"$false"},new[]{"cancel",cancel,"$false"},new[]{"already",already,"$true"}}){string fixture=preamble+item[1]+"try {\r\n"+bootstrap.Replace(role,item[2])+"\r\n} finally { Remove-Item -LiteralPath $directory -Recurse -Force }\r\n";File.WriteAllText(Path.Combine(folder,"uac-"+item[0]+"-fixture.ps1"),fixture,new System.Text.UTF8Encoding(true));}
    }
    static void TimeoutChecks()
    {
        string wait=AutomationService.MakeScript(new List<AutoStep>{S("等待窗口","missing-window")},BatInputEngine.SendInput,0,false);
        Check(wait.Contains("[int]$seconds=120")&&wait.Contains("WaitOne($seconds*1000)")&&wait.Contains("AddSeconds(120)")&&wait.Contains("} 120; if(-not $found)"),"Window waits and condition checks default to a two-minute deadline");
        Check(wait.Contains("BeginStop($null,$null)")&&wait.Contains("if($finished -or $null -eq $pending)")&&!wait.Contains(".Stop()")&&!wait.Contains("Start-Job"),"Timed-out checks cancel asynchronously without a blocking stop or extra PowerShell process");
        Check(wait.Contains("$script:itAbortWorkflow=$true; throw '窗口超时")&&wait.Contains("} catch { Write-Host ('FAILED:")&&wait.EndsWith("throw }"+Environment.NewLine),"Timeout terminates later actions and propagates a failing exit to BAT cleanup");
        foreach(var condition in new[]{S("如果文件存在",@"D:\file.txt"),S("如果网络可达","127.0.0.1"),S("如果系统版本","22000","26100")}){
            var flow=AutomationBlockService.Compose(condition,new List<AutoStep>{S("等待毫秒","1")},new List<AutoStep>{S("等待毫秒","2")},true);string generated=AutomationService.MakeScript(flow,BatInputEngine.SendInput,0,false);Check(generated.Contains("if((Invoke-ITBoundedCheck")&&generated.Contains("} else {"),"Checks are bounded while definite false still selects else: "+condition.Type);
        }
        Check(AutomationService.MakeScript(new List<AutoStep>{S("等待窗口","title","90")},BatInputEngine.Legacy,0,false).Contains("} 90; if(-not $found)"),"An explicitly shorter window deadline is retained");
        foreach(string seconds in new[]{"0","121","bad"})Reject(()=>AutomationService.MakeScript(new List<AutoStep>{S("等待窗口","title",seconds)},BatInputEngine.SendInput,0,false),"Window deadlines must be within 1 to 120: "+seconds);
        Reject(()=>AutomationService.MakeScript(new List<AutoStep>{S("等待窗口","")},BatInputEngine.SendInput,0,false),"Empty window target cannot accidentally match every window");
        string helper=(string)typeof(AutomationService).GetField("BoundedCheckHelper",BindingFlags.NonPublic|BindingFlags.Static).GetRawConstantValue();
        string fixture=@"$ErrorActionPreference='Stop'
"+helper+@"
if((Invoke-ITBoundedCheck 'true' { $true }) -ne $true){throw 'true result changed'}
if((Invoke-ITBoundedCheck 'false' { $false }) -ne $false){throw 'false result changed'}
foreach($bad in @({ throw 'fixture condition error' }, { 'not a boolean' }, { $true; $false })) {
 $script:itAbortWorkflow=$false; $failed=$false
 try { [void](Invoke-ITBoundedCheck 'bad' $bad) } catch { $failed=$true }
 if(-not $failed -or -not $script:itAbortWorkflow){throw 'failed/ambiguous check was not terminal'}
}
$script:itAbortWorkflow=$false; $timedOut=$false; $watch=[Diagnostics.Stopwatch]::StartNew()
try { [void](Invoke-ITBoundedCheck 'slow' { Start-Sleep -Seconds 30; $true } 1) } catch { $timedOut=$_.Exception.Message.Contains('超时') }
if(-not $timedOut -or -not $script:itAbortWorkflow -or $watch.Elapsed.TotalSeconds -gt 8){throw 'timeout did not return promptly'}
Write-Output 'PASS bounded checks fixture'
";
        string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"generated-tests");Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"bounded-checks-fixture.ps1"),fixture,new System.Text.UTF8Encoding(true));
        File.WriteAllText(Path.Combine(folder,"wait-timeout-fixture.ps1"),wait.Replace("AddSeconds(120)","AddSeconds(1)").Replace("} 120; if(-not $found)","} 1; if(-not $found)").Replace("{ $deadline=","{ function Get-Process { @() }; $deadline="),new System.Text.UTF8Encoding(true));
        var flowError=new List<AutoStep>{S("重试开始","3","1"),S("如果文件存在","missing"),S("等待毫秒","1"),S("否则"),S("等待毫秒","2"),S("结束条件"),S("重试结束"),S("等待毫秒","3")};string error=AutomationService.MakeScript(flowError,BatInputEngine.SendInput,0,false).Replace("Test-Path -LiteralPath 'missing' -PathType Leaf -ErrorAction Stop","throw 'fixture condition error'");
        File.WriteAllText(Path.Combine(folder,"condition-error-fixture.ps1"),error,new System.Text.UTF8Encoding(true));
        string falseCondition=AutomationService.MakeScript(AutomationBlockService.Compose(S("如果文件存在",Path.Combine(folder,"absent-"+Guid.NewGuid())),new List<AutoStep>{S("检查文件存在",Path.Combine(folder,"must-not-run-"+Guid.NewGuid()))},new List<AutoStep>{S("等待毫秒","1")},true),BatInputEngine.SendInput,0,false);
        File.WriteAllText(Path.Combine(folder,"false-condition-fixture.ps1"),falseCondition,new System.Text.UTF8Encoding(true));
    }
    static void RandomIpv4()
    {
        var config=AutomationService.RandomIpv4("自动有线","192.0.2","192.0.2.254|继续流程");Check(config.Network=="192.0.2.0/24"&&config.Hosts.Length==252&&config.Hosts.First()==1&&config.Hosts.Last()==253&&!config.Hosts.Contains(100),"Random IPv4 uses exact requested host ranges excluding 100/254/255/0");
        var finish=AutomationService.RandomIpv4("Ethernet Space","192.0.2.0/24","192.0.2.1|结束脚本");Check(finish.Finish&&!finish.Hosts.Contains(1)&&finish.Hosts.Length==251,"Gateway in the random range is excluded and finish mode is retained");
        Check(AutomationService.RandomIpv4("自动有线","172.26.8.198/20","172.26.0.1").Network=="172.26.8.0/20","Wider CIDR retains the selected first three octets and accepts a gateway elsewhere in that subnet");
        config=AutomationService.RandomIpv4("自动有线","192.0.2.128/25","192.0.2.129");Check(config.Hosts.All(n=>n>=130&&n<=253),"Narrow subnet excludes its network/broadcast and out-of-subnet pool members");
        foreach(var fields in new[]{new[]{"","192.0.2","192.0.2.254"},new[]{"自动有线","127.0.0","127.0.0.1"},new[]{"自动有线","192.0.2.0/32","192.0.2.1"},new[]{"自动有线","192.0.2","198.51.100.1"},new[]{"自动有线","192.0.2","192.0.2.0"},new[]{"自动有线","192.0.2","192.0.2.255"},new[]{"自动有线","192.0.2","192.0.2.1|invalid"},new[]{"自动有线","192.0.2.252/30","192.0.2.253"}})Reject(()=>AutomationService.RandomIpv4(fields[0],fields[1],fields[2]),"Invalid random IPv4 configuration rejected: "+String.Join(" / ",fields));
        foreach(var engine in new[]{BatInputEngine.SendInput,BatInputEngine.Legacy}){
            string next=AutomationService.MakeScript(new List<AutoStep>{S("随机设置 IPv4","自动有线","192.0.2","192.0.2.254|继续流程"),S("等待毫秒","1")},engine,0,false),end=AutomationService.MakeScript(new List<AutoStep>{S("随机设置 IPv4","自动有线","192.0.2","192.0.2.254|结束脚本"),S("等待毫秒","1")},engine,0,false);
            Check(next.Contains("Invoke-RandomIPv4 '自动有线' '192.0.2' 24 '192.0.2.254'")&&next.Contains("Assert-Admin")&&!next.Contains("Write-Host 'All steps complete.'; return")&&end.Contains("Write-Host 'All steps complete.'; return"),"Both success modes emit the requested flow semantics without bypassing UAC: "+engine);
        }
        var steps=new List<AutoStep>{S("随机设置 IPv4","Ethernet Space","192.0.2.0/24","192.0.2.254|结束脚本")};string file=Path.GetTempFileName();try{AutomationService.Save(file,steps);Check(AutomationService.Load(file).Single().C==steps[0].C,"Random IPv4 configuration and finish mode round-trip in existing workflow format");byte[] old=File.ReadAllBytes(file);Reject(()=>AutomationService.Save(file,new List<AutoStep>{S("随机设置 IPv4","自动有线","192.0.2","bad")}),"Invalid random IPv4 cannot overwrite a saved workflow");Check(old.SequenceEqual(File.ReadAllBytes(file)),"Failed random IPv4 save preserves the previous workflow");}finally{File.Delete(file);}
        string helper=(string)typeof(AutomationService).GetField("RandomIpv4Helper",BindingFlags.NonPublic|BindingFlags.Static).GetRawConstantValue();Check(helper.Contains("TotalSeconds -lt 10")&&helper.Contains("AddressState -eq 'Duplicate'")&&helper.Contains("$pool.RemoveAt($pick)")&&helper.Contains("Restore-RandomIp")&&!helper.Contains("Test-Connection"),"Conflict observation is bounded to ten seconds, uses ARP/DAD and does not reuse failed candidates or equate no Ping with safety");
        string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"generated-tests");Directory.CreateDirectory(folder);string fixture=@"$ErrorActionPreference='Stop'
__HELPER__
function Check([bool]$ok,[string]$name){if(-not $ok){throw ('Random IP fixture failed: '+$name)};Write-Output ('PASS '+$name)}
$script:NativeSet=${function:Set-RandomIpAddress};$script:NativeRestore=${function:Restore-RandomIp}
function netsh.exe {$script:NativeArgs=@($args);$global:LASTEXITCODE=0}
& $script:NativeSet 'Ethernet Space' '192.0.2.2' 24 '192.0.2.254'
Check ($script:NativeArgs -contains 'name=Ethernet Space') 'netsh receives exact interface as one argument'
Check ($script:NativeArgs -contains 'mask=255.255.255.0' -and $script:NativeArgs -contains 'gateway=192.0.2.254') 'netsh receives calculated mask and gateway'
function New-TestClock {$clock=[pscustomobject]@{Tick=0};$clock|Add-Member ScriptProperty Elapsed {$this.Tick++;[TimeSpan]::FromSeconds($this.Tick)};$script:LastClock=$clock;return $clock}
function Reset-Test {
 $script:address='10.0.0.10';$script:prefix=24;$script:gateway='10.0.0.1';$script:dhcp='Enabled';$script:dad=1;$script:up='Up';$script:dup='';$script:peerBusy='';$script:otherIp='';$script:multiple=$false;$script:routeBad=$false;$script:restoreFail=$false;$script:readFail=$false;$script:setFail=$false;$script:setCount=0;$script:restored=0;$script:probed=@();$script:assigned=@();$script:itAbortWorkflow=$false
}
function Get-NetAdapter {param([switch]$Physical,$ErrorAction) [pscustomobject]@{Name='Ethernet Space';ifIndex=7;Status=$script:up;MacAddress='02-00-00-00-00-10';NdisPhysicalMedium=14;InterfaceDescription='Ethernet'}}
function Get-NetIPInterface {param($InterfaceIndex,$AddressFamily,$ErrorAction) [pscustomobject]@{Dhcp=$script:dhcp;DadTransmits=$script:dad}}
function Get-NetIPAddress {param($InterfaceIndex,$AddressFamily,$ErrorAction)
 if($script:readFail){throw 'fixture interface query failed'}
 [pscustomobject]@{IPAddress=$script:address;InterfaceIndex=7;PrefixLength=$script:prefix;AddressState=$(if($script:address -eq $script:dup){'Duplicate'}else{'Preferred'})}
 if($script:multiple){[pscustomobject]@{IPAddress='10.0.0.11';InterfaceIndex=7;PrefixLength=24;AddressState='Preferred'}}
 if($script:otherIp.Length -gt 0){[pscustomobject]@{IPAddress=$script:otherIp;InterfaceIndex=8;PrefixLength=24;AddressState='Preferred'}}
}
function Get-NetRoute {param($InterfaceIndex,$AddressFamily,$DestinationPrefix,$ErrorAction) [pscustomobject]@{InterfaceIndex=7;DestinationPrefix='0.0.0.0/0';NextHop=$(if($script:routeBad -and $script:setCount -gt 0){'192.0.2.99'}else{$script:gateway})}}
function Get-NetNeighbor {param($InterfaceIndex,$AddressFamily,$ErrorAction) }
function Probe-RandomIp([string]$ip,[string]$source){$script:probed+=,$ip;if($ip -eq $script:peerBusy){return '020000000077'};return ''}
function Get-Random {param([int]$Minimum,[int]$Maximum) return $Minimum}
function Start-Sleep {param($Milliseconds,$Seconds) }
function Set-DnsClientServerAddress {throw 'random IPv4 must not modify DNS'}
function Set-RandomIpAddress([string]$nic,[string]$address,[int]$prefix,[string]$gateway){$script:setCount++;$script:assigned+=,$address;if($script:setFail){throw 'fixture set failed'};$script:address=$address;$script:prefix=$prefix;$script:gateway=$gateway;$script:dhcp='Disabled'}
function Restore-RandomIp($saved,[string]$candidate,[string]$gateway){$script:restored++;$script:restoredDhcp=$saved.Dhcp;$script:restoredAddress=$saved.Addresses[0].IPAddress;if($script:restoreFail){throw 'fixture restore failed'};$script:address=$saved.Addresses[0].IPAddress;$script:gateway=$saved.Routes[0].NextHop;$script:dhcp=$saved.Dhcp}
Reset-Test
Check ((Resolve-RandomIpNic '自动有线').Name -eq 'Ethernet Space') 'automatic wired selector resolves the connected physical adapter'
$result=Invoke-RandomIPv4 'Ethernet Space' '192.0.2' 24 '192.0.2.254' ([int[]]@(1,2,3))
Check ($result -eq '192.0.2.1' -and $script:setCount -eq 1 -and $script:restored -eq 0) 'free candidate sets address once and does not roll back success'
Check ($script:LastClock.Tick -ge 10) 'success waits the full ten-second observation with accelerated fixture clock'
Reset-Test;$script:peerBusy='192.0.2.1';$result=Invoke-RandomIPv4 'Ethernet Space' '192.0.2' 24 '192.0.2.254' ([int[]]@(1,2))
Check ($result -eq '192.0.2.2' -and $script:setCount -eq 1 -and $script:probed.Count -eq 2) 'ARP evidence skips an occupied candidate before assignment'
Reset-Test;$script:dup='192.0.2.1';$result=Invoke-RandomIPv4 'Ethernet Space' '192.0.2' 24 '192.0.2.254' ([int[]]@(1,2))
Check ($result -eq '192.0.2.2' -and $script:setCount -eq 2 -and ($script:assigned -join ',') -eq '192.0.2.1,192.0.2.2') 'DAD duplicate triggers a different candidate without replacement'
Reset-Test;$script:otherIp='192.0.2.1';$result=Invoke-RandomIPv4 'Ethernet Space' '192.0.2' 24 '192.0.2.254' ([int[]]@(1,2))
Check ($result -eq '192.0.2.2' -and $script:setCount -eq 1) 'other local interfaces are excluded'
foreach($bad in @('dad','down','multiple','readFail')) {
 Reset-Test;switch($bad){'dad'{$script:dad=0};'down'{$script:up='Disconnected'};'multiple'{$script:multiple=$true};'readFail'{$script:readFail=$true}}
 $failed=$false;try{[void](Invoke-RandomIPv4 'Ethernet Space' '192.0.2' 24 '192.0.2.254' ([int[]]@(1,2)))}catch{$failed=$true}
 Check ($failed -and $script:setCount -eq 0 -and $script:itAbortWorkflow) ('invalid initial state fails before modification: '+$bad)
}
Reset-Test;$script:dup='192.0.2.1';$failed=$false
try{[void](Invoke-RandomIPv4 'Ethernet Space' '192.0.2' 24 '192.0.2.254' ([int[]]@(1)))}catch{$failed=$true}
Check ($failed -and $script:restored -eq 1 -and $script:restoredDhcp -eq 'Enabled' -and $script:restoredAddress -eq '10.0.0.10') 'exhausted pool restores original mode and records terminal failure'
Reset-Test;$script:routeBad=$true;$failed=$false
try{[void](Invoke-RandomIPv4 'Ethernet Space' '192.0.2' 24 '192.0.2.254' ([int[]]@(1)))}catch{$failed=$true}
Check ($failed -and $script:restored -eq 1) 'gateway verification failure cannot be called success'
Reset-Test;$script:setFail=$true;$script:restoreFail=$true;$message=''
try{[void](Invoke-RandomIPv4 'Ethernet Space' '192.0.2' 24 '192.0.2.254' ([int[]]@(1)))}catch{$message=$_.Exception.Message}
Check ($message.Contains('恢复原配置也失败') -and $script:restored -eq 1) 'partial set plus restore failure is reported honestly'
Reset-Test
& $script:NativeRestore ([pscustomobject]@{Name='Ethernet Space';Index=7;Dhcp='Enabled';Addresses=@();Routes=@()}) '192.0.2.2' '192.0.2.254'
Check ($script:NativeArgs -contains 'name=Ethernet Space' -and $script:NativeArgs -contains 'source=dhcp') 'DHCP rollback targets exact original interface'
".Replace("__HELPER__",helper.Replace("[Diagnostics.Stopwatch]::StartNew()","(New-TestClock)"));
        foreach(var engine in new[]{BatInputEngine.SendInput,BatInputEngine.Legacy})foreach(bool stop in new[]{false,true}){
            var modeSteps=new List<AutoStep>{S("重试开始","2","1"),S("随机设置 IPv4","自动有线","192.0.2","192.0.2.254|"+(stop?"结束脚本":"继续流程")),S("重试结束"),S("等待毫秒","1")};
            string modeScript=AutomationService.MakeScript(modeSteps,engine,0,false).Replace("Assert-Admin"+Environment.NewLine,"# Mocked administrator check"+Environment.NewLine).Replace(helper,"function Invoke-RandomIPv4 { $script:randomCalls++; return '192.0.2.2' }");
            fixture+="$script:randomCalls=0;$script:followingCalls=0\nfunction Start-Sleep {param($Milliseconds,$Seconds) $script:followingCalls++}\n& ([scriptblock]::Create('"+modeScript.Replace("'","''")+"'))\nCheck ($script:randomCalls -eq 1 -and $script:followingCalls -eq "+(stop?"0":"1")+") 'Success mode executes correct subsequent flow through retry: "+engine+" / "+stop+"'\n";
        }
        int nativeStart=helper.IndexOf("using System;",StringComparison.Ordinal),nativeEnd=helper.IndexOf("'@",nativeStart,StringComparison.Ordinal);string native=helper.Substring(nativeStart,nativeEnd-nativeStart).Replace("[DllImport(\"iphlpapi.dll\", ExactSpelling=true)] private static extern uint SendARP(uint dst,uint src,[Out] byte[] mac,ref uint len);","private static uint SendARP(uint dst,uint src,byte[] mac,ref uint len){for(int i=0;i<6;i++)mac[i]=(byte)(i+1);len=6;return 0;}");
        if(native.Contains("DllImport"))throw new Exception("Native ARP fixture must remove OS calls before execution");
        fixture+="Add-Type -TypeDefinition '"+native.Replace("'","''")+"'\nCheck ([ITRandomIPv4Arp]::Probe('192.0.2.1','192.0.2.2') -eq '010203040506') 'compiled ARP task bridge returns normalized evidence with a managed native stub'\nWrite-Output 'PASS random IPv4 fixture'\n";File.WriteAllText(Path.Combine(folder,"random-ip-fixture.ps1"),fixture,new System.Text.UTF8Encoding(true));
    }
    static void RandomWait()
    {
        Check(AutomationService.RandomWaitBounds(" 1 ","20").SequenceEqual(new[]{1,20})&&AutomationService.RandomWaitBounds("0","0").SequenceEqual(new[]{0,0})&&AutomationService.RandomWaitBounds("3600","3600")[1]==3600,"Random wait accepts inclusive integer-second bounds including equal and zero bounds");
        foreach(var pair in new[]{new[]{"","20"},new[]{"1",""},new[]{"20","1"},new[]{"-1","20"},new[]{"1","3601"},new[]{"1.5","20"},new[]{"1","20; exit"},new[]{"1","2147483647"}})Reject(()=>AutomationService.RandomWaitBounds(pair[0],pair[1]),"Invalid random-wait bounds rejected: "+pair[0]+" / "+pair[1]);
        var steps=new List<AutoStep>{S("随机等待","1","20"),S("等待毫秒","500")};string script="";
        foreach(var engine in new[]{BatInputEngine.SendInput,BatInputEngine.Legacy}){script=AutomationService.MakeScript(steps,engine,0,false);Check(script.Contains("$itRandomWaitSeconds0 = Get-Random -Minimum 1 -Maximum 21")&&script.Contains("Start-Sleep -Seconds $itRandomWaitSeconds0")&&script.Contains("Start-Sleep -Milliseconds 500")&&!script.Contains("-SetSeed"),"Random wait samples at runtime with inclusive seconds and preserves fixed milliseconds: "+engine);}
        string retry=AutomationService.MakeScript(new List<AutoStep>{S("重试开始","2","1"),S("随机等待","1","20"),S("重试结束")},BatInputEngine.SendInput,0,false);Check(retry.IndexOf("Get-Random",StringComparison.Ordinal)>retry.IndexOf("for($itRetry",StringComparison.Ordinal)&&retry.IndexOf("Get-Random",StringComparison.Ordinal)<retry.IndexOf("} catch { if($script:itAbortWorkflow)",StringComparison.Ordinal),"Random wait is sampled inside retry execution rather than outside the loop");
        string two=AutomationService.MakeScript(new List<AutoStep>{S("随机等待","0","0"),S("随机等待","3600","3600")},BatInputEngine.SendInput,0,false);Check(two.Contains("$itRandomWaitSeconds0 = Get-Random -Minimum 0 -Maximum 1")&&two.Contains("$itRandomWaitSeconds1 = Get-Random -Minimum 3600 -Maximum 3601"),"Each random wait has its own runtime draw and equal bounds stay valid");
        string path=Path.GetTempFileName();try{AutomationService.Save(path,steps);var loaded=AutomationService.Load(path);Check(loaded[0].Type=="随机等待"&&loaded[0].A=="1"&&loaded[0].B=="20"&&loaded[1].Type=="等待毫秒","Workflow preserves random range and legacy fixed waits without a format change");string before=File.ReadAllText(path);Reject(()=>AutomationService.Save(path,new List<AutoStep>{S("随机等待","20","1")}),"Invalid random range cannot be saved");Check(File.ReadAllText(path)==before,"Rejected random-wait save leaves the existing workflow intact");}finally{File.Delete(path);}
        string snippet=String.Join("\n",script.Replace("\r","").Split('\n').Where(line=>line.StartsWith("$itRandomWaitSeconds",StringComparison.Ordinal)||line.StartsWith("Start-Sleep -Seconds $itRandomWaitSeconds",StringComparison.Ordinal)));
        string fixture=@"$ErrorActionPreference='Stop'
$script:draws=0
$script:waits=New-Object 'Collections.Generic.List[int]'
function Get-Random { param([int]$Minimum,[int]$Maximum) if($Minimum -ne 1 -or $Maximum -ne 21){throw 'wrong inclusive bounds'};$script:draws++;if($script:draws % 2 -eq 1){return $Minimum};return ($Maximum-1) }
function Start-Sleep { param([int]$Seconds) $script:waits.Add($Seconds) }
$step={
"+snippet+@"
}
1..4 | ForEach-Object { & $step }
if($script:draws -ne 4 -or ($script:waits -join ',') -ne '1,20,1,20'){throw 'runtime resampling or bound propagation failed'}
$real=@(1..200 | ForEach-Object { Microsoft.PowerShell.Utility\Get-Random -Minimum 1 -Maximum 21 })
if(@($real | Where-Object {$_ -lt 1 -or $_ -gt 20}).Count -ne 0){throw 'real draws escaped bounds'}
Write-Output 'PASS random wait fixture'
";
        string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"generated-tests");Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"random-wait-fixture.ps1"),fixture,new System.Text.UTF8Encoding(true));
    }
    static void ConditionBlocks()
    {
        var yes=new List<AutoStep>{S("等待毫秒","1000"),S("输入文字","中文; 'quoted'")};var no=new List<AutoStep>{S("等待毫秒","2000")};
        foreach(var condition in new[]{S("如果网络可达","192.0.2.1"),S("如果文件存在",@"D:\test.txt"),S("如果系统版本","22000","26100")}){var block=AutomationBlockService.Compose(condition,yes,no,true);Check(block.Count==6&&block[3].Type=="否则"&&block[5].Type=="结束条件","Two branch actions compose a closed workflow: "+condition.Type);}
        var inner=AutomationBlockService.Compose(S("如果文件存在",@"D:\a.txt"),yes,new List<AutoStep>(),false);var outer=AutomationBlockService.Compose(S("如果网络可达","192.0.2.1"),inner,no,true);
        var range=AutomationBlockService.Range(outer,0);Check(range.Count==outer.Count&&range.ElseIndex==inner.Count+1,"Nested branch split uses only the matching outer else");
        var child=AutomationBlockService.Range(outer,1);Check(child.Count==inner.Count&&child.ElseIndex==-1,"Legacy nested condition without else is preserved as a whole block");
        Check(AutomationBlockService.Range(outer,range.ElseIndex).Start==0&&AutomationBlockService.Range(outer,outer.Count-1).Start==0,"Else and end select their own matching condition header");
        var copy=AutomationBlockService.CopyRange(outer,0,outer.Count);copy[2].A="changed";Check(outer[2].A=="1000"&&!Object.ReferenceEquals(copy[0],outer[0]),"Editing or copying a branch draft cannot mutate the original workflow");
        Reject(()=>AutomationBlockService.Compose(S("如果网络可达","192.0.2.1"),new List<AutoStep>{S("否则")},no,true),"A branch cannot inject an unmatched else into its parent");
        Reject(()=>AutomationBlockService.Compose(S("如果网络可达","192.0.2.1"),new List<AutoStep>{S("如果文件存在","x")},no,true),"An unclosed nested condition cannot escape a branch");
        Reject(()=>AutomationBlockService.Compose(S("如果系统版本","26100","22000"),yes,no,true),"Condition editor rejects reversed system build bounds before save");
        Reject(()=>AutomationBlockService.Compose(S("如果网络可达","192.0.2.1,192.0.2.2"),yes,no,true),"Network condition accepts one host rather than a list");
        var moved=new List<AutoStep>{S("等待毫秒","9")};moved.AddRange(outer);int destination=AutomationBlockService.Move(moved,1,0);AutomationService.ValidateFlow(moved);Check(destination==0&&moved.Last().A=="9"&&moved.Count==outer.Count+1,"Drag moves a complete nested condition without separating delimiters");
        string before=String.Join("|",moved.Select(x=>x.ToString()));AutomationBlockService.Move(moved,0,2);Check(before==String.Join("|",moved.Select(x=>x.ToString())),"Dropping a condition inside its own range leaves it unchanged");
        var retry=new List<AutoStep>{S("重试开始","2","1"),S("等待毫秒","10"),S("重试结束")};Check(AutomationBlockService.Range(retry,2).Start==0&&AutomationBlockService.Range(retry,2).Count==3,"Retry end also resolves to its complete original retry block");
        Reject(()=>AutomationBlockService.Range(new List<AutoStep>{S("如果网络可达","192.0.2.1")},0),"Incomplete legacy condition cannot silently consume following workflow rows");
        string path=Path.Combine(Path.GetTempPath(),"branch-test-"+Guid.NewGuid()+".txt");try{AutomationService.Save(path,outer);var loaded=AutomationService.Load(path);Check(loaded.Select(x=>x.ToString()).SequenceEqual(outer.Select(x=>x.ToString())),"Generated branch blocks round-trip using the existing workflow format");}finally{if(File.Exists(path))File.Delete(path);}
    }
    static void MouseBridge()
    {
        string bridge=(string)typeof(AutomationService).GetField("CursorWinApiBridge",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
        var move=new List<AutoStep>{S("移动鼠标","1560","-240")};
        Check(new[]{BatInputEngine.SendInput,BatInputEngine.Legacy}.All(e=>AutomationService.MakeScript(move,e,0,false).Contains(bridge)),"Both BAT engines embed the same physical-coordinate replay bridge");
        string fixture=bridge.Replace("public static class ITAutomationCursor {","public static class ITAutomationCursor { public static bool Context=true,Physical=true,Readable=true,Clip=false; public static int X,Y,LogicalCalls,Restores;");
        var native=new Dictionary<string,string>{
            {"SetPhysicalCursorPos","private static bool SetPhysicalCursorPos(int x,int y) { if(!Physical)return false;X=Clip?x+100:x;Y=y;return true; }"},
            {"SetCursorPos","private static bool SetCursorPos(int x,int y) { LogicalCalls++;X=x;Y=y;return true; }"},
            {"GetPhysicalCursorPos","private static bool GetPhysicalCursorPos(out POINT p) { p=new POINT{X=X,Y=Y};return Readable; }"},
            {"SetThreadDpiAwarenessContext","private static IntPtr SetThreadDpiAwarenessContext(IntPtr c) { if(c.ToInt64()==1){Restores++;return new IntPtr(-4);}return Context?new IntPtr(1):IntPtr.Zero; }"},
            {"GetSystemMetrics","private static int GetSystemMetrics(int index) { return 1920; }"},
            {"SendInput","private static uint SendInput(uint count,INPUT[] items,int size) { return 0; }"}
        };
        fixture=System.Text.RegularExpressions.Regex.Replace(fixture,@"\[DllImport\([^\r\n]*\)\] private static extern [^\r\n]+;",delegate(System.Text.RegularExpressions.Match match){string name=System.Text.RegularExpressions.Regex.Match(match.Value,@"extern \w+ (\w+)\(").Groups[1].Value;return native[name];});
        if(fixture.Contains("DllImport("))throw new Exception("Unreplaced native call in mouse fixture");
        using(var provider=System.CodeDom.Compiler.CodeDomProvider.CreateProvider("CSharp"))
        {
            var options=new System.CodeDom.Compiler.CompilerParameters(new[]{"System.dll"}){GenerateInMemory=true};var compiled=provider.CompileAssemblyFromSource(options,fixture);
            if(compiled.Errors.HasErrors)throw new Exception(String.Join(";",compiled.Errors.Cast<System.CodeDom.Compiler.CompilerError>().Select(e=>e.ToString())));
            Type type=compiled.CompiledAssembly.GetType("ITAutomationCursor");Action<string,object> set=delegate(string name,object value){type.GetField(name).SetValue(null,value);};Func<string,int> get=delegate(string name){return (int)type.GetField(name).GetValue(null);};
            Func<bool> run=delegate{try{type.GetMethod("MoveTo").Invoke(null,new object[]{1560,-240});return true;}catch(TargetInvocationException ex){if(!(ex.InnerException is InvalidOperationException))throw;return false;}};
            Check(run()&&get("X")==1560&&get("Y")==-240&&get("Restores")==1,"Physical replay preserves negative pixels and restores the previous DPI context");
            set("Clip",true);Check(!run()&&get("Restores")==2,"A successful native call with a clipped position is reported as failure and restores DPI context");
            set("Clip",false);set("Physical",false);set("Context",false);Check(!run()&&get("LogicalCalls")==0,"Replay cannot silently use logical coordinates when physical positioning and per-monitor context are unavailable");
            set("Context",true);Check(run()&&get("LogicalCalls")==1&&get("X")==1560&&get("Y")==-240&&get("Restores")==3,"Cursor fallback runs only under per-monitor DPI context and verifies the physical point");
            set("Physical",true);set("Readable",false);Check(!run()&&get("Restores")==4,"Unverifiable physical position cannot be reported as successful replay");
        }
    }
    static void Portable()
    {
        var refs=typeof(MainForm).Assembly.GetReferencedAssemblies();Check(!refs.Any(r=>r.Name.Contains("Toolkit")||r.Name=="System.Management"),"Standalone executable references framework assemblies without toolkit or hardware dependencies");
        string dir=Path.Combine(Path.GetTempPath(),"bat-options-"+Guid.NewGuid());Directory.CreateDirectory(dir);
        try{
            var steps=new List<AutoStep>{S("等待毫秒","1")};var settings=new AutoWorkflowSettings{Engine=BatInputEngine.Legacy,HideConsole=true,DeleteOnSuccess=false,RunAsAdministrator=false,DiagnosticMode=false,StartupDelaySeconds=0};
            string file=Path.Combine(dir,"settings.workflow.txt");AutomationService.Save(file,steps,settings);AutoWorkflowSettings loaded;AutomationService.Load(file,out loaded);Check(loaded.Engine==settings.Engine&&loaded.HideConsole&&!loaded.DeleteOnSuccess&&!loaded.RunAsAdministrator&&!loaded.DiagnosticMode&&loaded.StartupDelaySeconds==0,"Standalone preserves all existing workflow generation settings");
            foreach(var engine in new[]{BatInputEngine.SendInput,BatInputEngine.Legacy}){settings.Engine=engine;string bat=Path.Combine(dir,engine+".bat");AutomationService.ExportBat(bat,steps,settings);Check(new FileInfo(bat).Length>1000&&File.ReadAllText(bat).Contains("powershell"),"Standalone exports a self-contained BAT without running it: "+engine);}
            string example=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"example.workflow.txt");AutomationService.Save(example,AutomationBlockService.Compose(S("如果文件存在",@"C:\Windows\explorer.exe"),new List<AutoStep>{S("等待毫秒","1000")},new List<AutoStep>{S("等待毫秒","500")},true),settings);
        }finally{Directory.Delete(dir,true);}
    }
    static int Main(){try{Flow();LauncherReview();TimeoutChecks();RandomIpv4();RandomWait();ConditionBlocks();MouseBridge();Portable();Console.WriteLine("STANDALONE TOTAL PASS "+passed);return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
