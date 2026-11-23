using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WinForms
{
    public sealed partial class MainForm
    {
        private bool ConfirmAction(string title, string message)
        {
            return MessageBox.Show(this, message + "\r\n\r\n确认执行？", title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
        }
        private bool RequireAdmin()
        {
            if (CommandService.IsAdministrator()) return true;
            if (MessageBox.Show(this, "这个操作需要管理员权限。现在以管理员身份重新启动工具箱吗？", "权限不足", MessageBoxButtons.YesNo, MessageBoxIcon.Question)==DialogResult.Yes)
                if (CommandService.RelaunchAsAdmin()) Close();
            return false;
        }
        private Form NewDialog(string title, int width, int height)
        {
            return new Form { Text = title, ClientSize = new Size(width,height), StartPosition=FormStartPosition.CenterParent,
                MinimumSize=new Size(Math.Min(width,590),Math.Min(height,440)), Font=Ui, BackColor=Canvas, AutoScaleMode=AutoScaleMode.Font };
        }
        private Label FormLabel(string caption, int x, int y, int width)
        {
            return new Label { Text=caption, Left=x, Top=y, Width=width, Height=27, TextAlign=ContentAlignment.MiddleLeft };
        }
        private TextBox FormText(string value, int x, int y, int width)
        {
            return new TextBox { Text=value, Left=x, Top=y, Width=width, Height=27 };
        }
        private Button FormButton(string title, int x, int y, int width, EventHandler action)
        {
            Button b = new Button { Text=title, Left=x,Top=y,Width=width,Height=34,
                Font=Ui, FlatStyle=FlatStyle.Flat, BackColor=Color.White,
                ForeColor=Navy, Cursor=Cursors.Hand };
            b.FlatAppearance.BorderColor=Color.FromArgb(204,216,231);
            b.FlatAppearance.MouseOverBackColor=Color.FromArgb(237,243,253);
            b.Click+=action; return b;
        }
        private void RestartWindowsService(RichTextBox output, string name, string label)
        {
            if (!RequireAdmin()) return;
            if (!ConfirmAction(label,"即将停止然后启动服务 " + name + "。正在使用设备或正在扫描的任务可能中断；如果服务尚未启动则直接尝试启动。")) return;
            Button b = new Button();
            DoJob(output, b, delegate {
                string status = CommandService.Run("sc.exe", "query " + name, 2);
                string stopped = CommandService.Run("sc.exe", "stop " + name, 2);
                System.Threading.Thread.Sleep(1200);
                string started = CommandService.Run("sc.exe", "start " + name, 3);
                return "【原状态】\r\n" + status + "\r\n【停止】\r\n" + stopped + "\r\n【启动】\r\n" + started + "\r\n【新状态】\r\n" + CommandService.Run("sc.exe", "query " + name, 2);
            });
        }
        private sealed class StartupSnapshot : IDisposable
        {
            public readonly List<StartupItem> Items=new List<StartupItem>();
            public readonly Dictionary<string,Image> Icons=new Dictionary<string,Image>(StringComparer.OrdinalIgnoreCase);
            public void Dispose(){foreach(var image in Icons.Values)image.Dispose();Icons.Clear();}
            public static StartupSnapshot Read(CancellationToken token)
            {
                var result=new StartupSnapshot();
                try{token.ThrowIfCancellationRequested();foreach(var item in StartupService.Read()){token.ThrowIfCancellationRequested();result.Items.Add(item);string key=item.Command??"";if(!result.Icons.ContainsKey(key))result.Icons[key]=StartupIconService.ForCommand(key);}token.ThrowIfCancellationRequested();return result;}
                catch{result.Dispose();throw;}
            }
        }
        private void BuildStartup()
        {
            FlowLayoutPanel bar;Label state;
            Panel p=Workspace("启动项管理", "勾选允许启动、取消禁止启动；后台读取清单与图标，不删除启动项。", out bar,out state);
            DataGridView grid=Grid();grid.Name="startupGrid";
            DataGridViewCheckBoxColumn checkbox=new DataGridViewCheckBoxColumn { Name="enabled",HeaderText="开机启动",Width=100,MinimumWidth=100 };
            grid.Columns.Add(new DataGridViewImageColumn { Name="icon", HeaderText="图标", Width=68, MinimumWidth=68, ImageLayout=DataGridViewImageCellLayout.Zoom });
            grid.Columns.Add(checkbox);AddColumn(grid,"name","启动项",220,false);AddColumn(grid,"source","来源",170,false);AddColumn(grid,"command","命令 / 文件路径",400,true);AddColumn(grid,"state","状态",90,false);
            grid.ReadOnly=false;foreach(DataGridViewColumn c in grid.Columns)c.ReadOnly=true;checkbox.ReadOnly=false;grid.RowTemplate.Height=36;
            p.Controls["pageBody"].Controls.Add(grid);grid.BringToFront();
            Button refresh=WorkspaceButton(bar,"刷新启动项",true),stop=WorkspaceButton(bar,"停止",false);
            WorkspaceButton(bar,"Windows 启动设置",false).Click+=delegate{CommandService.OpenTool("ms-settings:startupapps","");};
            WorkspaceButton(bar,"任务计划程序",false).Click+=delegate{CommandService.OpenTool("taskschd.msc","");};
            WorkspaceButton(bar,"打开用户启动目录",false).Click+=delegate{CommandService.OpenTool("explorer.exe","shell:startup");};
            var session=new PageSession(p,bar,stop,state);StartupSnapshot snapshot=null;bool loading=false;
            p.Disposed+=delegate{if(snapshot!=null){snapshot.Dispose();snapshot=null;}};
            refresh.Click+=async delegate {
                grid.Enabled=false;
                try{await session.Run(async delegate(CancellationToken token){
                    StartupSnapshot fresh=await Task.Run(new Func<StartupSnapshot>(delegate{return StartupSnapshot.Read(token);}));
                    if(!session.Alive){fresh.Dispose();return;}
                    loading=true;
                    try {
                        grid.Rows.Clear();if(snapshot!=null)snapshot.Dispose();snapshot=fresh;
                        var rows=new List<DataGridViewRow>();bool admin=CommandService.IsAdministrator();
                        foreach(var item in fresh.Items){var row=new DataGridViewRow();row.CreateCells(grid,fresh.Icons[item.Command??""],item.Enabled,item.Name,item.Source,item.Command,item.Status);row.Tag=item;if(item.Hive==RegistryHive.LocalMachine&&!admin){row.Cells[1].ReadOnly=true;row.DefaultCellStyle.BackColor=Color.FromArgb(246,246,246);}rows.Add(row);}
                        if(rows.Count>0)grid.Rows.AddRange(rows.ToArray());state.Text="已读取 "+rows.Count+" 项 · 图标与表头保持横排；可横向滚动查看完整命令";
                    }finally{loading=false;}
                });}finally{if(session.Alive)grid.Enabled=true;}
            };
            grid.CurrentCellDirtyStateChanged+=delegate{if(grid.IsCurrentCellDirty)grid.CommitEdit(DataGridViewDataErrorContexts.Commit);};
            grid.CellValueChanged+=delegate(object sender,DataGridViewCellEventArgs e){
                if(loading||session.Running||e.RowIndex<0||e.ColumnIndex!=1)return;var item=grid.Rows[e.RowIndex].Tag as StartupItem;if(item==null)return;
                bool enabled=Convert.ToBoolean(grid.Rows[e.RowIndex].Cells[1].Value);
                if(ConfirmAction("切换启动项",item.Name+"\r\n来源："+item.Source+"\r\n操作："+(enabled?"允许开机启动":"禁止开机启动")))
                {try{MessageBox.Show(this,StartupService.SetEnabled(item,enabled),"启动项操作结果");}catch(Exception ex){MessageBox.Show(this,"操作失败："+ex.Message);}}
                refresh.PerformClick();
            };
            p.BeginInvoke(new Action(delegate{if(session.Alive)refresh.PerformClick();}));
        }
        private void BuildOptimize()
        {
            FlowLayoutPanel bar;
            Panel p=Page("系统优化", "电源计划、屏幕/睡眠时间、休眠/快速启动、系统体验、默认输入法、图标缓存及安全配置。",out bar);
            bar.Visible=false;
            SplitContainer split=new SplitContainer { Dock=DockStyle.Fill, Orientation=Orientation.Horizontal, SplitterDistance=225,Panel1MinSize=105,Panel2MinSize=130 };
            FlowLayoutPanel actions=new FlowLayoutPanel { Dock=DockStyle.Fill, AutoScroll=true, Padding=new Padding(12),WrapContents=true,BackColor=Color.White };
            RichTextBox box=ReadBox(); split.Panel1.Controls.Add(actions);split.Panel2.Controls.Add(box);
            p.Controls["pageBody"].Controls.Add(ContentCard(split));
            Button refresh=null;
            refresh=Action(actions,"刷新电源状态",delegate { DoJob(box,refresh,PowerService.Status); },true);
            Action(actions,"平衡电源方案",delegate { SetPowerPlan(box,"381b4222-f694-41f0-9685-ff5bb260df2e","平衡模式"); },false);
            Action(actions,"高性能电源方案",delegate { SetPowerPlan(box,"8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c","高性能模式"); },false);
            Action(actions,"卓越性能（若可用）",delegate { SetPowerPlan(box,"e9a42b02-d5df-448d-aa00-03f14749eb61","卓越性能模式"); },false);
            Action(actions,"恢复上次电源计划",delegate { if(!RequireAdmin()||!ConfirmAction("恢复电源计划","恢复之前由本工具箱记录的上一电源计划（含 OEM 自定义计划）。"))return;Button b=new Button();DoJob(box,b,PowerService.RestorePreviousPowerPlan); },false);
            Action(actions,"设置屏幕/睡眠时间",delegate { PowerTimeDialog(); },false);
            Action(actions,"关闭休眠",delegate { TogglePower(box,"休眠",false); },false);
            Action(actions,"开启休眠",delegate { TogglePower(box,"休眠",true); },false);
            Action(actions,"关闭快速启动",delegate { TogglePower(box,"快速启动",false); },false);
            Action(actions,"开启快速启动",delegate { TogglePower(box,"快速启动",true); },false);
            Action(actions,"默认输入法设置",delegate { InputMethodDialog(); },false);
            Action(actions,"默认程序与文件关联",delegate { DefaultAppsDialog(); },false);
            Action(actions,"图标缓存修复",delegate { IconCacheDialog(); },false);
            Action(actions,"屏幕色彩检测",delegate { ScreenTestDialog(); },false);
            Action(actions,"Windows 更新管理",delegate { UpdateSettingsDialog(); },false);
            Action(actions,"Windows 安全防护",delegate { SecuritySettingsDialog(); },false);
            Action(actions,"启动项管理",delegate { ShowPage("启动项管理"); },false);
            Action(actions,"资源管理器/个性化开关",delegate { ShowPage("功能开关"); },false);
            box.Text=PowerService.Status();
        }
        private void SetPowerPlan(RichTextBox box,string guid,string name)
        {
            if(!RequireAdmin())return;
            if(!ConfirmAction("切换电源计划","切换为："+name+"。可能增加功耗、发热和风扇噪声；仅在系统已有此计划时才能成功。"))return;
            Button b=new Button(); DoJob(box,b,delegate { return PowerService.SetPlan(guid); });
        }
        private void TogglePower(RichTextBox box,string feature,bool on)
        {
            if(!RequireAdmin())return;
            if(!ConfirmAction("电源功能管理",(on?"开启":"关闭")+feature+"。关闭休眠还会使快速启动无法使用；部分更改需要重启。"))return;
            Button b=new Button(); DoJob(box,b,delegate { return feature=="休眠"? PowerService.ToggleHibernate(on):PowerService.ToggleFastStartup(on); });
        }
        private void PowerTimeDialog()
        {
            using(Form f=NewDialog("屏幕关闭 / 睡眠 / 休眠时间",690,560))
            {
                ComboBox type=new ComboBox {Left=190,Top=19,Width=425,DropDownStyle=ComboBoxStyle.DropDownList};
                type.Items.AddRange(new object[]{"关闭屏幕","进入睡眠","自动休眠"});type.SelectedIndex=0;
                TextBox ac=FormText("",190,82,425), dc=FormText("",190,126,425);
                Label current=new Label {Left=24,Top=171,Width=630,Height=58,ForeColor=Navy,
                    Text="正在读取活动电源方案的真实设置…"};
                Label tip=new Label {Left=24,Top=231,Width=628,Height=49,
                    Text="单位：分钟；0 表示从不。数值直接读取当前活动电源方案，不使用固定的 5/15 分钟默认值。\r\n实际可用项依硬件、现代待机和系统策略而定。"};
                RichTextBox result=ReadBox();result.Dock=DockStyle.None;result.SetBounds(24,336,638,198);
                string[] names={"monitor","standby","hibernate"};
                Action readActual=delegate {
                    uint a,d;string error;
                    if(PowerTimeReader.TryRead(names[type.SelectedIndex],out a,out d,out error))
                    {
                        // powercfg /change works in integer minutes. Avoid silently rounding
                        // a configuration whose original value has second precision.
                        ac.Text=a%60==0?(a/60).ToString():"";
                        dc.Text=d%60==0?(d/60).ToString():"";
                        current.Text="已读取实际设置：接电源 " + PowerTimeReader.Describe(a)+
                            "；电池 " + PowerTimeReader.Describe(d)+"。"+
                            ((a%60!=0||d%60!=0)?"\r\n当前值含秒数，若需修改，请手动填写整分钟数。":"");
                    }
                    else {ac.Text="";dc.Text="";current.Text="无法读取当前设置："+error+"。请勿将空白当成 0（从不）。";}
                };
                Button refresh=FormButton("读取当前值",24,291,140,delegate {readActual();});
                Button apply=FormButton("应用并重新读取",188,291,225,delegate {
                    int a,d;
                    if(!Int32.TryParse(ac.Text,out a)||!Int32.TryParse(dc.Text,out d)||a<0||d<0||a>100000||d>100000)
                    {MessageBox.Show(f,"请输入有效的整分钟数（0 表示从不）。");return;}
                    string category=names[type.SelectedIndex];
                    if(!ConfirmAction("修改电源时间",type.Text+"\r\n接通电源："+a+" 分钟\r\n电池："+d+" 分钟"))return;
                    try {
                        result.Text=PowerService.SetTimeout(category,a,d);
                        readActual();
                        uint actualA,actualD;string error;
                        if(PowerTimeReader.TryRead(category,out actualA,out actualD,out error)) {
                            if(actualA != (uint)a*60 || actualD != (uint)d*60)
                                result.AppendText("\r\n【警告】当前电源方案的实际值与输入值不一致，可能受权限/策略限制。\r\n");
                        }
                        result.AppendText("\r\n【重新读取】\r\n"+current.Text);
                    }catch(Exception ex){result.Text="修改失败："+ex.Message;}
                });
                f.Controls.AddRange(new Control[]{FormLabel("配置项",24,16,150),type,
                    FormLabel("接通电源（分钟）",24,79,160),ac,FormLabel("使用电池（分钟）",24,123,160),dc,
                    current,tip,refresh,apply,result});
                type.SelectedIndexChanged+=delegate {readActual();};
                readActual();f.ShowDialog(this);
            }
        }
        private void InputMethodDialog()
        {
            using(Form f=NewDialog("默认输入法与键盘布局",760,580))
            {
                ComboBox tips=new ComboBox {Left=167,Top=18,Width=566,DropDownStyle=ComboBoxStyle.DropDownList};
                Label status=new Label {Left=22,Top=162,Width=712,Height=67,ForeColor=Navy,
                    Text="系统默认输入法：正在读取…"};
                RichTextBox output=ReadBox();output.Dock=DockStyle.None;output.SetBounds(20,233,712,318);
                Action load=delegate {
                    try
                    {
                        tips.Items.Clear();
                        InputMethodSnapshot read=InputMethodService.Read();
                        string candidate=!String.IsNullOrWhiteSpace(read.OverrideTip)?read.OverrideTip:read.PreferredTip;
                        int selected=-1;
                        foreach(InputMethodEntry item in read.Entries)
                        {
                            int index=tips.Items.Add(item);
                            if(item.Id.Equals(candidate,StringComparison.OrdinalIgnoreCase))selected=index;
                        }
                        if(selected>=0)tips.SelectedIndex=selected;
                        string selectedName=selected>=0?((InputMethodEntry)tips.Items[selected]).Display:
                                            (String.IsNullOrWhiteSpace(candidate)?"无法识别":candidate);
                        if(!String.IsNullOrWhiteSpace(read.OverrideTip))
                            status.Text="Windows 默认输入法（已指定覆盖值）："+selectedName+
                                "\r\n已读取系统覆盖设置；不等于正在操作的窗口实时使用的输入法。";
                        else
                            status.Text="Windows 未指定独立的默认输入法覆盖值。\r\n"+
                                "系统按用户语言列表动态选择；首选候选："+selectedName;
                        output.Text="【可用输入法与键盘布局（Windows 语言列表 + TSF）】\r\n";
                        foreach(InputMethodEntry item in read.Entries)
                            output.AppendText(item.Display+"\r\n");
                        output.AppendText("\r\n【当前覆盖设置】\r\n"+
                            (String.IsNullOrWhiteSpace(read.OverrideTip)?"未设置（Windows 自动选择）":read.OverrideTip)+
                            "\r\n注意：若使用另一个管理员账号启动工具箱，这里读取和修改的是管理员账号的输入法。"+
                            "\r\n【诊断信息】\r\n"+read.Diagnostic);
                        if(read.Entries.Count==0)
                            output.AppendText("\r\n未识别到可供选择的输入法。请查看诊断信息或使用 Windows 原生设置。\r\n");
                    }
                    catch(Exception ex)
                    {
                        status.Text="输入法读取异常："+ex.Message;
                        output.Text="读取失败，请提供以下错误：\r\n"+ex.ToString();
                    }
                };
                Button scan=FormButton("刷新输入法列表",20,75,192,delegate {load();});
                Button apply=FormButton("设置选中输入法",226,75,183,delegate {
                    InputMethodEntry selected=tips.SelectedItem as InputMethodEntry;
                    if(selected==null){MessageBox.Show(f,"请先选择已注册的输入法。", "提示");return;}
                    if(!ConfirmAction("设置默认输入法",selected.Display+
                         "\r\n修改的是当前运行用户的 Windows 默认输入法覆盖值，不会改变系统显示语言。"))return;
                    try
                    {
                        string raw=InputMethodService.Set(selected.Id);
                        load();
                        if(raw.IndexOf("VERIFY|"+selected.Id,StringComparison.OrdinalIgnoreCase)<0)
                            MessageBox.Show(f,"Windows 未确认已应用该默认输入法。\r\n"+raw,
                                "设置未验证",MessageBoxButtons.OK,MessageBoxIcon.Warning);
                        else
                            MessageBox.Show(f,"已重新读取 Windows 默认输入法覆盖值。\r\n"+
                                "部分已打开窗口不会立即切换，可尝试重新打开窗口或注销后再验证。",
                                "设置结果",MessageBoxButtons.OK,MessageBoxIcon.Information);
                    }
                    catch(Exception ex)
                    {
                        MessageBox.Show(f,"设置失败："+ex.Message,"错误",MessageBoxButtons.OK,MessageBoxIcon.Error);
                    }
                });
                Button settings=FormButton("Windows 语言设置",423,75,178,delegate {CommandService.OpenTool("ms-settings:regionlanguage","");});
                Button typing=FormButton("高级键盘设置",20,120,192,delegate {CommandService.OpenTool("ms-settings:keyboard-advanced","");});
                Button control=FormButton("传统输入法配置",226,120,183,delegate {CommandService.OpenTool("control.exe","/name Microsoft.Language");});
                f.Controls.AddRange(new Control[]{FormLabel("已注册的可用输入法",20,17,145),tips,
                    scan,apply,settings,typing,control,status,output});
                load();f.ShowDialog(this);
            }
        }
        private void BuildShares()
        {
            FlowLayoutPanel bar;
            Panel p=Page("共享文件夹", "列表化查看本地共享、远程 SMB 映射；诊断端口、认证、签名与高级共享设置。",out bar);
            SplitContainer split=new SplitContainer {Dock=DockStyle.Fill,Orientation=Orientation.Horizontal,
                SplitterDistance=360,Panel1MinSize=170,Panel2MinSize=100};
            DataGridView grid=Grid();
            AddColumn(grid,"kind","类型",106,false);
            AddColumn(grid,"name","名称 / 盘符",174,false);
            AddColumn(grid,"address","共享路径",240,true);
            AddColumn(grid,"state","状态",182,false);
            AddColumn(grid,"folder","本地目录",210,false);
            split.Panel1.Controls.Add(grid);
            RichTextBox box=ReadBox();split.Panel2.Controls.Add(box);
            p.Controls["pageBody"].Controls.Add(ContentCard(split));
            grid.SelectionChanged+=delegate {
                if(grid.SelectedRows.Count==0)return;
                ShareDiagnosticsService.ShareRow x=grid.SelectedRows[0].Tag as ShareDiagnosticsService.ShareRow;
                if(x!=null)box.Text="【"+x.Kind+"】\r\n名称："+x.Name+"\r\nUNC："+x.Address+
                    "\r\n状态："+x.State+"\r\n本地目录："+x.Folder+
                    "\r\n注意：存在共享记录不等于当前账户具有访问权限。\r\n双击列表可以打开共享路径。";
            };
            grid.CellDoubleClick+=delegate(object sender,DataGridViewCellEventArgs e){
                if(e.RowIndex<0)return;
                ShareDiagnosticsService.ShareRow x=grid.Rows[e.RowIndex].Tag as ShareDiagnosticsService.ShareRow;
                if(x!=null && x.OpenPath.StartsWith(@"\\"))CommandService.OpenTool("explorer.exe","\""+x.OpenPath.Replace("\"","")+"\"");
            };
            Button read=null;
            read=Action(bar,"刷新共享列表",async delegate {
                read.Enabled=false;
                try {
                    string note="";
                    List<ShareDiagnosticsService.ShareRow> data=await Task.Run(delegate {return ShareDiagnosticsService.ReadRows(out note);});
                    if(grid.IsDisposed)return;
                    grid.Rows.Clear();
                    foreach(ShareDiagnosticsService.ShareRow x in data) {
                        int i=grid.Rows.Add(x.Kind,x.Name,x.Address,x.State,x.Folder);
                        grid.Rows[i].Tag=x;
                    }
                    box.Text="本机普通共享及 SMB 连接，共 "+data.Count+" 条。\r\n双击可打开相应共享。\r\n"+note;
                }catch(Exception ex){if(!box.IsDisposed)box.Text="刷新失败："+ex.Message;}
                finally {if(!read.IsDisposed)read.Enabled=true;}
            },true);
            Action(bar,"共享模式（账号 / 免账号 / 恢复）",delegate {ShareAccessModeDialog();},true);
            Action(bar,"共享 / SMB 诊断",delegate {ShareAdvancedDialog();},true);
            Action(bar,"管理共享权限",delegate {SharePermissionsDialog(box);},false);
            Action(bar,"新建共享",delegate {CreateShareDialog(box);},false);
            Action(bar,"取消共享",delegate {RemoveShareDialog(box);},false);
            Action(bar,"访问/映射共享",delegate {MapShareDialog(box);},false);
            Action(bar,"更多操作",delegate {ShareMoreActionsDialog();},false);
            read.PerformClick();
        }
        private void CreateShareDialog(RichTextBox output)
        {
            using(Form f=NewDialog("新建 SMB 文件共享",690,360))
            {
                TextBox path=FormText("",170,25,385),name=FormText("PublicFiles",170,72,385);
                Button browse=FormButton("选择目录",565,20,105,delegate { using(FolderBrowserDialog d=new FolderBrowserDialog()) if(d.ShowDialog(f)==DialogResult.OK)path.Text=d.SelectedPath; });
                Label tip=new Label { Left=24,Top=127,Width=630,Height=105,Text="默认仅开放共享层的 Everyone 读取，不自动授予 NTFS 写入权限。\r\n如需指定用户权限、部门组或更细的读写规则，请在 Windows 原生共享属性中配置。\r\n不会自动开启 SMB1；创建前需管理员权限。" };
                Button apply=FormButton("创建只读共享",170,253,180,delegate {
                    if(!RequireAdmin()||!ConfirmAction("创建 SMB 共享","文件夹："+path.Text+"\r\n共享名："+name.Text+"\r\n共享权限：Everyone 读取"))return;
                    string folderValue=path.Text.Trim();
                    string shareValue=name.Text.Trim();
                    f.Close();
                    Button b=new Button(); DoJob(output,b,delegate { return ShareService.Create(folderValue,shareValue,true); });
                });
                f.Controls.AddRange(new Control[]{FormLabel("文件夹路径",24,25,145),path,browse,FormLabel("共享名称",24,72,145),name,tip,apply});f.ShowDialog(this);
            }
        }
        private string PromptText(string title, string description, string initial)
        {
            using(Form f=NewDialog(title,600,220))
            {
                Label label=new Label{Text=description,Left=20,Top=15,Width=550,Height=54};
                TextBox value=FormText(initial,20,78,540);
                Button okay=FormButton("确定",395,127,160,delegate {f.DialogResult=DialogResult.OK;f.Close();});
                f.Controls.AddRange(new Control[]{label,value,okay});
                return f.ShowDialog(this)==DialogResult.OK?value.Text:"";
            }
        }
        private void RemoveShareDialog(RichTextBox output)
        {
            string name=PromptText("取消文件共享","输入要取消共享的名称（不会删除原文件夹）。不允许删除 C$、ADMIN$ 等默认管理共享。","");
            if(String.IsNullOrWhiteSpace(name))return;
            if(!RequireAdmin()||!ConfirmAction("取消共享","停止共享："+name+"\r\n正在访问的其他用户会被中断。"))return;
            Button b=new Button();DoJob(output,b,delegate { return ShareService.Remove(name)+"\r\n"+ShareService.ReadShares(); });
        }
        private void MapShareDialog(RichTextBox output)
        {
            using(Form f=NewDialog("访问或映射网络共享",630,290))
            {
                TextBox unc=FormText(@"\\192.168.1.10\Share",176,28,410), drive=FormText("Z:",176,78,110);
                Button open=FormButton("直接打开共享",176,139,175,delegate { if(unc.Text.StartsWith(@"\\")) CommandService.OpenTool("explorer.exe","\""+unc.Text.Replace("\"","")+"\""); else MessageBox.Show(f,"共享路径应以 \\\\ 开始。"); });
                Button map=FormButton("映射网络驱动器",363,139,190,delegate { if(!ConfirmAction("映射共享","路径："+unc.Text+"\r\n盘符："+drive.Text))return; string driveValue=drive.Text.Trim(), uncValue=unc.Text.Trim();
                    f.Close();
                    Button b=new Button(); DoJob(output,b,delegate {return ShareService.Map(driveValue,uncValue);}); });
                f.Controls.AddRange(new Control[]{FormLabel("UNC 共享路径",22,26,150),unc,FormLabel("映射盘符",22,76,150),drive,open,map});f.ShowDialog(this);
            }
        }
    }
}
