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
            grid.SortCompare+=delegate(object sender,DataGridViewSortCompareEventArgs e){double a,b;string x=Convert.ToString(e.CellValue1,CultureInfo.InvariantCulture),y=Convert.ToString(e.CellValue2,CultureInfo.InvariantCulture);if(Double.TryParse(x,NumberStyles.Float,CultureInfo.InvariantCulture,out a)&&Double.TryParse(y,NumberStyles.Float,CultureInfo.InvariantCulture,out b))e.SortResult=a.CompareTo(b);else e.SortResult=StringComparer.OrdinalIgnoreCase.Compare(x,y);e.Handled=true;};
            grid.AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.None;grid.RowTemplate.Height=36;return grid;
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
        private void BuildRouteWorkspace()
        {
            FlowLayoutPanel bar;Label state;Panel root=Workspace("路由与 MAC","网卡、IP、硬件地址、路由和邻居缓存 · 进入页面自动读取",out bar,out state);
            Button refresh=WorkspaceButton(bar,"刷新信息",true),stop=WorkspaceButton(bar,"停止",false),export=WorkspaceButton(bar,"导出当前表",false);
            var tabs=new ResultTabs();DataGridView adapters=AddResultTab(tabs,"网卡 / IP / MAC",new[]{"网卡","状态","MAC 地址","IP 地址","IPv4 掩码","网关","DNS","接口速率（Mbps）"});
            DataGridView routes=AddResultTab(tabs,"路由表",new[]{"网卡","地址族","目标网段","下一跳","跃点数","协议"}),neighbors=AddResultTab(tabs,"邻居 / ARP",new[]{"网卡","IP 地址","MAC 地址","状态","地址族"});root.Controls["pageBody"].Controls.Add(tabs.Tabs);tabs.Tabs.BringToFront();
            var session=new PageSession(root,bar,stop,state);export.Click+=delegate{ExportToolReport(this,TableCsv(tabs.Grids[tabs.Tabs.SelectedIndex]),"路由与MAC.csv");};
            refresh.Click+=async delegate{adapters.Rows.Clear();routes.Rows.Clear();neighbors.Rows.Clear();await session.Run(async delegate(CancellationToken token){RouteInventory data=await Task.Run(new Func<RouteInventory>(delegate{return RouteInventoryService.Read(token);}));if(!session.Alive)return;adapters.Rows.Clear();routes.Rows.Clear();neighbors.Rows.Clear();foreach(var row in data.Adapters)adapters.Rows.Add(row);foreach(var row in data.Routes)routes.Rows.Add(row);foreach(var row in data.Neighbors)neighbors.Rows.Add(row);state.Text=data.Warning.Length==0?"已读取 · 邻居缓存不是完整设备清单":data.Warning;});};
            root.BeginInvoke(new Action(delegate{if(session.Alive)refresh.PerformClick();}));
        }
        private void BuildWifiWorkspace()
        {
            FlowLayoutPanel bar;Label state;Panel root=Workspace("Wi-Fi 分析","接口 MAC 与热点 BSSID 分开显示 · 基本速率、声明速率集与连接收发速率分别列出",out bar,out state);
            Button refresh=WorkspaceButton(bar,"刷新信息",true),stop=WorkspaceButton(bar,"停止",false),export=WorkspaceButton(bar,"导出当前表",false);
            var tabs=new ResultTabs();DataGridView adapters=AddResultTab(tabs,"无线接口 / 当前连接",new[]{"无线网卡","接口 MAC","频率（MHz）","接收（Mbps）","发送（Mbps）","信号（%）","SSID","BSSID / AP MAC","无线制式","频段","信道","网络类型","状态"});
            DataGridView networks=AddResultTab(tabs,"附近热点（系统缓存）",new[]{"SSID","BSSID / AP MAC","无线制式","频段","频率（MHz）","信道","基本速率（Mbps）","信号（%）","声明速率集（Mbps）","网络类型","RSSI（dBm）","加密","所属网卡"});root.Controls["pageBody"].Controls.Add(tabs.Tabs);tabs.Tabs.BringToFront();
            SetColumnWidths(networks,new[]{120,145,85,58,85,50,128,70,220,100,100,80,200});
            SetColumnWidths(adapters,new[]{150,145,90,105,105,70,140,156,90,58,50,100,90});
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
