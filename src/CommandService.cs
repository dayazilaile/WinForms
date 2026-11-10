using System;
using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;

namespace ITDesktopToolkit
{
    public static class CommandService
    {
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
                Process.Start(i);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("无法以管理员运行：" + ex.Message, "权限提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }
        public static string Run(string fileName, string arguments, int timeoutMinutes)
        {
            StringBuilder stdout = new StringBuilder();
            object guard = new object();
            using (Process p = new Process())
            {
                p.StartInfo = new ProcessStartInfo(fileName, arguments);
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.RedirectStandardError = true;
                p.StartInfo.CreateNoWindow = true;
                p.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if (e.Data != null) lock (guard) { if (stdout.Length < 250000) stdout.AppendLine(e.Data); }
                };
                p.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if (e.Data != null) lock (guard) { if (stdout.Length < 250000) stdout.AppendLine("[错误] " + e.Data); }
                };
                try
                {
                    p.Start();
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    if (!p.WaitForExit(timeoutMinutes * 60 * 1000))
                    {
                        try { p.Kill(); } catch { }
                        return "操作超时，已尝试结束进程。请检查操作是否仍在后台运行。\r\n" + stdout;
                    }
                    p.WaitForExit();
                    return "命令：" + fileName + " " + arguments + "\r\n返回代码：" + p.ExitCode + "\r\n\r\n" + stdout.ToString();
                }
                catch (Exception ex)
                {
                    return "命令无法执行：" + ex.Message + "\r\n" + stdout;
                }
            }
        }
        public static void OpenTool(string target, string args)
        {
            try
            {
                ProcessStartInfo p = new ProcessStartInfo(target);
                p.Arguments = args;
                p.UseShellExecute = true;
                Process.Start(p);
                Paths.Log("Opened tool: " + target + " " + args);
            }
            catch (Exception ex) { MessageBox.Show("无法打开：" + target + "\r\n" + ex.Message, "提示"); }
        }
    }
}
