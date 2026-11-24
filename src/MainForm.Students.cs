using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace WinForms
{
    public sealed partial class MainForm
    {
        private List<StudentMachine> studentMachines;
        private string studentStorePath=Path.Combine(Paths.UserDir,"students-v055.tsv");
        private Func<CancellationToken,List<WakeNetwork>> wakeNetworkReader=StudentWakeService.Networks;
        private Func<IEnumerable<StudentMachine>,WakeNetwork,int,CancellationToken,Action<WakeResult>,Task> wakeSender=StudentWakeService.Wake;
        private sealed class StudentRoomChoice
        {
            public string Name;public bool All;
            public override string ToString(){return All?"全部机房":Name;}
        }
        private sealed class StudentPageData
        {public List<StudentMachine> Machines;public List<WakeNetwork> Networks;public string Warning="";}
        private sealed class WakeProgress
        {
            private readonly PageSession session;private readonly DataGridView grid;private readonly Label state;private readonly Dictionary<string,string> results;public int Count;
            public WakeProgress(PageSession session,DataGridView grid,Label state,Dictionary<string,string> results){this.session=session;this.grid=grid;this.state=state;this.results=results;}
            public void Add(WakeResult result)
            {
                if(!session.Alive)return;Count++;results[result.Machine.Mac]=result.Summary;
                foreach(DataGridViewRow row in grid.Rows){var machine=row.Tag as StudentMachine;if(machine!=null&&machine.Mac==result.Machine.Mac){row.Cells[4].Value=result.Summary;row.Cells[4].ToolTipText=result.Summary;}}
                state.Text="已处理 "+Count+" 台 · 发包不等于已开机";
            }
        }
        private ComboBox StudentChoice(FlowLayoutPanel bar,string caption,int width)
        {
            var panel=new Panel{Width=width,Height=60,Margin=new Padding(0,0,12,0)};panel.Controls.Add(new Label{Text=caption,Dock=DockStyle.Top,Height=22,ForeColor=Color.FromArgb(100,116,139)});
            var combo=new ComboBox{Dock=DockStyle.Bottom,DropDownStyle=ComboBoxStyle.DropDownList,AccessibleName=caption};panel.Controls.Add(combo);bar.Controls.Add(panel);return combo;
        }
        private void BuildStudentWorkspace(bool powerTrial=false)
        {
            FlowLayoutPanel bar;Label state;Panel root=Workspace(powerTrial?"学生机远程电源 · 待试用":"学生机管理与网络唤醒",powerTrial?"v0.6.2 功能，v0.6.4 整理入口 · 先检查远程条件；IP/MAC 可见不代表关机通道可用":"扫描并保存机房/IP/MAC，之后同网段按机房唤醒；远程电源移入新增 · 待试用",out bar,out state);
            ComboBox room=StudentChoice(bar,"机房",150);TextBox search=WorkspaceInput(bar,"搜索名称 / IP / MAC","",200);ComboBox network=StudentChoice(bar,"本机网卡 → 广播地址",360);
            Button reload=WorkspaceButton(bar,"刷新本机网卡",false),scan=WorkspaceButton(bar,"扫描网段并保存",true),add=WorkspaceButton(bar,"添加学生机",true),edit=WorkspaceButton(bar,"编辑学生机",false),remove=WorkspaceButton(bar,"删除记录",false),import=WorkspaceButton(bar,"导入 TSV",false),export=WorkspaceButton(bar,"导出 CSV",false),template=WorkspaceButton(bar,"导出模板",false),wake=WorkspaceButton(bar,"唤醒选中",true),wakeRoom=WorkspaceButton(bar,"唤醒该机房",false),stop=WorkspaceButton(bar,"停止",false);
            Button power=WorkspaceButton(bar,powerTrial?"远程电源 ▾":"远程电源（待试用）",false);power.Name="studentRemotePower";var powerMenu=new ContextMenuStrip();if(powerTrial)power.ContextMenuStrip=powerMenu;power.Click+=delegate{if(powerTrial)powerMenu.Show(power,new Point(0,power.Height));else SelectNavigationPage("学生机远程电源");};root.Disposed+=delegate{powerMenu.Dispose();};
            Button inspect=WorkspaceButton(bar,"检查远程条件",true),detail=WorkspaceButton(bar,"结果详情",false),localCheck=WorkspaceButton(bar,"导出本机只读检查BAT",false);inspect.Name="inspectStudentPower";detail.Name="studentPowerDetails";localCheck.Name="exportStudentLocalCheck";inspect.Visible=detail.Visible=localCheck.Visible=powerTrial;
            DataGridView grid=Table(new[]{"机房","名称","IPv4","MAC","操作结果","备注"});grid.Name="studentMachinesGrid";SetColumnWidths(grid,new[]{90,125,130,155,260,220});root.Controls["pageBody"].Controls.Add(grid);grid.BringToFront();
            var session=new PageSession(root,bar,stop,state);var results=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);bool changingRooms=false;
            var fullDetails=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            Action showDetail=delegate{if(grid.CurrentRow==null)return;var machine=grid.CurrentRow.Tag as StudentMachine;if(machine==null)return;string full;if(!fullDetails.TryGetValue(machine.Mac,out full))full="机房："+machine.Room+"\r\n清单名称："+machine.Name+"\r\nIP："+machine.IPv4+"\r\nMAC："+machine.Mac+"\r\n结果："+Convert.ToString(grid.CurrentRow.Cells[4].Value);ShowStudentPowerDetails(full);};detail.Click+=delegate{showDetail();};grid.CellDoubleClick+=delegate(object sender,DataGridViewCellEventArgs e){if(e.RowIndex>=0)showDetail();};
            var resultMenu=new ContextMenuStrip();resultMenu.Items.Add("查看完整结果").Click+=delegate{showDetail();};grid.ContextMenuStrip=resultMenu;root.Disposed+=delegate{resultMenu.Dispose();};grid.CellMouseDown+=delegate(object sender,DataGridViewCellMouseEventArgs e){if(e.Button==MouseButtons.Right&&e.RowIndex>=0){grid.ClearSelection();grid.CurrentCell=grid.Rows[e.RowIndex].Cells[Math.Max(0,e.ColumnIndex)];grid.Rows[e.RowIndex].Selected=true;}};
            Action render=delegate {
                if(!session.Alive)return;grid.SuspendLayout();try{grid.Rows.Clear();if(studentMachines==null)return;var chosen=room.SelectedItem as StudentRoomChoice;string query=search.Text.Trim();var rows=new List<DataGridViewRow>();
                    foreach(var item in studentMachines){if(chosen!=null&&!chosen.All&&!String.Equals(item.Room,chosen.Name,StringComparison.OrdinalIgnoreCase))continue;if(query.Length>0&&(item.Room+" "+item.Name+" "+item.IPv4+" "+item.Mac).IndexOf(query,StringComparison.OrdinalIgnoreCase)<0)continue;string result;results.TryGetValue(item.Mac,out result);var row=new DataGridViewRow();row.CreateCells(grid,item.Room,item.Name,item.IPv4,item.Mac,result??"未发送",item.Note);row.Tag=item;string full;if(fullDetails.TryGetValue(item.Mac,out full))row.Cells[4].ToolTipText=full;rows.Add(row);}if(rows.Count>0)grid.Rows.AddRange(rows.ToArray());
                    state.Text="显示 "+rows.Count+" / "+studentMachines.Count+" 台 · 进入页面不发包 · 需 BIOS/网卡启用 WOL，关机后仍有网卡供电";
                }finally{grid.ResumeLayout();}
            };
            Action updateRooms=delegate {
                var previous=room.SelectedItem as StudentRoomChoice;string name=previous==null||previous.All?null:previous.Name;changingRooms=true;
                try{room.Items.Clear();room.Items.Add(new StudentRoomChoice{All=true});var names=new SortedSet<string>(StringComparer.OrdinalIgnoreCase);if(studentMachines!=null)foreach(var machine in studentMachines)names.Add(machine.Room);foreach(string value in names)room.Items.Add(new StudentRoomChoice{Name=value});room.SelectedIndex=0;if(name!=null)for(int i=1;i<room.Items.Count;i++)if(String.Equals(((StudentRoomChoice)room.Items[i]).Name,name,StringComparison.OrdinalIgnoreCase)){room.SelectedIndex=i;break;}}finally{changingRooms=false;}
                render();
            };
            Action<List<StudentMachine>> save=delegate(List<StudentMachine> machines){StudentRoster.Save(studentStorePath,machines);studentMachines=machines;updateRooms();};
            reload.Click+=async delegate{await session.Run(async delegate(CancellationToken token){var cached=studentMachines;string path=studentStorePath;var data=await Task.Run(new Func<StudentPageData>(delegate{token.ThrowIfCancellationRequested();var machines=cached??StudentRoster.Load(path);List<WakeNetwork> networks;string warning="";try{networks=wakeNetworkReader(token);}catch(OperationCanceledException){throw;}catch(Exception ex){networks=new List<WakeNetwork>();warning=ex.Message;}token.ThrowIfCancellationRequested();return new StudentPageData{Machines=machines,Networks=networks,Warning=warning};}));if(!session.Alive)return;studentMachines=data.Machines;network.Items.Clear();foreach(var item in data.Networks)network.Items.Add(item);if(network.Items.Count>0)network.SelectedIndex=0;updateRooms();if(network.Items.Count==0)state.Text+=" · 无可用 IPv4 广播网卡"+(data.Warning.Length==0?"":"："+data.Warning);});};
            room.SelectedIndexChanged+=delegate{if(!changingRooms)render();};search.TextChanged+=delegate{if(!session.Running)render();};
            scan.Click+=delegate{
                if(studentMachines==null){state.Text="请先成功读取原清单";return;}var source=network.SelectedItem as WakeNetwork;if(source==null){state.Text="请先选择本机 IPv4 网卡";return;}
                var selected=room.SelectedItem as StudentRoomChoice;var result=StudentScanDialog(source,selected==null||selected.All?"":selected.Name,studentMachines);
                if(result!=null){studentMachines=result.Machines;updateRooms();for(int i=1;i<room.Items.Count;i++)if(String.Equals(((StudentRoomChoice)room.Items[i]).Name,result.Room,StringComparison.OrdinalIgnoreCase)){room.SelectedIndex=i;break;}state.Text="扫描结果已保存，共 "+studentMachines.Count+" 台；下次直接选择机房唤醒即可";}
            };
            add.Click+=delegate{try{if(studentMachines==null){state.Text="请先成功读取清单，避免覆盖未读取的数据";return;}var chosen=room.SelectedItem as StudentRoomChoice;StudentMachine item=EditStudentMachine(null,chosen==null||chosen.All?"":chosen.Name);if(item!=null){var machines=new List<StudentMachine>(studentMachines??new List<StudentMachine>());machines.Add(item);save(StudentRoster.Parse(StudentRoster.Serialize(machines)));}}catch(Exception ex){state.Text="保存未完成："+ex.Message;}};
            edit.Click+=delegate{try{if(grid.CurrentRow==null)return;var original=grid.CurrentRow.Tag as StudentMachine;if(original==null)return;var item=EditStudentMachine(original,original.Room);if(item==null)return;var machines=new List<StudentMachine>(studentMachines);int index=machines.FindIndex(delegate(StudentMachine x){return x.Mac==original.Mac;});if(index>=0){machines[index]=item;save(StudentRoster.Parse(StudentRoster.Serialize(machines)));}}catch(Exception ex){state.Text="保存未完成："+ex.Message;}};
            remove.Click+=delegate{try{if(grid.SelectedRows.Count==0)return;if(!ConfirmAction("删除学生机记录","只从本机清单删除所选记录，不操作学生机。"))return;var selected=new HashSet<string>(StringComparer.OrdinalIgnoreCase);foreach(DataGridViewRow row in grid.SelectedRows)selected.Add(((StudentMachine)row.Tag).Mac);var machines=studentMachines.FindAll(delegate(StudentMachine x){return !selected.Contains(x.Mac);});save(machines);}catch(Exception ex){state.Text="删除未完成："+ex.Message;}};
            import.Click+=delegate{try{if(studentMachines==null){state.Text="请先成功读取原清单，避免覆盖未读取的数据";return;}string text=ReadToolFile(this);if(text==null)return;var machines=StudentRoster.Merge(studentMachines??new List<StudentMachine>(),StudentRoster.Parse(text));save(machines);state.Text="已导入并保存 "+machines.Count+" 台；同 MAC 更新，其他记录保留";}catch(Exception ex){state.Text="导入未完成，原清单保留："+ex.Message;}};
            export.Click+=delegate{ExportToolReport(this,TableCsv(grid),"学生机清单.csv");};
            template.Click+=delegate{using(var dialog=new SaveFileDialog{InitialDirectory=Paths.BaseDir,FileName="学生机清单模板.tsv",Filter="TSV 清单|*.tsv",OverwritePrompt=true})if(dialog.ShowDialog(this)==DialogResult.OK)try{File.WriteAllText(dialog.FileName,StudentRoster.Serialize(new[]{new StudentMachine{Room="机房1",Name="学生机01（请修改）",IPv4="192.0.2.1",Mac="02:00:00:00:00:01",Note="模板样例，请改为真实 IP/MAC"}}),new UTF8Encoding(true));}catch(Exception ex){state.Text="模板保存失败："+ex.Message;}};
            Func<bool,Task> run=async delegate(bool wholeRoom){
                if(session.Running)return;var targets=new List<StudentMachine>();var chosen=room.SelectedItem as StudentRoomChoice;
                if(wholeRoom){if(chosen==null||chosen.All){state.Text="请先选择具体机房；该操作包含该机房全部学生机，不受搜索文字限制";return;}foreach(var item in studentMachines)if(String.Equals(item.Room,chosen.Name,StringComparison.OrdinalIgnoreCase))targets.Add(item);}
                else foreach(DataGridViewRow row in grid.SelectedRows)if(row.Tag is StudentMachine)targets.Add((StudentMachine)row.Tag);
                var source=network.SelectedItem as WakeNetwork;if(source==null){state.Text="请选择可用的本机 IPv4 网卡";return;}
                try{targets=StudentWakeService.ValidateTargets(targets,source,9);}catch(ArgumentException ex){state.Text=ex.Message;return;}
                foreach(var item in targets){results[item.Mac]="本次未完成";fullDetails.Remove(item.Mac);}
                foreach(DataGridViewRow row in grid.Rows){var item=row.Tag as StudentMachine;if(item!=null&&targets.Exists(delegate(StudentMachine target){return target.Mac==item.Mac;})){row.Cells[4].Value="本次未完成";row.Cells[4].ToolTipText="本次未完成";}}
                grid.Enabled=false;var progress=new WakeProgress(session,grid,state,results);Action<WakeResult> receive=new Action<WakeResult>(progress.Add);
                try{await session.Run(async delegate(CancellationToken token){await wakeSender(targets,source,9,token,receive);if(session.Alive)state.Text="本次已处理 "+progress.Count+" 台 · 请查看每行发送结果，未确认学生机实际开机";});}finally{if(session.Alive)grid.Enabled=true;}
            };
            wake.Click+=async delegate{await run(false);};wakeRoom.Click+=async delegate{await run(true);};updateRooms();
            Func<StudentPowerAction,bool,Task> runPower=async delegate(StudentPowerAction action,bool wholeRoom){
                if(session.Running||!session.Alive)return;var targets=new List<StudentMachine>();var chosen=room.SelectedItem as StudentRoomChoice;
                if(studentMachines==null){state.Text="请先成功读取学生机清单。";return;}
                if(wholeRoom){if(chosen==null||chosen.All){state.Text="请先选择具体机房；远程电源不会对全部机房一键执行。";return;}foreach(var item in studentMachines)if(String.Equals(item.Room,chosen.Name,StringComparison.OrdinalIgnoreCase))targets.Add(item);}
                else foreach(DataGridViewRow row in grid.SelectedRows)if(row.Tag is StudentMachine)targets.Add((StudentMachine)row.Tag);
                var source=network.SelectedItem as WakeNetwork;int seconds=action==StudentPowerAction.Abort?0:60;StudentPowerPlan plan;
                try{plan=StudentPowerService.Prepare(targets,source,action,seconds,studentPowerLocalAddresses());}catch(Exception ex){state.Text="未发送："+ex.Message;return;}
                if(!(studentPowerConfirmation==null?ConfirmStudentPower(plan):studentPowerConfirmation(plan))){state.Text="已取消，未发送远程电源请求。";return;}
                if(!session.Alive)return;studentPowerOutstanding++;grid.Enabled=false;string previous=session.CancellationMessage;session.CancellationMessage="已停止后续远程请求；正在处理的请求可能仍返回，已接受请求需另行取消。";
                foreach(var item in plan.Targets){results[item.Mac]="本次尚未发送";fullDetails.Remove(item.Mac);}render();
                try{await session.Run(async delegate(CancellationToken token){state.Text="正在核对并发送远程"+plan.Title+"请求；停止不会撤销已接受的请求。";
                    var batch=await studentPowerSender(plan.Targets,source,action,seconds,token,delegate(StudentPowerResult result){if(!session.Alive)return;results[result.Machine.Mac]=result.Summary;fullDetails[result.Machine.Mac]="时间："+DateTimeOffset.Now.ToString("o")+"\r\n目标机房："+result.Machine.Room+"\r\n清单名称："+result.Machine.Name+"\r\nIP："+result.Machine.IPv4+"\r\nMAC："+result.Machine.Mac+"\r\n操作："+plan.Title+"\r\n结果："+result.Summary+"\r\nWindows 返回码："+(result.Accepted?"0（仅表示请求接受）":result.Error==0?"未调用/未取得":result.Error.ToString());foreach(DataGridViewRow row in grid.Rows)if(((StudentMachine)row.Tag).Mac==result.Machine.Mac){row.Cells[4].Value=result.Summary;row.Cells[4].ToolTipText=fullDetails[result.Machine.Mac];}});
                    if(session.Alive)state.Text=(batch.Stopped?"已停止后续发送":"本次请求已返回")+" · 处理 "+batch.Completed+"/"+batch.Total+" 台，目标接受 "+batch.Accepted+" 台；未确认实际完成，已接受请求需另行取消。";
                });}finally{studentPowerOutstanding--;session.CancellationMessage=previous;if(session.Alive)grid.Enabled=true;if(studentPowerOutstanding==0&&!status.IsDisposed&&status.Text.StartsWith("远程电源请求仍在处理",StringComparison.Ordinal))SetStatus("远程电源请求已返回，请查看操作结果；未确认实际完成。");}
            };
            foreach(bool whole in new[]{false,true}){
                bool roomScope=whole;string scope=whole?"该机房":"选中";
                foreach(StudentPowerAction action in new[]{StudentPowerAction.Shutdown,StudentPowerAction.Restart,StudentPowerAction.Abort}){
                    StudentPowerAction operation=action;string label=action==StudentPowerAction.Abort?"取消"+scope+"待执行关机/重启":(action==StudentPowerAction.Shutdown?"关机":"重启")+scope+"（60秒）";
                    var item=new ToolStripMenuItem(label){Name="studentPower"+action+(whole?"Room":"Selected")};item.Click+=async delegate{await runPower(operation,roomScope);};powerMenu.Items.Add(item);
                }if(!whole)powerMenu.Items.Add(new ToolStripSeparator());
            }
            inspect.Click+=async delegate{
                if(!session.Alive||session.Running)return;var source=network.SelectedItem as WakeNetwork;var targets=new List<StudentMachine>();foreach(DataGridViewRow row in grid.SelectedRows)if(row.Tag is StudentMachine)targets.Add((StudentMachine)row.Tag);StudentPowerPlan plan;
                try{plan=StudentPowerService.Prepare(targets,source,StudentPowerAction.Shutdown,60,studentPowerLocalAddresses());}catch(Exception ex){state.Text="诊断未开始："+ex.Message;return;}
                grid.Enabled=false;try{await session.Run(async delegate(CancellationToken token){state.Text="只读检查选中设备：MAC、TCP135/445；不会发送关机或修改设置。";await studentPowerInspector(plan,source,token,delegate(StudentPowerDiagnostic result){if(!session.Alive)return;results[result.Machine.Mac]=result.Summary;fullDetails[result.Machine.Mac]=result.Details;foreach(DataGridViewRow row in grid.Rows)if(((StudentMachine)row.Tag).Mac==result.Machine.Mac){row.Cells[4].Value=result.Summary;row.Cells[4].ToolTipText=result.Details;}});if(session.Alive)state.Text="只读通道检查已返回；不是关机成功，也未验证远程权限。双击行看详情，错误53可导出本机检查BAT在教师机/学生机本机核对。";});}finally{if(session.Alive)grid.Enabled=true;}
            };
            localCheck.Click+=delegate{try{if(studentLocalCheckExporter!=null){studentLocalCheckExporter(StudentPowerDiagnostics.LocalCheckScript());state.Text="只读检查BAT已交给保存处理；没有在本机或学生机执行。";return;}using(var file=new SaveFileDialog{InitialDirectory=Paths.BaseDir,FileName="远程电源_本机只读检查.bat",Filter="BAT 脚本|*.bat",OverwritePrompt=true})if(file.ShowDialog(this)==DialogResult.OK){StudentPowerDiagnostics.ExportLocalCheck(file.FileName);state.Text="已导出只读检查脚本；可在教师机/学生机本机运行，完整权限导出需管理员。日志在该脚本自己的Logs，不自动修改服务/防火墙/权限。";}}catch(Exception ex){state.Text="只读检查脚本未导出："+ex.Message;}};
            root.BeginInvoke(new Action(delegate{if(session.Alive)reload.PerformClick();}));
        }
        private StudentMachine EditStudentMachine(StudentMachine original,string room)
        {
            using(Form form=NewDialog(original==null?"添加学生机":"编辑学生机",690,390))
            {
                string[] labels={"机房","名称","IPv4（可空）","有线 MAC","备注"};string[] values=original==null?new[]{room,"","","",""}:new[]{original.Room,original.Name,original.IPv4,original.Mac,original.Note};var fields=new TextBox[5];
                for(int i=0;i<5;i++){form.Controls.Add(new Label{Text=labels[i],Left=20,Top=25+i*45,Width=120,Height=28});fields[i]=FormText(values[i],145,22+i*45,505);fields[i].AccessibleName=labels[i];form.Controls.Add(fields[i]);}
                form.Controls.Add(new Label{Text="IP 可空，按 MAC 唤醒；请录入真实有线网卡 MAC，需学生机启用 WOL。",Left=20,Top=255,Width=640,Height=30});StudentMachine result=null;
                var save=FormButton("保存记录",500,300,150,delegate{try{result=StudentRoster.Validate(new StudentMachine{Room=fields[0].Text,Name=fields[1].Text,IPv4=fields[2].Text,Mac=fields[3].Text,Note=fields[4].Text});form.DialogResult=DialogResult.OK;form.Close();}catch(Exception ex){MessageBox.Show(form,ex.Message,"请核对记录");}});form.Controls.Add(save);form.AcceptButton=save;
                return form.ShowDialog(this)==DialogResult.OK?result:null;
            }
        }
    }
}
