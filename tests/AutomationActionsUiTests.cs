using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using WinForms;

public static class AutomationActionsUiTests
{
    static int passed;
    static void Check(bool value,string name){if(!value)throw new Exception(name);passed++;Console.WriteLine("PASS "+name);}
    static IEnumerable<Control> All(Control root){foreach(Control c in root.Controls){yield return c;foreach(var child in All(c))yield return child;}}
    static T[] Find<T>(Control root)where T:Control{return All(root).OfType<T>().ToArray();}
    static void Screenshot(Form form,string name){form.Refresh();Application.DoEvents();using(var bitmap=new Bitmap(form.ClientSize.Width,form.ClientSize.Height)){try{using(var g=Graphics.FromImage(bitmap))g.CopyFromScreen(form.PointToScreen(Point.Empty),Point.Empty,form.ClientSize);}catch{form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.ClientSize));}bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,name));}}
    static List<AutoStep> Edit(MainForm main,Action<Form> tick)
    {
        Exception problem=null;using(var timer=new System.Windows.Forms.Timer{Interval=40}){
            timer.Tick+=delegate{try{var f=Application.OpenForms.Cast<Form>().FirstOrDefault(x=>x.Text=="添加自动化动作");if(f!=null)tick(f);}catch(Exception ex){problem=ex;foreach(var f in Application.OpenForms.Cast<Form>().Where(f=>f!=main).ToArray())f.Close();}};
            timer.Start();var result=(List<AutoStep>)typeof(MainForm).GetMethod("EditStep",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(main,new object[]{null});timer.Stop();if(problem!=null)throw problem;return result;
        }
    }
    public static int Run(MainForm main)
    {
        passed=0;var fileField=typeof(MainForm).GetField("automationProcessFilePicker",BindingFlags.Instance|BindingFlags.NonPublic);var renameField=typeof(MainForm).GetField("automationRenamePicker",BindingFlags.Instance|BindingFlags.NonPublic);var serviceField=typeof(MainForm).GetField("automationServiceReader",BindingFlags.Instance|BindingFlags.NonPublic);object oldFile=fileField.GetValue(main),oldRename=renameField.GetValue(main),oldService=serviceField.GetValue(main);int picks=0,reads=0;
        try{
            fileField.SetValue(main,new Func<IWin32Window,string>(delegate{picks++;return @"D:\Programs\Demo App.exe";}));
            renameField.SetValue(main,new Func<IWin32Window,bool,string>(delegate(IWin32Window owner,bool folder){return folder?@"D:\old-folder":@"D:\old.txt";}));
            serviceField.SetValue(main,new Func<CancellationToken,AutomationServiceSnapshot>(delegate(CancellationToken token){reads++;token.ThrowIfCancellationRequested();var snapshot=new AutomationServiceSnapshot();snapshot.Rows.Add(new AutomationServiceRow{Name="RpcSs",Display="关键服务",State="Running",Selectable=false,Note="受保护"});snapshot.Rows.Add(new AutomationServiceRow{Name="DemoVendorSvc",Display="供应商服务",State="Running"});return snapshot;}));
            bool handled=false;var result=Edit(main,delegate(Form form){if(handled)return;handled=true;Find<ComboBox>(form).First().SelectedItem="结束进程";
                var mode=Find<ComboBox>(form).Single(c=>c.Name=="processEndMode");Check(mode.Visible&&mode.Text=="正常关闭"&&reads==0,"Process editor defaults to graceful close and does not enumerate services");
                Find<Button>(form).Single(b=>b.Text=="选择程序文件（自动填写进程名）").PerformClick();Check(Find<TextBox>(form).Single(t=>t.AccessibleName=="目标进程名称").Text=="Demo App.exe"&&picks==1,"Selecting an EXE fills only its process name without running it or entering PID");mode.SelectedItem="强制结束";Screenshot(form,"automation-process-v067.png");Find<Button>(form).Single(b=>b.Text=="确定添加 / 保存").PerformClick();});
            Check(result.Count==1&&result[0].Type=="结束进程"&&result[0].A=="Demo App.exe"&&result[0].B=="强制结束"&&result[0].C=="15","Editor preserves selected process name/mode/timeout without selected machine path");
            int stage=0;result=Edit(main,delegate(Form form){
                if(stage==0){stage=1;Find<ComboBox>(form).First().SelectedItem="停止服务";form.BeginInvoke(new Action(delegate{Find<Button>(form).Single(b=>b.Name=="selectAutomationService").PerformClick();}));return;}
                if(stage==1){var list=Application.OpenForms.Cast<Form>().SingleOrDefault(f=>f.Text=="选择要停止的服务（只读列表）");if(list==null)return;var grid=Find<DataGridView>(list).Single();if(grid.Rows.Count<2)return;Check(grid.ReadOnly&&reads==1,"Service picker performs one read-only catalogue query");grid.CurrentCell=grid.Rows[0].Cells[0];Check(!Find<Button>(list).Single(b=>b.Name=="chooseAutomationService").Enabled,"Protected service is visible but cannot be selected for stop");Find<TextBox>(list).Single(t=>t.Name=="automationServiceSearch").Text="供应商";Check(grid.Rows.Count==1,"Service picker searches display names without additional native query");stage=2;Find<Button>(list).Single(b=>b.Name=="chooseAutomationService").PerformClick();return;}
                if(stage==2){stage=3;Check(Find<TextBox>(form).Single(t=>t.AccessibleName=="服务内部名称").Text=="DemoVendorSvc","Choosing display row fills internal service name instead of display label");Screenshot(form,"automation-service-v067.png");Find<Button>(form).Single(b=>b.Text=="确定添加 / 保存").PerformClick();}
            });
            Check(result.Count==1&&result[0].Type=="停止服务"&&result[0].A=="DemoVendorSvc"&&result[0].B=="30"&&result[0].C=="","Service editor retains one exact internal name and default bounded timeout");
            foreach(bool folder in new[]{false,true}){
                handled=false;result=Edit(main,delegate(Form form){if(handled)return;handled=true;Find<ComboBox>(form).First().SelectedItem=folder?"重命名文件夹":"重命名文件";Find<Button>(form).Single(b=>b.Text==(folder?"选择要重命名的文件夹":"选择要重命名的文件")).PerformClick();var source=Find<TextBox>(form).Single(t=>t.AccessibleName==(folder?"文件夹完整路径":"文件完整路径"));var name=Find<TextBox>(form).Single(t=>t.AccessibleName==(folder?"新文件夹名称":"新名称（含后缀）"));Check(source.Text==(folder?@"D:\old-folder":@"D:\old.txt")&&name.Text=="","Rename selection fills only source and leaves new name explicit");name.Text="../bad";Find<Button>(form).Single(b=>b.Text=="确定添加 / 保存").PerformClick();Check(form.Visible&&Find<Label>(form).Any(l=>l.Visible&&l.Text.Contains("新名称")),"Invalid rename stays in editor with visible explanation");name.Text=folder?"新文件夹":"new.txt";Screenshot(form,folder?"automation-folder-rename-v067.png":"automation-file-rename-v067.png");Find<Button>(form).Single(b=>b.Text=="确定添加 / 保存").PerformClick();});
                Check(result.Count==1&&result[0].Type==(folder?"重命名文件夹":"重命名文件")&&result[0].C=="","Rename action saves without executing a file or directory operation");
            }
        }finally{fileField.SetValue(main,oldFile);renameField.SetValue(main,oldRename);serviceField.SetValue(main,oldService);}return passed;
    }
}
