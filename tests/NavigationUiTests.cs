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
                var originalLoad=Field<System.Threading.Tasks.Task<DatabaseService>>(main,"databaseLoad");var gate=new System.Threading.Tasks.TaskCompletionSource<DatabaseService>();main.GetType().GetField("databaseLoad",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(main,gate.Task);
                main.Show();Application.DoEvents();Check(main.Visible&&!gate.Task.IsCompleted&&Field<string>(main,"currentPage")=="系统概览","Overview renders while database task is still pending");Pump(()=>originalLoad.IsCompleted,30000);gate.SetResult(originalLoad.GetAwaiter().GetResult());Application.DoEvents();var tree=Field<TreeView>(main,"navigationTree");var search=Field<TextBox>(main,"navigationSearch");
                Check(tree.Nodes.Count==7&&Leaves(tree)==30,"Seven categories / 30 features (including trial), no duplicate system category");
                Check(Field<string>(main,"currentPage")=="系统概览"&&tree.Nodes[0].IsExpanded,"Every launch opens system overview despite saved prior page");
                Click(tree,tree.Nodes[0]);Check(!tree.Nodes[0].IsExpanded,"Category text click collapses");Click(tree,tree.Nodes[1]);Check(tree.Nodes[1].IsExpanded&&tree.Nodes.Cast<TreeNode>().Count(n=>n.IsExpanded)==1,"Accordion expands only one group");
                var clearSearch=Find<Button>(main).Single(b=>b.Name=="navigationClear");Check(!clearSearch.Visible,"Search clear icon is hidden for empty input");
                search.Text="DNS";Application.DoEvents();Check(clearSearch.Visible&&clearSearch.Parent.Name=="navigationSearchFrame"&&clearSearch.FlatAppearance.BorderSize==0,"Clear icon stays inside the search field without a separate border");Check(Leaves(tree)==4,"Search DNS aliases including trial");search.Text="不存在的功能XYZ";Application.DoEvents();Check(Leaves(tree)==0,"Empty search result has no fake feature");clearSearch.PerformClick();Application.DoEvents();Check(search.Text==""&&!clearSearch.Visible&&Leaves(tree)==30,"Inline clear resets search and restores all navigation entries");
                var software=new SoftwareSnapshot();for(int i=0;i<500;i++)software.Entries.Add(new SoftwareEntry{Name="样例软件 "+i,Version="1.0",Publisher="测试发布者",Scope="当前用户",Architecture="64 位登记区",Location="C:\\Example",Key="sample"+i});
                main.GetType().GetField("softwareCache",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(main,software);
                Navigate(main,tree,"软件环境检查");Application.DoEvents();var softwareBody=Field<Panel>(main,"content");var softwareGrid=Find<DataGridView>(softwareBody).Single();
                Check(softwareGrid.Rows.Count==500&&softwareGrid.Columns.Count==6&&Application.OpenForms.Count==1,"Software inventory opens directly as six-column list from cache");
                Find<TextBox>(softwareBody).Single(t=>t.AccessibleName=="搜索名称 / 版本 / 发布者").Text="样例软件 123";Application.DoEvents();Check(softwareGrid.Rows.Count==1,"Software search filters cached list without a new registry read");
                Screenshot(main,"software-v053.png");search.Text="软件";Application.DoEvents();Screenshot(main,"search-v053.png");clearSearch.PerformClick();Application.DoEvents();
                Navigate(main,tree,"启动项管理");Application.DoEvents();var startupBody=Field<Panel>(main,"content");Pump(()=>Find<Button>(startupBody).Single(b=>b.Text=="刷新启动项").Enabled,30000);var startup=Find<DataGridView>(startupBody).Single();
                Check(startup.Columns["icon"].Width>=64&&startup.ColumnHeadersDefaultCellStyle.WrapMode==DataGridViewTriState.False&&startup.Columns["command"].MinimumWidth>=280,"Startup icon header remains horizontal and command column cannot collapse");
                var actions=Field<System.Collections.Generic.List<AutoStep>>(main,"autoSteps");actions.Add(new AutoStep{Type="运行程序并等待",A="notepad.exe",B="example.txt",C="30"});Navigate(main,tree,"BAT 自动化");Application.DoEvents();var autoBody=Field<Panel>(main,"content");var actionGrid=Find<DataGridView>(autoBody).Single();
                Check(!Find<Button>(autoBody).Any(b=>b.Text=="上移"||b.Text=="下移"),"BAT editor removes old move buttons while retaining drag handle");
                Find<Button>(autoBody).Single(b=>b.Text=="复制动作").PerformClick();Check(actions.Count==2&&!Object.ReferenceEquals(actions[0],actions[1])&&actions[1].A==actions[0].A&&actions[1].B==actions[0].B&&actions[1].C==actions[0].C&&actionGrid.CurrentRow.Index==1,"Copy duplicates all step parameters independently immediately after selection");
                actions[1].A="other.exe";Check(actions[0].A=="notepad.exe","Editing a copy leaves the original step unchanged");
                Call(actionGrid,"OnKeyDown",new KeyEventArgs(Keys.Control|Keys.D));Check(actions.Count==3&&actionGrid.CurrentRow.Index==2,"Ctrl+D creates another editable step copy");Screenshot(main,"automation-v053.png");
                int prompts=0;string fake=InspectionService.Serialize(new System.Collections.Generic.Dictionary<string,string>{{"系统/名称","测试系统"},{"CPU/1/型号","测试 CPU"},{"内存/1/厂商","KINGBANK"},{"内存/1/容量","16 GiB"},{"内存/1/配置频率 MHz","3200"},{"主板/1/型号","测试主板"},{"显卡/1/型号","测试显卡"},{"网卡/样例/型号","测试网卡"}});
                main.GetType().GetField("configurationReader",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(main,new Func<System.Threading.CancellationToken,string>(delegate{return fake;}));
                main.GetType().GetField("configurationExportPrompt",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(main,new Func<Form,bool>(delegate{prompts++;return false;}));
                using(var timer=new Timer{Interval=40}){
                    bool clicked=false;timer.Tick+=delegate{foreach(Form f in Application.OpenForms.Cast<Form>().ToArray())if(f.Text=="电脑配置对比"){if(!clicked){clicked=true;Find<Button>(f).Single(b=>b.Text=="采集本机报告").PerformClick();}else if(prompts>0){Check(Find<RichTextBox>(f).Single().Text.Contains("测试 CPU")&&prompts==1,"Collect shows hardware configuration and asks once to export; No preserves the report");Screenshot(f,"configuration-v054.png");f.Close();}break;}};
                    timer.Start();Call(main,"ConfigurationCompareDialog");timer.Stop();
                }
                foreach(string key in new[]{"端口扫描","主机发现","路由与MAC","Wi-Fi分析"})
                {
                    Navigate(main,tree,key);Application.DoEvents();var body=Field<Panel>(main,"content");
                    Check(Find<DataGridView>(body).Length>0&&!Find<Button>(body).Any(b=>b.Text.StartsWith("打开")),"Direct inline page without extra open button: "+key);
                    Check(Application.OpenForms.Count==1,"Inline navigation never launches modal: "+key);
                    if(key=="路由与MAC"||key=="Wi-Fi分析"){var button=Find<Button>(body).Single(b=>b.Text=="刷新信息");Pump(()=>button.Enabled,30000);Check(Find<Panel>(body).Count(p=>p.Name=="resultTabs")==1,"Auto-read completes with structured tables: "+key);}
                }
                var routeFixture=new RouteInventory();routeFixture.Adapters.Add(new RouteAdapterRow{Name="样例网卡",State="已连接",Mac="02:00:00:00:00:01",IPv4="192.0.2.1",Prefix="24",IPv6="2001:db8::1",Gateway4="192.0.2.254",Gateway6="fe80::1",Speed="1000"});
                routeFixture.Routes.Add(new object[]{"样例网卡","IPv4","0.0.0.0/0","192.0.2.254","10","NetMgmt"});routeFixture.Routes.Add(new object[]{"样例网卡","IPv6","::/0","fe80::1","10","NetMgmt"});
                main.GetType().GetField("routeReader",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(main,new Func<System.Threading.CancellationToken,RouteInventory>(delegate{return routeFixture;}));
                Navigate(main,tree,"路由与MAC");Application.DoEvents();var routeBody=Field<Panel>(main,"content");Pump(()=>Find<Button>(routeBody).Single(b=>b.Text=="刷新信息").Enabled,30000);var af=Find<ComboBox>(routeBody).Single(c=>c.AccessibleName=="地址族");var adapter=Find<DataGridView>(routeBody).Single(g=>g.Columns.Count==9);
                Check(adapter.Columns[3].HeaderText=="IPv4"&&adapter.Columns[4].HeaderText=="掩码"&&adapter.Columns[4].Width<=60&&(string)adapter.Rows[0].Cells[3].Value=="192.0.2.1"&&(string)adapter.Rows[0].Cells[4].Value=="24","IPv4 address and numeric mask occupy adjacent compact columns without CIDR or dotted mask");
                Check(adapter.Columns[5].HeaderText=="IPv6"&&(string)adapter.Rows[0].Cells[5].Value=="2001:db8::1","IPv6 appears beside IPv4 and its prefix on the same adapter row");
                af.SelectedIndex=1;Application.DoEvents();Check((string)adapter.Rows[0].Cells[3].Value=="192.0.2.1"&&(string)adapter.Rows[0].Cells[6].Value=="fe80::1","Family switch updates gateway without hiding either local address family");af.SelectedIndex=0;Screenshot(main,"route-v054.png");
                Find<Button>(routeBody).Single(b=>b.Text=="路由表").PerformClick();Application.DoEvents();var routeGrid=Find<DataGridView>(routeBody).Single(g=>g.Name=="routeTable");
                Check(routeGrid.Visible&&routeGrid.Columns[0].HeaderText=="目标"&&routeGrid.Columns[1].HeaderText=="掩码"&&routeGrid.Columns[2].HeaderText=="下一跳"&&(string)routeGrid.Rows[0].Cells[0].Value=="0.0.0.0"&&(int)routeGrid.Rows[0].Cells[1].Value==0,"Route/MAC table splits destination and numeric mask before next hop");Screenshot(main,"route-v055.png");var routeHeaders=routeGrid.Columns.Cast<DataGridViewColumn>().Select(c=>c.HeaderText).ToArray();
                Navigate(main,tree,"网络诊断");Application.DoEvents();var diagnosticBody=Field<Panel>(main,"content");Pump(()=>Find<Button>(diagnosticBody).Single(b=>b.Text=="刷新网卡").Enabled,30000);var routeButton=Find<Button>(diagnosticBody).Single(b=>b.Text=="查看路由表");routeButton.PerformClick();Pump(()=>routeButton.Enabled,30000);var directRoute=Find<DataGridView>(diagnosticBody).Single(g=>g.Name=="routeTable");
                Check(directRoute.Visible&&directRoute.Columns.Cast<DataGridViewColumn>().Select(c=>c.HeaderText).SequenceEqual(routeHeaders)&&(string)directRoute.Rows[0].Cells[2].Value=="192.0.2.254"&&Application.OpenForms.Count==1,"Network diagnostic route action opens the same structured inline schema without command text or extra modal");Screenshot(main,"network-route-v055.png");
                Navigate(main,tree,"系统代理诊断");Application.DoEvents();var proxyBody=Field<Panel>(main,"content");Check(tree.SelectedNode.Parent.Text=="网络诊断"&&Find<DataGridView>(proxyBody).Single().Rows.Count==0&&Find<Label>(proxyBody).Any(l=>l.Text.StartsWith("就绪"))&&!Find<Label>(proxyBody).Any(l=>l.Text.Contains("新增待试用")),"Proxy diagnostic moves into network category and remains idle on entry");
                string studentDir=Path.Combine(Path.GetTempPath(),"students-ui-"+Guid.NewGuid());Directory.CreateDirectory(studentDir);
                try {
                    var students=new System.Collections.Generic.List<StudentMachine>{new StudentMachine{Room="测试机房A",Name="学生机01",IPv4="192.0.2.11",Mac="02:00:00:00:00:11",Note="排版测试"},new StudentMachine{Room="测试机房A",Name="学生机02",IPv4="192.0.2.12",Mac="02:00:00:00:00:12"},new StudentMachine{Room="测试机房B",Name="学生机03",IPv4="192.0.2.13",Mac="02:00:00:00:00:13"}};
                    string rosterPath=Path.Combine(studentDir,"students.tsv");var network=WakeNetwork.Create("测试网卡","192.0.2.1","255.255.255.0");var sends=new System.Collections.Generic.List<string>();
                    main.GetType().GetField("studentMachines",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(main,students);main.GetType().GetField("studentStorePath",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(main,rosterPath);
                    main.GetType().GetField("wakeNetworkReader",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(main,new Func<System.Threading.CancellationToken,System.Collections.Generic.List<WakeNetwork>>(delegate{return new System.Collections.Generic.List<WakeNetwork>{network};}));
                    Func<System.Collections.Generic.IEnumerable<StudentMachine>,WakeNetwork,int,System.Threading.CancellationToken,Action<WakeResult>,System.Threading.Tasks.Task> fakeWake=async delegate(System.Collections.Generic.IEnumerable<StudentMachine> machines,WakeNetwork n,int port,System.Threading.CancellationToken token,Action<WakeResult> receive){foreach(var item in machines){await System.Threading.Tasks.Task.Delay(5,token);sends.Add(item.Mac);receive(new WakeResult{Machine=item,Packets=3});}};
                    main.GetType().GetField("wakeSender",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(main,fakeWake);
                    Navigate(main,tree,"学生机与网络唤醒");Application.DoEvents();var studentBody=Field<Panel>(main,"content");Pump(()=>Find<Button>(studentBody).Single(b=>b.Text=="刷新本机网卡").Enabled,30000);var studentGrid=Find<DataGridView>(studentBody).Single();var room=Find<ComboBox>(studentBody).Single(c=>c.AccessibleName=="机房");
                    Check(tree.SelectedNode.Parent.Text=="学生机管理"&&studentGrid.Rows.Count==3&&sends.Count==0,"Student management has an independent category and entry never sends wake packets");
                    room.SelectedIndex=1;Application.DoEvents();studentGrid.ClearSelection();studentGrid.CurrentCell=studentGrid.Rows[0].Cells[1];studentGrid.Rows[0].Selected=true;var wake=Find<Button>(studentBody).Single(b=>b.Text=="唤醒选中");wake.PerformClick();Pump(()=>wake.Enabled,30000);
                    Check(sends.Count==1&&sends[0]=="02:00:00:00:00:11"&&studentGrid.Rows.Cast<DataGridViewRow>().Any(r=>Convert.ToString(r.Cells[4].Value).Contains("未确认开机")),"Explicit wake acts on selected student and displays unconfirmed delivery status");
                    Find<TextBox>(studentBody).Single(t=>t.AccessibleName=="搜索名称 / IP / MAC").Text="01";var wakeRoom=Find<Button>(studentBody).Single(b=>b.Text=="唤醒该机房");wakeRoom.PerformClick();Pump(()=>wakeRoom.Enabled,30000);
                    Check(sends.Count==3&&sends.Skip(1).OrderBy(x=>x).SequenceEqual(students.Take(2).Select(x=>x.Mac).OrderBy(x=>x)),"Room wake includes that whole room and excludes another room despite search filtering");
                    Find<TextBox>(studentBody).Single(t=>t.AccessibleName=="搜索名称 / IP / MAC").Clear();Application.DoEvents();Screenshot(main,"students-v055.png");
                    bool saved=false;using(var timer=new Timer{Interval=40}){timer.Tick+=delegate{foreach(Form f in Application.OpenForms.Cast<Form>().ToArray())if(f.Text=="添加学生机"){Find<TextBox>(f).Single(t=>t.AccessibleName=="机房").Text="测试机房C";Find<TextBox>(f).Single(t=>t.AccessibleName=="名称").Text="学生机04";Find<TextBox>(f).Single(t=>t.AccessibleName=="IPv4（可空）").Text="192.0.2.14";Find<TextBox>(f).Single(t=>t.AccessibleName=="有线 MAC").Text="02:00:00:00:00:14";saved=true;Find<Button>(f).Single(b=>b.Text=="保存记录").PerformClick();break;}};timer.Start();Find<Button>(studentBody).Single(b=>b.Text=="添加学生机").PerformClick();timer.Stop();}
                    Check(saved&&StudentRoster.Load(rosterPath).Count==4,"Adding a student validates and persists the roster to its configured local file");
                    room.SelectedIndex=room.Items.Count-1;Application.DoEvents();studentGrid.CurrentCell=studentGrid.Rows[0].Cells[1];
                    using(var timer=new Timer{Interval=40}){timer.Tick+=delegate{foreach(Form f in Application.OpenForms.Cast<Form>().ToArray())if(f.Text=="编辑学生机"){Find<TextBox>(f).Single(t=>t.AccessibleName=="名称").Text="学生机04已编辑";Find<Button>(f).Single(b=>b.Text=="保存记录").PerformClick();break;}};timer.Start();Find<Button>(studentBody).Single(b=>b.Text=="编辑学生机").PerformClick();timer.Stop();}
                    Check(StudentRoster.Load(rosterPath).Single(x=>x.Mac=="02:00:00:00:00:14").Name=="学生机04已编辑","Editing a student updates the intended persisted MAC record without dropping other rooms");
                    Navigate(main,tree,"端口扫描");
                }finally{Directory.Delete(studentDir,true);}
                foreach(string key in new[]{"DNS记录查询","ICMP路由跟踪","本机连接与监听","网络配置核对"})
                {
                    Navigate(main,tree,key);Application.DoEvents();var trial=Field<Panel>(main,"content");
                    Check(tree.SelectedNode.Parent.Text.Contains("新增")&&Find<DataGridView>(trial).Single().Rows.Count==0&&Find<Button>(trial).Single(b=>b.Text=="停止").Enabled==false&&Find<Label>(trial).Any(l=>l.Text.StartsWith("待试用")),"Trial remains separate and idle until explicit action: "+key);
                }
                var audit=Field<Panel>(main,"content");var auditGrid=Find<DataGridView>(audit).Single();Check(auditGrid.AutoSizeColumnsMode==DataGridViewAutoSizeColumnsMode.Fill&&auditGrid.Columns[3].MinimumWidth>=260,"Trial IPv4 audit table fills available width with room for explanations");var read=Find<Button>(audit).Single(b=>b.Text=="读取本机信息");read.PerformClick();Pump(()=>read.Enabled,30000);
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
                Check(tables.SelectMany(g=>g.Columns.Cast<DataGridViewColumn>()).Any(c=>c.HeaderText=="基本速率 Mb")&&tables.SelectMany(g=>g.Columns.Cast<DataGridViewColumn>()).Any(c=>c.HeaderText=="接收 Mb"),"Basic and link rates have distinct columns");
                Find<Button>(panel).Single(b=>b.Text=="无线接口 / 当前连接").PerformClick();Application.DoEvents();Screenshot(main,"wifi-v054-interface.png");Find<Button>(panel).Single(b=>b.Text=="附近热点（系统缓存）").PerformClick();Application.DoEvents();Screenshot(main,"wifi-v054-hotspots.png");Check(tables.Single(g=>g.Visible).Columns[8].HeaderText=="基本速率 Mb","Basic rates keep their own compact unit header");
                Check(tables.All(g=>g.Columns[0].Width>=220)&&tables.All(g=>g.Columns.Cast<DataGridViewColumn>().All(c=>!c.HeaderText.Contains("（"))),"SSID has a wide leading column and Wi-Fi headers omit parenthesized units");
                var hotspots=tables.Single(g=>g.Columns[2].HeaderText=="RSSI");Check(hotspots.Columns[12].HeaderText=="声明速率集 Mb"&&(string)hotspots.Rows[0].Cells[3].Value=="82%","RSSI is near the front, declared rate set is last, and percent is attached to signal");
                hotspots.Rows.Add(new WifiBssRow{Ssid="弱信号样例",Signal="9",Rssi="-85"}.Cells());Call(hotspots,"OnColumnHeaderMouseClick",new DataGridViewCellMouseEventArgs(3,-1,0,0,new MouseEventArgs(MouseButtons.Left,1,0,0,0)));Check((string)hotspots.Rows[0].Cells[3].Value=="9%","Signal sorting remains numeric with a percent suffix");
                var grid=tables.First();object originalCell=grid.Rows[0].Cells[0].Value;grid.Rows[0].Cells[0].Value="=SUM(1,2)";string csv=(string)typeof(MainForm).GetMethod("TableCsv",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{grid});Check(csv.Contains("'=SUM(1,2)"),"CSV neutralizes formula injection from network-supplied names");grid.Rows[0].Cells[0].Value=originalCell;
                main.Size=new Size(1000,670);Application.DoEvents();Check(Find<Panel>(panel).Single(p=>p.Name=="resultTabs").Height>180,"Tables remain usable at minimum window size");Screenshot(main,"wifi-v054-compact.png");
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
