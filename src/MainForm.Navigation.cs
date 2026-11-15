using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace ITDesktopToolkit
{
    public sealed partial class MainForm
    {
        private TreeView navigationTree;
        private TextBox navigationSearch;
        private bool rebuildingNavigation;
        private string expandedNavigationGroup="系统与硬件",lastNavigationPage="系统概览";
        private void BuildNavigation(Panel sidebar)
        {
            try
            {
                string file=Path.Combine(Paths.UserDir,"navigation-v050.txt");
                if(File.Exists(file))
                {
                    string[] lines=File.ReadAllLines(file,Encoding.UTF8);
                    if(lines.Length>=2&&NavigationCatalog.Find(lines[1])!=null){expandedNavigationGroup=lines[0];lastNavigationPage=lines[1];}
                }
            }catch(Exception ex){Paths.Log("Read navigation state: "+ex.Message);}
            var logo=new Label{Dock=DockStyle.Top,Height=80,Text="  IT 桌面运维工具箱\r\n  V0.5.1 · 本地离线运行",ForeColor=Color.White,Font=new Font("DengXian",13F,FontStyle.Bold),TextAlign=ContentAlignment.MiddleLeft};
            var searchPanel=new Panel{Dock=DockStyle.Top,Height=66,Padding=new Padding(10,24,10,8)};
            var caption=new Label{Text="搜索功能（如 DNS / 打印机）",Left=10,Top=3,AutoSize=true,ForeColor=Color.LightSteelBlue};searchPanel.Controls.Add(caption);
            navigationSearch=new TextBox{Dock=DockStyle.Fill,AccessibleName="搜索功能"};
            var clear=new Button{Text="×",Dock=DockStyle.Right,Width=27,AccessibleName="清空搜索",TabStop=false,BackColor=Color.White,ForeColor=Navy,FlatStyle=FlatStyle.Flat};
            searchPanel.Controls.Add(navigationSearch);searchPanel.Controls.Add(clear);
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
            navigationSearch.TextChanged+=delegate{PopulateNavigation();};
            navigationSearch.KeyDown+=delegate(object sender,KeyEventArgs e){if(e.KeyCode==Keys.Enter){foreach(TreeNode group in navigationTree.Nodes)if(group.Nodes.Count>0){navigationTree.SelectedNode=group.Nodes[0];break;}e.Handled=true;e.SuppressKeyPress=true;}};
            navigationTree.AfterSelect+=delegate(object sender,TreeViewEventArgs e){if(rebuildingNavigation)return;if(e.Node.Tag is string){ShowPage((string)e.Node.Tag);}};
            navigationTree.NodeMouseClick+=delegate(object sender,TreeNodeMouseClickEventArgs e){if(e.Button==MouseButtons.Left&&e.Node.Tag==null&&e.Node.Nodes.Count>0){if(e.Node.IsExpanded)e.Node.Collapse();else e.Node.Expand();}};
            navigationTree.KeyDown+=delegate(object sender,KeyEventArgs e){TreeNode n=navigationTree.SelectedNode;if(n!=null&&n.Tag==null&&(e.KeyCode==Keys.Enter||e.KeyCode==Keys.Space)){if(n.IsExpanded)n.Collapse();else n.Expand();e.Handled=true;e.SuppressKeyPress=true;}};
            navigationTree.BeforeExpand+=delegate(object sender,TreeViewCancelEventArgs e){if(rebuildingNavigation||navigationSearch.Text.Trim().Length>0)return;foreach(TreeNode n in navigationTree.Nodes)if(n!=e.Node)n.Collapse();};
            navigationTree.AfterExpand+=delegate(object sender,TreeViewEventArgs e){if(!rebuildingNavigation&&navigationSearch.Text.Trim().Length==0){expandedNavigationGroup=e.Node.Text;SaveNavigationState();}};
            navigationTree.AfterCollapse+=delegate(object sender,TreeViewEventArgs e){if(!rebuildingNavigation&&navigationSearch.Text.Trim().Length==0&&expandedNavigationGroup==e.Node.Text){expandedNavigationGroup="";SaveNavigationState();}};
            if(expandedNavigationGroup=="安全与维护")expandedNavigationGroup="系统与硬件";
            PopulateNavigation();
            Shown+=delegate{SelectNavigationPage(lastNavigationPage);};
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
        private void SaveNavigationState()
        {
            try{File.WriteAllLines(Path.Combine(Paths.UserDir,"navigation-v050.txt"),new string[]{expandedNavigationGroup,currentPage.Length==0?lastNavigationPage:currentPage},Encoding.UTF8);}
            catch(Exception ex){Paths.Log("Save navigation state: "+ex.Message);}
        }
    }
}
