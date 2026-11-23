using System;
using System.Windows.Forms;
namespace WinForms
{
    internal static class Program
    {
        [STAThread]private static void Main(){string problem=WindowsCompatibility.CurrentStartupProblem();if(problem.Length>0){MessageBox.Show(problem,"运行环境提示");return;}Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new MainForm());}
    }
}
