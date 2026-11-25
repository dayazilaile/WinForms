using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WinForms
{
    public sealed partial class MainForm
    {
        private Func<IOptimizationBackend> optimizationBackendFactory=delegate{return new OptimizationWindowsBackend();};
        private Func<string> optimizationBackupFactory=delegate{return Path.Combine(Paths.MachineDir,"Optimization",DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")+".xml");};
        private Func<string,bool> optimizationConfirmation=null;
        private Func<string> optimizationRestorePicker=null;
        private Func<bool,CancellationToken,TempCleanResult> optimizationTempCleaner=delegate(bool recent,CancellationToken token){return OptimizationMaintenance.CleanTemp(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),recent,token);};
        private Func<string,string> optimizationMaintenance=delegate(string kind){return kind=="refresh"?OptimizationMaintenance.RefreshIcons():OptimizationMaintenance.Explorer(kind=="cache");};
        private Action<string> optimizationSettings=delegate(string target){CommandService.OpenTool(target,"");};
        private bool ConfirmOptimization(string message){return optimizationConfirmation==null?ConfirmAction("确认系统优化",message):optimizationConfirmation(message);}
        private static void OptimizationPost(Control owner,Action action)
        {if(owner.IsDisposed||!owner.IsHandleCreated)return;try{owner.BeginInvoke(new Action(delegate{if(!owner.IsDisposed)action();}));}catch(InvalidOperationException){}}
        private void BuildOptimizationProfile()
        {
            FlowLayoutPanel bar;Label state;var root=Workspace("一键系统优化 · 待试用","v0.6.6 · 勾选后备份原设置，逐项执行并核验；恢复只适用于同机、同账户和相同系统Build",out bar,out state);
            var detect=WorkspaceButton(bar,"检测本机",false);var recommended=WorkspaceButton(bar,"一键推荐优化",true);var all=WorkspaceButton(bar,"选择全部配置项",false);var apply=WorkspaceButton(bar,"执行勾选优化",true);apply.Name="applyOptimization";
            var restore=WorkspaceButton(bar,"恢复本次 / 选择备份",false);restore.Name="restoreOptimization";var choose=WorkspaceButton(bar,"选择其他备份",false);choose.Name="chooseOptimizationBackup";var stop=WorkspaceButton(bar,"停止",false);var admin=WorkspaceButton(bar,"以管理员重开",false);
            var refresh=WorkspaceButton(bar,"刷新系统图标",false);var cache=WorkspaceButton(bar,"清理 / 重建图标缓存",false);var explorer=WorkspaceButton(bar,"重启资源管理器",false);var temp=WorkspaceButton(bar,"开机（解决欢迎加载慢）",false);temp.Name="openOptimizationTemp";
            var security=WorkspaceButton(bar,"安全防护状态（只读）",false);var uac=WorkspaceButton(bar,"UAC系统设置",false);var storage=WorkspaceButton(bar,"Windows新内容保存设置",false);var web=WorkspaceButton(bar,"网址 / 代理诊断",false);
            var grid=Table(new[]{"执行","项目","执行 / 恢复结果","说明"});grid.Name="optimizationOptions";grid.ReadOnly=false;grid.Columns.RemoveAt(0);grid.Columns.Insert(0,new DataGridViewCheckBoxColumn{Name="checked",HeaderText="执行",Width=65});SetColumnWidths(grid,new[]{65,240,230,580});grid.Columns[3].AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill;for(int i=1;i<grid.Columns.Count;i++)grid.Columns[i].ReadOnly=true;grid.MultiSelect=false;
            var detailResults=new Dictionary<string,OptimizationResult>();
            foreach(var option in OptimizationService.Options){int i=grid.Rows.Add(option.Recommended,option.Name,"尚未执行",option.Help+(option.Admin?" [需管理员]":""));grid.Rows[i].Tag=option.Id;grid.Rows[i].Cells[3].ToolTipText=option.Help;}
            grid.CellDoubleClick+=delegate(object sender,DataGridViewCellEventArgs e){if(e.RowIndex<0)return;var option=OptimizationService.Find((string)grid.Rows[e.RowIndex].Tag);OptimizationResult result;detailResults.TryGetValue(option.Id,out result);using(var form=NewDialog("优化项目说明与结果",860,550)){var text=ReadBox();text.Text=option.Name+"\r\n\r\n"+option.Help+(result==null?"\r\n尚无执行结果。":"\r\n\r\n"+result.Status+"\r\n"+result.Detail+"\r\n备份："+result.Backup);form.Controls.Add(text);form.ShowDialog(this);}};
            var split=new SplitContainer{Dock=DockStyle.Fill,Orientation=Orientation.Horizontal,SplitterDistance=340,Panel1MinSize=120,Panel2MinSize=95};var output=ReadBox();output.Name="optimizationOutput";split.Panel1.Controls.Add(grid);split.Panel2.Controls.Add(output);root.Controls["pageBody"].Controls.Add(split);split.BringToFront();
            output.Text="默认不勾选D盘路径/安装卷、网址代理和更新控制，确认适用后自行勾选。\r\nD盘标准文件夹只改新保存位置，不迁移旧文件；OneDrive/网络重定向跳过。\r\n不自动降低UAC、不关闭实时防护或防火墙。停止只阻止后续项目，当前修改会完成核验和备份记录。\r\n刷新、图标缓存重建、Explorer重启及Temp清理是独立维护动作，不会在勾选优化中自动执行。";
            var session=new PageSession(root,bar,stop,state);session.CancellationMessage="已停止后续优化；已执行的项目可从备份恢复。";string lastBackup="";
            grid.CurrentCellDirtyStateChanged+=delegate{if(grid.IsCurrentCellDirty)grid.CommitEdit(DataGridViewDataErrorContexts.Commit);};
            recommended.Click+=delegate{foreach(DataGridViewRow row in grid.Rows)row.Cells[0].Value=OptimizationService.Find((string)row.Tag).Recommended;apply.PerformClick();};all.Click+=delegate{foreach(DataGridViewRow row in grid.Rows)row.Cells[0].Value=true;};
            detect.Click+=async delegate{await session.Run(async delegate(CancellationToken token){var backend=await Task.Run(optimizationBackendFactory,token);if(session.Alive){var native=backend as OptimizationWindowsBackend;output.Text=native==null?"检测后端已就绪，未修改设置。":native.Description;state.Text="检测已返回；没有执行优化。";}});};
            admin.Click+=delegate{CommandService.RelaunchAsAdmin();};temp.Click+=delegate{SelectNavigationPage("开机临时文件清理");};
            storage.Click+=delegate{try{optimizationSettings("ms-settings:savelocations");}catch(Exception ex){state.Text=ex.Message;}};
            uac.Click+=delegate{try{optimizationSettings("UserAccountControlSettings.exe");}catch(Exception ex){state.Text=ex.Message;}};
            security.Click+=async delegate{await session.Run(async delegate(CancellationToken token){string text=await Task.Run(new Func<string>(SecurityDiagnostics.Inspect),token);if(session.Alive)output.Text=text;});};
            web.Click+=delegate{SelectNavigationPage("系统代理诊断");};
            Action<OptimizationResult> receive=delegate(OptimizationResult result){OptimizationPost(root,delegate{if(!session.Alive)return;detailResults[result.Id]=result;foreach(DataGridViewRow row in grid.Rows)if((string)row.Tag==result.Id){row.Cells[2].Value=result.Status;row.Cells[2].ToolTipText=result.Detail;}output.AppendText("\r\n【"+result.Name+"】"+result.Status+"\r\n"+result.Detail+"\r\n备份："+result.Backup+"\r\n");});};
            apply.Click+=async delegate{
                var ids=new List<string>();grid.EndEdit();foreach(DataGridViewRow row in grid.Rows)if(Convert.ToBoolean(row.Cells[0].Value))ids.Add((string)row.Tag);if(ids.Count==0){state.Text="先勾选要执行的项目。";return;}
                string review=String.Join("\r\n",ids.Select(id=>"• "+OptimizationService.Find(id).Name))+"\r\n\r\n先保存每项原设置到软件Data。D盘修改不迁移旧文件，更新/代理属于自定义选项。不会自动清理Temp、重启Explorer或关闭安全防护。";if(!ConfirmOptimization(review)){state.Text="已取消，未修改系统。";return;}
                grid.Enabled=false;try{await session.Run(async delegate(CancellationToken token){await OptimizationService.Gate.WaitAsync(token);try{await Task.Run(delegate{var backend=optimizationBackendFactory();if(backend is OptimizationWindowsBackend&&ids.Any(id=>OptimizationService.Find(id).Admin)&&!CommandService.IsAdministrator())throw new UnauthorizedAccessException("所选项目需要管理员权限；请先以管理员重开。本次没有修改任何配置。");lastBackup=optimizationBackupFactory();OptimizationService.Apply(ids,backend,lastBackup,token,receive);},token);if(session.Alive)state.Text="本次已返回，请逐项查看核验/失败/跳过结果；备份在软件Data。";}finally{OptimizationService.Gate.Release();}});}finally{if(session.Alive)grid.Enabled=true;}
            };
            restore.Click+=async delegate{
                string path=lastBackup;if(optimizationRestorePicker!=null)path=optimizationRestorePicker();else if(String.IsNullOrEmpty(path)||!File.Exists(path))using(var file=new OpenFileDialog{InitialDirectory=Paths.MachineDir,Filter="优化备份|*.xml",Title="选择本机本账户的优化备份"}){if(file.ShowDialog(this)!=DialogResult.OK)return;path=file.FileName;}
                if(String.IsNullOrEmpty(path))return;if(!ConfirmOptimization("恢复备份："+path+"\r\n恢复原设置，不搬运文件、不回拨时钟，也不恢复已删除的缓存/Temp文件。"))return;
                grid.Enabled=false;try{await session.Run(async delegate(CancellationToken token){await OptimizationService.Gate.WaitAsync(token);try{await Task.Run(delegate{var backend=optimizationBackendFactory();if(backend is OptimizationWindowsBackend&&!CommandService.IsAdministrator()&&OptimizationJournal.Load(path).Entries.Any(e=>e.Phase!="准备"&&e.Phase!="已恢复"&&OptimizationService.Find(e.Id).Admin))throw new UnauthorizedAccessException("此备份包含管理员配置，请先以管理员重开，未执行恢复。");OptimizationService.Restore(path,backend,token,receive);},token);if(session.Alive)state.Text="恢复已返回，请检查各项结果；失败项目可以重试。";}finally{OptimizationService.Gate.Release();}});}finally{if(session.Alive)grid.Enabled=true;}
            };
            choose.Click+=delegate{string path;if(optimizationRestorePicker!=null)path=optimizationRestorePicker();else using(var file=new OpenFileDialog{InitialDirectory=Paths.MachineDir,Filter="优化备份|*.xml",Title="选择其他优化备份"}){if(file.ShowDialog(this)!=DialogResult.OK)return;path=file.FileName;}if(String.IsNullOrEmpty(path))return;lastBackup=path;restore.PerformClick();};
            Action<Button,string> maintenance=delegate(Button button,string kind){button.Click+=async delegate{
                if(kind!="refresh"&&!ConfirmOptimization("将关闭并重启本账户、本会话的资源管理器，打开的文件夹窗口可能关闭。"+(kind=="cache"?"只重建图标缓存，不删除用户文件；缓存删除不可恢复。":"")))return;
                await session.Run(async delegate(CancellationToken token){await OptimizationService.Gate.WaitAsync(token);try{string text=await Task.Run(delegate{return optimizationMaintenance(kind);},token);if(session.Alive){output.Text=text;state.Text="维护请求已返回，请查看结果。";}OperationAudit.Record("系统维护 / "+kind,"已返回",text);}finally{OptimizationService.Gate.Release();}});
            };};maintenance(refresh,"refresh");maintenance(cache,"cache");maintenance(explorer,"explorer");
        }
        private void BuildOptimizationTemp()
        {
            FlowLayoutPanel bar;Label state;var root=Workspace("开机（解决欢迎加载慢） · 待试用","v0.6.6 · 仅清理当前运行账户的Local\\Temp；可能改善部分问题，不保证解决欢迎加载慢",out bar,out state);
            var clean=WorkspaceButton(bar,"开始清理当前用户Temp",true);clean.Name="cleanOptimizationTemp";var stop=WorkspaceButton(bar,"停止",false);var back=WorkspaceButton(bar,"返回优化方案",false);
            var recent=new CheckBox{Text="保留最近24小时文件（推荐）",Checked=true,AutoSize=true,Margin=new Padding(8,30,8,0),Name="keepRecentTemp"};bar.Controls.Add(recent);var output=ReadBox();output.Name="tempCleanOutput";root.Controls["pageBody"].Controls.Add(output);output.BringToFront();var session=new PageSession(root,bar,stop,state);
            output.Text="当前账户："+Environment.UserName+"\r\n清理目标："+Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Temp")+"\r\n只处理Temp内文件，保留目录；占用、只读、链接及工具箱自身目录跳过。\r\n不清理整个Local、不修改启动项。删除不可恢复，备份和工具日志仍只在软件目录。";
            back.Click+=delegate{SelectNavigationPage("一键系统优化");};clean.Click+=async delegate{
                bool keep=recent.Checked;if(!ConfirmOptimization("清理当前运行账户的Local\\Temp，"+(keep?"保留最近24小时文件。":"也尝试清理最近文件。")+"\r\n占用文件和链接跳过，删除不可恢复。请先结束安装、更新等任务。")){state.Text="已取消，未清理。";return;}
                await session.Run(async delegate(CancellationToken token){await OptimizationService.Gate.WaitAsync(token);try{var result=await Task.Run(delegate{return optimizationTempCleaner(keep,token);});if(session.Alive){output.Text=result.Summary;state.Text=result.Stopped?"已停止，已删除内容无法撤回。":"清理已返回，请看实际删除/跳过数量。";}OperationAudit.Record("用户Temp清理",result.Stopped?"部分清理后停止":"已返回",result.Summary);}finally{OptimizationService.Gate.Release();}});
            };
        }
    }
}
