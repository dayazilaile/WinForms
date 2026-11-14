using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using ITDesktopToolkit;
class NavigationUiTests
{
    static int passed;
    static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);passed++;}
    static T Field<T>(object instance,string name){return (T)instance.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(instance);}
    static object Call(object instance,string name,params object[] args){return instance.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(instance,args);}
    static int Leaves(TreeView tree){int n=0;foreach(TreeNode g in tree.Nodes)n+=g.Nodes.Count;return n;}
    static void Click(TreeView tree,TreeNode node){Call(tree,"OnNodeMouseClick",new TreeNodeMouseClickEventArgs(node,MouseButtons.Left,1,node.Bounds.Left+5,node.Bounds.Top+5));Application.DoEvents();}
    static void Modal(MainForm main,string method,params object[] args)
    {
        bool opened=false;var watch=System.Diagnostics.Stopwatch.StartNew();
        using(var timer=new Timer{Interval=50})
        {
            timer.Tick+=delegate{foreach(Form form in Application.OpenForms.Cast<Form>().ToArray())if(form!=main){opened=true;if(method=="ShowWifiSecret")Check(!form.Controls.OfType<FlowLayoutPanel>().SelectMany(p=>p.Controls.OfType<Button>()).Any(b=>b.Text.Contains("导出")),"Password window has no export action");Check(form.Width>=780&&form.Controls.OfType<RichTextBox>().Count()==1,"Dialog renders: "+form.Text);if(method=="NetworkScanDialog"&&args.Length>0&&!(bool)args[0])using(var shot=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(shot,new Rectangle(0,0,form.Width,form.Height));shot.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"network-tools-dialog.png"));}form.Close();return;}if(watch.ElapsedMilliseconds>3000){timer.Stop();throw new Exception("Modal did not open: "+method);}};
            timer.Start();try{Call(main,method,args);}finally{timer.Stop();}
        }
        Check(opened,"Modal closed cleanly: "+method);
    }
    [STAThread]static int Main()
    {
        Application.EnableVisualStyles();string path=Path.Combine(Paths.UserDir,"navigation-v050.txt");byte[] original=File.Exists(path)?File.ReadAllBytes(path):null;
        try
        {
            File.WriteAllLines(path,new[]{"网络诊断","端口扫描"},Encoding.UTF8);
            using(var main=new MainForm())
            {
                main.Show();Application.DoEvents();var tree=Field<TreeView>(main,"navigationTree");var search=Field<TextBox>(main,"navigationSearch");
                Check(tree.Nodes.Count==6&&Leaves(tree)==24,"Main menu has 6 groups / 24 leaves");
                Check(Field<string>(main,"currentPage")=="端口扫描"&&tree.Nodes.Cast<TreeNode>().Count(n=>n.IsExpanded)==1,"Restore last page and exactly one expanded group");
                TreeNode systems=tree.Nodes[0];Click(tree,systems);Check(systems.IsExpanded&&tree.Nodes.Cast<TreeNode>().Count(n=>n.IsExpanded)==1,"Group click expands and collapses siblings");
                Click(tree,systems);Check(!systems.IsExpanded,"Click same group text again collapses it");
                search.Text="DNS";Application.DoEvents();Check(Leaves(tree)==2,"Search aliases narrows actual tree nodes");
                Check(Field<string>(main,"currentPage")=="端口扫描","Typing search does not launch another page");
                search.Text="不存在的功能XYZ";Application.DoEvents();Check(Leaves(tree)==0&&tree.Nodes.Count==1&&tree.Nodes[0].Tag==null,"No results shown without fake feature leaf");
                search.Clear();Application.DoEvents();Check(Leaves(tree)==24&&tree.Nodes.Cast<TreeNode>().All(n=>!n.IsExpanded),"Clearing search preserves collapsed category state");
                tree.Nodes[0].Expand();tree.SelectedNode=tree.Nodes[0].Nodes.Cast<TreeNode>().Single(n=>(string)n.Tag=="软件环境检查");Application.DoEvents();Check(Field<string>(main,"currentPage")=="软件环境检查","Leaf click navigates to existing feature");
                var saved=File.ReadAllLines(path,Encoding.UTF8);Check(saved[0]=="系统与硬件"&&saved[1]=="软件环境检查","Page and category persisted");
                foreach(var key in new[]{"端口扫描","主机发现","路由与MAC","DHCP与网络服务","Wi-Fi分析","已保存的Wi-Fi","摄像头发现"}){Call(main,"ShowPage",key);Check(Field<string>(main,"currentPage")==key,"New page reachable: "+key);}
                Modal(main,"NetworkScanDialog",false);Modal(main,"NetworkScanDialog",true);
                for(int mode=0;mode<4;mode++)Modal(main,"NetworkInformationDialog",mode);
                Modal(main,"CameraDiscoveryDialog");
                Modal(main,"ShowWifiSecret","test-profile-not-read");
                tree.Nodes[1].Expand();tree.SelectedNode=tree.Nodes[1].Nodes.Cast<TreeNode>().Single(n=>(string)n.Tag=="端口扫描");Application.DoEvents();
                using(var image=new Bitmap(main.Width,main.Height)){main.DrawToBitmap(image,new Rectangle(0,0,main.Width,main.Height));image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"navigation-v050.png"));}
                main.Close();
            }
            Console.WriteLine("NAVIGATION UI TOTAL PASS "+passed);return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
        finally{if(original==null){if(File.Exists(path))File.Delete(path);}else File.WriteAllBytes(path,original);}
    }
}
