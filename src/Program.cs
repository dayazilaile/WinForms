using System;
using System.Windows.Forms;

namespace WinForms
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            string problem=WindowsCompatibility.CurrentStartupProblem();
            if(problem.Length>0){MessageBox.Show(problem,"运行环境提示",MessageBoxButtons.OK,MessageBoxIcon.Information);return;}
            try{System.IO.Directory.CreateDirectory(Paths.UserDir);}catch(System.Exception ex){MessageBox.Show("软件目录不可写，请把完整文件夹放到可写的 U 盘或目录；不会改用电脑用户目录。\r\n"+ex.Message,"便携目录提示");return;}
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
