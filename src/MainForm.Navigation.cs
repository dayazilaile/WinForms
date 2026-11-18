using System;
using System.Drawing;
using System.Windows.Forms;

namespace ITDesktopToolkit
{
    public sealed partial class MainForm
    {
        private TreeView navigationTree;
        private TextBox navigationSearch;
        private bool rebuildingNavigation;
        private string expandedNavigationGroup="系统与硬件";
        private void BuildNavigation(Panel sidebar)
        {
            var logo=new Label{Dock=DockStyle.Top,Height=80,Text="  IT 桌面运维工具箱\r\n  V0.5.9 · 本地离线运行",ForeColor=Color.White,Font=new Font("DengXian",13F,FontStyle.Bold),TextAlign=ContentAlignment.MiddleLeft};
            var searchPanel=new Panel{Dock=DockStyle.Top,Height=76,Padding=new Padding(10,28,10,10)};
            var caption=new Label{Text="搜索功能（如 DNS / 打印机）",Left=12,Top=5,AutoSize=true,ForeColor=Color.LightSteelBlue};searchPanel.Controls.Add(caption);
            var searchFrame=new Panel{Dock=DockStyle.Fill,BackColor=Color.White,Padding=new Padding(8,6,4,6),Name="navigationSearchFrame"};
            navigationSearch=new TextBox{Dock=DockStyle.Fill,AccessibleName="搜索功能",BorderStyle=BorderStyle.None,BackColor=Color.White,ForeColor=Navy};
            var clear=new Button{Text="",Name="navigationClear",Dock=DockStyle.Right,Width=24,AccessibleName="清空搜索",TabStop=false,Visible=false,BackColor=Color.White,ForeColor=Color.FromArgb(100,116,139),FlatStyle=FlatStyle.Flat,Cursor=Cursors.Hand};
            clear.FlatAppearance.BorderSize=0;clear.FlatAppearance.MouseOverBackColor=Color.FromArgb(241,245,249);clear.FlatAppearance.MouseDownBackColor=Color.FromArgb(226,232,240);
            clear.Paint+=delegate(object sender,PaintEventArgs e){e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;float x=clear.ClientSize.Width/2F,y=clear.ClientSize.Height/2F;using(var pen=new Pen(clear.ForeColor,1.5F)){e.Graphics.DrawLine(pen,x-3.5F,y-3.5F,x+3.5F,y+3.5F);e.Graphics.DrawLine(pen,x+3.5F,y-3.5F,x-3.5F,y+3.5F);}};
            searchFrame.Controls.Add(navigationSearch);searchFrame.Controls.Add(clear);searchPanel.Controls.Add(searchFrame);searchFrame.BringToFront();
            navigationTree=new TreeView{Dock=DockStyle.Fill,BackColor=Navy,ForeColor=Color.White,BorderStyle=BorderStyle.None,ShowLines=false,ShowRootLines=false,ShowPlusMinus=false,FullRowSelect=true,HideSelection=false,Indent=18,ItemHeight=38,AccessibleName="功能分类与二级菜单"};
            navigationTree.DrawMode=TreeViewDrawMode.OwnerDrawAll;
            navigationTree.DrawNode+=delegate(object sender,DrawTreeNodeEventArgs e){
                bool active=e.Node.Tag is string&&(string)e.Node.Tag==currentPage;
                Rectangle row=new Rectangle(8,e.Bounds.Top+2,Math.Max(0,navigationTree.ClientSize.Width-16),navigationTree.ItemHeight-4);
                using(var baseBrush=new SolidBrush(Navy))e.Graphics.FillRectangle(baseBrush,new Rectangle(0,e.Bounds.Top,navigationTree.ClientSize.Width,navigationTree.ItemHeight));
                if(active){using(var brush=new SolidBrush(Color.FromArgb(30,58,95)))using(var path=Rounded(row,7))e.Graphics.FillPath(brush,path);using(var accent=new SolidBrush(Color.FromArgb(96,165,250)))e.Graphics.FillRectangle(accent,10,row.Top+7,3,row.Height-14);}
                bool group=e.Node.Tag==null;int textLeft=group?30:39;
                if(group&&e.Node.Nodes.Count>0){int x=17,y=row.Top+row.Height/2;Point[] points=e.Node.IsExpanded?new[]{new Point(x-4,y-2),new Point(x+4,y-2),new Point(x,y+3)}:new[]{new Point(x-2,y-4),new Point(x-2,y+4),new Point(x+3,y)};using(var brush=new SolidBrush(Color.FromArgb(148,163,184)))e.Graphics.FillPolygon(brush,points);}
                TextRenderer.DrawText(e.Graphics,e.Node.Text,group?UiActive:Ui,new Rectangle(textLeft,row.Top,Math.Max(0,navigationTree.ClientSize.Width-textLeft-12),row.Height),active||group?Color.White:Color.FromArgb(193,205,222),TextFormatFlags.VerticalCenter|TextFormatFlags.Left|TextFormatFlags.NoPrefix|TextFormatFlags.EndEllipsis);
                if(navigationTree.Focused&&e.Node==navigationTree.SelectedNode)ControlPaint.DrawFocusRectangle(e.Graphics,row,Color.LightSteelBlue,Navy);
            };
            sidebar.Controls.Add(navigationTree);sidebar.Controls.Add(searchPanel);sidebar.Controls.Add(logo);
            clear.Click+=delegate{navigationSearch.Clear();navigationSearch.Focus();};
            navigationSearch.TextChanged+=delegate{clear.Visible=navigationSearch.Text.Length>0;PopulateNavigation();};
            navigationSearch.KeyDown+=delegate(object sender,KeyEventArgs e){if(e.KeyCode==Keys.Enter){foreach(TreeNode group in navigationTree.Nodes)if(group.Nodes.Count>0){navigationTree.SelectedNode=group.Nodes[0];break;}e.Handled=true;e.SuppressKeyPress=true;}};
            navigationTree.AfterSelect+=delegate(object sender,TreeViewEventArgs e){if(rebuildingNavigation)return;if(e.Node.Tag is string){ShowPage((string)e.Node.Tag);}};
            navigationTree.NodeMouseClick+=delegate(object sender,TreeNodeMouseClickEventArgs e){if(e.Button==MouseButtons.Left&&e.Node.Tag==null&&e.Node.Nodes.Count>0){if(e.Node.IsExpanded)e.Node.Collapse();else e.Node.Expand();}};
            navigationTree.KeyDown+=delegate(object sender,KeyEventArgs e){TreeNode n=navigationTree.SelectedNode;if(n!=null&&n.Tag==null&&(e.KeyCode==Keys.Enter||e.KeyCode==Keys.Space)){if(n.IsExpanded)n.Collapse();else n.Expand();e.Handled=true;e.SuppressKeyPress=true;}};
            navigationTree.BeforeExpand+=delegate(object sender,TreeViewCancelEventArgs e){if(rebuildingNavigation||navigationSearch.Text.Trim().Length>0)return;foreach(TreeNode n in navigationTree.Nodes)if(n!=e.Node)n.Collapse();};
            navigationTree.AfterExpand+=delegate(object sender,TreeViewEventArgs e){if(!rebuildingNavigation&&navigationSearch.Text.Trim().Length==0){expandedNavigationGroup=e.Node.Text;}};
            navigationTree.AfterCollapse+=delegate(object sender,TreeViewEventArgs e){if(!rebuildingNavigation&&navigationSearch.Text.Trim().Length==0&&expandedNavigationGroup==e.Node.Text){expandedNavigationGroup="";}};
            if(expandedNavigationGroup=="安全与维护")expandedNavigationGroup="系统与硬件";
            PopulateNavigation();
            Shown+=async delegate {
                expandedNavigationGroup="系统与硬件";PopulateNavigation();SelectNavigationPage("系统概览");
                try{await ReadyDatabase();if(!IsDisposed)SetStatus("当前页面："+currentPage+"   |   数据库：硬件 "+db.HardwareRuleCount+" 条 / 蓝屏 "+db.BugcheckRuleCount+" 条");}
                catch(Exception ex){Paths.Log("Load DB: "+ex);if(!IsDisposed)SetStatus("数据库读取失败："+ex.Message);}
            };
        }
        private static System.Drawing.Drawing2D.GraphicsPath Rounded(Rectangle r,int radius)
        {
            var path=new System.Drawing.Drawing2D.GraphicsPath();int d=radius*2;path.AddArc(r.Left,r.Top,d,d,180,90);path.AddArc(r.Right-d,r.Top,d,d,270,90);path.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);path.AddArc(r.Left,r.Bottom-d,d,d,90,90);path.CloseFigure();return path;
        }
        private void PopulateNavigation()
        {
            rebuildingNavigation=true;navigationTree.BeginUpdate();
            try
            {
                navigationTree.Nodes.Clear();TreeNode group=null;string groupName=null;
                foreach(var entry in NavigationCatalog.Search(navigationSearch.Text))
                {
                    if(entry.Group!=groupName){groupName=entry.Group;group=new TreeNode(groupName);navigationTree.Nodes.Add(group);}
                    group.Nodes.Add(new TreeNode(entry.Key){Tag=entry.Key});
                }
                foreach(TreeNode n in navigationTree.Nodes)if(n.Text==expandedNavigationGroup||navigationSearch.Text.Trim().Length>0)n.Expand();
                if(navigationTree.Nodes.Count==0)navigationTree.Nodes.Add(new TreeNode("没有匹配的功能"));
                foreach(TreeNode n in navigationTree.Nodes)foreach(TreeNode child in n.Nodes)if(n.IsExpanded&&(string)child.Tag==currentPage)navigationTree.SelectedNode=child;
            }
            finally{navigationTree.EndUpdate();rebuildingNavigation=false;}
        }
        private void SelectNavigationPage(string key)
        {
            foreach(TreeNode group in navigationTree.Nodes)foreach(TreeNode child in group.Nodes)if((string)child.Tag==key){rebuildingNavigation=true;if(group.IsExpanded)navigationTree.SelectedNode=child;rebuildingNavigation=false;ShowPage(key);if(group.IsExpanded)child.EnsureVisible();return;}
            ShowPage("系统概览");
        }
    }
}
