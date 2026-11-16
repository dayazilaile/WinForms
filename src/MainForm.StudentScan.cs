using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace ITDesktopToolkit
{
    public sealed partial class MainForm
    {
        private Func<string,WakeNetwork,CancellationToken,Action<StudentScanRow>,Task<StudentScanResult>> studentScanner=StudentDiscoveryService.Scan;
        private sealed class StudentScanSaved{public List<StudentMachine> Machines;public string Room;}
        private sealed class StudentScanBuffer
        {
            private readonly object gate=new object();private readonly List<StudentScanRow> rows=new List<StudentScanRow>();
            public void Add(StudentScanRow row){lock(gate)rows.Add(row);}
            public List<StudentScanRow> Snapshot(){lock(gate)return new List<StudentScanRow>(rows);}
        }
        private StudentScanSaved StudentScanDialog(WakeNetwork network,string roomName,IEnumerable<StudentMachine> machines)
        {
            using(Form form=NewDialog("扫描网段并保存学生机",1120,760))
            {
                form.MinimumSize=new Size(850,600);var bar=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,WrapContents=true,Padding=new Padding(12)};
                TextBox subnet=WorkspaceInput(bar,"扫描网段 / IPv4",StudentDiscoveryService.DefaultSubnet(network),240),room=WorkspaceInput(bar,"保存到机房",roomName,180);
                Button start=WorkspaceButton(bar,"扫描网段",true),stop=WorkspaceButton(bar,"停止",false),select=WorkspaceButton(bar,"勾选可用设备",false),save=WorkspaceButton(bar,"保存勾选",true);
                var state=new Label{Dock=DockStyle.Bottom,Height=42,Padding=new Padding(12,0,12,0),TextAlign=ContentAlignment.MiddleLeft,AutoEllipsis=true,Text="只查询所选局域网 IP/MAC；请在结果中勾选真实学生机，不会自动保存或唤醒"};
                DataGridView grid=Grid();grid.Name="studentScanGrid";grid.ReadOnly=false;
                grid.Columns.Add(new DataGridViewCheckBoxColumn{Name="save",HeaderText="保存",Width=60});AddColumn(grid,"ip","IPv4",150,false);AddColumn(grid,"mac","MAC",165,false);AddColumn(grid,"name","名称",210,false);AddColumn(grid,"status","来源 / 状态",400,true);
                grid.Columns[1].ReadOnly=true;grid.Columns[2].ReadOnly=true;grid.Columns[4].ReadOnly=true;foreach(DataGridViewColumn column in grid.Columns)column.SortMode=DataGridViewColumnSortMode.NotSortable;
                form.Controls.Add(grid);form.Controls.Add(bar);form.Controls.Add(state);grid.BringToFront();
                var original=StudentRoster.Merge(machines,new StudentMachine[0]);var known=new Dictionary<string,StudentMachine>(StringComparer.OrdinalIgnoreCase);foreach(var item in original)known.Add(item.Mac,item);
                var session=new PageSession(form,bar,stop,state);StudentScanSaved saved=null;StudentScanBuffer buffer=null;int shown=0;
                Action render=delegate{if(!session.Alive||buffer==null)return;var rows=buffer.Snapshot();while(shown<rows.Count){var item=rows[shown++];StudentMachine previous;if(item.CanSave&&known.TryGetValue(item.Mac,out previous))item.Name=previous.Name;var row=new DataGridViewRow();row.CreateCells(grid,false,item.IPv4,item.Mac,item.Name,item.Status);row.Tag=item;row.Cells[0].ReadOnly=!item.CanSave;row.Cells[3].ReadOnly=!item.CanSave||(item.CanSave&&known.ContainsKey(item.Mac));if(!item.CanSave)row.DefaultCellStyle.ForeColor=Color.Gray;grid.Rows.Add(row);}state.Text="已扫描 "+rows.Count+" 个地址；已取得 MAC "+rows.FindAll(x=>x.CanSave).Count+" 条 · 勾选并核对后再保存";};
                using(var timer=new System.Windows.Forms.Timer{Interval=250})
                {
                    timer.Tick+=delegate{render();};form.Disposed+=delegate{timer.Stop();};
                    start.Click+=async delegate{
                        try{StudentDiscoveryService.Targets(subnet.Text,network);}catch(Exception ex){state.Text=ex.Message;return;}
                        grid.Rows.Clear();buffer=new StudentScanBuffer();shown=0;grid.Enabled=false;timer.Start();string range=subnet.Text.Trim();var current=buffer;Action<StudentScanRow> receive=new Action<StudentScanRow>(current.Add);StudentScanResult result=null;
                        try{await session.Run(async delegate(CancellationToken token){result=await studentScanner(range,network,token,receive);});}
                        finally{timer.Stop();if(session.Alive){render();var rows=current.Snapshot();StudentDiscoveryService.MarkConflicts(rows);foreach(DataGridViewRow row in grid.Rows){var item=(StudentScanRow)row.Tag;StudentMachine previous;string text=item.Status;if(known.TryGetValue(item.Mac,out previous))text+="；已保存到 "+previous.Room+"，名称与分组保留";row.Cells[4].Value=text;}grid.Enabled=true;state.Text="已有结果 "+rows.Count+" 条，可保存已勾选的有效 MAC；未解析不代表离线"+(result==null?"（本轮未完成）":result.Warning.Length==0?"":"；"+result.Warning);}}
                    };
                    select.Click+=delegate{foreach(DataGridViewRow row in grid.Rows){var item=(StudentScanRow)row.Tag;row.Cells[0].Value=item.CanSave&&!item.Conflict;}grid.EndEdit();};
                    save.Click+=delegate{try{grid.EndEdit();var chosen=new List<StudentScanRow>();foreach(DataGridViewRow row in grid.Rows)if(Convert.ToBoolean(row.Cells[0].Value)){var item=(StudentScanRow)row.Tag;chosen.Add(new StudentScanRow{IPv4=item.IPv4,Mac=item.Mac,Name=Convert.ToString(row.Cells[3].Value),Status=item.Status});}var merged=StudentDiscoveryService.MergeDiscovered(original,chosen,room.Text);StudentRoster.Save(studentStorePath,merged);saved=new StudentScanSaved{Machines=merged,Room=room.Text.Trim()};form.DialogResult=DialogResult.OK;form.Close();}catch(Exception ex){state.Text="保存未完成，原清单保留："+ex.Message;}};
                    return form.ShowDialog(this)==DialogResult.OK?saved:null;
                }
            }
        }
    }
}
