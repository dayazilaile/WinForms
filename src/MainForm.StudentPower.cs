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
        private int studentPowerOutstanding;
        private void PreventStudentPowerClose(object sender,FormClosingEventArgs args)
        {if(studentPowerOutstanding>0&&args.CloseReason==CloseReason.UserClosing){args.Cancel=true;status.Text="远程电源请求仍在处理；请停止后续发送并等待当前请求返回，再关闭工具箱。";}}
        private Func<IEnumerable<string>> studentPowerLocalAddresses=StudentPowerService.LocalAddresses;
        private Func<StudentPowerPlan,bool> studentPowerConfirmation=null;
        private Func<StudentPowerPlan,WakeNetwork,CancellationToken,Action<StudentPowerDiagnostic>,Task> studentPowerInspector=StudentPowerDiagnostics.Inspect;
        private Action<string> studentLocalCheckExporter=null;
        private void ShowStudentPowerDetails(string text)
        {
            using(var form=NewDialog("学生机操作结果详情",860,590)){var box=ReadBox();box.Text=text;form.Controls.Add(box);form.ShowDialog(this);}
        }
        private Func<IEnumerable<StudentMachine>,WakeNetwork,StudentPowerAction,int,CancellationToken,Action<StudentPowerResult>,Task<StudentPowerBatch>> studentPowerSender=StudentPowerService.Run;
        private bool ConfirmStudentPower(StudentPowerPlan plan)
        {
            using(var form=NewDialog("确认学生机远程电源操作",860,570)){
                var layout=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=3,ColumnCount=1,Padding=new Padding(16)};
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute,120));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,56));
                var hint=new Label{Dock=DockStyle.Fill,Text="操作："+plan.Title+" · 共 "+plan.Targets.Count+" 台。请核对下面的真实目标。\r\n"+
                    (plan.Action==StudentPowerAction.Abort?"取消目标机尚未执行的关机/重启，不保证能撤销已经开始的操作。":"目标接受后倒计时 "+plan.Seconds+" 秒；不强制关闭未保存应用，因此可能被应用阻止。")+
                    "\r\n当前 Windows 账户需要目标机远程关机权限，且远程服务/防火墙允许。\r\n发送前核对 IP/MAC；停止只停止后续发送，已接受的请求需另行取消。"};
                var grid=Table(new[]{"机房","学生机","IPv4","MAC"});grid.Name="studentPowerTargets";grid.ReadOnly=true;grid.Dock=DockStyle.Fill;SetColumnWidths(grid,new[]{140,200,170,220});grid.Columns[3].AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill;
                foreach(var machine in plan.Targets)grid.Rows.Add(machine.Room,machine.Name,machine.IPv4,machine.Mac);
                var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft};
                var cancel=FormButton("取消",0,0,110,delegate{form.DialogResult=DialogResult.Cancel;});cancel.DialogResult=DialogResult.Cancel;form.CancelButton=cancel;
                var confirm=FormButton("确认"+plan.Title,0,0,210,delegate{form.DialogResult=DialogResult.OK;});confirm.Name="confirmStudentPower";buttons.Controls.AddRange(new Control[]{cancel,confirm});
                layout.Controls.Add(hint,0,0);layout.Controls.Add(grid,0,1);layout.Controls.Add(buttons,0,2);form.Controls.Add(layout);return form.ShowDialog(this)==DialogResult.OK;
            }
        }
    }
}
