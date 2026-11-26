using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using WinForms;

class AutomationStopTests
{
    static int passed;
    static void Check(bool value,string name){if(!value)throw new Exception(name);passed++;Console.WriteLine("PASS "+name);}
    static void Reject(Action action,string name){try{action();}catch(ArgumentException){Check(true,name);return;}throw new Exception("Expected rejection: "+name);}
    static AutoStep Step(string type,string a="",string b="",string c=""){return new AutoStep{Type=type,A=a,B=b,C=c};}
    static int Main()
    {
        try{
            Check(AutomationService.Types.Contains("停止服务")&&AutomationService.Types.Contains("结束进程")&&AutomationService.Guide("结束进程").Contains("选择程序文件"),"Stop actions are discoverable and describe selecting EXE rather than entering PID");
            Check(AutomationService.ProcessNameFromFile(@"C:\Program Files\Demo\Demo App (2).EXE")=="Demo App (2).EXE"&&AutomationService.CheckEndProcessName("Demo App (2).EXE")=="Demo App (2)","EXE selection extracts only exact process basename including spaces/parentheses");
            Check(AutomationService.CheckStopServiceName(" MSSQL$SQLEXPRESS ")=="MSSQL$SQLEXPRESS"&&AutomationService.CheckProcessEndMode("")=="正常关闭"&&AutomationService.StopActionTimeout("",30)==30,"SQL service names and conservative defaults retained");
            foreach(string name in new[]{"","*","foo?",@"C:\Demo.exe","..","foo\nbar"})Reject(()=>AutomationService.CheckEndProcessName(name),"Invalid process target rejected: "+name);
            foreach(string name in new[]{"","*","RpcSs","WinDefend",@"server\service","foo\nbar"})Reject(()=>AutomationService.CheckStopServiceName(name),"Invalid/protected service rejected: "+name);
            foreach(string name in new[]{"lsass.exe","svchost","POWERSHELL.EXE","cmd.exe","MsMpEng.exe"})Reject(()=>AutomationService.CheckEndProcessName(name),"Critical/runner process rejected: "+name);
            Reject(()=>AutomationService.ProcessNameFromFile(@"D:\close.bat"),"BAT is not mistaken for its own process name");Reject(()=>AutomationService.CheckProcessEndMode("force anything"),"Unrecognized process ending mode rejected");
            foreach(string value in new[]{"0","121","bad"})Reject(()=>AutomationService.StopActionTimeout(value,15),"Invalid bounded stop timeout rejected: "+value);
            Reject(()=>AutomationService.ValidateFlow(new List<AutoStep>{Step("停止服务","DemoService","30","unexpected")}),"Unused service parameter cannot silently carry extra instructions");
            foreach(string type in new[]{"停止服务","结束进程"})Reject(()=>AutomationService.ValidateFlow(new List<AutoStep>{Step("重试开始"),Step(type,"Demo","",type=="结束进程"?"15":""),Step("重试结束")}),"Stop cannot repeatedly terminate restarted targets through automatic retry: "+type);
            string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"generated-stop-actions");Directory.CreateDirectory(folder);
            var steps=new List<AutoStep>{Step("停止服务","Vendor's service","30"),Step("结束进程","Demo & App $(1).exe","正常关闭","15")};
            foreach(var engine in new[]{BatInputEngine.SendInput,BatInputEngine.Legacy}){
                string script=AutomationService.MakeScript(steps,engine,0,false);Check(script.Contains("Invoke-ITStopService 'Vendor''s service' 30")&&script.Contains("Invoke-ITEndProcess 'Demo & App $(1)' '正常关闭' 15"),"Exact quoted names and selected modes emitted without command injection: "+engine);
                Check(!script.Contains("Stop-Service -Force")&&!script.Contains("Set-Service")&&!script.Contains("GetProcessById")&&!script.Contains("taskkill")&&script.Contains("$process.Handle"),"Stop helper preserves startup types/dependencies and uses held name-selected instances: "+engine);
                File.WriteAllText(Path.Combine(folder,engine+".ps1"),script,new System.Text.UTF8Encoding(true));string bat=Path.Combine(folder,engine+".bat");AutomationService.ExportBat(bat,steps,new AutoWorkflowSettings{Engine=engine,DiagnosticMode=false,DeleteOnSuccess=false,RunAsAdministrator=false,StartupDelaySeconds=0});Check(File.Exists(bat)&&new FileInfo(bat).Length>100,"Both engines export a standalone BAT containing stop workflow: "+engine);
            }
            Check(!AutomationService.MakeScript(new List<AutoStep>{Step("等待毫秒","1")},BatInputEngine.SendInput,0,false).Contains("function Invoke-ITStopService"),"Unrelated workflows do not include stop helpers");
            string workflow=Path.Combine(folder,"roundtrip.workflow.txt");AutomationService.Save(workflow,steps);var loaded=AutomationService.Load(workflow);Check(loaded.Count==2&&loaded[0].A==steps[0].A&&loaded[1].B==steps[1].B&&loaded[1].C=="15","New actions save/load names and modes without PIDs or selected machine paths");
            Check(AutomationService.Types.Contains("重命名文件")&&AutomationService.Types.Contains("重命名文件夹")&&AutomationService.CheckRenameName("新名称.txt")=="新名称.txt","File and folder rename actions accept a concrete new basename");
            foreach(string bad in new[]{"","..","CON.txt","LPT1"," name","name.","dir/name","name*"})Reject(()=>AutomationService.CheckRenameName(bad),"Invalid rename basename rejected: "+bad);
            foreach(string bad in new[]{"relative.txt",@"C:\",@"D:\\",@"\\server\share\",@"C:\file.txt:stream"})Reject(()=>AutomationService.CheckRenamePath(bad),"Invalid/root/ADS rename source rejected: "+bad);
            var renames=new List<AutoStep>{Step("重命名文件",@"D:\some[1].txt","new's name.txt"),Step("重命名文件夹",@"D:\old folder","new folder")};
            foreach(var engine in new[]{BatInputEngine.SendInput,BatInputEngine.Legacy}){string script=AutomationService.MakeScript(renames,engine,0,false);Check(script.Contains("Invoke-ITRenameItem 'D:")&&script.Contains("new''s name.txt"),"Rename script uses exact quoted paths/new names: "+engine);File.WriteAllText(Path.Combine(folder,"rename-"+engine+".ps1"),script,new System.Text.UTF8Encoding(true));}
            foreach(string type in new[]{"重命名文件","重命名文件夹"})Reject(()=>AutomationService.ValidateFlow(new List<AutoStep>{Step("重试开始"),Step(type,@"D:\old","new"),Step("重试结束")}),"Rename is not automatically repeated after target identity changes: "+type);
            string renameHelper=(string)typeof(AutomationService).GetField("RenameActionHelper",BindingFlags.Static|BindingFlags.NonPublic).GetRawConstantValue();File.WriteAllText(Path.Combine(folder,"rename-helper.ps1"),renameHelper,new System.Text.UTF8Encoding(true));
            string helper=(string)typeof(AutomationService).GetField("StopActionHelper",BindingFlags.Static|BindingFlags.NonPublic).GetRawConstantValue();File.WriteAllText(Path.Combine(folder,"helper.ps1"),helper,new System.Text.UTF8Encoding(true));
            Check(helper.Contains("$process.SessionId -ne $session")&&helper.Contains("$remaining -gt 0")&&helper.Contains("StopPending"),"Runtime scopes processes to current session and rechecks service/process outcomes");
            Console.WriteLine("AUTOMATION STOP TOTAL PASS "+passed);return 0;
        }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
}
