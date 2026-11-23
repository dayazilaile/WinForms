using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WinForms
{
    public sealed partial class MainForm : Form
    {
        private static readonly Color Navy = Color.FromArgb(15, 23, 42);
        private static readonly Color Accent = Color.FromArgb(37, 99, 235);
        private static readonly Color Canvas = Color.FromArgb(244, 247, 251);
        private static readonly Font Ui = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular);
        private static readonly Font UiActive = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
        private DatabaseService db = new DatabaseService();
        private Task<DatabaseService> databaseLoad;
        private readonly Panel content = new Panel();
        private readonly Label status = new Label();
        private List<HardwareItem> inventory = new List<HardwareItem>();
        private FaultScanResult lastScan;
        private string currentPage = "";

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);

        public MainForm()
        {
            Text = ProductInfo.WindowTitle;
            Width = 1220;
            Height = 790;
            MinimumSize = new Size(1000, 670);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Canvas;
            Font = Ui;
            AutoScaleMode = AutoScaleMode.Dpi;
            BeginDatabaseLoad();

            Panel sidebar = new Panel { Dock = DockStyle.Left, Width = 260, BackColor = Navy };
            Panel footer = new Panel { Dock = DockStyle.Bottom, Height = 30, BackColor = Color.FromArgb(237, 241, 247) };
            status.Dock = DockStyle.Fill;
            status.TextAlign = ContentAlignment.MiddleLeft;
            status.Padding = new Padding(12, 0, 0, 0);
            status.ForeColor = Color.FromArgb(64, 74, 91);
            status.Font = new Font("Microsoft YaHei UI", 9F);
            footer.Controls.Add(status);
            content.Dock = DockStyle.Fill;
            content.BackColor = Canvas;
            Controls.Add(content);
            Controls.Add(footer);
            Controls.Add(sidebar);

            BuildNavigation(sidebar);
        }

        private void BeginDatabaseLoad()
        {
            databaseLoad=Task.Run(new Func<DatabaseService>(delegate {
                var snapshot=new DatabaseService();snapshot.Reload();FaultCatalog.Reload();GenericCodeCatalog.Reload();return snapshot;
            }));
        }
        private async Task<DatabaseService> ReadyDatabase()
        {
            Task<DatabaseService> pending;
            DatabaseService snapshot;
            do { pending=databaseLoad; snapshot=await pending; } while(pending!=databaseLoad);
            db=snapshot;return snapshot;
        }
        private async void UpdateDatabaseDescription(RichTextBox box)
        {
            box.Text="正在后台读取离线数据库…";
            try { await ReadyDatabase(); if(!box.IsDisposed)box.Text=DbDescription(); }
            catch(Exception ex){if(!box.IsDisposed)box.Text="数据库读取失败："+ex.Message;Paths.Log("Load DB: "+ex);}
        }
        private void SetStatus(string message)
        {
            status.Text = (Paths.LastLogError.Length>0?"[软件目录日志不可写] ":"")+"权限：" + (CommandService.IsAdministrator() ? "管理员" : "普通用户") + "     |     " + message;
        }
        private void ShowPage(string name)
        {
            if(NavigationCatalog.Find(name)==null)return;
            currentPage = name;
            while(content.Controls.Count>0){Control old=content.Controls[0];content.Controls.Remove(old);old.Dispose();}
            switch (name)
            {
                case "系统概览": BuildOverview(); break;
                case "详细硬件配置": BuildHardware(); break;
                case "网络诊断": BuildNetwork(); break;
                case "蓝屏与异常关机": BuildFaults(); break;
                case "系统修复": BuildRepairs(); break;
                case "功能开关": BuildSwitches(); break;
                case "打印机与服务": BuildPrinter(); break;
                case "启动项管理": BuildStartup(); break;
                case "系统优化": BuildOptimize(); break;
                case "共享文件夹": BuildShares(); break;
                case "BAT 自动化": BuildAutomation(); break;
                case "Windows 管理入口": BuildToolLaunchers(); break;
                case "电脑配置对比": BuildFieldTool("电脑配置对比", "采集系统、架构、CPU、主板、内存、显卡、磁盘和网卡配置，导出或对比。", ConfigurationCompareDialog); break;
                case "网络持续检测": BuildFieldTool("网络持续检测", "持续检测网关/服务器的延迟、响应异常和波动，可导出逐条 CSV。", ContinuousNetworkDialog); break;
                case "软件环境检查": BuildSoftwareWorkspace(); break;
                case "U 盘检查与防护": BuildFieldTool("U 盘检查与防护", "只读检查可疑迹象、查看杀毒记录，设置所选 NTFS U 盘普通用户只读权限及本机策略。", UsbProtectionDialog); break;
                case "端口扫描": BuildScanWorkspace(false); break;
                case "主机发现": BuildScanWorkspace(true); break;
                case "路由与MAC": BuildRouteWorkspace(); break;
                case "DHCP与网络服务": BuildFieldTool(name,"查看 DHCP 租约/服务器配置，检查 DNS、HTTP 与 TCP 服务。",delegate{NetworkInformationDialog(1);}); break;
                case "Wi-Fi分析": BuildWifiWorkspace(); break;
                case "已保存的Wi-Fi": BuildSavedWifiWorkspace(); break;
                case "摄像头发现": BuildFieldTool(name,"通过 WS-Discovery 发现支持 ONVIF 的局域网设备。",CameraDiscoveryDialog); break;
                case "DNS记录查询":case "ICMP路由跟踪":case "本机连接与监听":case "系统代理诊断":case "网络配置核对":BuildDiagnosticWorkspace(name);break;
                case "学生机与网络唤醒": BuildStudentWorkspace(); break;
                case "离线数据库": BuildDatabase(); break;
            }
            SetStatus("当前页面：" + name + "   |   数据库：硬件 " + db.HardwareRuleCount + " 条 / 蓝屏 " + db.BugcheckRuleCount + " 条");
        }

        private Panel Page(string name, string description, out FlowLayoutPanel toolbar)
        {
            Panel p = new Panel { Dock = DockStyle.Fill, BackColor = Canvas };
            Panel header = new Panel { Dock = DockStyle.Top, Height = 92, BackColor = Canvas };
            Label title = new Label { Text = name, Location = new Point(25, 13), AutoSize = true, ForeColor = Navy,
                Font = new Font("Microsoft YaHei UI", 18F, FontStyle.Bold) };
            Label subtitle = new Label { Text = description, Location = new Point(27, 55), AutoSize = true,
                ForeColor = Color.FromArgb(99, 110, 125), Font = new Font("Microsoft YaHei UI", 9F) };
            header.Controls.Add(title);
            header.Controls.Add(subtitle);
            toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 58, BackColor = Canvas, Padding = new Padding(21, 8, 5, 5),
                WrapContents = false, AutoScroll = true, FlowDirection = FlowDirection.LeftToRight };
            Panel body = new Panel { Name = "pageBody", Dock = DockStyle.Fill, BackColor = Canvas };
            p.Controls.Add(body);
            p.Controls.Add(toolbar);
            p.Controls.Add(header);
            content.Controls.Add(p);
            return p;
        }
        private Button Action(FlowLayoutPanel bar, string text, EventHandler handler, bool primary)
        {
            Button b = new Button { Text = text, AutoSize = false, Height = 36, Width = Math.Max(116, text.Length * 13 + 29),
                Margin = new Padding(4, 2, 8, 2), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
                BackColor = primary ? Accent : Color.White, ForeColor = primary ? Color.White : Navy };
            b.FlatAppearance.BorderColor = Color.FromArgb(204, 216, 231);
            b.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(31, 87, 186) : Color.FromArgb(237, 243, 253);
            b.FlatAppearance.BorderSize = primary ? 0 : 1;
            b.Click += handler;
            bar.Controls.Add(b);
            return b;
        }
        private RichTextBox ReadBox()
        {
            return new RichTextBox { ReadOnly = true, DetectUrls = false, BorderStyle = BorderStyle.None,
                Font = new Font("Microsoft YaHei UI", 10F), BackColor = Color.White, ForeColor = Color.FromArgb(37, 50, 68),
                WordWrap = true, ScrollBars = RichTextBoxScrollBars.Vertical, Dock = DockStyle.Fill };
        }
        private Panel ContentCard(Control child)
        {
            Panel outer = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 6, 24, 20), BackColor = Canvas };
            Panel inner = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(18) };
            child.Dock = DockStyle.Fill;
            inner.Controls.Add(child);
            outer.Controls.Add(inner);
            return outer;
        }
        private DataGridView Grid()
        {
            DataGridView g = new DataGridView();
            g.Dock = DockStyle.Fill;
            g.ReadOnly = true;
            g.AllowUserToAddRows = false;
            g.AllowUserToDeleteRows = false;
            g.AllowUserToResizeRows = false;
            g.RowHeadersVisible = false;
            g.MultiSelect = false;
            g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            g.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            g.BackgroundColor = Color.White;
            g.BorderStyle = BorderStyle.None;
            g.ColumnHeadersHeight = 38;
            g.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
            g.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
            g.RowTemplate.Height = 33;
            g.EnableHeadersVisualStyles = false;
            g.GridColor = Color.FromArgb(229, 234, 242);
            g.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            g.DefaultCellStyle.Padding = new Padding(5, 0, 5, 0);
            g.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(241, 245, 249);
            g.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(71,85,105);
            g.ColumnHeadersDefaultCellStyle.Font = UiActive;
            g.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248,250,252);
            g.DefaultCellStyle.ForeColor = Navy;
            g.DefaultCellStyle.SelectionBackColor = Color.FromArgb(219, 234, 254);
            g.DefaultCellStyle.SelectionForeColor = Navy;
            g.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(244, 247, 251);
            g.Font = Ui;
            return g;
        }
        private void AddColumn(DataGridView g, string name, string title, int width, bool fill)
        {
            DataGridViewTextBoxColumn c = new DataGridViewTextBoxColumn();
            c.Name = name;
            c.HeaderText = title;
            c.Width = width;
            if (fill) { c.MinimumWidth = Math.Min(width, 280); c.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill; }
            g.Columns.Add(c);
        }
        private void SaveText(string defaultName, string text)
        {
            using (SaveFileDialog s = new SaveFileDialog())
            {
                s.Filter = "文本报告 (*.txt)|*.txt";
                s.FileName = defaultName;
                s.InitialDirectory = Paths.BaseDir;
                if (s.ShowDialog(this) == DialogResult.OK)
                {
                    try
                    {
                        File.WriteAllText(s.FileName, text, new UTF8Encoding(true));
                        Paths.Log("Report saved: " + s.FileName);
                        MessageBox.Show(this, "报告已保存：\r\n" + s.FileName, "导出成功");
                    }
                    catch (Exception ex) { MessageBox.Show(this, "保存失败：" + ex.Message); }
                }
            }
        }
        private async void DoJob(RichTextBox box, Button button, Func<string> work)
        {
            using(var audit=OperationAudit.Begin(currentPage+" / "+button.Text))
            {
            button.Enabled = false;
            box.Text = "正在执行，请不要关闭程序……\r\n";
            SetStatus("正在运行任务……");
            try
            {
                string output = await Task.Run(work);
                audit.End(OperationAudit.Outcome(output),"返回内容仅显示在界面，未写入日志。");
                if (!this.IsDisposed && !box.IsDisposed && box.IsHandleCreated) box.Text = output;
                if(!this.IsDisposed) SetStatus("任务已结束，请查看实际结果和返回码");
            }
            catch (Exception ex)
            {
                if (!this.IsDisposed && !box.IsDisposed && box.IsHandleCreated) box.Text = "任务执行异常：\r\n" + ex;
                if(!this.IsDisposed) SetStatus("任务失败");
                audit.End("失败",ex.Message);
            }
            finally { if (!button.IsDisposed) button.Enabled = true; }
            }
        }

        private void BuildOverview()
        {
            FlowLayoutPanel bar;
            Panel p = Page("系统概览", "实时读取 Windows、CPU、主板、内存与磁盘信息；不会修改系统。", out bar);
            RichTextBox box = ReadBox();
            Panel card = ContentCard(box);
            p.Controls["pageBody"].Controls.Add(card);
            Button refresh = null;
            refresh = Action(bar, "刷新系统信息", delegate { DoJob(box, refresh, HardwareService.GetOverview); }, true);
            Action(bar, "导出 TXT", delegate { SaveText("系统配置_" + Environment.MachineName + ".txt", box.Text); }, false);
            Action(bar, "修改计算机名称", delegate { ComputerNameDialog(); }, false);
            Action(bar, "管理员模式重启", delegate { if (CommandService.RelaunchAsAdmin()) Close(); }, false);
            DoJob(box, refresh, HardwareService.GetOverview);
        }

        private void BuildHardware()
        {
            FlowLayoutPanel bar;
            Panel p = Page("详细硬件配置", "包含 CPU、主板、内存、显卡、显示器（EDID）、磁盘和物理网卡；支持品牌型号匹配。", out bar);
            SplitContainer split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 350,
                BackColor = Canvas, Panel1MinSize = 140, Panel2MinSize = 105 };
            DataGridView grid = Grid();
            AddColumn(grid, "type", "类别", 72, false);
            AddColumn(grid, "brand", "品牌/厂商", 125, false);
            AddColumn(grid, "model", "详细型号 / 零件号", 260, true);
            AddColumn(grid, "name", "设备 / 容量 / 插槽", 230, false);
            AddColumn(grid, "state", "来源", 100, false);
            split.Panel1.Controls.Add(grid);
            RichTextBox detail = ReadBox();
            split.Panel2.Controls.Add(detail);
            grid.SelectionChanged += delegate
            {
                if (grid.SelectedRows.Count > 0 && grid.SelectedRows[0].Tag is HardwareItem)
                    detail.Text = ((HardwareItem)grid.SelectedRows[0].Tag).Details();
            };
            Panel card = ContentCard(split);
            p.Controls["pageBody"].Controls.Add(card);
            var cancel=new CancellationTokenSource();CancellationToken token=cancel.Token;p.Disposed+=delegate{cancel.Cancel();cancel.Dispose();};
            Button scan = null;
            scan = Action(bar, "扫描硬件", async delegate
            {
                scan.Enabled = false;
                detail.Text = "正在读取设备清单……";
                try
                {
                    DatabaseService source = await ReadyDatabase();
                    if (scan.IsDisposed) return;
                    var fresh = await Task.Run(delegate { return HardwareService.Inventory(source,token); });
                    if (grid.IsDisposed || detail.IsDisposed) return;
                    inventory=fresh;
                    grid.Rows.Clear();
                    foreach (HardwareItem x in inventory)
                    {
                        int i = grid.Rows.Add(x.Type, x.DisplayBrand, x.DisplayModel, x.Name, x.MatchState);
                        grid.Rows[i].Tag = x;
                        if (x.Type == "异常设备") grid.Rows[i].DefaultCellStyle.BackColor = Color.FromArgb(255, 233, 232);
                        if (x.Type == "已禁用设备") grid.Rows[i].DefaultCellStyle.BackColor = Color.FromArgb(255, 248, 217);
                    }
                    int errors = 0, disabled = 0;
                    foreach (HardwareItem x in inventory) { if (x.Type == "异常设备") errors++; if (x.Type == "已禁用设备") disabled++; }
                    detail.Text = "主要硬件/显示器/网卡：" + (inventory.Count - errors - disabled) + " 条\r\n" +
                        "异常设备：" + errors + " 条（红色）\r\n" +
                        "已禁用设备：" + disabled + " 条（黄色，不一定故障）\r\n\r\n" +
                        "显示器优先读取 EDID，读取不到时使用 PnP / Windows 显示设备信息；多屏不强行对应顺序。\r\n" +
                        "已隐藏普通 USB 外设、蓝牙 PAN、回环与虚拟网卡。\r\n" +
                        "选择上方设备查看原始型号、硬件 ID、规格和匹配依据。";
                    SetStatus("硬件扫描完成：" + inventory.Count + " 条");
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { if (!detail.IsDisposed) detail.Text = "硬件扫描异常：" + ex; }
                finally { if (!scan.IsDisposed) scan.Enabled = true; }
            }, true);
            Action(bar, "重新加载数据库", async delegate
            {
                try { BeginDatabaseLoad(); await ReadyDatabase(); if (!p.IsDisposed) MessageBox.Show(this, "已重新加载数据库。请重新点击扫描硬件。", "完成"); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message); }
            }, false);
            Action(bar, "导出详细配置", delegate
            {
                StringBuilder s = new StringBuilder("硬件检测报告\r\n导出：" + DateTime.Now + "\r\n\r\n");
                foreach (HardwareItem x in inventory) s.AppendLine(x.Details() + "\r\n" + new string('-', 65));
                SaveText("硬件清单_" + Environment.MachineName + ".txt", s.ToString());
            }, false);
            scan.PerformClick();
        }

        private void BuildNetwork()
        {
            FlowLayoutPanel bar;Label state;Panel root=Workspace("网络诊断","网卡信息与路由表分开显示；路由表按目标、掩码、下一跳排列",out bar,out state);
            var host=new Panel{Dock=DockStyle.Fill,BackColor=Color.White};RichTextBox box=ReadBox();DataGridView routes=RouteTable();routes.Visible=false;host.Controls.Add(box);host.Controls.Add(routes);root.Controls["pageBody"].Controls.Add(host);host.BringToFront();
            Button refresh=WorkspaceButton(bar,"刷新网卡",true),test=WorkspaceButton(bar,"Ping / DNS / 端口",false),settings=WorkspaceButton(bar,"设置 IP / DNS / MAC",false),route=WorkspaceButton(bar,"查看路由表",false),all=WorkspaceButton(bar,"ipconfig /all",false),stop=WorkspaceButton(bar,"停止",false),export=WorkspaceButton(bar,"导出当前结果",false);
            var family=new ComboBox{Width=100,DropDownStyle=ComboBoxStyle.DropDownList,Margin=new Padding(0,23,8,0),AccessibleName="路由地址族",Visible=false};family.Items.AddRange(new object[]{"IPv4","IPv6"});family.SelectedIndex=0;bar.Controls.Add(family);
            var session=new PageSession(root,bar,stop,state);RouteInventory routeData=null;
            Func<Func<CancellationToken,string>,Task> readText=async delegate(Func<CancellationToken,string> read){
                box.Visible=true;routes.Visible=false;family.Visible=false;box.BringToFront();
                await session.Run(async delegate(CancellationToken token){string value=await Task.Run(new Func<string>(delegate{token.ThrowIfCancellationRequested();var text=read(token);token.ThrowIfCancellationRequested();return text;}));if(session.Alive){box.Text=value;state.Text="本机信息已读取，请核对实际返回内容";}});
            };
            refresh.Click+=async delegate{await readText(delegate{return NetworkService.GetReport();});};
            all.Click+=async delegate{await readText(delegate(CancellationToken token){return CommandService.Run("ipconfig.exe","/all",2,token);});};
            test.Click+=delegate{box.Visible=true;routes.Visible=false;family.Visible=false;box.BringToFront();NetworkDialog(box);};settings.Click+=delegate{NetworkConfigDialog();};
            family.SelectedIndexChanged+=delegate{if(routeData!=null)BindRouteTable(routes,routeData,family.Text);};
            route.Click+=async delegate{
                box.Visible=false;routes.Visible=true;family.Visible=true;routes.BringToFront();
                await session.Run(async delegate(CancellationToken token){var data=await Task.Run(new Func<RouteInventory>(delegate{return routeReader(token);}));if(!session.Alive)return;BindRouteTable(routes,data,family.Text);routeData=data;state.Text=data.Warning.Length==0?"路由表已读取 · 目标、掩码、下一跳":"路由读取未完成："+data.Warning;});
            };
            export.Click+=delegate{if(routes.Visible)ExportToolReport(this,TableCsv(routes),"路由表.csv");else SaveText("网络诊断_"+Environment.MachineName+".txt",box.Text);};
            root.BeginInvoke(new Action(delegate{if(session.Alive)refresh.PerformClick();}));
        }
        private void NetworkDialog(RichTextBox output)
        {
            using (Form f = new Form())
            {
                f.Text = "网络连通性检测（结果直接显示在此窗口）";
                f.ClientSize = new Size(590, 390);
                f.MinimumSize = new Size(545, 350);
                f.StartPosition = FormStartPosition.CenterParent;
                f.Font = Ui;
                f.MaximizeBox = false;
                f.AutoScaleMode = AutoScaleMode.Font;

                // 使用表格布局，避免高 DPI、字体缩放时标签或控件相互遮盖。
                TableLayoutPanel layout = new TableLayoutPanel();
                layout.Dock = DockStyle.Fill;
                layout.Padding = new Padding(16, 14, 16, 14);
                layout.ColumnCount = 2;
                layout.RowCount = 4;
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 122F));
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45F));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45F));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 49F));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
                Label l = new Label { Text = "IP 或主机名", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoSize = false };
                TextBox host = new TextBox { Dock = DockStyle.Fill, Text = "www.baidu.com", Margin = new Padding(0, 4, 0, 10) };
                Label pl = new Label { Text = "TCP 端口", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoSize = false };
                TextBox port = new TextBox { Dock = DockStyle.Fill, Text = "443", Margin = new Padding(0, 4, 0, 10) };
                layout.Controls.Add(l, 0, 0);
                layout.Controls.Add(host, 1, 0);
                layout.Controls.Add(pl, 0, 1);
                layout.Controls.Add(port, 1, 1);
                FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0), AutoScroll = true };
                Button ping = new Button { Text = "Ping（4 次）", Width = 116, Height = 33, Margin = new Padding(0, 4, 10, 0) };
                Button dns = new Button { Text = "DNS 解析", Width = 116, Height = 33, Margin = new Padding(0, 4, 10, 0) };
                Button tcp = new Button { Text = "TCP 测试", Width = 116, Height = 33, Margin = new Padding(0, 4, 10, 0) };
                buttons.Controls.AddRange(new Control[] { ping, dns, tcp });
                layout.Controls.Add(buttons, 0, 2);
                layout.SetColumnSpan(buttons, 2);
                RichTextBox resultBox = new RichTextBox { ReadOnly = true, Dock = DockStyle.Fill, WordWrap = true,
                    BackColor = Color.White, Font = new Font("Consolas", 10F), Text = "输入地址后点击 Ping。\r\n测试结果与延迟将显示在这里，也会同步到主窗口。" };
                layout.Controls.Add(resultBox, 0, 3);
                layout.SetColumnSpan(resultBox, 2);
                f.Controls.Add(layout);
                f.AcceptButton = ping;
                ping.Click += delegate { RunNetworkTest(f, output, resultBox, host.Text, "ping", 0, new Control[] { ping, dns, tcp, host, port }); };
                dns.Click += delegate { RunNetworkTest(f, output, resultBox, host.Text, "dns", 0, new Control[] { ping, dns, tcp, host, port }); };
                tcp.Click += delegate
                {
                    int n;
                    if (!Int32.TryParse(port.Text, out n) || n < 1 || n > 65535)
                        MessageBox.Show(f, "请输入 1~65535 范围内的端口。", "输入错误");
                    else RunNetworkTest(f, output, resultBox, host.Text, "tcp", n, new Control[] { ping, dns, tcp, host, port });
                };
                f.ShowDialog(this);
            }
        }
        private async void RunNetworkTest(Form dialog, RichTextBox output, RichTextBox resultBox, string host, string kind, int port, Control[] controls)
        {
            host = host.Trim();
            if (String.IsNullOrEmpty(host) || host.Length > 255 || host.Contains(" ") || host.Contains("\r") || host.Contains("\n"))
            {
                MessageBox.Show(dialog, "请输入有效的 IP 地址或主机名。"); return;
            }
            foreach (Control c in controls) c.Enabled = false;
            if (!resultBox.IsDisposed) resultBox.Text = "正在测试 " + host + " ……";
            if (!output.IsDisposed) output.Text = "正在测试 " + host + " ……";
            string result;
            try
            {
                string target = host;
                result = await Task.Run(delegate
                {
                    if (kind == "ping") return NetworkService.PingHost(target);
                    if (kind == "dns") return NetworkService.LookupDns(target);
                    return NetworkService.TestTcp(target, port);
                });
            }
            catch (Exception ex) { result = "检测失败：" + ex.Message; }
            if (!resultBox.IsDisposed) resultBox.Text = result;
            if (!output.IsDisposed) output.Text = result;
            if (!dialog.IsDisposed) foreach (Control c in controls) if (!c.IsDisposed) c.Enabled = true;
        }

        private void BuildFaults()
        {
            FlowLayoutPanel bar;
            Panel p = Page("蓝屏与异常关机", "读取 System/Application 事件日志、BugCheck/WHEA/磁盘错误、Minidump 文件元数据。", out bar);
            ComboBox daysInput = new ComboBox { Width = 120, Height = 31, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(5, 5, 6, 5) };
            daysInput.Items.AddRange(new object[] { "最近 7 天", "最近 30 天", "最近 90 天" });
            daysInput.SelectedIndex = 1;
            bar.Controls.Add(daysInput);
            SplitContainer split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal,
                SplitterDistance = 355, BackColor = Canvas, Panel1MinSize = 160, Panel2MinSize = 110 };
            DataGridView grid = Grid();
            AddColumn(grid, "time", "事件时间", 160, false);
            AddColumn(grid, "kind", "类别", 115, false);
            AddColumn(grid, "id", "ID", 60, false);
            AddColumn(grid, "source", "事件来源", 200, false);
            AddColumn(grid, "summary", "内容摘要", 440, true);
            split.Panel1.Controls.Add(grid);
            RichTextBox detail = ReadBox();
            split.Panel2.Controls.Add(detail);
            grid.CellDoubleClick += delegate(object sender, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex >= 0 && grid.Rows[e.RowIndex].Tag is FaultEntry)
                    EventDetailsDialog((FaultEntry)grid.Rows[e.RowIndex].Tag);
            };
            grid.SelectionChanged += delegate
            {
                if (grid.SelectedRows.Count > 0 && grid.SelectedRows[0].Tag is FaultEntry)
                    detail.Text = ((FaultEntry)grid.SelectedRows[0].Tag).Details();
            };
            Panel card = ContentCard(split);
            p.Controls["pageBody"].Controls.Add(card);
            Button scan = null;
            scan = Action(bar, "扫描事件日志", async delegate
            {
                scan.Enabled = false;
                daysInput.Enabled = false;
                detail.Text = "正在读取事件日志，请稍候。日志量较大时可能需要较长时间。";
                SetStatus("正在读取蓝屏与异常关机记录……");
                try
                {
                    int days = new int[] { 7, 30, 90 }[daysInput.SelectedIndex < 0 ? 1 : daysInput.SelectedIndex];
                    DatabaseService source = await ReadyDatabase();
                    if (scan.IsDisposed) return;
                    var fresh = await Task.Run(delegate { return EventService.Scan(days, source); });
                    if (grid.IsDisposed || detail.IsDisposed) return;
                    lastScan=fresh;
                    grid.Rows.Clear();
                    foreach (FaultEntry e in lastScan.Events)
                    {
                        int n = grid.Rows.Add(e.Time.ToString("yyyy-MM-dd HH:mm:ss"), e.DisplayKind, e.EventId, e.Source, e.Summary);
                        DataGridViewRow row = grid.Rows[n];
                        row.Tag = e;
                        if (e.IsBlueScreenRelated)
                        {
                            // 蓝屏记录及明确携带 BugcheckCode 的重启：整行浅红、深红粗体；选中时仍明显为红。
                            row.DefaultCellStyle.BackColor = Color.FromArgb(255, 226, 229);
                            row.DefaultCellStyle.ForeColor = Color.FromArgb(160, 24, 40);
                            row.DefaultCellStyle.Font = UiActive;
                            row.DefaultCellStyle.SelectionBackColor = Color.FromArgb(184, 37, 54);
                            row.DefaultCellStyle.SelectionForeColor = Color.White;
                        }
                        else if (e.Kind == "异常关机" || e.Kind == "意外重启")
                        {
                            // 无蓝屏证据的 41/6008 只标浅黄，避免将断电/强制关机误判成蓝屏。
                            row.DefaultCellStyle.BackColor = Color.FromArgb(255, 246, 225);
                            row.DefaultCellStyle.ForeColor = Color.FromArgb(122, 78, 19);
                            row.DefaultCellStyle.SelectionBackColor = Color.FromArgb(232, 181, 85);
                            row.DefaultCellStyle.SelectionForeColor = Color.FromArgb(52, 38, 18);
                        }
                    }
                    detail.Text = "扫描范围：最近 " + days + " 天\r\n" +
                        "蓝屏事件：" + lastScan.BlueScreens + "\r\n" +
                        "带蓝屏停止码的意外重启：" + lastScan.BlueScreenRelatedRestarts + "\r\n" +
                        "异常关机/意外重启合并估计次数：" + lastScan.UnexpectedShutdowns + "（10分钟近似去重）\r\n" +
                        "WHEA 硬件事件：" + lastScan.HardwareErrors + "\r\n" +
                        "DMP 文件：" + lastScan.DumpFiles.Count + "\r\n" +
                        "总关联事件：" + lastScan.Events.Count + "\r\n\r\n" +
                        "DMP 文件列表：\r\n" + (lastScan.DumpFiles.Count == 0 ? "无可访问文件\r\n" : String.Join("\r\n", lastScan.DumpFiles)) +
                        "\r\n\r\n读取提醒：\r\n" + (lastScan.Warnings.Count == 0 ? "无" : String.Join("\r\n", lastScan.Warnings)) +
                        "\r\n\r\n颜色说明：\r\n" +
                        "红色：BugCheck 1001 或 Kernel-Power 41 明确带非零 BugcheckCode。\r\n" +
                        "黄色：普通 41/6008 异常关机，未发现直接蓝屏证据。\r\n" +
                        "注意：日志记录不能直接证明故障根因；本版不会分析 DMP 调用栈。";
                    SetStatus("日志扫描完成：" + lastScan.Events.Count + " 条相关记录");
                }
                catch (Exception ex) { if(!detail.IsDisposed)detail.Text = "读取日志失败：" + ex; Paths.Log("Scan exception " + ex); }
                finally { if (!scan.IsDisposed) scan.Enabled = true; if (!daysInput.IsDisposed) daysInput.Enabled = true; }
            }, true);
            Action(bar, "导出故障报告", delegate
            {
                if (lastScan == null) { MessageBox.Show(this, "请先扫描日志。"); return; }
                SaveText("故障报告_" + Environment.MachineName + "_" + DateTime.Now.ToString("yyyyMMdd") + ".txt", EventService.BuildReport(lastScan));
            }, false);
            Action(bar, "查看完整事件", delegate
            {
                if (grid.SelectedRows.Count > 0 && grid.SelectedRows[0].Tag is FaultEntry)
                    EventDetailsDialog((FaultEntry)grid.SelectedRows[0].Tag);
                else MessageBox.Show(this, "请先选择一条事件记录。", "事件详情");
            }, false);
            Action(bar, "查询故障代码", delegate { GenericErrorSearchDialog(); }, false);
            Action(bar, "打开 Minidump", delegate
            {
                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Minidump");
                if (Directory.Exists(folder)) CommandService.OpenTool("explorer.exe", "\"" + folder + "\"");
                else MessageBox.Show(this, "Minidump 目录不存在，可能未产生或未保存转储文件。");
            }, false);
            scan.PerformClick();
        }

        private void BuildRepairs()
        {
            FlowLayoutPanel bar;
            Panel p = Page("系统修复", "只提供明确的修复命令，修改前提示影响并检查管理员权限；不自动删除系统文件。", out bar);
            // 修复功能较多，用可换行的操作面板；原来单行按钮在窄窗口里会被隐藏。
            bar.Visible = false;
            SplitContainer repairSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal,
                SplitterDistance = 218, Panel1MinSize = 115, Panel2MinSize = 120, BackColor = Canvas };
            FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true,
                FlowDirection = FlowDirection.LeftToRight, BackColor = Color.White, Padding = new Padding(8, 10, 8, 6) };
            repairSplit.Panel1.Controls.Add(actions);
            RichTextBox box = ReadBox();
            repairSplit.Panel2.Controls.Add(box);
            Panel card = ContentCard(repairSplit);
            p.Controls["pageBody"].Controls.Add(card);
            box.Text = "【系统修复与诊断 · V" + ProductInfo.Version + "】\r\n\r\n" +
                "诊断：SFC 验证、DISM 快速检测、Windows 更新及服务状态。\r\n" +
                "可执行修复：刷新 DNS 缓存、SFC 修复、DISM 修复、Spooler/WIA 服务重启。\r\n" +
                "默认应用修复：分类查看 Office、压缩包、音乐、视频、图片和协议的当前关联，选择默认程序或进入系统重置。\r\n" +
                "快捷入口：Windows 更新、系统还原。\r\n\r\n" +
                "说明：移除了磁盘清理、健康扫描和系统盘只检查入口。\r\n" +
                "企业电脑可能被组策略限制；不要在远程维护时直接重置网络。\r\n" +
                "若需增加高风险操作，将先实现配置备份和逐项确认。";
            RepairButton(actions, box, "刷新 DNS 缓存", "ipconfig.exe", "/flushdns", false, false, 2);
            Action(actions, "默认应用与文件关联修复", delegate { DefaultAppsRepairDialog(); }, false);
            RepairButton(actions, box, "DISM 快速检测", "dism.exe", "/Online /Cleanup-Image /CheckHealth", true, false, 4);
            RepairButton(actions, box, "更新服务状态", "sc.exe", "query wuauserv", false, false, 2);
            RepairButton(actions, box, "BITS 服务状态", "sc.exe", "query bits", false, false, 2);
            RepairButton(actions, box, "SFC 只验证", "sfc.exe", "/verifyonly", true, false, 20);
            RepairButton(actions, box, "SFC 扫描修复", "sfc.exe", "/scannow", true, true, 45);
            RepairButton(actions, box, "DISM 组件修复", "dism.exe", "/Online /Cleanup-Image /RestoreHealth", true, true, 60);
            RepairButton(actions, box, "重启打印服务", "net.exe", "stop spooler", true, true, 5, true);
            Action(actions, "重启 WIA 扫描服务", delegate { RestartWindowsService(box, "stisvc", "WIA 扫描服务"); }, false);
            Action(actions, "Windows 更新管理", delegate { UpdateSettingsDialog(); }, false);
            Action(actions, "Windows 安全防护", delegate { SecuritySettingsDialog(); }, false);
            Action(actions, "修改计算机名称", delegate { ComputerNameDialog(); }, false);
            Action(actions, "Windows 更新设置", delegate { CommandService.OpenTool("ms-settings:windowsupdate", ""); }, false);
            Action(actions, "系统保护/还原", delegate { CommandService.OpenTool("SystemPropertiesProtection.exe", ""); }, false);
        }
        private void RepairButton(FlowLayoutPanel bar, RichTextBox output, string title, string exe, string args, bool admin, bool confirm, int minutes)
        {
            RepairButton(bar, output, title, exe, args, admin, confirm, minutes, false);
        }
        private void RepairButton(FlowLayoutPanel bar, RichTextBox output, string title, string exe, string args, bool admin, bool confirm, int minutes, bool restartSpooler)
        {
            Button b = null;
            b = Action(bar, title, delegate
            {
                if (admin && !CommandService.IsAdministrator())
                {
                    if (MessageBox.Show(this, "此功能需要管理员权限。现在以管理员模式重新打开工具箱吗？", "权限不足", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                        if (CommandService.RelaunchAsAdmin()) Close();
                    return;
                }
                if (confirm && MessageBox.Show(this, "准备执行：" + title + "\r\n\r\n" + exe + " " + args +
                    (restartSpooler ? "\r\n此操作会暂时中断当前打印任务。" : "\r\n操作可能耗时，可能影响当前系统服务。") +
                    "\r\n\r\n确认执行吗？", "操作确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                Paths.Log("Run repair: " + title);
                DoJob(output, b, delegate
                {
                    if (restartSpooler)
                    {
                        string stopped = CommandService.Run("net.exe", "stop spooler", minutes);
                        string started = CommandService.Run("net.exe", "start spooler", minutes);
                        return "【停止服务】\r\n" + stopped + "\r\n【启动服务】\r\n" + started;
                    }
                    return CommandService.Run(exe, args, minutes);
                });
            }, false);
        }

        private void BuildSwitches()
        {
            FlowLayoutPanel toolbar;
            Panel page = Page("Windows 功能开关", "右侧复选框=真实设置；绿色建议标记不代表当前已开启。修改前确认、修改后验证。", out toolbar);
            toolbar.Visible = false;
            TabControl tabs = new TabControl { Dock = DockStyle.Fill, Font = Ui, Padding = new Point(15, 7) };
            Panel host = ContentCard(tabs);
            page.Controls["pageBody"].Controls.Add(host);
            TabPage quick = new TabPage("开关与状态");
            TabPage system = new TabPage("Windows 设置入口");
            TabPage report = new TabPage("全部状态报告");
            tabs.TabPages.Add(quick);
            tabs.TabPages.Add(system);
            tabs.TabPages.Add(report);

            DataGridView grid = Grid();
            DataGridViewCheckBoxColumn enabledSwitch=new DataGridViewCheckBoxColumn { Name="toggle", HeaderText="启用", Width=64, ThreeState=true, TrueValue=true, FalseValue=false };
            grid.Columns.Add(enabledSwitch);
            AddColumn(grid, "category", "分类", 105, false);
            AddColumn(grid, "feature", "功能", 210, false);
            AddColumn(grid, "state", "当前状态", 115, false);
            AddColumn(grid, "recommend", "使用建议", 125, false);
            AddColumn(grid, "note", "说明", 300, true);
            RichTextBox details = ReadBox();
            Panel bottom = new Panel { Dock = DockStyle.Bottom, Height = 152, Padding = new Padding(0, 6, 0, 0) };
            bottom.Controls.Add(details);
            FlowLayoutPanel commands = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 58, AutoScroll = true,
                WrapContents = false, BackColor = Color.White, Padding = new Padding(3, 8, 3, 0) };
            quick.Controls.Add(grid);
            quick.Controls.Add(bottom);
            quick.Controls.Add(commands);

            Action(commands, "刷新状态", delegate { RefreshSwitchGrid(grid, details); }, true);
            Label tip=new Label { Text="点击第一列复选框即可修改；灰色方框表示系统未明确设置",AutoSize=true,Margin=new Padding(12,15,0,0) };
            commands.Controls.Add(tip);
            grid.CellContentClick+=delegate(object sender,DataGridViewCellEventArgs e) {
                if(e.RowIndex<0||e.ColumnIndex!=0)return;
                SwitchRule rule=grid.Rows[e.RowIndex].Tag as SwitchRule;
                if(rule==null)return;
                grid.ClearSelection();grid.Rows[e.RowIndex].Selected=true;
                int? raw=rule.ReadValue();
                bool enabled=!raw.HasValue||raw.Value!=rule.EnabledValue;
                ApplySwitch(grid,details,enabled);
            };
            grid.SelectionChanged += delegate
            {
                if (grid.SelectedRows.Count > 0 && grid.SelectedRows[0].Tag is SwitchRule)
                    details.Text = ((SwitchRule)grid.SelectedRows[0].Tag).Explain();
            };
            RefreshSwitchGrid(grid, details);

            FlowLayoutPanel shortcuts = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true,
                Padding = new Padding(12), BackColor = Color.White, WrapContents = true };
            system.Controls.Add(shortcuts);
            AddSettingsButton(shortcuts, "远程桌面 RDP", "ms-settings:remotedesktop", "");
            AddSettingsButton(shortcuts, "系统代理", "ms-settings:network-proxy", "");
            AddSettingsButton(shortcuts, "防火墙", "firewall.cpl", "");
            AddSettingsButton(shortcuts, "网络与共享", "control.exe", "/name Microsoft.NetworkAndSharingCenter");
            AddSettingsButton(shortcuts, "网络适配器", "ncpa.cpl", "");
            AddSettingsButton(shortcuts, "Windows 功能", "optionalfeatures.exe", "");
            AddSettingsButton(shortcuts, "Windows 更新", "ms-settings:windowsupdate", "");
            AddSettingsButton(shortcuts, "Windows 安全中心", "ms-settings:windowsdefender", "");
            AddSettingsButton(shortcuts, "剪贴板设置", "ms-settings:clipboard", "");
            AddSettingsButton(shortcuts, "电源与睡眠", "ms-settings:powersleep", "");
            AddSettingsButton(shortcuts, "系统电源计划", "powercfg.cpl", "");
            AddSettingsButton(shortcuts, "存储设置", "ms-settings:storagesense", "");
            AddSettingsButton(shortcuts, "任务栏设置", "ms-settings:taskbar", "");
            AddSettingsButton(shortcuts, "默认应用", "ms-settings:defaultapps", "");
            AddSettingsButton(shortcuts, "已安装应用", "appwiz.cpl", "");
            AddSettingsButton(shortcuts, "打印机设置", "control.exe", "printers");
            AddSettingsButton(shortcuts, "用户账户", "netplwiz.exe", "");
            AddSettingsButton(shortcuts, "组策略编辑器", "gpedit.msc", "");
            AddSettingsButton(shortcuts, "本地服务", "services.msc", "");
            AddSettingsButton(shortcuts, "系统还原", "SystemPropertiesProtection.exe", "");
            Label warning = new Label {
                Text = "提示：这一页是官方设置入口，不会擅自开启 RDP、关闭防火墙或解除企业策略。",
                AutoSize = false, Height = 60, Width = 700, TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(117, 80, 21)
            };
            shortcuts.Controls.Add(warning);

            RichTextBox full = ReadBox();
            FlowLayoutPanel reportBar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 55, BackColor = Color.White };
            Action(reportBar, "重新读取全部状态", delegate { full.Text = SwitchService.Report(); }, true);
            Action(reportBar, "导出 TXT", delegate { SaveText("Windows功能状态_" + Environment.MachineName + ".txt", full.Text); }, false);
            report.Controls.Add(full);
            report.Controls.Add(reportBar);
            full.Text = SwitchService.Report();
        }

        private void AddSettingsButton(FlowLayoutPanel panel, string label, string command, string args)
        {
            Button b = new Button { Text = label, Width = 155, Height = 53, Margin = new Padding(6),
                FlatStyle = FlatStyle.Flat, BackColor = Canvas, ForeColor = Navy, Cursor = Cursors.Hand };
            b.FlatAppearance.BorderColor = Color.FromArgb(217, 226, 237);
            b.Click += delegate { CommandService.OpenTool(command, args); };
            panel.Controls.Add(b);
        }
        private void RefreshSwitchGrid(DataGridView grid, RichTextBox details)
        {
            if (grid.IsDisposed) return;
            grid.Rows.Clear();
            foreach (SwitchRule rule in SwitchService.Rules)
            {
                int? v=rule.ReadValue();
                object tick = !v.HasValue ? (object)null : (v.Value==rule.EnabledValue ? (object)true : v.Value==rule.DisabledValue ? (object)false : (object)null);
                int index = grid.Rows.Add(tick, rule.Category, rule.Name, rule.State(), rule.Recommendation(), rule.Help);
                grid.Rows[index].Tag = rule;
                if(rule.Recommendation().StartsWith("建议")) {
                    grid.Rows[index].Cells[4].Style.ForeColor = Color.FromArgb(27,130,63);
                    grid.Rows[index].Cells[4].Style.Font = new Font(Ui, FontStyle.Bold);
                }
                if (rule.Caution) grid.Rows[index].DefaultCellStyle.BackColor = Color.FromArgb(255, 248, 223);
            }
            if (grid.Rows.Count > 0)
            {
                grid.Rows[0].Selected = true;
                details.Text = ((SwitchRule)grid.Rows[0].Tag).Explain();
            }
        }
        private void ApplySwitch(DataGridView grid, RichTextBox details, bool enabled)
        {
            if (grid.SelectedRows.Count == 0 || !(grid.SelectedRows[0].Tag is SwitchRule))
            {
                MessageBox.Show(this, "请先在列表中选择一个功能。"); return;
            }
            SwitchRule rule = (SwitchRule)grid.SelectedRows[0].Tag;
            string action = enabled ? "开启" : "关闭";
            if (MessageBox.Show(this,
                    "确认" + action + "「" + rule.Name + "」吗？\r\n\r\n" + rule.Help +
                    (rule.Caution ? "\r\n\r\n警告：显示系统保护文件后不要误删！" : "") +
                    "\r\n\r\n该操作修改当前用户注册表配置，可能受组策略和系统版本约束。",
                    "修改 Windows 设置", MessageBoxButtons.YesNo,
                    rule.Caution ? MessageBoxIcon.Warning : MessageBoxIcon.Question) != DialogResult.Yes) return;
            try
            {
                int? before = rule.ReadValue();
                rule.Write(enabled);
                SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
                Paths.Log("Switch changed: " + rule.Name + " from " + (before.HasValue ? before.Value.ToString() : "unset") +
                    " to " + (enabled ? rule.EnabledValue : rule.DisabledValue));
                grid.SelectedRows[0].Cells[0].Value = enabled;
                grid.SelectedRows[0].Cells[3].Value = rule.State();
                details.Text = rule.Explain();
                MessageBox.Show(this,
                    "已写入并重新读取：" + rule.Name + " = " + rule.State() +
                    "\r\n\r\n部分设置需重新打开资源管理器窗口，或受企业策略覆盖。",
                    "操作完成");
            }
            catch (Exception ex)
            {
                details.Text = "设置失败：" + ex.Message + "\r\n\r\n" + rule.Explain();
                MessageBox.Show(this, "写入或验证失败：" + ex.Message, "操作失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void BuildPrinter()
        {
            FlowLayoutPanel bar;
            Panel p = Page("打印机与服务", "读取 Spooler 服务状态，打开打印机、打印队列与服务管理；重启服务需管理员权限。", out bar);
            Action(bar, "打印机专项诊断", delegate { PrinterDiagnosticsDialog(); }, true);
            RichTextBox box = ReadBox();
            Panel card = ContentCard(box);
            p.Controls["pageBody"].Controls.Add(card);
            Button state = null;
            state = Action(bar, "检查打印服务", delegate { DoJob(box, state, delegate { return CommandService.Run("sc.exe", "query spooler", 2); }); }, true);
            Action(bar, "打开打印机", delegate { CommandService.OpenTool("control.exe", "printers"); }, false);
            Action(bar, "打印管理", delegate { CommandService.OpenTool("printmanagement.msc", ""); }, false);
            RepairButton(bar, box, "重启 Spooler", "net.exe", "stop spooler", true, true, 5, true);
            Action(bar, "打开服务列表", delegate { CommandService.OpenTool("services.msc", ""); }, false);
            Action(bar, "重启 WIA 扫描服务", delegate { RestartWindowsService(box, "stisvc", "WIA 扫描服务"); }, false);
            DoJob(box, state, delegate { return CommandService.Run("sc.exe", "query spooler", 2); });
        }

        private void BuildToolLaunchers()
        {
            FlowLayoutPanel bar;
            Panel p = Page("Windows 管理入口", "打开 Windows 原生管理程序；不同系统版本和版本类型可能不包含全部组件。", out bar);
            FlowLayoutPanel grid = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.White,
                Padding = new Padding(14), AutoScroll = true, WrapContents = true };
            Panel card = ContentCard(grid);
            p.Controls["pageBody"].Controls.Add(card);
            string[,] tools = {
                { "设备管理器", "devmgmt.msc", "" }, { "磁盘管理", "diskmgmt.msc", "" }, { "事件查看器", "eventvwr.msc", "" },
                { "服务管理器", "services.msc", "" }, { "任务计划程序", "taskschd.msc", "" }, { "计算机管理", "compmgmt.msc", "" },
                { "网络连接", "ncpa.cpl", "" }, { "系统属性", "sysdm.cpl", "" }, { "资源监视器", "resmon.exe", "" },
                { "系统信息", "msinfo32.exe", "" }, { "DX 诊断", "dxdiag.exe", "" }, { "控制面板", "control.exe", "" },
                { "程序与功能", "appwiz.cpl", "" }, { "注册表编辑器", "regedit.exe", "" }, { "任务管理器", "taskmgr.exe", "" },
                { "电源选项", "powercfg.cpl", "" }, { "环境变量", "SystemPropertiesAdvanced.exe", "" }, { "默认应用", "ms-settings:defaultapps", "" }, { "输入法", "ms-settings:typing", "" }, { "本地组策略", "gpedit.msc", "" }, { "打印机", "control.exe", "printers" }
            };
            for (int i = 0; i < tools.GetLength(0); i++)
            {
                string label = tools[i, 0], target = tools[i, 1], args = tools[i, 2];
                Button b = new Button { Text = label, Width = 156, Height = 56, Margin = new Padding(6),
                    FlatStyle = FlatStyle.Flat, BackColor = Canvas, ForeColor = Navy, Cursor = Cursors.Hand };
                b.FlatAppearance.BorderColor = Color.FromArgb(224, 231, 239);
                b.Click += delegate { CommandService.OpenTool(target, args); };
                grid.Controls.Add(b);
            }
            Action(bar, "管理员模式重启", delegate { if (CommandService.RelaunchAsAdmin()) Close(); }, false);
            Action(bar, "图标缓存修复", delegate { IconCacheDialog(); }, false);
            Action(bar, "屏幕色彩检测", delegate { ScreenTestDialog(); }, false);
            Action(bar, "RDP 端口管理", delegate { RdpSettingsDialog(); }, false);
            Action(bar, "默认程序管理", delegate { DefaultAppsDialog(); }, false);
            Action(bar, "环境变量管理", delegate { CommandService.OpenTool("SystemPropertiesAdvanced.exe", ""); }, false);
        }

        private void BuildDatabase()
        {
            FlowLayoutPanel bar;
            Panel p = Page("离线数据库管理", "TSV 文本数据库，可用记事本编辑、拷贝给其他电脑；支持新增硬件匹配规则。", out bar);
            bar.Height=100;bar.WrapContents=true;
            RichTextBox box = ReadBox();
            Panel card = ContentCard(box);
            p.Controls["pageBody"].Controls.Add(card);
            Action(bar, "重载数据库", async delegate
            {
                try { BeginDatabaseLoad(); await ReadyDatabase(); if (!box.IsDisposed) { box.Text = DbDescription(); SetStatus("数据库已重新载入"); } }
                catch (Exception ex) { MessageBox.Show(this, "数据库读取失败：" + ex.Message); }
            }, true);
            Action(bar, "编辑硬件库", delegate
            {
                if (File.Exists(db.HardwarePath)) CommandService.OpenTool("notepad.exe", "\"" + db.HardwarePath + "\"");
                else MessageBox.Show(this, "数据库文件不存在：" + db.HardwarePath);
            }, false);
            Action(bar, "查询错误代码", delegate { GenericErrorSearchDialog(); }, false);
            Action(bar, "编辑错误码库", delegate { CommandService.OpenTool("notepad.exe", "\"" + Path.Combine(Paths.DbDir, "generic_codes.tsv") + "\""); }, false);
            Action(bar, "编辑通用故障库", delegate { CommandService.OpenTool("notepad.exe", "\"" + Path.Combine(Paths.DbDir, "fault_codes.tsv") + "\""); }, false);
            Action(bar, "编辑蓝屏库", delegate
            {
                if (File.Exists(db.BugcheckPath)) CommandService.OpenTool("notepad.exe", "\"" + db.BugcheckPath + "\"");
                else MessageBox.Show(this, "数据库文件不存在：" + db.BugcheckPath);
            }, false);
            Action(bar, "新增硬件规则", async delegate { try{await ReadyDatabase(); if (!p.IsDisposed) { AddRuleForm(); if (!box.IsDisposed) box.Text = DbDescription(); }}catch(Exception ex){if(!p.IsDisposed)MessageBox.Show(this,ex.Message);} }, false);
            Action(bar, "打开数据库目录", delegate
            {
                if (Directory.Exists(Paths.DbDir)) CommandService.OpenTool("explorer.exe", "\"" + Paths.DbDir + "\"");
                else MessageBox.Show(this, "数据库目录不存在。");
            }, false);
            UpdateDatabaseDescription(box);
        }
        private string DbDescription()
        {
            return "【离线数据库状态】\r\n" +
                "目录：" + Paths.DbDir + "\r\n" +
                "硬件规则：" + db.HardwareRuleCount + " 条\r\n" +
                "蓝屏停止代码：" + db.BugcheckRuleCount + " 条\r\n" +
                "PCI 厂商/设备 ID（内置扩充库 + 用户覆盖）：" + db.Ids.PciVendorCount + " / " + db.Ids.PciDeviceCount + " 条\r\n" +
                "USB 厂商/设备 ID（内置扩充库 + 用户覆盖）：" + db.Ids.UsbVendorCount + " / " + db.Ids.UsbDeviceCount + " 条\r\n" +
                "事件故障字典：" + FaultCatalog.Count + " 条 / 通用错误码：" + GenericCodeCatalog.Count + " 条\r\n\r\n" +
                "【hardware_models.tsv 字段】\r\n" +
                "类别<TAB>关键词<TAB>品牌<TAB>补充名称<TAB>说明\r\n" +
                "示例：显卡<TAB>VEN_10DE<TAB>NVIDIA<TAB>英伟达 GPU<TAB>只识别 GPU 厂商，不能推出具体板卡品牌\r\n\r\n" +
                "类别可用：CPU、主板、整机、内存、硬盘、显卡、有线网卡、无线网卡、异常设备、已禁用设备，或 *（所有类别）。\r\n" +
                "匹配规则：旧五列保持包含匹配；新增库支持型号精确匹配/前缀与制造商条件，精确命中优先。\r\n" +
                "如果多个硬件共用同一关键词，只能表示家族或芯片厂商，不能强行确定具体型号。\r\n\r\n" +
                "【bugcheck_codes.tsv 字段】\r\n" +
                "停止代码<TAB>英文名称<TAB>简要含义<TAB>排查建议\r\n" +
                "例：0x0000003B<TAB>SYSTEM_SERVICE_EXCEPTION<TAB>系统服务异常<TAB>分析 DMP 和驱动\r\n\r\n" +
                "还可以将公开 PCI/USB 厂商库 pci.ids、usb.ids 放到此目录，重载后离线补充芯片厂商与设备名。\r\n" +
                "版本、来源和许可见 Database\\SOURCES-v052.md；内置数据只在本地读取，不会自动联网。\r\n" +
                "保存文本后点击“重载数据库”即可生效，无需编译 EXE。\r\n" +
                "数据库补充内容仅供参考；系统原始检测值会保留。\r\n" +
                "日志位置：" + OperationAudit.LogPath;
        }
        private void AddRuleForm()
        {
            using (Form f = new Form())
            {
                f.Text = "新增本地硬件匹配规则";
                f.Width = 620; f.Height = 345;
                f.StartPosition = FormStartPosition.CenterParent;
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.MaximizeBox = false;
                f.Font = Ui;
                string[] captions = { "类别", "关键字（模型或硬件ID）", "厂商品牌", "补充名称", "说明/匹配局限" };
                string[] examples = { "显卡", "VEN_10DE", "NVIDIA", "英伟达 GPU", "仅用于识别芯片厂商" };
                TextBox[] fields = new TextBox[5];
                for (int i = 0; i < 5; i++)
                {
                    f.Controls.Add(new Label { Text = captions[i], Left = 18, Top = 19 + i * 43, Width = 175, Height = 26 });
                    fields[i] = new TextBox { Left = 205, Top = 16 + i * 43, Width = 385, Text = examples[i] };
                    f.Controls.Add(fields[i]);
                }
                Button save = new Button { Text = "保存到本地数据库", Left = 390, Top = 243, Width = 200, Height = 35 };
                save.Click += async delegate
                {
                    try
                    {
                        string[] values=new string[5];for(int i=0;i<5;i++)values[i]=fields[i].Text.Trim();save.Enabled=false;
                        var edited=new DatabaseService();await Task.Run(delegate{edited.AddHardwareRule(values[0],values[1],values[2],values[3],values[4]);});
                        db=edited;databaseLoad=Task.FromResult(edited);if(f.IsDisposed)return;
                        MessageBox.Show(f, "记录已追加到数据库。回到硬件页重新扫描即可匹配。", "保存成功");
                        f.DialogResult = DialogResult.OK;
                        f.Close();
                    }
                    catch (Exception ex) { if(!f.IsDisposed)MessageBox.Show(f, "保存失败：" + ex.Message); }
                    finally{if(!save.IsDisposed)save.Enabled=true;}
                };
                f.Controls.Add(save);
                f.ShowDialog(this);
            }
        }
    }
}
