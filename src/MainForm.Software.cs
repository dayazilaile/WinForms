using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace ITDesktopToolkit
{
    public sealed partial class MainForm
    {
        private SoftwareSnapshot softwareCache;
        private Func<List<SoftwareBaselineEntry>> softwareBaselineReader=delegate{return SoftwareBaselineService.Load(SoftwareBaselineService.DefaultPath);};
        private Action<List<SoftwareBaselineEntry>> softwareBaselineWriter=delegate(List<SoftwareBaselineEntry> items){SoftwareBaselineService.Save(SoftwareBaselineService.DefaultPath,items);};
        private Func<string> softwareBaselineImportReader=null;
        private Action<string> softwareBaselineExporter=null;
        private Action<List<SoftwareBaselineEntry>,bool> softwareCleanupExporter=null;
        private Func<SoftwareEntry,SoftwareUninstallPlan> softwareUninstallPlanner=SoftwareUninstallService.Prepare;
        private Action<SoftwareUninstallPlan> softwareUninstallLauncher=SoftwareUninstallService.Start;
        private Func<SoftwareUninstallPlan,bool> softwareUninstallConfirmation=delegate(SoftwareUninstallPlan plan){return MessageBox.Show("准备卸载："+plan.Name+"\r\n启动程序："+plan.FileName+"\r\n参数："+plan.Arguments+"\r\n卸载过程由该软件的卸载程序处理，是否继续？","卸载软件",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)==DialogResult.Yes;};
        private void BuildSoftwareWorkspace()
        {
            FlowLayoutPanel bar;Label state;
            Panel root=Workspace("软件清单与环境检查","建立标准清单，对照多出和缺少的软件；右键启动所选软件的卸载向导",out bar,out state);
            TextBox filter=WorkspaceInput(bar,"搜索名称 / 版本 / 发布者","",230);
            var viewField=new Panel{Width=140,Height=60,Margin=new Padding(0,0,12,0)};
            viewField.Controls.Add(new Label{Text="显示范围",Dock=DockStyle.Top,Height=22,ForeColor=Color.FromArgb(100,116,139)});
            var view=new ComboBox{Name="softwareView",AccessibleName="软件显示范围",Dock=DockStyle.Bottom,DropDownStyle=ComboBoxStyle.DropDownList};view.Items.AddRange(new object[]{"全部软件","多出软件","缺少软件"});view.SelectedIndex=0;viewField.Controls.Add(view);bar.Controls.Add(viewField);
            Button refresh=WorkspaceButton(bar,"刷新软件清单",true),stop=WorkspaceButton(bar,"停止",false),export=WorkspaceButton(bar,"导出列表 CSV",false),standard=WorkspaceButton(bar,"标准清单 ▾",false);
            var grid=Table(new[]{"对照结果","软件名称","版本","发布者","安装范围","登记区","安装位置"});grid.Name="softwareGrid";grid.MultiSelect=false;SetColumnWidths(grid,new[]{120,280,120,200,100,120,260});grid.Columns[1].MinimumWidth=250;
            var details=ReadBox();details.Name="softwareChecks";details.Dock=DockStyle.Bottom;details.Height=100;
            var body=root.Controls["pageBody"];body.Controls.Add(grid);body.Controls.Add(details);grid.BringToFront();
            var session=new PageSession(root,bar,stop,state);SoftwareSnapshot snapshot=softwareCache;string requirements="",baselineError="";List<SoftwareBaselineEntry> baseline=null;
            try{var loaded=softwareBaselineReader();if(loaded!=null)baseline=SoftwareBaselineService.Checked(loaded);}catch(Exception ex){baselineError="标准清单读取失败，未作多出/缺少判断："+ex.Message;}
            Action render=delegate {
                var rows=new List<DataGridViewRow>();SoftwareBaselineComparison comparison=snapshot!=null&&baseline!=null?SoftwareBaselineService.Compare(snapshot,baseline):null;
                var extra=comparison==null?new HashSet<SoftwareEntry>():new HashSet<SoftwareEntry>(comparison.Extra);
                if(snapshot!=null&&view.SelectedIndex!=2)foreach(var item in SoftwareInventoryService.Filter(snapshot,filter.Text)){
                    if(view.SelectedIndex==1&&(comparison==null||!extra.Contains(item)))continue;
                    var row=new DataGridViewRow();row.CreateCells(grid,baseline==null?"未建立标准":extra.Contains(item)?"多出软件":"标准内",item.Name,item.Version,item.Publisher,item.Scope,item.Architecture,item.Location);row.Tag=item;if(extra.Contains(item))row.DefaultCellStyle.ForeColor=Color.FromArgb(180,83,9);rows.Add(row);
                }
                if(comparison!=null&&view.SelectedIndex==2)foreach(var item in comparison.Missing)if((item.Name+"\n"+item.Publisher).IndexOf(filter.Text.Trim(),StringComparison.OrdinalIgnoreCase)>=0){var row=new DataGridViewRow();row.CreateCells(grid,snapshot.Warnings.Count>0?"未找到 / 待核对":"缺少软件",item.Name,"",item.Publisher,"","","");rows.Add(row);}
                grid.Rows.Clear();if(rows.Count>0)grid.Rows.AddRange(rows.ToArray());
                var info=new StringBuilder("软件清单仅包含注册表登记项，不调用 MSI 修复；登记区不等于程序实际架构。\r\n");
                info.AppendLine(baseline==null?"请从“标准清单”建立或导入正确清单；未建立时不会把全部软件当成多出。":"标准清单 "+baseline.Count+" 项 · 完整名称精确匹配（忽略大小写和首尾空格）；发布者留空时不限制，版本不用于判断多出。");
                if(baselineError.Length>0)info.AppendLine(baselineError);
                if(snapshot!=null){info.AppendLine(".NET Framework v4 Release："+snapshot.FrameworkRelease+"；VC++ 年份/x86/x64 请按软件名称核对。");foreach(string warning in snapshot.Warnings)info.AppendLine("部分读取失败："+warning);if(requirements.Length>0)info.AppendLine(InspectionService.CheckRequirements(snapshot.Versions(),requirements));state.Text="显示 "+rows.Count+" 项 · 已登记 "+snapshot.Entries.Count+(comparison==null?" · 尚未建立标准清单":" · 多出 "+comparison.Extra.Count+" · "+(snapshot.Warnings.Count>0?"未找到（读取不完整） ":"缺少 ")+comparison.Missing.Count);}
                else state.Text="正在等待软件清单 · 标准清单可导入或编辑";
                details.Text=info.ToString();
            };
            Action<List<SoftwareBaselineEntry>> saveBaseline=delegate(List<SoftwareBaselineEntry> items){var checkedItems=SoftwareBaselineService.Checked(items);softwareBaselineWriter(checkedItems);baseline=checkedItems;baselineError="";view.SelectedIndex=1;render();};
            var standardMenu=new ContextMenuStrip();standard.ContextMenuStrip=standardMenu;
            var capture=standardMenu.Items.Add("从本机建立 / 更新标准");capture.Name="captureSoftwareBaseline";
            var edit=standardMenu.Items.Add("编辑标准清单");edit.Name="editSoftwareBaseline";
            var import=standardMenu.Items.Add("导入标准清单");import.Name="importSoftwareBaseline";
            var exportStandard=standardMenu.Items.Add("导出标准清单");exportStandard.Name="exportSoftwareBaseline";
            var cleanup=standardMenu.Items.Add("导出批量卸载脚本…");cleanup.Name="exportSoftwareCleanup";
            standard.Click+=delegate{standardMenu.Show(standard,new Point(0,standard.Height));};
            standardMenu.Opening+=delegate{capture.Enabled=snapshot!=null&&snapshot.Entries.Count>0&&!session.Running;edit.Enabled=baseline!=null&&!session.Running;exportStandard.Enabled=cleanup.Enabled=baseline!=null&&!session.Running;};
            capture.Click+=delegate{try{if(snapshot!=null)EditSoftwareBaseline(SoftwareBaselineService.Capture(snapshot),saveBaseline,snapshot.Warnings.Count>0);}catch(Exception ex){MessageBox.Show(this,ex.Message,"标准清单");}};
            edit.Click+=delegate{if(baseline!=null)EditSoftwareBaseline(baseline,saveBaseline,false);};
            import.Click+=delegate{try{string text=softwareBaselineImportReader==null?ReadToolFile(this):softwareBaselineImportReader();if(text!=null)saveBaseline(SoftwareBaselineService.Parse(text));}catch(Exception ex){MessageBox.Show(this,ex.Message,"导入标准清单失败");}};
            exportStandard.Click+=delegate{if(baseline==null)return;string text=SoftwareBaselineService.Serialize(baseline);if(softwareBaselineExporter!=null)softwareBaselineExporter(text);else ExportToolReport(this,text,"软件标准清单.txt");};
            cleanup.Click+=delegate{if(baseline!=null)ExportSoftwareCleanup(baseline);};
            standardMenu.Items.Add(new ToolStripSeparator());var versionCheck=new ToolStripMenuItem("版本检查");standardMenu.Items.Add(versionCheck);
            versionCheck.DropDownItems.Add("导入关键词 / 最低版本清单").Click+=delegate{try{string text=ReadToolFile(this);if(text!=null){InspectionService.CheckRequirements(new Dictionary<string,string>(),text);requirements=text;render();}}catch(Exception ex){MessageBox.Show(this,ex.Message);}};
            versionCheck.DropDownItems.Add("导出版本检查模板").Click+=delegate{ExportToolReport(this,"# 软件名称关键词<Tab>最低版本（可空）\r\nGoogle Chrome\t120.0\r\nMicrosoft Visual C++\r\n","软件检查清单.txt");};
            var menu=new ContextMenuStrip();var uninstall=menu.Items.Add("卸载所选软件");uninstall.Name="uninstallSoftware";var manage=menu.Items.Add("在 Windows 设置中管理");grid.ContextMenuStrip=menu;bool uninstallBusy=false;root.Disposed+=delegate{menu.Dispose();};
            root.Disposed+=delegate{standardMenu.Dispose();};
            grid.CellMouseDown+=delegate(object sender,DataGridViewCellMouseEventArgs e){if(e.Button!=MouseButtons.Right)return;grid.ClearSelection();if(e.RowIndex>=0){grid.CurrentCell=grid.Rows[e.RowIndex].Cells[0];grid.Rows[e.RowIndex].Selected=true;}else grid.CurrentCell=null;};
            menu.Opening+=delegate{var item=grid.CurrentRow==null?null:grid.CurrentRow.Tag as SoftwareEntry;uninstall.Enabled=!session.Running&&!uninstallBusy&&item!=null&&item.HasUninstaller;manage.Enabled=!session.Running&&!uninstallBusy&&item!=null;};
            manage.Click+=delegate{CommandService.OpenTool("ms-settings:appsfeatures","");};
            uninstall.Click+=async delegate{
                if(uninstallBusy||session.Running||grid.CurrentRow==null)return;var item=grid.CurrentRow.Tag as SoftwareEntry;if(item==null||!item.HasUninstaller)return;uninstallBusy=true;
                try{state.Text="正在核对所选软件的卸载登记…";var plan=await Task.Run(new Func<SoftwareUninstallPlan>(delegate{return softwareUninstallPlanner(item);}));if(!session.Alive)return;if(!plan.CanStart){state.Text=plan.Reason;return;}if(!softwareUninstallConfirmation(plan)){state.Text="已取消卸载，未启动卸载程序";return;}softwareUninstallLauncher(plan);softwareCache=null;state.Text="已启动 "+plan.Name+" 的卸载程序；完成后点击刷新软件清单";}
                catch(Exception ex){if(session.Alive)state.Text="卸载程序未启动："+ex.Message;}
                finally{uninstallBusy=false;}
            };
            filter.TextChanged+=delegate{if(session.Alive&&!session.Running)render();};
            view.SelectedIndexChanged+=delegate{if(session.Alive&&!session.Running)render();};
            refresh.Click+=async delegate{await session.Run(async delegate(CancellationToken token){var fresh=await Task.Run(new Func<SoftwareSnapshot>(delegate{return SoftwareInventoryService.Read(token);}));if(!session.Alive)return;softwareCache=snapshot=fresh;render();});};
            export.Click+=delegate{ExportToolReport(this,TableCsv(grid),"软件清单.csv");};
            render();root.BeginInvoke(new Action(delegate{if(session.Alive&&snapshot==null)refresh.PerformClick();}));
        }
    }
}
