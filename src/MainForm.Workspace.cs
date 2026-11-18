using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace ITDesktopToolkit
{
    public sealed partial class MainForm
    {
        private sealed class PageSession : IDisposable
        {
            private readonly Control page;private readonly FlowLayoutPanel bar;private readonly Button stop;private readonly Label status;
            private CancellationTokenSource cancel;private bool disposed;public bool Running{get;private set;}
            public bool Alive{get{return !disposed&&!page.IsDisposed;}}
            public PageSession(Control page,FlowLayoutPanel bar,Button stop,Label status){this.page=page;this.bar=bar;this.stop=stop;this.status=status;stop.Enabled=false;stop.Click+=delegate{if(cancel!=null)cancel.Cancel();};page.Disposed+=delegate{Dispose();};}
            public async Task Run(Func<CancellationToken,Task> work)
            {
                if(Running||!Alive)return;Running=true;cancel=new CancellationTokenSource();var states=new Dictionary<Control,bool>();foreach(Control c in bar.Controls){states[c]=c.Enabled;c.Enabled=false;}stop.Enabled=true;status.Text="正在读取 / 检测…";
                try{await work(cancel.Token);}
                catch(OperationCanceledException){if(Alive)status.Text="已停止 · 未完成目标没有检测结论";}
                catch(Exception ex){Paths.Log("Workspace task failed: "+ex);if(Alive){status.Text="读取 / 检测未完成："+ex.Message;status.AccessibleDescription=ex.Message;}}
                finally{Running=false;cancel.Dispose();cancel=null;if(Alive){foreach(var pair in states)if(!pair.Key.IsDisposed)pair.Key.Enabled=pair.Value;stop.Enabled=false;}}
            }
            public void Dispose(){disposed=true;if(cancel!=null)cancel.Cancel();}
        }
        private sealed class ResultTabs : Panel
        {
            private readonly FlowLayoutPanel selector=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,WrapContents=true,Padding=new Padding(0,0,0,8),BackColor=Canvas};
            private readonly Panel body=new Panel{Dock=DockStyle.Fill,BackColor=Color.White};
            private readonly List<Panel> pages=new List<Panel>();private readonly List<Button> buttons=new List<Button>();private int selected;
            public readonly List<DataGridView> Grids=new List<DataGridView>();
            public ResultTabs(){Name="resultTabs";Dock=DockStyle.Fill;BackColor=Canvas;Controls.Add(body);Controls.Add(selector);}
            public ResultTabs Tabs{get{return this;}}
            public int SelectedIndex{get{return selected;}set{if(value<0||value>=pages.Count)throw new ArgumentOutOfRangeException("value");selected=value;for(int i=0;i<pages.Count;i++){pages[i].Visible=i==selected;buttons[i].BackColor=i==selected?Accent:Color.FromArgb(226,232,240);buttons[i].ForeColor=i==selected?Color.White:Color.FromArgb(71,85,105);buttons[i].Font=i==selected?UiActive:Ui;}pages[selected].BringToFront();}}
            public void Add(string title,DataGridView grid)
            {
                int index=pages.Count;var page=new Panel{Dock=DockStyle.Fill,BackColor=Color.White};page.Controls.Add(grid);pages.Add(page);body.Controls.Add(page);Grids.Add(grid);
                var button=new Button{Text=title,Name="resultTab"+index,Width=Math.Max(110,Math.Min(240,title.Length*13+28)),Height=36,FlatStyle=FlatStyle.Flat,Margin=new Padding(0,0,8,0),Cursor=Cursors.Hand};button.FlatAppearance.BorderSize=0;button.Click+=delegate{SelectedIndex=index;};buttons.Add(button);selector.Controls.Add(button);SelectedIndex=selected;
            }
        }
        private Panel Workspace(string title,string description,out FlowLayoutPanel bar,out Label state)
        {
            Panel root=Page(title,description,out bar);bar.WrapContents=true;bar.AutoSize=true;bar.AutoSizeMode=AutoSizeMode.GrowAndShrink;bar.MinimumSize=new Size(0,72);bar.Padding=new Padding(24,0,16,8);
            var body=(Panel)root.Controls["pageBody"];body.Padding=new Padding(24,0,24,18);
            state=new Label{Dock=DockStyle.Bottom,Height=36,Text="就绪",TextAlign=ContentAlignment.MiddleLeft,ForeColor=Color.FromArgb(100,116,139),AutoEllipsis=true,Padding=new Padding(10,0,0,0),BackColor=Color.White};body.Controls.Add(state);var hint=new ToolTip{AutoPopDelay=20000,InitialDelay=300};Label stateLabel=state;stateLabel.TextChanged+=delegate{if(!stateLabel.IsDisposed)hint.SetToolTip(stateLabel,stateLabel.Text);};root.Disposed+=delegate{hint.Dispose();};return root;
        }
        private TextBox WorkspaceInput(FlowLayoutPanel bar,string caption,string text,int width)
        {
            var field=new Panel{Width=width,Height=60,Margin=new Padding(0,0,12,0)};
            field.Controls.Add(new Label{Text=caption,Height=22,Dock=DockStyle.Top,ForeColor=Color.FromArgb(100,116,139),Font=Ui});
            var input=new TextBox{Text=text,Dock=DockStyle.Bottom,Font=new Font(Ui.FontFamily,10.5F),BorderStyle=BorderStyle.FixedSingle,BackColor=Color.White,ForeColor=Navy,AccessibleName=caption};field.Controls.Add(input);bar.Controls.Add(field);return input;
        }
        private Button WorkspaceButton(FlowLayoutPanel bar,string text,bool primary)
        {Button b=Action(bar,text,delegate{},primary);b.Margin=new Padding(0,23,8,0);return b;}
        private DataGridView Table(string[] headers)
        {
            DataGridView grid=Grid();grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.None;grid.MultiSelect=true;grid.ColumnHeadersHeight=40;grid.Font=Ui;
            foreach(string header in headers){int index=grid.Columns.Add("c"+grid.Columns.Count,header);grid.Columns[index].Width=Math.Max(110,Math.Min(220,header.Length*12+34));grid.Columns[index].SortMode=DataGridViewColumnSortMode.Automatic;}
            grid.SortCompare+=delegate(object sender,DataGridViewSortCompareEventArgs e){double a,b;string x=Convert.ToString(e.CellValue1,CultureInfo.InvariantCulture),y=Convert.ToString(e.CellValue2,CultureInfo.InvariantCulture);if(Double.TryParse(x.TrimEnd('%'),NumberStyles.Float,CultureInfo.InvariantCulture,out a)&&Double.TryParse(y.TrimEnd('%'),NumberStyles.Float,CultureInfo.InvariantCulture,out b))e.SortResult=a.CompareTo(b);else e.SortResult=StringComparer.OrdinalIgnoreCase.Compare(x,y);e.Handled=true;};
            grid.AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.None;grid.RowTemplate.Height=36;return grid;
        }
        private sealed class SignalRowComparer : System.Collections.IComparer
        {
            private readonly int column,direction;
            public SignalRowComparer(int column,bool ascending){this.column=column;direction=ascending?1:-1;}
            public int Compare(object left,object right)
            {
                string x=Convert.ToString(((DataGridViewRow)left).Cells[column].Value,CultureInfo.InvariantCulture),y=Convert.ToString(((DataGridViewRow)right).Cells[column].Value,CultureInfo.InvariantCulture);double a,b;
                bool hasA=Double.TryParse(x.TrimEnd('%'),NumberStyles.Float,CultureInfo.InvariantCulture,out a),hasB=Double.TryParse(y.TrimEnd('%'),NumberStyles.Float,CultureInfo.InvariantCulture,out b);
                return direction*(hasA&&hasB?a.CompareTo(b):hasA?-1:hasB?1:StringComparer.OrdinalIgnoreCase.Compare(x,y));
            }
        }
        private static void ConfigureSignalSort(DataGridView grid,int index)
        {
            grid.Columns[index].SortMode=DataGridViewColumnSortMode.Programmatic;
            grid.ColumnHeaderMouseClick+=delegate(object sender,DataGridViewCellMouseEventArgs e){
                if(e.ColumnIndex!=index||e.Button!=MouseButtons.Left)return;bool ascending=grid.Columns[index].HeaderCell.SortGlyphDirection!=SortOrder.Ascending;
                grid.Sort(new SignalRowComparer(index,ascending));foreach(DataGridViewColumn column in grid.Columns)column.HeaderCell.SortGlyphDirection=SortOrder.None;grid.Columns[index].HeaderCell.SortGlyphDirection=ascending?SortOrder.Ascending:SortOrder.Descending;
            };
        }
        private DataGridView AddResultTab(ResultTabs results,string title,string[] headers)
        {
            var grid=Table(headers);results.Add(title,grid);return grid;
        }
        private static void SetColumnWidths(DataGridView grid,int[] widths)
        {for(int i=0;i<widths.Length;i++)grid.Columns[i].Width=widths[i];}

        private static string CsvCell(object value)
        {
            string text=Convert.ToString(value,CultureInfo.InvariantCulture)??"";
            if(value is string&&text.TrimStart(' ','\t','\r','\n').Length>0){char first=text.TrimStart(' ','\t','\r','\n')[0];if(first=='='||first=='+'||first=='-'||first=='@')text="'"+text;}
            return "\""+text.Replace("\"","\"\"")+"\"";
        }
        internal static string TableCsv(DataGridView grid)
        {
            var columns=new List<DataGridViewColumn>();foreach(DataGridViewColumn column in grid.Columns)columns.Add(column);columns.Sort(delegate(DataGridViewColumn a,DataGridViewColumn b){return a.DisplayIndex.CompareTo(b.DisplayIndex);});
            var text=new StringBuilder();var headers=new List<string>();foreach(var column in columns)headers.Add(CsvCell(column.HeaderText));text.AppendLine(String.Join(",",headers.ToArray()));
            foreach(DataGridViewRow row in grid.Rows){var cells=new List<string>();foreach(var column in columns)cells.Add(CsvCell(row.Cells[column.Index].Value));text.AppendLine(String.Join(",",cells.ToArray()));}return text.ToString();
        }
        private sealed class ScanRows
        {
            private readonly object gate=new object();private readonly List<object[]> rows=new List<object[]>();private int shown,positive;private readonly bool discovery;
            public ScanRows(bool discovery){this.discovery=discovery;}
            public void AddHost(HostProbe r){lock(gate){if(r.Responded)positive++;rows.Add(new object[]{r.Address,r.Responded?"有响应":r.Icmp=="TimedOut"?"未响应":r.Icmp,r.Milliseconds.HasValue?(object)r.Milliseconds.Value:"未知"});}}
            public void AddPort(PortProbe r){lock(gate){if(r.State=="开放")positive++;rows.Add(new object[]{r.Host,r.Port,CommonPorts.Service(r.Port),r.State,r.Time.ToString("HH:mm:ss")});}}
            public void Render(DataGridView grid,Label state){lock(gate){while(shown<rows.Count){grid.Rows.Add(rows[shown]);shown++;}state.Text="已完成 "+shown+" · "+(discovery?"Ping 响应":"开放端口")+" "+positive;}}
        }
        private void BuildScanWorkspace(bool discovery)
        {
            FlowLayoutPanel bar;Label state;string title=discovery?"主机发现":"端口扫描";
            Panel root=Workspace(title,discovery?"使用 ICMP Ping 发现设备，不依赖 TCP 服务端口。":"常用 TCP 端口预设 · 可自定义范围 · 不发送应用数据",out bar,out state);
            TextBox target=WorkspaceInput(bar,discovery?"IPv4 或网段（如 192.168.1.0/24）":"目标主机名 / IP","",260);
            TextBox ports=discovery?null:WorkspaceInput(bar,"TCP 端口（逗号或范围）",CommonPorts.DefaultText,300);
            TextBox workers=WorkspaceInput(bar,"并发（1–32）","16",92),timeout=WorkspaceInput(bar,"超时（毫秒）","800",112);
            Button start=WorkspaceButton(bar,discovery?"开始 Ping":"开始扫描",true),stop=WorkspaceButton(bar,"停止",false),export=WorkspaceButton(bar,"导出 CSV",false);
            if(!discovery){Button preset=WorkspaceButton(bar,"常用 TCP（"+CommonPorts.Count+" 项）",false);preset.Click+=delegate{ports.Text=CommonPorts.DefaultText;};}
            var grid=Table(discovery?new[]{"IP 地址","Ping 状态","往返延迟（ms）"}:new[]{"IP 地址","TCP 端口","常见服务（参考）","连接状态","检测时间"});root.Controls["pageBody"].Controls.Add(grid);grid.BringToFront();
            grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill;
            grid.CellFormatting+=delegate(object sender,DataGridViewCellFormattingEventArgs e){if(Convert.ToString(e.Value)=="开放"||Convert.ToString(e.Value)=="有响应"){e.CellStyle.ForeColor=Color.FromArgb(21,128,61);e.CellStyle.SelectionForeColor=Color.FromArgb(21,128,61);}};
            var session=new PageSession(root,bar,stop,state);export.Click+=delegate{ExportToolReport(this,TableCsv(grid),title+".csv");};state.Text=discovery?"请输入网段 · 未响应不代表设备离线":"预设包含 "+CommonPorts.Count+" 个常用 TCP 端口 · 服务名称仅供参考，UDP 不在此测试范围";
            start.Click+=async delegate{
                int parallel,ms;int[] chosen=null;string host=target.Text.Trim();
                try{if(!Int32.TryParse(workers.Text,out parallel)||parallel<1||parallel>32||!Int32.TryParse(timeout.Text,out ms)||ms<100||ms>10000)throw new ArgumentException("并发 1–32，超时 100–10000 毫秒。");if(discovery)NetworkDiscoveryService.ParseSubnet(host);else{NetworkDiscoveryService.SingleHost(host);chosen=NetworkDiscoveryService.ParsePorts(ports.Text);}}catch(Exception ex){state.Text=ex.Message;return;}
                grid.Rows.Clear();var buffer=new ScanRows(discovery);Action<HostProbe> onHost=new Action<HostProbe>(buffer.AddHost);Action<PortProbe> onPort=new Action<PortProbe>(buffer.AddPort);
                Action update=delegate{if(session.Alive)buffer.Render(grid,state);};
                using(var timer=new System.Windows.Forms.Timer{Interval=300})
                {
                    timer.Tick+=delegate{update();};EventHandler stopTimer=delegate{timer.Stop();};root.Disposed+=stopTimer;timer.Start();
                    try{await session.Run(async delegate(CancellationToken token){
                        try
                        {
                            if(discovery)await NetworkDiscoveryService.Discover(host,parallel,ms,token,onHost);
                            else await NetworkDiscoveryService.ScanPorts(host,chosen,parallel,ms,token,onPort);
                        }
                        finally{update();}
                        if(session.Alive)state.Text+=" · 本轮完成";
                    });}finally{timer.Stop();root.Disposed-=stopTimer;}
                }
            };
        }
        private static void FillTable(DataGridView grid,IEnumerable<object[]> source)
        {
            var rows=new List<DataGridViewRow>();foreach(var cells in source){var row=new DataGridViewRow();row.CreateCells(grid,cells);rows.Add(row);}
            grid.SuspendLayout();try{grid.Rows.Clear();if(rows.Count>0)grid.Rows.AddRange(rows.ToArray());}finally{grid.ResumeLayout();}
        }
        private DataGridView RouteTable()
        {
            var grid=Table(new[]{"目标","掩码","下一跳","网卡","跃点","协议","地址族"});grid.Name="routeTable";
            SetColumnWidths(grid,new[]{180,60,180,145,70,100,80});grid.Columns[1].ToolTipText="前缀长度，IPv4 为 0–32，IPv6 为 0–128";return grid;
        }
        private static void BindRouteTable(DataGridView grid,RouteInventory data,string family)
        {
            var rows=new List<object[]>();foreach(var row in data.Routes)if(Convert.ToString(row[1])==family)rows.Add(RouteInventoryService.RouteCells(row));
            grid.Columns[0].Width=family=="IPv6"?320:180;grid.Columns[2].Width=family=="IPv6"?320:180;FillTable(grid,rows);
        }
        private Func<CancellationToken,RouteInventory> routeReader=RouteInventoryService.Read;
        private void BuildRouteWorkspace()
        {
            FlowLayoutPanel bar;Label state;Panel root=Workspace("路由与 MAC","IPv4、掩码位数、IPv6 相邻显示；切换地址族查看网关、DNS、路由与邻居",out bar,out state);
            var family=new ComboBox{Width=110,DropDownStyle=ComboBoxStyle.DropDownList,Margin=new Padding(0,23,12,0),AccessibleName="地址族"};family.Items.AddRange(new object[]{"IPv4","IPv6"});family.SelectedIndex=0;bar.Controls.Add(family);
            Button refresh=WorkspaceButton(bar,"刷新信息",true),stop=WorkspaceButton(bar,"停止",false),export=WorkspaceButton(bar,"导出当前表",false);
            var tabs=new ResultTabs();DataGridView adapters=AddResultTab(tabs,"网卡 / IP / MAC",new[]{"网卡","状态","MAC","IPv4","掩码","IPv6","IPv4 网关","IPv4 DNS","速率 Mb"});
            DataGridView routes=RouteTable();tabs.Add("路由表",routes);DataGridView neighbors=AddResultTab(tabs,"邻居 / ARP",new[]{"网卡","IP 地址","MAC 地址","状态","地址族"});
            SetColumnWidths(adapters,new[]{110,68,142,128,48,235,132,175,75});SetColumnWidths(neighbors,new[]{180,200,180,90,80});
            adapters.Columns[1].MinimumWidth=68;neighbors.Columns[3].MinimumWidth=90;
            adapters.Columns[4].ToolTipText="IPv4 子网掩码的前缀长度，例如 255.255.255.0 显示为 24";
            adapters.Columns[8].ToolTipText="接口链路速率，单位 Mb/s（Mbps）；不是实际下载速度";
            root.Controls["pageBody"].Controls.Add(tabs);tabs.BringToFront();
            var session=new PageSession(root,bar,stop,state);RouteInventory data=null;
            Action render=delegate {
                string selected=family.Text;bool ipv6=selected=="IPv6";
                adapters.Columns[6].HeaderText=selected+" 网关";adapters.Columns[7].HeaderText=selected+" DNS";
                adapters.Columns[6].Width=ipv6?260:132;adapters.Columns[7].Width=ipv6?290:175;neighbors.Columns[1].Width=ipv6?330:175;
                var ar=new List<object[]>();var nr=new List<object[]>();
                if(data!=null){foreach(var row in data.Adapters)ar.Add(row.Cells(selected));foreach(var row in data.Neighbors)if(Convert.ToString(row[4])==selected)nr.Add(row);}
                FillTable(adapters,ar);if(data!=null)BindRouteTable(routes,data,selected);FillTable(neighbors,nr);
                if(data!=null)state.Text=data.Warning.Length==0?"已读取 · 当前 "+selected+" · 邻居缓存不是完整设备清单":data.Warning;
            };
            family.SelectedIndexChanged+=delegate{render();};
            export.Click+=delegate{ExportToolReport(this,TableCsv(tabs.Grids[tabs.SelectedIndex]),"路由与MAC_"+family.Text+".csv");};
            refresh.Click+=async delegate{await session.Run(async delegate(CancellationToken token){var fresh=await Task.Run(new Func<RouteInventory>(delegate{return routeReader(token);}));if(!session.Alive)return;data=fresh;render();});};
            root.BeginInvoke(new Action(delegate{if(session.Alive)refresh.PerformClick();}));
        }
        private void BuildWifiWorkspace()
        {
            FlowLayoutPanel bar;Label state;Panel root=Workspace("Wi-Fi 分析","接口 MAC 与热点 BSSID 分开显示 · 基本速率、声明速率集与连接收发速率分别列出",out bar,out state);
            Button refresh=WorkspaceButton(bar,"刷新信息",true),stop=WorkspaceButton(bar,"停止",false),export=WorkspaceButton(bar,"导出当前表",false);
            var tabs=new ResultTabs();DataGridView adapters=AddResultTab(tabs,"无线接口 / 当前连接",new[]{"SSID","MAC","BSSID","信号","频率","接收 Mb","发送 Mb","制式","频段","信道","类型","状态","网卡"});
            DataGridView networks=AddResultTab(tabs,"附近热点（系统缓存）",new[]{"SSID","BSSID","RSSI","信号","制式","频段","频率","信道","基本速率 Mb","加密","类型","网卡","声明速率集 Mb"});root.Controls["pageBody"].Controls.Add(tabs.Tabs);tabs.Tabs.BringToFront();
            SetColumnWidths(networks,new[]{240,150,65,65,85,58,70,55,115,80,95,190,230});
            networks.Columns[0].MinimumWidth=220;networks.Columns[2].ToolTipText="RSSI，单位 dBm";networks.Columns[6].ToolTipText="频率，单位 MHz";networks.Columns[8].ToolTipText="基本速率，单位 Mb/s";networks.Columns[12].ToolTipText="声明支持速率集，单位 Mb/s；不是下载速度";
            SetColumnWidths(adapters,new[]{240,150,150,65,75,95,95,90,58,55,95,85,190});
            adapters.Columns[0].MinimumWidth=220;adapters.Columns[4].ToolTipText="频率，单位 MHz";adapters.Columns[5].ToolTipText="接收链路速率，单位 Mb/s";adapters.Columns[6].ToolTipText="发送链路速率，单位 Mb/s";
            ConfigureSignalSort(adapters,3);ConfigureSignalSort(networks,3);
            tabs.SelectedIndex=1;
            root.Tag=new object[]{adapters,networks,state};var session=new PageSession(root,bar,stop,state);
            export.Click+=delegate{ExportToolReport(this,TableCsv(tabs.Grids[tabs.Tabs.SelectedIndex]),"WiFi分析.csv");};
            refresh.Click+=async delegate{adapters.Rows.Clear();networks.Rows.Clear();await session.Run(async delegate(CancellationToken token){var data=await Task.Run(new Func<WifiInventory>(delegate{return WifiInventoryService.Read(token);}));if(!session.Alive)return;BindWifi(root,data);});};
            root.BeginInvoke(new Action(delegate{if(session.Alive)refresh.PerformClick();}));
        }
        private void BindWifi(Panel root,WifiInventory data)
        {
            var controls=(object[])root.Tag;var adapters=(DataGridView)controls[0];var networks=(DataGridView)controls[1];var state=(Label)controls[2];adapters.Rows.Clear();networks.Rows.Clear();foreach(var row in data.Adapters)adapters.Rows.Add(row.Cells());foreach(var row in data.Networks)networks.Rows.Add(row.Cells());
            state.Text=data.Notices.Count>0?String.Join("；",data.Notices.ToArray()):data.Adapters.Count==0?"未发现无线网卡":"无线接口 "+data.Adapters.Count+" · 系统缓存热点 "+data.Networks.Count+" · 连接速率不是下载速度；未提供的字段显示未知";
        }
    }
}
