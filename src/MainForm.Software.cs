using System;
using System.Collections.Generic;
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
        private Func<SoftwareEntry,SoftwareUninstallPlan> softwareUninstallPlanner=SoftwareUninstallService.Prepare;
        private Action<SoftwareUninstallPlan> softwareUninstallLauncher=SoftwareUninstallService.Start;
        private Func<SoftwareUninstallPlan,bool> softwareUninstallConfirmation=delegate(SoftwareUninstallPlan plan){return MessageBox.Show("准备卸载："+plan.Name+"\r\n启动程序："+plan.FileName+"\r\n参数："+plan.Arguments+"\r\n卸载过程由该软件的卸载程序处理，是否继续？","卸载软件",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)==DialogResult.Yes;};
        private void BuildSoftwareWorkspace()
        {
            FlowLayoutPanel bar;Label state;
            Panel root=Workspace("软件清单与环境检查","按名称、版本、发布者列出登记软件；右键可卸载，后台读取与缓存搜索",out bar,out state);
            TextBox filter=WorkspaceInput(bar,"搜索名称 / 版本 / 发布者","",260);
            Button refresh=WorkspaceButton(bar,"刷新软件清单",true),stop=WorkspaceButton(bar,"停止",false),export=WorkspaceButton(bar,"导出列表 CSV",false),import=WorkspaceButton(bar,"导入检查清单",false),template=WorkspaceButton(bar,"导出清单模板",false);
            var grid=Table(new[]{"软件名称","版本","发布者","安装范围","登记区","安装位置"});grid.Name="softwareGrid";SetColumnWidths(grid,new[]{300,145,210,100,120,300});grid.Columns[0].MinimumWidth=260;
            var details=ReadBox();details.Name="softwareChecks";details.Dock=DockStyle.Bottom;details.Height=125;
            var body=root.Controls["pageBody"];body.Controls.Add(grid);body.Controls.Add(details);grid.BringToFront();
            var session=new PageSession(root,bar,stop,state);SoftwareSnapshot snapshot=softwareCache;string requirements="";
            Action render=delegate {
                var rows=new List<DataGridViewRow>();if(snapshot!=null)foreach(var item in SoftwareInventoryService.Filter(snapshot,filter.Text)){var row=new DataGridViewRow();row.CreateCells(grid,item.Cells());row.Tag=item;rows.Add(row);}grid.Rows.Clear();if(rows.Count>0)grid.Rows.AddRange(rows.ToArray());
                var info=new StringBuilder("软件清单仅包含注册表登记项，不调用 MSI 修复；登记区不等于程序实际架构。\r\n");
                if(snapshot!=null){info.AppendLine(".NET Framework v4 Release："+snapshot.FrameworkRelease+"；VC++ 年份/x86/x64 请按软件名称核对。");foreach(string warning in snapshot.Warnings)info.AppendLine("部分读取失败："+warning);if(requirements.Length>0)info.AppendLine(InspectionService.CheckRequirements(snapshot.Versions(),requirements));state.Text="显示 "+rows.Count+" / "+snapshot.Entries.Count+" 项 · 不涵盖全部便携软件与其他用户应用";}
                else info.AppendLine("清单格式：软件名称关键词<Tab>最低版本（可空）。");
                details.Text=info.ToString();
            };
            var menu=new ContextMenuStrip();var uninstall=menu.Items.Add("卸载所选软件");uninstall.Name="uninstallSoftware";var manage=menu.Items.Add("在 Windows 设置中管理");grid.ContextMenuStrip=menu;bool uninstallBusy=false;root.Disposed+=delegate{menu.Dispose();};
            grid.CellMouseDown+=delegate(object sender,DataGridViewCellMouseEventArgs e){if(e.Button==MouseButtons.Right&&e.RowIndex>=0){grid.ClearSelection();grid.CurrentCell=grid.Rows[e.RowIndex].Cells[0];grid.Rows[e.RowIndex].Selected=true;}};
            menu.Opening+=delegate{var item=grid.CurrentRow==null?null:grid.CurrentRow.Tag as SoftwareEntry;uninstall.Enabled=!session.Running&&!uninstallBusy&&item!=null&&item.HasUninstaller;manage.Enabled=!session.Running&&!uninstallBusy;};
            manage.Click+=delegate{CommandService.OpenTool("ms-settings:appsfeatures","");};
            uninstall.Click+=async delegate{
                if(uninstallBusy||session.Running||grid.CurrentRow==null)return;var item=grid.CurrentRow.Tag as SoftwareEntry;if(item==null||!item.HasUninstaller)return;uninstallBusy=true;
                try{state.Text="正在核对所选软件的卸载登记…";var plan=await Task.Run(new Func<SoftwareUninstallPlan>(delegate{return softwareUninstallPlanner(item);}));if(!session.Alive)return;if(!plan.CanStart){state.Text=plan.Reason;return;}if(!softwareUninstallConfirmation(plan)){state.Text="已取消卸载，未启动卸载程序";return;}softwareUninstallLauncher(plan);softwareCache=null;state.Text="已启动 "+plan.Name+" 的卸载程序；完成后点击刷新软件清单";}
                catch(Exception ex){if(session.Alive)state.Text="卸载程序未启动："+ex.Message;}
                finally{uninstallBusy=false;}
            };
            filter.TextChanged+=delegate{if(session.Alive&&!session.Running)render();};
            refresh.Click+=async delegate{await session.Run(async delegate(CancellationToken token){var fresh=await Task.Run(new Func<SoftwareSnapshot>(delegate{return SoftwareInventoryService.Read(token);}));if(!session.Alive)return;softwareCache=snapshot=fresh;render();});};
            export.Click+=delegate{ExportToolReport(this,TableCsv(grid),"软件清单.csv");};
            import.Click+=delegate{try{string text=ReadToolFile(this);if(text!=null){InspectionService.CheckRequirements(new Dictionary<string,string>(),text);requirements=text;render();}}catch(Exception ex){MessageBox.Show(this,ex.Message);}};
            template.Click+=delegate{ExportToolReport(this,"# 软件名称关键词<Tab>最低版本（可空）\r\nGoogle Chrome\t120.0\r\nMicrosoft Visual C++\r\n","软件检查清单.txt");};
            render();root.BeginInvoke(new Action(delegate{if(session.Alive&&snapshot==null)refresh.PerformClick();}));
        }
    }
}
