using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WinForms
{
    public sealed class StudentPowerDiagnostic
    {
        public readonly DateTimeOffset ObservedAt=DateTimeOffset.Now;
        public StudentMachine Machine;public string Mac="未核对",Rpc="未检查",Smb="未检查",Note="";
        public string Summary {get{return "135："+Rpc+"；445："+Smb+"；"+Note;}}
        public string Details {get{return "时间："+ObservedAt.ToString("o")+"\r\n目标机房："+Machine.Room+"\r\n清单名称："+Machine.Name+"\r\n目标 IP："+Machine.IPv4+"\r\n保存 MAC："+Machine.Mac+"\r\n解析 MAC："+Mac+"\r\nTCP 135："+Rpc+"\r\nTCP 445："+Smb+"\r\n说明："+Note+"\r\n\r\n只读检查没有发送关机/重启，也没有验证远程账户权限。端口可连不等于 RPC 接口已可用。";}}
    }
    public static class StudentPowerDiagnostics
    {
        public static Task Inspect(StudentPowerPlan plan,WakeNetwork network,CancellationToken token,Action<StudentPowerDiagnostic> receive)
        {return InspectCore(plan,network,token,StudentPowerService.ResolveMac,Connect,receive);}
        internal static async Task InspectCore(StudentPowerPlan plan,WakeNetwork network,CancellationToken token,Func<string,WakeNetwork,CancellationToken,Task<string>> arp,Func<string,int,CancellationToken,Task<string>> connect,Action<StudentPowerDiagnostic> receive)
        {
            token.ThrowIfCancellationRequested();foreach(var machine in plan.Targets){token.ThrowIfCancellationRequested();var result=new StudentPowerDiagnostic{Machine=machine};
                try{result.Mac=StudentWakeService.NormalizeMac(await arp(machine.IPv4,network,token));token.ThrowIfCancellationRequested();
                    if(result.Mac!=machine.Mac)result.Note="MAC 与记录不符，先重新扫描核对；未连接该地址端口。";
                    else{result.Rpc=await connect(machine.IPv4,135,token);token.ThrowIfCancellationRequested();result.Smb=await connect(machine.IPv4,445,token);token.ThrowIfCancellationRequested();result.Note="MAC 相符；RPC 接口及远程账户权限仍未验证。错误53请重点检查 SMB/命名管道通道。";}
                }catch(OperationCanceledException){throw;}catch(Exception ex){result.Note="检查未完成："+ex.Message;}
                token.ThrowIfCancellationRequested();OperationAudit.Record("远程电源只读诊断","已返回，未验证权限",result.Details);receive(result);
            }
        }
        internal static async Task<string> Connect(string ip,int port,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();using(var client=new TcpClient()){
                try{var pending=client.ConnectAsync(ip,port);ObserveFault(pending);
                    var done=await Task.WhenAny(pending,Task.Delay(1500,token));token.ThrowIfCancellationRequested();if(done!=pending)return "超时/未响应";await pending;return "可连接（仅TCP）";
                }catch(OperationCanceledException){throw;}catch(SocketException ex){return "不可连接（"+ex.SocketErrorCode+"）";}catch(Exception ex){return "未完成（"+ex.GetType().Name+"）";}
            }
        }
        private static void ObserveFault(Task task){task.ContinueWith(delegate(Task failed){GC.KeepAlive(failed.Exception);},TaskContinuationOptions.OnlyOnFaulted|TaskContinuationOptions.ExecuteSynchronously);}
        public static string LocalCheckScript()
        {
            // Plain CMD only. Never changes services, firewall, policy, credentials or power state.
            return @"@echo off
setlocal EnableExtensions DisableDelayedExpansion
set ""SP_SYSTEM=%SystemRoot%\System32""
if exist ""%SystemRoot%\Sysnative\sc.exe"" set ""SP_SYSTEM=%SystemRoot%\Sysnative""
set ""SP_DIR=%~dp0Logs""
if not exist ""%SP_DIR%"" mkdir ""%SP_DIR%""
if not exist ""%SP_DIR%"" (
 echo [ERROR] Script-directory Logs is not writable. No fallback is used.
 pause
 exit /b 1
)
set ""SP_LOG=%SP_DIR%\RemotePower-%COMPUTERNAME%.txt""
set ""SP_RIGHTS=%SP_DIR%\RemotePower-%COMPUTERNAME%-rights.inf""
>>""%SP_LOG%"" echo ===== Read-only remote power check: %DATE% %TIME% =====
if errorlevel 1 (
 echo [ERROR] Could not write the local diagnostic log. No settings changed.
 pause
 exit /b 1
)
ver >>""%SP_LOG%"" 2>&1
""%SP_SYSTEM%\hostname.exe"" >>""%SP_LOG%"" 2>&1
""%SP_SYSTEM%\whoami.exe"" /user >>""%SP_LOG%"" 2>&1
""%SP_SYSTEM%\whoami.exe"" /priv >>""%SP_LOG%"" 2>&1
""%SP_SYSTEM%\ipconfig.exe"" /all >>""%SP_LOG%"" 2>&1
""%SP_SYSTEM%\tzutil.exe"" /g >>""%SP_LOG%"" 2>&1
""%SP_SYSTEM%\sc.exe"" query RpcSs >>""%SP_LOG%"" 2>&1
""%SP_SYSTEM%\sc.exe"" query RpcEptMapper >>""%SP_LOG%"" 2>&1
""%SP_SYSTEM%\sc.exe"" query LanmanWorkstation >>""%SP_LOG%"" 2>&1
""%SP_SYSTEM%\sc.exe"" query LanmanServer >>""%SP_LOG%"" 2>&1
""%SP_SYSTEM%\sc.exe"" query RemoteRegistry >>""%SP_LOG%"" 2>&1
""%SP_SYSTEM%\netstat.exe"" -ano -p tcp | ""%SP_SYSTEM%\findstr.exe"" /R /C:"":135 "" /C:"":445 "" >>""%SP_LOG%"" 2>&1
""%SP_SYSTEM%\netsh.exe"" advfirewall show currentprofile >>""%SP_LOG%"" 2>&1
>>""%SP_LOG%"" echo Local policy export below needs local administrator rights. It does not prove the teacher's remote rights.
""%SP_SYSTEM%\secedit.exe"" /export /cfg ""%SP_RIGHTS%"" /areas USER_RIGHTS /log ""%SP_DIR%\RemotePower-%COMPUTERNAME%-secedit.log"" /quiet >>""%SP_LOG%"" 2>&1
set ""SP_CODE=%ERRORLEVEL%""
>>""%SP_LOG%"" echo User-rights export exit code: %SP_CODE%
if not ""%SP_CODE%""==""0"" goto rights_unread
""%SP_SYSTEM%\findstr.exe"" /I /B /C:""SeRemoteShutdownPrivilege"" ""%SP_RIGHTS%"" >>""%SP_LOG%"" 2>&1
:rights_unread
>>""%SP_LOG%"" echo End of read-only collection. Missing/denied fields remain unknown. No services or security rules changed.
echo Read-only collection finished. No shutdown/restart was sent and no configuration was repaired.
echo Results: ""%SP_LOG%""
echo Use an authorized local administrator for a complete rights export. Keep this folder's Logs.
pause
exit /b 0
".Replace("\r\n","\n").Replace("\n","\r\n");
        }
        public static void ExportLocalCheck(string path)
        {
            string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.WriteAllText(temp,LocalCheckScript(),Encoding.ASCII);if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}
        }
    }
}
