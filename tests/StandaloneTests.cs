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
    static void RandomWait()
    {
        Check(AutomationService.RandomWaitBounds(" 1 ","20").SequenceEqual(new[]{1,20})&&AutomationService.RandomWaitBounds("0","0").SequenceEqual(new[]{0,0})&&AutomationService.RandomWaitBounds("3600","3600")[1]==3600,"Random wait accepts inclusive integer-second bounds including equal and zero bounds");
        foreach(var pair in new[]{new[]{"","20"},new[]{"1",""},new[]{"20","1"},new[]{"-1","20"},new[]{"1","3601"},new[]{"1.5","20"},new[]{"1","20; exit"},new[]{"1","2147483647"}})Reject(()=>AutomationService.RandomWaitBounds(pair[0],pair[1]),"Invalid random-wait bounds rejected: "+pair[0]+" / "+pair[1]);
        var steps=new List<AutoStep>{S("随机等待","1","20"),S("等待毫秒","500")};string script="";
        foreach(var engine in new[]{BatInputEngine.SendInput,BatInputEngine.Legacy}){script=AutomationService.MakeScript(steps,engine,0,false);Check(script.Contains("$itRandomWaitSeconds0 = Get-Random -Minimum 1 -Maximum 21")&&script.Contains("Start-Sleep -Seconds $itRandomWaitSeconds0")&&script.Contains("Start-Sleep -Milliseconds 500")&&!script.Contains("-SetSeed"),"Random wait samples at runtime with inclusive seconds and preserves fixed milliseconds: "+engine);}
        string retry=AutomationService.MakeScript(new List<AutoStep>{S("重试开始","2","1"),S("随机等待","1","20"),S("重试结束")},BatInputEngine.SendInput,0,false);Check(retry.IndexOf("Get-Random",StringComparison.Ordinal)>retry.IndexOf("for($itRetry",StringComparison.Ordinal)&&retry.IndexOf("Get-Random",StringComparison.Ordinal)<retry.IndexOf("} catch { if($itRetry",StringComparison.Ordinal),"Random wait is sampled inside retry execution rather than outside the loop");
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
    static int Main(){try{Flow();RandomWait();ConditionBlocks();MouseBridge();Portable();Console.WriteLine("STANDALONE TOTAL PASS "+passed);return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
