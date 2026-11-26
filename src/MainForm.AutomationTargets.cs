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
        private Func<IWin32Window,string> automationProcessFilePicker=null;
        private Func<IWin32Window,bool,string> automationRenamePicker=null;
        private string PickAutomationRename(IWin32Window owner,bool folder)
        {
            if(automationRenamePicker!=null)return automationRenamePicker(owner,folder);
            if(folder){using(var pick=new FolderBrowserDialog{Description="选择要重命名的普通文件夹（不移动内容）",ShowNewFolderButton=false})return pick.ShowDialog(owner)==DialogResult.OK?pick.SelectedPath:null;}
            using(var pick=new OpenFileDialog{Title="选择要重命名的文件",Filter="所有文件|*.*",CheckFileExists=true,CheckPathExists=true,RestoreDirectory=true})return pick.ShowDialog(owner)==DialogResult.OK?pick.FileName:null;
        }
        private Func<CancellationToken,AutomationServiceSnapshot> automationServiceReader=delegate(CancellationToken token){return AutomationServiceCatalog.Read(token);};
        private string PickAutomationProcessFile(IWin32Window owner)
        {
            if(automationProcessFilePicker!=null)return automationProcessFilePicker(owner);
            using(var file=new OpenFileDialog{Filter="EXE程序 (*.exe)|*.exe",CheckFileExists=true,CheckPathExists=true,RestoreDirectory=true,Title="选择要结束的应用主EXE（只读取文件名，不运行）"})return file.ShowDialog(owner)==DialogResult.OK?file.FileName:null;
        }
        private string PickAutomationService(IWin32Window owner)
        {
            using(var form=NewDialog("选择要停止的服务（只读列表）",910,620)){
                var bar=new FlowLayoutPanel{Dock=DockStyle.Top,Height=48,Padding=new Padding(10)};var search=new TextBox{Width=330,Name="automationServiceSearch",AccessibleName="搜索服务名称或显示名"};var refresh=new Button{Text="读取 / 刷新服务",Width=150,Height=30,Name="readAutomationServices"};bar.Controls.AddRange(new Control[]{search,refresh});
                var footer=new Panel{Dock=DockStyle.Bottom,Height=74};var status=new Label{Dock=DockStyle.Top,Height=32,Text="只读取制作脚本这台电脑的服务；选中后自动填名称，不会停止服务。",AutoEllipsis=true};var select=new Button{Text="选中并填入",Width=160,Height=32,Left=590,Top=35,Name="chooseAutomationService",Enabled=false};var cancel=new Button{Text="取消",Width=110,Height=32,Left=765,Top=35,DialogResult=DialogResult.Cancel};footer.Controls.AddRange(new Control[]{status,select,cancel});
                var grid=Grid();grid.Name="automationServicesGrid";AddColumn(grid,"name","服务内部名称",230,false);AddColumn(grid,"display","显示名称",230,false);AddColumn(grid,"state","状态",105,false);AddColumn(grid,"note","说明",290,true);grid.MultiSelect=false;form.Controls.Add(grid);form.Controls.Add(bar);form.Controls.Add(footer);form.CancelButton=cancel;
                AutomationServiceSnapshot snapshot=null;CancellationTokenSource current=null;string result=null;
                Action render=delegate{if(form.IsDisposed)return;grid.Rows.Clear();if(snapshot!=null)foreach(var row in snapshot.Rows)if((row.Name+" "+row.Display).IndexOf(search.Text.Trim(),StringComparison.OrdinalIgnoreCase)>=0){int i=grid.Rows.Add(row.Name,row.Display,row.State,row.Note);grid.Rows[i].Tag=row;if(!row.Selectable)grid.Rows[i].DefaultCellStyle.ForeColor=Color.Gray;}select.Enabled=grid.CurrentRow!=null&&((AutomationServiceRow)grid.CurrentRow.Tag).Selectable;};
                grid.SelectionChanged+=delegate{select.Enabled=grid.CurrentRow!=null&&grid.CurrentRow.Tag is AutomationServiceRow&&((AutomationServiceRow)grid.CurrentRow.Tag).Selectable;};search.TextChanged+=delegate{render();};
                select.Click+=delegate{if(grid.CurrentRow==null)return;var row=(AutomationServiceRow)grid.CurrentRow.Tag;if(!row.Selectable){status.Text=row.Note;return;}try{result=AutomationService.CheckStopServiceName(row.Name);form.DialogResult=DialogResult.OK;}catch(Exception ex){status.Text=ex.Message;}};
                grid.CellDoubleClick+=delegate(object sender,DataGridViewCellEventArgs e){if(e.RowIndex>=0&&select.Enabled)select.PerformClick();};
                refresh.Click+=async delegate{if(current!=null)return;var cancelRead=new CancellationTokenSource();current=cancelRead;refresh.Enabled=false;select.Enabled=false;status.Text="正在只读查询本机服务…";
                    try{var data=await Task.Run(delegate{return automationServiceReader(cancelRead.Token);},cancelRead.Token);if(form.IsDisposed||cancelRead.IsCancellationRequested)return;snapshot=data;render();status.Text="已读取 "+data.Rows.Count+" 项；无法读取 "+data.Unreadable+" 项。仅填入内部名称，运行BAT时在目标电脑重新核对。";}
                    catch(OperationCanceledException){}catch(Exception ex){if(!form.IsDisposed)status.Text="读取失败："+ex.Message;}
                    finally{current=null;cancelRead.Dispose();if(!form.IsDisposed)refresh.Enabled=true;}
                };
                form.FormClosed+=delegate{if(current!=null)current.Cancel();};form.Shown+=delegate{refresh.PerformClick();};
                return form.ShowDialog(owner)==DialogResult.OK?result:null;
            }
        }
    }
}
