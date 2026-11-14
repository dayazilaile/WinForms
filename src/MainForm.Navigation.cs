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
            var logo=new Label{Dock=DockStyle.Top,Height=80,Text="  IT 桌面运维工具箱\r\n  V0.5.0 · 本地离线运行",ForeColor=Color.White,Font=new Font("DengXian",13F,FontStyle.Bold),TextAlign=ContentAlignment.MiddleLeft};
            var searchPanel=new Panel{Dock=DockStyle.Top,Height=66,Padding=new Padding(10,24,10,8)};
            var caption=new Label{Text="搜索功能（如 DNS / 打印机）",Left=10,Top=3,AutoSize=true,ForeColor=Color.LightSteelBlue};searchPanel.Controls.Add(caption);
            navigationSearch=new TextBox{Dock=DockStyle.Fill,AccessibleName="搜索功能"};
            var clear=new Button{Text="×",Dock=DockStyle.Right,Width=27,AccessibleName="清空搜索",TabStop=false,BackColor=Color.White,ForeColor=Navy,FlatStyle=FlatStyle.Flat};
            searchPanel.Controls.Add(navigationSearch);searchPanel.Controls.Add(clear);
            navigationTree=new TreeView{Dock=DockStyle.Fill,BackColor=Navy,ForeColor=Color.White,BorderStyle=BorderStyle.None,ShowLines=false,ShowRootLines=true,FullRowSelect=true,HideSelection=false,Indent=16,ItemHeight=33,AccessibleName="功能分类与二级菜单"};
            navigationTree.DrawMode=TreeViewDrawMode.OwnerDrawText;
            navigationTree.DrawNode+=delegate(object sender,DrawTreeNodeEventArgs e){
                bool selected=e.Node==navigationTree.SelectedNode;Color background=selected?Color.FromArgb(43,80,130):Navy;
                Rectangle bounds=new Rectangle(e.Bounds.Left,e.Bounds.Top,Math.Max(0,navigationTree.ClientSize.Width-e.Bounds.Left),e.Bounds.Height);
                using(var brush=new SolidBrush(background))e.Graphics.FillRectangle(brush,bounds);
                TextRenderer.DrawText(e.Graphics,e.Node.Text,e.Node.Tag==null?UiActive:Ui,bounds,Color.White,background,TextFormatFlags.VerticalCenter|TextFormatFlags.Left|TextFormatFlags.NoPrefix);
            };
            sidebar.Controls.Add(navigationTree);sidebar.Controls.Add(searchPanel);sidebar.Controls.Add(logo);
            clear.Click+=delegate{navigationSearch.Clear();navigationSearch.Focus();};
            navigationSearch.TextChanged+=delegate{PopulateNavigation();};
            navigationSearch.KeyDown+=delegate(object sender,KeyEventArgs e){if(e.KeyCode==Keys.Enter){foreach(TreeNode group in navigationTree.Nodes)if(group.Nodes.Count>0){navigationTree.SelectedNode=group.Nodes[0];break;}e.Handled=true;e.SuppressKeyPress=true;}};
            navigationTree.AfterSelect+=delegate(object sender,TreeViewEventArgs e){if(rebuildingNavigation)return;if(e.Node.Tag is string){ShowPage((string)e.Node.Tag);}};
            navigationTree.NodeMouseClick+=delegate(object sender,TreeNodeMouseClickEventArgs e){if(e.Button==MouseButtons.Left&&e.Node.Tag==null&&e.Node.Nodes.Count>0&&e.X>=e.Node.Bounds.Left){if(e.Node.IsExpanded)e.Node.Collapse();else e.Node.Expand();}};
            navigationTree.KeyDown+=delegate(object sender,KeyEventArgs e){TreeNode n=navigationTree.SelectedNode;if(n!=null&&n.Tag==null&&(e.KeyCode==Keys.Enter||e.KeyCode==Keys.Space)){if(n.IsExpanded)n.Collapse();else n.Expand();e.Handled=true;e.SuppressKeyPress=true;}};
            navigationTree.BeforeExpand+=delegate(object sender,TreeViewCancelEventArgs e){if(rebuildingNavigation||navigationSearch.Text.Trim().Length>0)return;foreach(TreeNode n in navigationTree.Nodes)if(n!=e.Node)n.Collapse();};
            navigationTree.AfterExpand+=delegate(object sender,TreeViewEventArgs e){if(!rebuildingNavigation&&navigationSearch.Text.Trim().Length==0){expandedNavigationGroup=e.Node.Text;SaveNavigationState();}};
            navigationTree.AfterCollapse+=delegate(object sender,TreeViewEventArgs e){if(!rebuildingNavigation&&navigationSearch.Text.Trim().Length==0&&expandedNavigationGroup==e.Node.Text){expandedNavigationGroup="";SaveNavigationState();}};
            PopulateNavigation();
            Shown+=delegate{SelectNavigationPage(lastNavigationPage);};
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
