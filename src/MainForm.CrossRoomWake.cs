using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WinForms
{
    public sealed partial class MainForm
    {
        private Func<CrossRoomWakePlan,CancellationToken,Action<CrossRoomWakeResult>,Task> crossRoomWakeSender=CrossRoomWakeService.Run;
        private Func<CrossRoomWakePlan,bool> crossRoomWakeConfirmation=null;
        private void BuildCrossRoomWake()
        {
            FlowLayoutPanel bar;Label state;
            var root=Workspace("跨机房唤醒 · 待试用","v0.6.5 · 无需改网络配置的尝试；先试单播，发包不代表已送达/开机",out bar,out state);
            ComboBox room=StudentChoice(bar,"目标机房",150),network=StudentChoice(bar,"本机发送网卡",310),mode=StudentChoice(bar,"发送方式",230);
            room.Name="crossWakeRoom";network.Name="crossWakeNetwork";mode.Name="crossWakeMode";
            mode.Items.AddRange(new object[]{"单播（先试）","目标网段广播（需现有转发）","本地广播（同一 VLAN 时）","组合尝试（三种方式）"});mode.SelectedIndex=0;
            TextBox prefix=WorkspaceInput(bar,"目标掩码位数（通常24）","24",160);prefix.Name="crossWakePrefix";
            Button reload=WorkspaceButton(bar,"读取机房与网卡",false),manage=WorkspaceButton(bar,"管理原机房清单",false),selected=WorkspaceButton(bar,"尝试唤醒选中",true),whole=WorkspaceButton(bar,"尝试唤醒该机房",false),detail=WorkspaceButton(bar,"结果详情",false),stop=WorkspaceButton(bar,"停止",false);
            reload.Name="reloadCrossWake";selected.Name="sendCrossWakeSelected";whole.Name="sendCrossWakeRoom";
            var grid=Table(new[]{"机房","名称","IPv4","MAC","发送结果"});grid.Name="crossWakeGrid";SetColumnWidths(grid,new[]{95,150,145,165,350});grid.Columns[4].AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill;root.Controls["pageBody"].Controls.Add(grid);grid.BringToFront();
            var hint=new Label{Dock=DockStyle.Bottom,Height=64,Padding=new Padding(8),ForeColor=Color.FromArgb(100,116,139),Text="单播可能在关机一段时间后因网关 ARP 缓存失效而失效；目标广播可能被交换机/路由器丢弃。\r\n本地广播仅同一广播域有效；跨网段无法直接核对目标 MAC，请确认 IP/MAC 记录仍有效。目标掩码按实际填写。"};root.Controls["pageBody"].Controls.Add(hint);hint.BringToFront();
            var session=new PageSession(root,bar,stop,state);session.CancellationMessage="已停止后续唤醒包；已发出的包无法撤回，未确认开机。";
            var machines=new List<StudentMachine>();var results=new Dictionary<string,CrossRoomWakeResult>(StringComparer.OrdinalIgnoreCase);bool updating=false;
            Action render=delegate{
                if(!session.Alive)return;grid.Rows.Clear();string chosen=room.SelectedItem as string;
                foreach(var m in machines)if(String.Equals(m.Room,chosen,StringComparison.OrdinalIgnoreCase)){
                    CrossRoomWakeResult r;results.TryGetValue(m.Mac,out r);int i=grid.Rows.Add(m.Room,m.Name,m.IPv4,m.Mac,r==null?"尚未尝试":r.Summary);grid.Rows[i].Tag=m;if(r!=null)grid.Rows[i].Cells[4].ToolTipText=r.Details;
                }
                state.Text=grid.Rows.Count==0?"没有该机房记录；先在原学生机管理中保存/导入 IP 和 MAC。":"显示 "+grid.Rows.Count+" 台；进入页面仅读取记录，不发送唤醒包。";
            };
            room.SelectedIndexChanged+=delegate{if(!updating&&!session.Running)render();};
            Action showDetail=delegate{if(grid.CurrentRow==null)return;var m=grid.CurrentRow.Tag as StudentMachine;CrossRoomWakeResult r;if(m!=null&&results.TryGetValue(m.Mac,out r))ShowStudentPowerDetails(r.Details);else state.Text="这台设备尚无本次发送结果。";};
            detail.Click+=delegate{showDetail();};grid.CellDoubleClick+=delegate(object sender,DataGridViewCellEventArgs e){if(e.RowIndex>=0)showDetail();};
            manage.Click+=delegate{SelectNavigationPage("学生机与网络唤醒");};
            reload.Click+=async delegate{await session.Run(async delegate(CancellationToken token){
                var cached=studentMachines;string path=studentStorePath;
                var data=await Task.Run(delegate{token.ThrowIfCancellationRequested();var list=StudentRoster.Parse(StudentRoster.Serialize(cached??StudentRoster.Load(path)));var networks=wakeNetworkReader(token);token.ThrowIfCancellationRequested();return new StudentPageData{Machines=list,Networks=networks};});
                if(!session.Alive)return;machines=data.Machines;string previous=room.SelectedItem as string;updating=true;
                try{room.Items.Clear();var rooms=new SortedSet<string>(StringComparer.OrdinalIgnoreCase);foreach(var m in machines)rooms.Add(m.Room);foreach(string r in rooms)room.Items.Add(r);if(previous!=null&&room.Items.Contains(previous))room.SelectedItem=previous;else if(room.Items.Count>0)room.SelectedIndex=0;network.Items.Clear();foreach(var n in data.Networks)network.Items.Add(n);if(network.Items.Count>0)network.SelectedIndex=0;}finally{updating=false;}
                render();if(network.Items.Count==0)state.Text="未发现可用的本机 IPv4 网卡；未发送。";
            });};
            Func<bool,Task> send=async delegate(bool entireRoom){
                if(!session.Alive||session.Running)return;var targets=new List<StudentMachine>();string chosen=room.SelectedItem as string;
                if(chosen==null){state.Text="请先读取并选择具体机房。";return;}
                if(entireRoom){foreach(var m in machines)if(String.Equals(m.Room,chosen,StringComparison.OrdinalIgnoreCase))targets.Add(m);}
                else foreach(DataGridViewRow row in grid.SelectedRows)if(row.Tag is StudentMachine)targets.Add((StudentMachine)row.Tag);
                CrossRoomWakePlan plan;int bits;
                try{if(!Int32.TryParse(prefix.Text.Trim(),out bits))throw new ArgumentException("目标掩码请填写位数，例如24。");plan=CrossRoomWakeService.Prepare(targets,network.SelectedItem as WakeNetwork,bits,(CrossRoomWakeMode)mode.SelectedIndex,studentPowerLocalAddresses());}
                catch(Exception ex){state.Text="未发送："+ex.Message;return;}
                if(!(crossRoomWakeConfirmation==null?ConfirmCrossRoomWake(plan):crossRoomWakeConfirmation(plan))){state.Text="已取消，没有发送唤醒包。";return;}
                if(!session.Alive)return;
                foreach(var m in plan.Targets){results.Remove(m.Mac);foreach(DataGridViewRow row in grid.Rows)if(((StudentMachine)row.Tag).Mac==m.Mac){row.Cells[4].Value="本次尚未发送";row.Cells[4].ToolTipText="本次尚未发送";}}
                grid.Enabled=false;int completed=0;
                try{await session.Run(async delegate(CancellationToken token){await crossRoomWakeSender(plan,token,delegate(CrossRoomWakeResult r){if(!session.Alive)return;completed++;results[r.Machine.Mac]=r;foreach(DataGridViewRow row in grid.Rows)if(((StudentMachine)row.Tag).Mac==r.Machine.Mac){row.Cells[4].Value=r.Summary;row.Cells[4].ToolTipText=r.Details;}});if(session.Alive)state.Text="已处理 "+completed+"/"+plan.Targets.Count+" 台；发包不代表送达/开机。请实际查看学生机，日志在软件 Logs。";});}
                finally{if(session.Alive)grid.Enabled=true;}
            };
            selected.Click+=async delegate{await send(false);};whole.Click+=async delegate{await send(true);};
            root.BeginInvoke(new Action(delegate{if(session.Alive)reload.PerformClick();}));
        }
        private bool ConfirmCrossRoomWake(CrossRoomWakePlan plan)
        {
            using(var form=NewDialog("确认跨机房唤醒尝试",900,570)){
                var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(16),ColumnCount=1,RowCount=3};layout.RowStyles.Add(new RowStyle(SizeType.Absolute,112));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,48));
                layout.Controls.Add(new Label{Dock=DockStyle.Fill,Text="目标机房："+plan.Targets[0].Room+"；共 "+plan.Targets.Count+" 台；目标掩码：/"+plan.Prefix+"\r\n发送网卡："+plan.Source.Source+"；方式："+CrossRoomWakeService.ModeTitle(plan.Mode)+"；UDP 9，每条路径最多3包。\r\n发包使用保存的 IP/MAC，跨网段未直接核对对应关系；请确认记录有效。不会更改网络设置。UDP 没有回执，交给系统不代表实际送达或开机。"},0,0);
                var grid=Table(new[]{"学生机","IPv4","MAC","发包目的地址"});grid.MultiSelect=false;SetColumnWidths(grid,new[]{150,140,165,330});grid.Columns[3].AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill;
                foreach(var m in plan.Targets){var endpoints=CrossRoomWakeService.Destinations(plan,m);var labels=new List<string>();foreach(var e in endpoints)labels.Add(e.ToString());grid.Rows.Add(m.Name,m.IPv4,m.Mac,String.Join("；",labels));}layout.Controls.Add(grid,0,1);
                var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft};var cancel=FormButton("取消",0,0,110,delegate{form.DialogResult=DialogResult.Cancel;});var confirm=FormButton("确认尝试",0,0,140,delegate{form.DialogResult=DialogResult.OK;});buttons.Controls.AddRange(new Control[]{cancel,confirm});form.CancelButton=cancel;layout.Controls.Add(buttons,0,2);form.Controls.Add(layout);return form.ShowDialog(this)==DialogResult.OK;
            }
        }
    }
}
