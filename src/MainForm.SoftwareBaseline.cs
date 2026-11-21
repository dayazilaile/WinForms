using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ITDesktopToolkit
{
    public sealed partial class MainForm
    {
        private void ExportSoftwareCleanup(List<SoftwareBaselineEntry> entries)
        {
            var rules=SoftwareBaselineService.Checked(entries);
            using(var dialog=new Form{Text="导出批量卸载脚本",StartPosition=FormStartPosition.CenterParent,ClientSize=new Size(660,340),Font=Ui,BackColor=Canvas,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false})
            {
                var info=new Label{Left=22,Top=16,Width=610,Height=84,Text="内置当前标准（"+rules.Count+" 项），下发到学生机后重新读取本机软件，保留标准内项目。\r\n单个 BAT，使用正常 UAC；结束只弹窗，不生成结果报告或脚本日志。\r\n请在交互式桌面运行，以便看到确认和完成弹窗。"};
                var confirm=new RadioButton{Name="cleanupConfirm",Text="确认后批量卸载（支持静默的优先静默，其余打开卸载向导）",Left=24,Top=112,Width=610,Height=32,Checked=true};
                var automatic=new RadioButton{Name="cleanupAutomatic",Text="自动执行（仅使用登记的静默卸载方式，不猜测静默参数）",Left=24,Top=154,Width=610,Height=32};
                var note=new Label{Left=24,Top=202,Width=610,Height=62,Text="保护项或不支持的卸载方式会跳过，最后提示未全部完成。\r\n用户软件在原账户处理，全机软件按需申请 UAC；火绒、系统组件和驱动会跳过。"};
                var save=new Button{Text="生成 BAT",Left=410,Top=284,Width=106,Height=34};var cancel=new Button{Text="取消",Left=532,Top=284,Width=106,Height=34,DialogResult=DialogResult.Cancel};dialog.CancelButton=cancel;
                save.Click+=delegate{try{if(softwareCleanupExporter!=null)softwareCleanupExporter(rules,automatic.Checked);else using(var file=new SaveFileDialog{FileName=automatic.Checked?"标准软件清理_自动.bat":"标准软件清理_确认.bat",Filter="BAT 脚本|*.bat",OverwritePrompt=true}){if(file.ShowDialog(dialog)!=DialogResult.OK)return;SoftwareCleanupScriptService.ExportBat(file.FileName,rules,automatic.Checked);}dialog.DialogResult=DialogResult.OK;dialog.Close();}catch(Exception ex){MessageBox.Show(dialog,"生成失败："+ex.Message);}};
                dialog.Controls.AddRange(new Control[]{info,confirm,automatic,note,save,cancel});dialog.ShowDialog(this);
            }
        }
        private void EditSoftwareBaseline(IEnumerable<SoftwareBaselineEntry> entries,Action<List<SoftwareBaselineEntry>> commit,bool incomplete)
        {
            using(var dialog=new Form{Text="编辑软件标准清单",StartPosition=FormStartPosition.CenterParent,Size=new Size(900,580),MinimumSize=new Size(780,480),Font=Ui,BackColor=Canvas,AutoScaleMode=AutoScaleMode.Font})
            {
                var hint=new Label{Dock=DockStyle.Top,Height=66,Padding=new Padding(16,10,16,4),Text="勾选应保留的软件，可修改完整名称、发布者，或新增/删除行。发布者留空表示不限制；版本升级本身不算多出。\r\n"+(incomplete?"本机软件读取不完整，请补全并核对后保存。":"建议在配置正确的电脑生成，核对后导出到其他电脑。")};
                var grid=Table(new[]{"保留","软件完整名称","发布者（可空）"});grid.Name="softwareBaselineEditor";grid.ReadOnly=false;grid.AllowUserToAddRows=true;grid.AllowUserToDeleteRows=true;grid.MultiSelect=false;grid.SelectionMode=DataGridViewSelectionMode.CellSelect;
                grid.Columns.RemoveAt(0);grid.Columns.Insert(0,new DataGridViewCheckBoxColumn{Name="keep",HeaderText="保留",Width=65});grid.Columns[0].SortMode=DataGridViewColumnSortMode.NotSortable;
                grid.Columns[1].AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill;grid.Columns[1].FillWeight=60;grid.Columns[1].MinimumWidth=300;grid.Columns[2].AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill;grid.Columns[2].FillWeight=40;grid.Columns[2].MinimumWidth=220;
                grid.DefaultValuesNeeded+=delegate(object sender,DataGridViewRowEventArgs e){e.Row.Cells[0].Value=true;};
                foreach(var item in SoftwareBaselineService.Checked(entries))grid.Rows.Add(true,item.Name,item.Publisher);
                var bottom=new Panel{Dock=DockStyle.Bottom,Height=90,Padding=new Padding(12)};var error=new Label{Name="softwareBaselineError",Dock=DockStyle.Top,Height=30,ForeColor=Color.Firebrick,AutoEllipsis=true};bottom.Controls.Add(error);
                var buttons=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=38,FlowDirection=FlowDirection.RightToLeft};bottom.Controls.Add(buttons);
                var cancel=new Button{Text="取消",Width=90,Height=34,DialogResult=DialogResult.Cancel};
                var save=new Button{Text="保存标准清单",Width=145,Height=34};
                var remove=new Button{Text="删除所选行",Width=125,Height=34};
                buttons.Controls.Add(cancel);buttons.Controls.Add(save);buttons.Controls.Add(remove);dialog.CancelButton=cancel;
                remove.Click+=delegate{if(grid.CurrentRow!=null&&!grid.CurrentRow.IsNewRow)grid.Rows.Remove(grid.CurrentRow);};
                save.Click+=delegate{try{grid.EndEdit();var draft=new List<SoftwareBaselineEntry>();foreach(DataGridViewRow row in grid.Rows)if(!row.IsNewRow&&Convert.ToBoolean(row.Cells[0].Value))draft.Add(new SoftwareBaselineEntry{Name=Convert.ToString(row.Cells[1].Value),Publisher=Convert.ToString(row.Cells[2].Value)});commit(SoftwareBaselineService.Checked(draft));dialog.DialogResult=DialogResult.OK;dialog.Close();}catch(Exception ex){error.Text=ex.Message;}};
                dialog.Controls.Add(grid);dialog.Controls.Add(bottom);dialog.Controls.Add(hint);grid.BringToFront();dialog.ShowDialog(this);
            }
        }
    }
}
