using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using System.Drawing;

namespace WinForms
{
    public sealed class WindowCatalogItem
    {
        public string Title, ProcessName;
        public bool Minimized;
        public override string ToString() { return Title + "    [" + ProcessName + "] " + (Minimized ? "（已最小化）" : ""); }
    }

    public static class WindowCatalog
    {
        private delegate bool EnumWindowProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowProc callback, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetWindowTextLength(IntPtr hWnd);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder title, int maxCount);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        public static List<WindowCatalogItem> Read()
        {
            List<WindowCatalogItem> result = new List<WindowCatalogItem>();
            EnumWindows(delegate(IntPtr hWnd, IntPtr unused) {
                if(!IsWindowVisible(hWnd))return true;
                int len=GetWindowTextLength(hWnd);
                if(len<1 || len>16000)return true;
                StringBuilder b=new StringBuilder(len+2);
                GetWindowText(hWnd,b,b.Capacity);
                string title=b.ToString().Trim();
                if(title.Length==0)return true;
                uint pid;GetWindowThreadProcessId(hWnd,out pid);
                string processName="未知";
                try {using(Process p=Process.GetProcessById((int)pid)){processName=p.ProcessName+".exe";}}catch{}
                result.Add(new WindowCatalogItem {Title=title,ProcessName=processName,Minimized=IsIconic(hWnd)});
                return true;
            }, IntPtr.Zero);
            result.Sort(delegate(WindowCatalogItem x,WindowCatalogItem y){return String.Compare(x.Title,y.Title,StringComparison.CurrentCultureIgnoreCase);});
            return result;
        }

        public static WindowCatalogItem Pick(IWin32Window owner)
        {
            using(Form f=new Form())
            {
                f.Text="选择已打开的窗口（包含最小化窗口）";
                f.ClientSize=new Size(820,520);
                f.MinimumSize=new Size(650,380);
                f.StartPosition=FormStartPosition.CenterParent;
                f.Font=new Font("DengXian",10F);
                DataGridView grid=new DataGridView {Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,
                    SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,
                    BackgroundColor=Color.White,RowHeadersVisible=false};
                grid.Columns.Add("title","窗口标题");grid.Columns.Add("process","进程");grid.Columns.Add("status","状态");
                grid.Columns[0].FillWeight=65;grid.Columns[1].FillWeight=25;grid.Columns[2].FillWeight=10;
                Panel bottom=new Panel {Dock=DockStyle.Bottom,Height=65};
                Button refresh=new Button {Text="刷新窗口列表",Left=14,Top=13,Width=150,Height=38};
                Button choose=new Button {Text="选择此窗口",Left=650,Top=13,Width=150,Height=38,Anchor=AnchorStyles.Top|AnchorStyles.Right};
                Label note=new Label {Text="只读取当前桌面可见/最小化的普通窗口，不读取窗口内文字。导出的 BAT 会在目标电脑重新按标题与进程匹配。",Left=180,Top=12,Width=455,Height=44};
                bottom.Controls.AddRange(new Control[]{refresh,choose,note});
                f.Controls.Add(grid);f.Controls.Add(bottom);
                Action read=delegate {
                    grid.Rows.Clear();
                    foreach(WindowCatalogItem w in Read()) { int row=grid.Rows.Add(w.Title,w.ProcessName,w.Minimized?"最小化":"已打开");grid.Rows[row].Tag=w; }
                };
                WindowCatalogItem selected=null;
                Action accept=delegate {if(grid.CurrentRow!=null){selected=grid.CurrentRow.Tag as WindowCatalogItem;if(selected!=null){f.DialogResult=DialogResult.OK;f.Close();}}};
                refresh.Click+=delegate {read();};
                choose.Click+=delegate {accept();};
                grid.CellDoubleClick+=delegate {accept();};
                read();
                return f.ShowDialog(owner)==DialogResult.OK?selected:null;
            }
        }
    }
}
