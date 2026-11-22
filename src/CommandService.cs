using System;
using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ITDesktopToolkit
{
    public static class CommandService
    {
        internal static event Action<string> ProcessStarting;
        internal static void NotifyProcess(string file){var handler=ProcessStarting;if(handler!=null)handler(file);}
        internal static ProcessStartInfo CreateCommand(string file,string args)
        {return new ProcessStartInfo(SystemCommandPath.Resolve(file),args){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};}

        public static bool IsAdministrator()
        {
            try
            {
                WindowsIdentity user = WindowsIdentity.GetCurrent();
                WindowsPrincipal p = new WindowsPrincipal(user);
                return p.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }
        public static bool RelaunchAsAdmin()
        {
            try
            {
                ProcessStartInfo i = new ProcessStartInfo(Application.ExecutablePath);
                i.UseShellExecute = true;
                i.Verb = "runas";
                NotifyProcess(i.FileName);Process.Start(i);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("无法以管理员运行：" + ex.Message, "权限提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }
        public static string Run(string fileName, string arguments, int timeoutMinutes)
        { return Run(fileName,arguments,timeoutMinutes,CancellationToken.None); }
        public static string Run(string fileName,string arguments,int timeoutMinutes,CancellationToken cancel)
        {
            if(timeoutMinutes<1||timeoutMinutes>1440)throw new ArgumentException("命令超时须为 1–1440 分钟。");
            cancel.ThrowIfCancellationRequested();
            StringBuilder stdout = new StringBuilder();
            var outputDone=new TaskCompletionSource<bool>();var errorDone=new TaskCompletionSource<bool>();
            object guard = new object();
            using (Process p = new Process())
            {
                p.StartInfo = CreateCommand(fileName, arguments);
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.RedirectStandardError = true;
                p.StartInfo.CreateNoWindow = true;
                p.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if(e.Data==null)outputDone.TrySetResult(true);else lock(guard){if(stdout.Length<250000)stdout.AppendLine(e.Data.Substring(0,Math.Min(e.Data.Length,250000-stdout.Length)));}
                };
                p.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if(e.Data==null)errorDone.TrySetResult(true);else lock(guard){if(stdout.Length<250000)stdout.AppendLine("[错误] "+e.Data.Substring(0,Math.Min(e.Data.Length,250000-stdout.Length)));}
                };
                try
                {
                    NotifyProcess(p.StartInfo.FileName);p.Start();
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    var clock=Stopwatch.StartNew();
                    while(!p.WaitForExit(100))
                    {
                        if(cancel.IsCancellationRequested){try{p.Kill();}catch{};throw new OperationCanceledException(cancel);}
                        if(clock.Elapsed.TotalMinutes>=timeoutMinutes){try{p.Kill();}catch{};lock(guard)return "操作超时，已尝试结束主进程。请检查是否仍有子进程运行。\r\n"+stdout;}
                    }
                    cancel.ThrowIfCancellationRequested();
                    // A child may inherit output handles. Bound stream draining instead of waiting forever.
                    var drain=Stopwatch.StartNew();
                    while(!outputDone.Task.IsCompleted||!errorDone.Task.IsCompleted)
                    {
                        cancel.ThrowIfCancellationRequested();if(drain.ElapsedMilliseconds>=1000)break;
                        cancel.WaitHandle.WaitOne(20);
                    }
                    lock(guard)return "命令："+fileName+" "+arguments+"\r\n返回代码："+p.ExitCode+"\r\n\r\n"+stdout+((outputDone.Task.IsCompleted&&errorDone.Task.IsCompleted)?"":"\r\n[输出未完整结束：可能有子进程持有输出句柄]");
                }
                catch(OperationCanceledException){try{if(!p.HasExited)p.Kill();}catch{};throw;}
                catch (Exception ex)
                {
                    try{if(!p.HasExited)p.Kill();}catch{}
                    lock(guard)return "命令无法执行：" + ex.Message + "\r\n" + stdout;
                }
            }
        }
        public static void OpenTool(string target, string args)
        {
            try
            {
                WindowsCompatibility.ManagementTarget(ref target,ref args,WindowsCompatibility.Legacy);
                target=SystemCommandPath.Resolve(target);
                if((target.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)||target.EndsWith(".msc",StringComparison.OrdinalIgnoreCase)||target.EndsWith(".cpl",StringComparison.OrdinalIgnoreCase))&&!System.IO.File.Exists(target))throw new System.IO.FileNotFoundException("当前系统未安装该管理组件，请先核对系统版本/服务器角色。",target);
                ProcessStartInfo p = new ProcessStartInfo(target);
                p.Arguments = args;
                p.UseShellExecute = true;
                NotifyProcess(p.FileName);Process.Start(p);
                Paths.Log("Opened tool: " + target + " " + args);
            }
            catch (Exception ex) { MessageBox.Show("无法打开：" + target + "\r\n" + ex.Message, "提示"); }
        }
    }
}
