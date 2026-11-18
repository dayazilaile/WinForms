using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ITDesktopToolkit;
class FeatureTests
{
    static int passed;
    static void Check(bool value,string name){if(!value)throw new Exception(name);passed++;Console.WriteLine("PASS "+name);}
    static void Reject(Action work,string name){try{work();}catch(ArgumentException){Check(true,name);return;}throw new Exception("Expected rejection: "+name);}
    static AutoStep S(string t,string a="",string b="",string c=""){return new AutoStep{Type=t,A=a,B=b,C=c};}
    static void Reports()
    {
        var a=new Dictionary<string,string>{{"网络/名称","中文\t含换行\n值"},{"服务", "Running"}};
        var b=InspectionService.Parse(InspectionService.Serialize(a));Check(b["网络/名称"]==a["网络/名称"],"Unicode report round trip including separators");
        b["服务"]="Stopped";b.Add("新字段","");string diff=InspectionService.Compare(a,b);Check(diff.Contains("差异项：2")&&diff.Contains("Running")&&diff.Contains("Stopped"),"Compare changed and absent fields");
        Check(InspectionService.Compare(a,a).Contains("差异项：0"),"Identical report");
        bool malformed=false;try{InspectionService.Parse("not a report");}catch(FormatException){malformed=true;}Check(malformed,"Invalid report rejected");
        var apps=new Dictionary<string,string>{{"Example App [Registry64]","2.0"},{"Other","custom"}};
        string result=InspectionService.CheckRequirements(apps,"Example\t2.0.0\nMissing\nOther\t1.0\nExample\t3.0");
        Check(result.Contains("[符合]")&&result.Contains("[缺失]")&&result.Contains("[版本需人工核对]")&&result.Contains("[版本偏低]"),"Software presence, padded versions, unknown and older versions");
    }
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
    static void Usb()
    {
        string dir=Path.Combine(Path.GetTempPath(),"toolkit-usb-"+Guid.NewGuid());Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir,"notes.txt"),"normal document");
            File.WriteAllText(Path.Combine(dir,"report.pdf.exe"),"inert text fixture, not executable");
            File.WriteAllText(Path.Combine(dir,"autorun.inf"),"[autorun]\nopen=never-run.exe");
            var before=Directory.GetFiles(dir).ToDictionary(p=>p,p=>File.ReadAllBytes(p));
            var scan=(UsbScanResult)typeof(UsbProtectionService).GetMethod("ScanFolder",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{dir,CancellationToken.None});
            Check(scan.Files==3&&scan.Findings.Count==2&&scan.Findings.Any(x=>x.Detail.Contains("伪装")),"USB scan flags double extension and autorun, leaves normal document unflagged");
            Check(scan.Findings.All(x=>x.Hash.Length==64),"SHA256 evidence");
            Check(before.All(x=>File.ReadAllBytes(x.Key).SequenceEqual(x.Value))&&Directory.GetFiles(dir).Length==3,"USB scan does not modify files");
            var c=new CancellationTokenSource();c.Cancel();var stopped=(UsbScanResult)typeof(UsbProtectionService).GetMethod("ScanFolder",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{dir,c.Token});Check(stopped.Cancelled&&stopped.Files==0,"USB cancellation");c.Dispose();
            Reject(()=>UsbProtectionService.ValidateDrive(dir),"USB public API rejects non-root path");
        }
        finally{Directory.Delete(dir,true);}
    }
    static async Task Network()
    {
        Reject(()=>NetworkMonitorService.Targets("https://example.com/path"),"Network URL rejected");
        Check(NetworkMonitorService.Targets("127.0.0.1,127.0.0.1 localhost").Length==2,"Network target deduplication");
        var samples=new List<NetworkSample>{new NetworkSample{Host="a",Status="Success",Milliseconds=10},new NetworkSample{Host="a",Status="Success",Milliseconds=30},new NetworkSample{Host="a",Status="TimedOut",Milliseconds=-1}};
        string summary=NetworkMonitorService.Summary(samples);Check(summary.Contains("33.3%")&&summary.Contains("10/20.0/30")&&summary.Contains("20.0 ms"),"Loss, RTT and jitter calculations");
        Check(NetworkMonitorService.Export(samples).Split('\n').Length==5,"CSV includes each observation");
        var cancel=new CancellationTokenSource();cancel.Cancel();int received=0;await NetworkMonitorService.Run(new[]{"127.0.0.1"},5,1,cancel.Token,s=>received++);Check(received==0,"Cancelled monitor sends no new probes");cancel.Dispose();
        var actual=new List<NetworkSample>();await NetworkMonitorService.Run(new[]{"127.0.0.1"},2,1,CancellationToken.None,s=>actual.Add(s));
        Check(actual.Count==2&&actual.All(s=>s.Status!="未知"),"Live monitor records each probe result including platform errors");
        Console.WriteLine("LOOPBACK ICMP statuses: "+String.Join(",",actual.Select(s=>s.Status).ToArray()));
        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
        try{int port=((IPEndPoint)listener.LocalEndpoint).Port;Check((await PrinterDiagnosticsService.TestPort("127.0.0.1",port)).Contains("TCP 可连接"),"Printer TCP diagnostic connects to loopback listener without print data");}finally{listener.Stop();}
    }
    static int Main(){try{MouseBridge();Reports();Flow();Usb();Network().GetAwaiter().GetResult();Console.WriteLine("TOTAL PASS "+passed);return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
