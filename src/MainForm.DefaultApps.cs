using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace WinForms
{
    public sealed partial class MainForm
    {
        private Func<string,string> defaultApplicationReader=FileAssociationService.FriendlyName;
        private Action<string,string> defaultApplicationSettings=CommandService.OpenTool;
        private Func<IntPtr,string,string,Version,bool> defaultApplicationChooser=DefaultAppsService.ChooseLegacy;
        private Func<string,List<DefaultApplicationOption>> defaultApplicationOptions=DefaultAppsService.RegisteredApplications;
        private Func<IWin32Window,string,IList<DefaultApplicationOption>,DefaultApplicationChoice> defaultApplicationPicker;
        private Version defaultApplicationWindows=Environment.OSVersion.Version;
        private void DefaultAppsRepairDialog()
        {
            using(var form=NewDialog("默认应用与文件关联修复",1000,760))
            {
                form.MinimumSize=new Size(840,680);
                var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=5,Padding=new Padding(16)};
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute,98));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,46));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,54));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,94));
                var hint=new Label{Dock=DockStyle.Fill,AutoEllipsis=false,Text=DefaultAppsService.Guidance(defaultApplicationWindows)+"\r\nOffice、解压和播放器需要先安装；应用名称只作核对线索，不据此判断是否被劫持。\r\n用于本机当前账户，系统确认后会保存默认程序；返回此窗口刷新核对。"};
                var filter=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};
                var category=new ComboBox{Name="associationCategory",DropDownStyle=ComboBoxStyle.DropDownList,Width=155};category.Items.Add("全部类型");
                var categories=new HashSet<string>();foreach(var format in DefaultAppsService.Formats)if(categories.Add(format.Category))category.Items.Add(format.Category);category.SelectedIndex=0;
                var search=new TextBox{Name="associationSearch",Width=260,AccessibleName="搜索格式或默认程序"};
                filter.Controls.AddRange(new Control[]{category,new Label{Text="搜索格式 / 程序",AutoSize=true,Margin=new Padding(12,6,4,0)},search});
                var grid=Table(new[]{"类别","格式 / 协议","用途","当前默认程序"});grid.Name="defaultAssociationsGrid";grid.Dock=DockStyle.Fill;grid.ReadOnly=true;grid.SelectionMode=DataGridViewSelectionMode.FullRowSelect;grid.MultiSelect=false;
                SetColumnWidths(grid,new[]{130,110,190,330});grid.Columns[3].AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill;
                var state=new Label{Name="associationStatus",Dock=DockStyle.Fill,AutoEllipsis=false};
                var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoScroll=true,WrapContents=false};
                var cache=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                Action render=delegate{
                    string chosen=Convert.ToString(category.SelectedItem),query=search.Text.Trim();grid.Rows.Clear();
                    foreach(var format in DefaultAppsService.Formats){string app;cache.TryGetValue(format.Extension,out app);if(category.SelectedIndex>0&&format.Category!=chosen)continue;
                        if(query.Length>0&&(format.Category+" "+format.Extension+" "+format.Description+" "+app).IndexOf(query,StringComparison.OrdinalIgnoreCase)<0)continue;
                        int index=grid.Rows.Add(format.Category,format.Extension,format.Description,app);grid.Rows[index].Tag=format;}
                    state.Text="当前账户："+Environment.UserName+" · 显示 "+grid.Rows.Count+" 项。选中一行后选择默认程序；系统中确认后返回刷新核对。";
                };
                Action refresh=delegate{cache.Clear();foreach(var format in DefaultAppsService.Formats)cache[format.Extension]=DefaultAppsService.ReadApplication(format.Extension,defaultApplicationReader);render();};
                Action openSettings=delegate{
                    try{defaultApplicationSettings(DefaultAppsService.SettingsTarget(defaultApplicationWindows),DefaultAppsService.SettingsArguments(defaultApplicationWindows));
                        state.Text="已请求打开系统默认应用。选中扩展名/协议完成确认后，返回这里点击刷新核对；此时尚不能确认已修改。";}
                    catch(Exception ex){state.Text="默认应用设置未打开："+ex.Message;}
                };
                Action choose=delegate{
                    if(grid.CurrentRow==null){state.Text="请先选择格式或协议。";return;}var format=(DefaultAppFormat)grid.CurrentRow.Tag;
                    if(!DefaultAppsService.SupportsNativeSelection(defaultApplicationWindows)){
                        try{var options=defaultApplicationOptions(format.Extension);var choice=defaultApplicationPicker==null?ChooseDefaultApplicationDialog(form,format.Extension,options):defaultApplicationPicker(form,format.Extension,options);
                            if(choice==null){state.Text="已取消选择，未打开设置或修改默认程序。";return;}
                            defaultApplicationSettings(DefaultAppsService.ApplicationSettingsTarget(defaultApplicationWindows,choice.Application),DefaultAppsService.SettingsArguments(defaultApplicationWindows));
                            state.Text="已请求打开系统确认入口；格式："+format.Extension+(choice.Application==null?"":"；所选应用："+choice.Application.Name)+"。\r\n请在 Windows 中确认默认程序，再返回刷新。当前尚未确认关联已变更。";
                        }catch(Exception ex){state.Text="无法选择默认程序："+ex.Message;}return;
                    }
                    try{bool accepted;using(var audit=OperationAudit.Begin("选择默认程序",format.Extension)){
                        accepted=defaultApplicationChooser(form.Handle,format.Extension,Paths.UserDir,defaultApplicationWindows);audit.End(accepted?"选择窗口已关闭":"已取消","请按刷新结果核对实际关联");}
                        refresh();state.Text=accepted?"系统选择窗口已关闭。当前显示实际查询结果，请核对；关闭窗口不代表关联已变更。":"已取消选择，未请求后续修改。";}
                    catch(Exception ex){state.Text="无法选择默认程序："+ex.Message+"。可使用系统默认应用入口。";}
                };
                Action reset=delegate{openSettings();state.Text+=DefaultAppsService.SupportsNativeSelection(defaultApplicationWindows)?"\r\n请在默认程序中选择已安装的系统应用并设置默认值；没有统一重置按钮。":"\r\n在默认应用页面找到“重置”（重置所有默认应用），阅读系统提示后确认。会影响个人自定义关联；完成后返回刷新。";};
                Button select=FormButton("选择默认程序",0,0,155,delegate{choose();});select.Name="chooseDefaultApplication";
                Button reload=FormButton("刷新关联",0,0,110,delegate{refresh();});reload.Name="refreshDefaultAssociations";
                Button defaults=FormButton("恢复系统默认（指导）",0,0,195,delegate{reset();});defaults.Name="resetDefaultApplications";
                Button settings=FormButton("系统默认应用",0,0,145,delegate{openSettings();});settings.Name="openDefaultApplications";
                foreach(var button in new[]{select,reload,defaults,settings})button.Margin=new Padding(0,4,8,0);
                buttons.Controls.AddRange(new Control[]{select,reload,defaults,settings});
                category.SelectedIndexChanged+=delegate{render();};search.TextChanged+=delegate{render();};grid.CellDoubleClick+=delegate(object sender,DataGridViewCellEventArgs e){if(e.RowIndex>=0)choose();};
                layout.Controls.Add(hint,0,0);layout.Controls.Add(filter,0,1);layout.Controls.Add(grid,0,2);layout.Controls.Add(buttons,0,3);layout.Controls.Add(state,0,4);
                form.Controls.Add(layout);refresh();form.ShowDialog(this);
            }
        }
        private DefaultApplicationChoice ChooseDefaultApplicationDialog(IWin32Window owner,string extension,IList<DefaultApplicationOption> applications)
        {
            using(var form=NewDialog("选择默认程序 · "+extension,760,480)){
                form.Name="chooseDefaultApplicationDialog";var layout=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=3,ColumnCount=1,Padding=new Padding(16)};
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute,86));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,54));
                var hint=new Label{Dock=DockStyle.Fill,Text="格式："+extension+"。以下为登记支持此格式的应用，选择后打开 Windows 页面确认保存。\r\nWin10 和较早 Win11 可能仍需在系统总页定位此格式；所选应用不会在此直接写为默认。\r\n未列出的程序可通过“系统中选择其他应用”处理；没有候选项也可能是应用未登记或读取受限。"};
                var grid=Table(new[]{"应用","适用范围"});grid.Name="defaultApplicationChoices";grid.ReadOnly=true;grid.Dock=DockStyle.Fill;grid.MultiSelect=false;grid.SelectionMode=DataGridViewSelectionMode.FullRowSelect;SetColumnWidths(grid,new[]{480,160});grid.Columns[0].AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill;
                foreach(var item in applications){int i=grid.Rows.Add(item.Name,item.PerUser?"当前用户":"本机用户");grid.Rows[i].Tag=item;}
                var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false,AutoScroll=true};DefaultApplicationChoice result=null;
                var confirm=FormButton("选此应用，进入系统确认",0,0,230,delegate{if(grid.CurrentRow==null)return;result=new DefaultApplicationChoice{Application=(DefaultApplicationOption)grid.CurrentRow.Tag};form.DialogResult=DialogResult.OK;});confirm.Name="confirmDefaultApplicationChoice";confirm.Enabled=applications.Count>0;
                var other=FormButton("系统中选择其他应用",0,0,200,delegate{result=new DefaultApplicationChoice();form.DialogResult=DialogResult.OK;});other.Name="otherDefaultApplicationChoice";
                var cancel=FormButton("取消",0,0,95,delegate{form.DialogResult=DialogResult.Cancel;});cancel.DialogResult=DialogResult.Cancel;form.CancelButton=cancel;
                foreach(var button in new[]{confirm,other,cancel})button.Margin=new Padding(0,4,8,0);buttons.Controls.AddRange(new Control[]{confirm,other,cancel});
                layout.Controls.Add(hint,0,0);layout.Controls.Add(grid,0,1);layout.Controls.Add(buttons,0,2);form.Controls.Add(layout);return form.ShowDialog(owner)==DialogResult.OK?result:null;
            }
        }
    }
}
