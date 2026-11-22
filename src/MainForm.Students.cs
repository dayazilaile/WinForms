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
                foreach(DataGridViewRow row in grid.Rows){var machine=row.Tag as StudentMachine;if(machine!=null&&machine.Mac==result.Machine.Mac)row.Cells[4].Value=result.Summary;}
                state.Text="已处理 "+Count+" 台 · 发包不等于已开机";
            }
        }
        private ComboBox StudentChoice(FlowLayoutPanel bar,string caption,int width)
        {
            var panel=new Panel{Width=width,Height=60,Margin=new Padding(0,0,12,0)};panel.Controls.Add(new Label{Text=caption,Dock=DockStyle.Top,Height=22,ForeColor=Color.FromArgb(100,116,139)});
            var combo=new ComboBox{Dock=DockStyle.Bottom,DropDownStyle=ComboBoxStyle.DropDownList,AccessibleName=caption};panel.Controls.Add(combo);bar.Controls.Add(panel);return combo;
        }
        private void BuildStudentWorkspace()
        {
            FlowLayoutPanel bar;Label state;Panel root=Workspace("学生机管理与网络唤醒","Wake-on-LAN · 先扫描并保存机房，之后按机房唤醒；也可手工录入 MAC",out bar,out state);
            ComboBox room=StudentChoice(bar,"机房",150);TextBox search=WorkspaceInput(bar,"搜索名称 / IP / MAC","",200);ComboBox network=StudentChoice(bar,"本机网卡 → 广播地址",360);
            Button reload=WorkspaceButton(bar,"刷新本机网卡",false),scan=WorkspaceButton(bar,"扫描网段并保存",true),add=WorkspaceButton(bar,"添加学生机",true),edit=WorkspaceButton(bar,"编辑学生机",false),remove=WorkspaceButton(bar,"删除记录",false),import=WorkspaceButton(bar,"导入 TSV",false),export=WorkspaceButton(bar,"导出 CSV",false),template=WorkspaceButton(bar,"导出模板",false),wake=WorkspaceButton(bar,"唤醒选中",true),wakeRoom=WorkspaceButton(bar,"唤醒该机房",false),stop=WorkspaceButton(bar,"停止",false);
            DataGridView grid=Table(new[]{"机房","名称","IPv4","MAC","唤醒结果","备注"});grid.Name="studentMachinesGrid";SetColumnWidths(grid,new[]{90,125,130,155,260,220});root.Controls["pageBody"].Controls.Add(grid);grid.BringToFront();
            var session=new PageSession(root,bar,stop,state);var results=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);bool changingRooms=false;
            Action render=delegate {
                if(!session.Alive)return;grid.SuspendLayout();try{grid.Rows.Clear();if(studentMachines==null)return;var chosen=room.SelectedItem as StudentRoomChoice;string query=search.Text.Trim();var rows=new List<DataGridViewRow>();
                    foreach(var item in studentMachines){if(chosen!=null&&!chosen.All&&!String.Equals(item.Room,chosen.Name,StringComparison.OrdinalIgnoreCase))continue;if(query.Length>0&&(item.Room+" "+item.Name+" "+item.IPv4+" "+item.Mac).IndexOf(query,StringComparison.OrdinalIgnoreCase)<0)continue;string result;results.TryGetValue(item.Mac,out result);var row=new DataGridViewRow();row.CreateCells(grid,item.Room,item.Name,item.IPv4,item.Mac,result??"未发送",item.Note);row.Tag=item;rows.Add(row);}if(rows.Count>0)grid.Rows.AddRange(rows.ToArray());
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
                foreach(var item in targets)results[item.Mac]="本次未完成";
                foreach(DataGridViewRow row in grid.Rows){var item=row.Tag as StudentMachine;if(item!=null&&targets.Exists(delegate(StudentMachine target){return target.Mac==item.Mac;}))row.Cells[4].Value="本次未完成";}
                grid.Enabled=false;var progress=new WakeProgress(session,grid,state,results);Action<WakeResult> receive=new Action<WakeResult>(progress.Add);
                try{await session.Run(async delegate(CancellationToken token){await wakeSender(targets,source,9,token,receive);if(session.Alive)state.Text="本次已处理 "+progress.Count+" 台 · 请查看每行发送结果，未确认学生机实际开机";});}finally{if(session.Alive)grid.Enabled=true;}
            };
            wake.Click+=async delegate{await run(false);};wakeRoom.Click+=async delegate{await run(true);};updateRooms();
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
