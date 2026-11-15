using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using ITDesktopToolkit;
class NavigationUiTests
{
    static int passed;static int uiErrors;
    static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);passed++;}
    static T Field<T>(object instance,string name){return (T)instance.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(instance);}
    static object Call(object instance,string name,params object[] args){return instance.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(instance,args);}
    static T[] Find<T>(Control root)where T:Control{return root.Controls.Cast<Control>().SelectMany(c=>new Control[]{c}.Concat(Descendants(c))).OfType<T>().ToArray();}
    static System.Collections.Generic.IEnumerable<Control> Descendants(Control root){foreach(Control c in root.Controls){yield return c;foreach(Control child in Descendants(c))yield return child;}}
    static void Navigate(MainForm main,TreeView tree,string key){foreach(TreeNode group in tree.Nodes)foreach(TreeNode node in group.Nodes)if((string)node.Tag==key){group.Expand();tree.SelectedNode=node;return;}throw new Exception("Navigation entry missing: "+key);}
    static int Leaves(TreeView tree){return tree.Nodes.Cast<TreeNode>().Sum(g=>g.Nodes.Count);}
    static void Pump(Func<bool> finished,int timeout=5000){var watch=System.Diagnostics.Stopwatch.StartNew();while(!finished()){Application.DoEvents();if(watch.ElapsedMilliseconds>timeout)throw new Exception("UI operation did not finish");System.Threading.Thread.Sleep(10);}Application.DoEvents();}
    static void Click(TreeView tree,TreeNode node){Call(tree,"OnNodeMouseClick",new TreeNodeMouseClickEventArgs(node,MouseButtons.Left,1,node.Bounds.Left+5,node.Bounds.Top+5));Application.DoEvents();}
    static void Screenshot(Form form,string name){form.Refresh();Application.DoEvents();using(var image=new Bitmap(form.ClientSize.Width,form.ClientSize.Height)){try{using(var g=Graphics.FromImage(image))g.CopyFromScreen(form.PointToScreen(Point.Empty),Point.Empty,form.ClientSize);}catch(System.Runtime.InteropServices.ExternalException){form.DrawToBitmap(image,new Rectangle(Point.Empty,form.ClientSize));Console.WriteLine("Screenshot fallback: "+name);}image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,name));}}
    static void Modal(MainForm main,string method,params object[] args)
    {
        bool opened=false;
        using(var timer=new Timer{Interval=50})
        {
            timer.Tick+=delegate{foreach(Form form in Application.OpenForms.Cast<Form>().ToArray())if(form!=main){opened=true;if(method=="ShowWifiSecret")Check(!Find<Button>(form).Any(b=>b.Text.Contains("导出")),"Password window has no export action");Check(form.Width>=780&&Find<RichTextBox>(form).Length==1,"Dialog renders: "+form.Text);form.Close();return;}};
            timer.Start();try{Call(main,method,args);}finally{timer.Stop();}
        }
        Check(opened,"Modal closed cleanly: "+method);
    }
    [STAThread]static int Main()
    {
        Application.EnableVisualStyles();Application.ThreadException+=delegate{uiErrors++;};string path=Path.Combine(Paths.UserDir,"navigation-v050.txt");byte[] original=File.Exists(path)?File.ReadAllBytes(path):null;
        try
        {
            File.WriteAllLines(path,new[]{"安全与维护","U 盘检查与防护"},Encoding.UTF8);
            using(var main=new MainForm())
            {
                main.Show();Application.DoEvents();var tree=Field<TreeView>(main,"navigationTree");var search=Field<TextBox>(main,"navigationSearch");
                Check(tree.Nodes.Count==6&&Leaves(tree)==29,"Six categories / 29 features (including trial), no duplicate system category");
                Check(Field<string>(main,"currentPage")=="U 盘检查与防护"&&tree.Nodes[0].IsExpanded,"Legacy safety category state migrates to system/hardware");
                Click(tree,tree.Nodes[0]);Check(!tree.Nodes[0].IsExpanded,"Category text click collapses");Click(tree,tree.Nodes[1]);Check(tree.Nodes[1].IsExpanded&&tree.Nodes.Cast<TreeNode>().Count(n=>n.IsExpanded)==1,"Accordion expands only one group");
                search.Text="DNS";Application.DoEvents();Check(Leaves(tree)==4,"Search DNS aliases including trial");search.Text="不存在的功能XYZ";Application.DoEvents();Check(Leaves(tree)==0,"Empty search result has no fake feature");search.Clear();
                foreach(string key in new[]{"端口扫描","主机发现","路由与MAC","Wi-Fi分析"})
                {
                    Navigate(main,tree,key);Application.DoEvents();var body=Field<Panel>(main,"content");
                    Check(Find<DataGridView>(body).Length>0&&!Find<Button>(body).Any(b=>b.Text.StartsWith("打开")),"Direct inline page without extra open button: "+key);
                    Check(Application.OpenForms.Count==1,"Inline navigation never launches modal: "+key);
                    if(key=="路由与MAC"||key=="Wi-Fi分析"){var button=Find<Button>(body).Single(b=>b.Text=="刷新信息");Pump(()=>button.Enabled,30000);Check(Find<Panel>(body).Count(p=>p.Name=="resultTabs")==1,"Auto-read completes with structured tables: "+key);}
                }
                foreach(string key in new[]{"DNS记录查询","ICMP路由跟踪","本机连接与监听","系统代理诊断","网络配置核对"})
                {
                    Navigate(main,tree,key);Application.DoEvents();var trial=Field<Panel>(main,"content");
                    Check(tree.SelectedNode.Parent.Text.Contains("新增")&&Find<DataGridView>(trial).Single().Rows.Count==0&&Find<Button>(trial).Single(b=>b.Text=="停止").Enabled==false&&Find<Label>(trial).Any(l=>l.Text.StartsWith("待试用")),"Trial remains separate and idle until explicit action: "+key);
                }
                var audit=Field<Panel>(main,"content");var read=Find<Button>(audit).Single(b=>b.Text=="读取本机信息");read.PerformClick();Pump(()=>read.Enabled,30000);
                Check(Find<DataGridView>(audit).Single().Rows.Count>0,"Trial local IPv4 audit executes and fills table without probing network");
                Navigate(main,tree,"DNS记录查询");Application.DoEvents();var dnsPage=Field<Panel>(main,"content");Find<TextBox>(dnsPage).Single(t=>t.AccessibleName=="DNS 名称 / PTR 地址").Text="bad; exit";var query=Find<Button>(dnsPage).Single(b=>b.Text=="查询记录");query.PerformClick();Pump(()=>query.Enabled);
                Check(Find<DataGridView>(dnsPage).Single().Rows.Count==0&&Find<Label>(dnsPage).Any(l=>l.Text.Contains("格式无效")),"Trial UI displays validation error without DNS query");
                Navigate(main,tree,"ICMP路由跟踪");Application.DoEvents();Navigate(main,tree,"DNS记录查询");Application.DoEvents();Screenshot(main,"trial-v052.png");
                Navigate(main,tree,"主机发现");Application.DoEvents();Check(!Find<TextBox>(Field<Panel>(main,"content")).Any(t=>t.AccessibleName=="TCP 端口（逗号或范围）"),"Ping page has no TCP port input");
                Navigate(main,tree,"端口扫描");Application.DoEvents();var panel=Field<Panel>(main,"content");var ports=Find<TextBox>(panel).Single(t=>t.AccessibleName=="TCP 端口（逗号或范围）");Check(NetworkDiscoveryService.ParsePorts(ports.Text).Length==CommonPorts.Count,"Inline scanner defaults to complete common preset");
                var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
                try
                {
                    ports.Text=((IPEndPoint)listener.LocalEndpoint).Port.ToString();Find<TextBox>(panel).Single(t=>t.AccessibleName=="目标主机名 / IP").Text="127.0.0.1";
                    var start=Find<Button>(panel).Single(b=>b.Text=="开始扫描");start.PerformClick();Pump(()=>start.Enabled);
                    var result=Find<DataGridView>(panel).Single();Check(result.Rows.Count==1&&(string)result.Rows[0].Cells[3].Value=="开放","Inline UI runs real local TCP scan and renders result");
                    Screenshot(main,"navigation-v051.png");
                }finally{listener.Stop();}
                Navigate(main,tree,"Wi-Fi分析");Application.DoEvents();panel=Field<Panel>(main,"content");Pump(()=>Find<Button>(panel).Single(b=>b.Text=="刷新信息").Enabled,30000);
                var fixture=new WifiInventory();fixture.Adapters.Add(new WifiAdapterRow{Name="样例无线网卡（测试数据）",Mac="02:00:00:00:00:01",State="已连接",Ssid="样例网络",Bssid="02:00:00:00:00:02",Type="基础结构",Phy="802.11ac",Frequency="5180",Band="5 GHz",Channel="36",Signal="82",Rx="866.7",Tx="780"});
                fixture.Networks.Add(new WifiBssRow{Ssid="样例网络（测试数据）",Bssid="02:00:00:00:00:02",Type="基础结构",Phy="802.11ac",Frequency="5180",Band="5 GHz",Channel="36",Signal="82",Rssi="-48",BasicRates="6, 12, 24",SupportedRates="6, 9, 12, 18, 24, 36, 48, 54",Security="加密",Adapter="样例无线网卡"});
                var root=Field<Panel>(main,"content").Controls.OfType<Panel>().Single();Call(main,"BindWifi",root,fixture);
                var tables=Find<DataGridView>(panel);Check(tables.Count(g=>g.Visible)==1&&tables.Single(g=>g.Visible).Columns[0].HeaderText=="SSID","Nearby hotspot table is the default and only one table is visible");Check(tables.Length==2&&tables.All(g=>g.Columns.Count==13)&&tables.All(g=>g.Rows.Count==1),"WiFi interfaces and APs rendered as separate 13-column tables");
                Check(tables.SelectMany(g=>g.Columns.Cast<DataGridViewColumn>()).Any(c=>c.HeaderText=="基本速率（Mbps）")&&tables.SelectMany(g=>g.Columns.Cast<DataGridViewColumn>()).Any(c=>c.HeaderText=="接收（Mbps）"),"Basic and link rates have distinct columns");
                Find<Button>(panel).Single(b=>b.Text=="无线接口 / 当前连接").PerformClick();Application.DoEvents();Screenshot(main,"wifi-v051-interface.png");Find<Button>(panel).Single(b=>b.Text=="附近热点（系统缓存）").PerformClick();Application.DoEvents();Screenshot(main,"wifi-v051-hotspots.png");Check(tables.Single(g=>g.Visible).Columns[6].HeaderText=="基本速率（Mbps）","Basic rates placed beside MAC/frequency in leading columns");
                var grid=tables.First();object originalCell=grid.Rows[0].Cells[0].Value;grid.Rows[0].Cells[0].Value="=SUM(1,2)";string csv=(string)typeof(MainForm).GetMethod("TableCsv",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{grid});Check(csv.Contains("'=SUM(1,2)"),"CSV neutralizes formula injection from network-supplied names");grid.Rows[0].Cells[0].Value=originalCell;
                main.Size=new Size(1000,670);Application.DoEvents();Check(Find<Panel>(panel).Single(p=>p.Name=="resultTabs").Height>180,"Tables remain usable at minimum window size");Screenshot(main,"wifi-v051-compact.png");
                Navigate(main,tree,"路由与MAC");Navigate(main,tree,"Wi-Fi分析");Navigate(main,tree,"端口扫描");Pump(()=>true);Check(uiErrors==0,"Rapid navigation cancels auto-read without disposed-control errors");
                using(var testPage=new Panel())
                {
                    var testBar=new FlowLayoutPanel();var testStop=new Button();var testState=new Label();testPage.Controls.Add(testBar);testPage.Controls.Add(testState);testBar.Controls.Add(testStop);
                    Type sessionType=typeof(MainForm).GetNestedType("PageSession",BindingFlags.NonPublic);object operation=Activator.CreateInstance(sessionType,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new object[]{testPage,testBar,testStop,testState},null);
                    bool cancelled=false;Func<System.Threading.CancellationToken,System.Threading.Tasks.Task> waiting=async delegate(System.Threading.CancellationToken token){try{await System.Threading.Tasks.Task.Delay(30000,token);}catch(OperationCanceledException){cancelled=true;throw;}};
                    var running=(System.Threading.Tasks.Task)sessionType.GetMethod("Run").Invoke(operation,new object[]{waiting});testPage.Dispose();Pump(()=>running.IsCompleted);Check(cancelled&&!running.IsFaulted,"Disposing an active page cancels work without updating disposed controls");
                }
                Modal(main,"NetworkInformationDialog",1);Modal(main,"NetworkInformationDialog",3);Modal(main,"CameraDiscoveryDialog");Modal(main,"ShowWifiSecret","test-profile-not-read");
                main.Close();Application.DoEvents();Check(uiErrors==0,"No unhandled UI exceptions");
            }
            Console.WriteLine("NAVIGATION UI TOTAL PASS "+passed);return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
        finally{if(original==null){if(File.Exists(path))File.Delete(path);}else File.WriteAllBytes(path,original);}
    }
}
