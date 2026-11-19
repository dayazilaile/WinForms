using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using ITDesktopToolkit;
class StandaloneUiTests
{
    static int passed;static int uiErrors;
    static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);passed++;}
    static T Field<T>(object instance,string name){return (T)instance.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(instance);}
    static object Call(object instance,string name,params object[] args){return instance.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(instance,args);}
    static T[] Find<T>(Control root)where T:Control{return root.Controls.Cast<Control>().SelectMany(c=>new Control[]{c}.Concat(Descendants(c))).OfType<T>().ToArray();}
    static System.Collections.Generic.IEnumerable<Control> Descendants(Control root){foreach(Control c in root.Controls){yield return c;foreach(Control child in Descendants(c))yield return child;}}
    static void Pump(Func<bool> finished,int timeout=5000){var watch=System.Diagnostics.Stopwatch.StartNew();while(!finished()){Application.DoEvents();if(watch.ElapsedMilliseconds>timeout)throw new Exception("UI operation did not finish");System.Threading.Thread.Sleep(10);}Application.DoEvents();}
    static void Screenshot(Form form,string name){form.Refresh();Application.DoEvents();using(var image=new Bitmap(form.ClientSize.Width,form.ClientSize.Height)){try{using(var g=Graphics.FromImage(image))g.CopyFromScreen(form.PointToScreen(Point.Empty),Point.Empty,form.ClientSize);}catch(System.Runtime.InteropServices.ExternalException){form.DrawToBitmap(image,new Rectangle(Point.Empty,form.ClientSize));Console.WriteLine("Screenshot fallback: "+name);}image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,name));}}
    [STAThread]static int Main()
    {
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.ThreadException+=delegate(object sender,System.Threading.ThreadExceptionEventArgs e){uiErrors++;Console.Error.WriteLine(e.Exception);};
        try{using(var main=new MainForm()){
            main.Show();Application.DoEvents();
            Check(main.Text.Contains("v0.5.13-BAT")&&Find<TreeView>(main).Length==0&&Find<DataGridView>(main).Length==1,"Standalone opens directly in BAT editor without toolkit navigation");
            Check(!Directory.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Database")),"Standalone starts in a clean folder with no Database directory");
                var actions=Field<System.Collections.Generic.List<AutoStep>>(main,"autoSteps");actions.Add(new AutoStep{Type="运行程序并等待",A="notepad.exe",B="example.txt",C="30"});foreach(Control old in Field<Panel>(main,"content").Controls.Cast<Control>().ToArray())old.Dispose();Call(main,"BuildAutomation");Application.DoEvents();var autoBody=Field<Panel>(main,"content");var actionGrid=Find<DataGridView>(autoBody).Single();
                Check(Find<CheckBox>(autoBody).Single(c=>c.Text.Contains("自动弹出 UAC")).Checked&&Find<CheckBox>(autoBody).Single(c=>c.Text.Contains("执行到末尾后删除")).Checked&&Find<CheckBox>(autoBody).Single(c=>c.Text.Contains("启用跨机房环境诊断")).Checked&&Find<CheckBox>(autoBody).Any(c=>c.Text=="隐藏运行窗口"),"All existing automation options and their explicit default selections remain available");
                Check(!Find<Button>(autoBody).Any(b=>b.Text=="上移"||b.Text=="下移"),"BAT editor removes old move buttons while retaining drag handle");
                Find<Button>(autoBody).Single(b=>b.Text=="复制动作").PerformClick();Check(actions.Count==2&&!Object.ReferenceEquals(actions[0],actions[1])&&actions[1].A==actions[0].A&&actions[1].B==actions[0].B&&actions[1].C==actions[0].C&&actionGrid.CurrentRow.Index==1,"Copy duplicates all step parameters independently immediately after selection");
                actions[1].A="other.exe";Check(actions[0].A=="notepad.exe","Editing a copy leaves the original step unchanged");
                Call(actionGrid,"OnKeyDown",new KeyEventArgs(Keys.Control|Keys.D));Check(actions.Count==3&&actionGrid.CurrentRow.Index==2,"Ctrl+D creates another editable step copy");Screenshot(main,"bat-editor.png");
                int beforeRandom=actions.Count;bool randomAdded=false;
                using(var timer=new Timer{Interval=40}){timer.Tick+=delegate{var f=Application.OpenForms.Cast<Form>().LastOrDefault(w=>w.Text=="添加自动化动作"&&w.Enabled);if(f==null)return;Find<ComboBox>(f).Single(c=>c.Items.Contains("随机等待")).SelectedItem="随机等待";var low=Find<TextBox>(f).Single(t=>t.AccessibleName=="最短等待（秒）");var high=Find<TextBox>(f).Single(t=>t.AccessibleName=="最长等待（秒）");Check(low.Text=="1"&&high.Text=="20"&&!Find<TextBox>(f).Single(t=>t.AccessibleName=="参数 3").Visible,"Random wait shows named second bounds, default 1 to 20, and hides unused parameter");low.Text="20";high.Text="1";Find<Button>(f).Single(b=>b.Text=="确定添加 / 保存").PerformClick();Check(f.Visible&&actions.Count==beforeRandom&&Find<Label>(f).Single(l=>l.Name=="conditionError").Text.Length>0,"Reversed random range stays in the editor without adding an action");low.Text="1";high.Text="20";Screenshot(f,"random-wait-v0512.png");randomAdded=true;Find<Button>(f).Single(b=>b.Text=="确定添加 / 保存").PerformClick();};timer.Start();Find<Button>(autoBody).Single(b=>b.Text=="添加动作").PerformClick();timer.Stop();}
                Check(randomAdded&&actions.Count==beforeRandom+1&&actions.Last().Type=="随机等待"&&actions.Last().A=="1"&&actions.Last().B=="20"&&actions.Last().C=="","Saving random wait stores only the normalized second range");actionGrid.CurrentCell=actionGrid.Rows[beforeRandom].Cells[2];
                using(var timer=new Timer{Interval=40}){timer.Tick+=delegate{var f=Application.OpenForms.Cast<Form>().LastOrDefault(w=>w.Text=="编辑自动化动作"&&w.Enabled);if(f==null)return;var low=Find<TextBox>(f).Single(t=>t.AccessibleName=="最短等待（秒）");var high=Find<TextBox>(f).Single(t=>t.AccessibleName=="最长等待（秒）");Check(low.Text=="1"&&high.Text=="20","Reopening random wait keeps its saved bounds");low.Text="5";high.Text="50";Find<Button>(f).Single(b=>b.Text=="确定添加 / 保存").PerformClick();};timer.Start();Find<Button>(autoBody).Single(b=>b.Text=="编辑动作").PerformClick();timer.Stop();}
                Check(actions[beforeRandom].A=="5"&&actions[beforeRandom].B=="50"&&AutomationService.MakeScript(actions,BatInputEngine.SendInput,0,false).Contains("-Minimum 5 -Maximum 51"),"Edited random bounds export with the upper second included");
                int branchStage=0;int branchStart=actions.Count;
                using(var timer=new Timer{Interval=40})
                {
                    timer.Tick+=delegate{
                        var f=Application.OpenForms.Cast<Form>().LastOrDefault(w=>w.Text=="添加自动化动作"&&w.Enabled);if(f==null)return;
                        var choice=Find<ComboBox>(f).Single(c=>c.Items.Contains("如果网络可达"));
                        if(branchStage==0){choice.SelectedItem="如果网络可达";Find<TextBox>(f).Single(t=>t.AccessibleName=="目标 IP / 主机名").Text="192.0.2.1";Check(Find<GroupBox>(f).Count(g=>g.Visible&&g.Text.Contains("时执行"))==2&&!Find<TextBox>(f).Single(t=>t.AccessibleName=="参数 3").Visible,"Network condition shows two branch columns with a named target field and hides unused parameters");branchStage=1;f.BeginInvoke(new Action(delegate{Find<Button>(Find<GroupBox>(f).Single(g=>g.Text=="条件成立时执行")).Single(b=>b.Text=="添加动作").PerformClick();}));}
                        else if(branchStage==1){choice.SelectedItem="等待毫秒";Find<TextBox>(f).Single(t=>t.AccessibleName=="参数 1").Text="1000";branchStage=2;Find<Button>(f).Single(b=>b.Text=="确定添加 / 保存").PerformClick();}
                        else if(branchStage==2){branchStage=3;Check(Find<ListBox>(Find<GroupBox>(f).Single(g=>g.Text=="条件成立时执行")).Single().Items.Count==1,"Confirmed child action appears in the true branch draft");f.BeginInvoke(new Action(delegate{Find<Button>(Find<GroupBox>(f).Single(g=>g.Text=="条件不成立时执行")).Single(b=>b.Text=="添加动作").PerformClick();}));}
                        else if(branchStage==3){choice.SelectedItem="等待毫秒";Find<TextBox>(f).Single(t=>t.AccessibleName=="参数 1").Text="2000";branchStage=4;Find<Button>(f).Single(b=>b.Text=="确定添加 / 保存").PerformClick();}
                        else if(branchStage==4){choice.SelectedItem="如果系统版本";Check(Find<TextBox>(f).Single(t=>t.AccessibleName=="Build 上限（可空）").Visible&&Find<ListBox>(f).All(l=>l.Items.Count==1),"System condition labels its Build bounds while retaining both branch drafts");choice.SelectedItem="如果文件存在";Check(Find<TextBox>(f).Single(t=>t.AccessibleName=="文件完整路径").Visible&&Find<Button>(f).Any(b=>b.Visible&&b.Text=="浏览选择要判断的文件"),"File condition has a concrete file-path field and matching file-picker caption");choice.SelectedItem="如果网络可达";var target=Find<TextBox>(f).Single(t=>t.AccessibleName=="目标 IP / 主机名");target.Clear();Find<Button>(f).Single(b=>b.Text=="确定添加 / 保存").PerformClick();Check(f.Visible&&actions.Count==branchStart&&Find<Label>(f).Single(l=>l.Name=="conditionError").Text.Length>0,"Invalid condition stays in editor and leaves main workflow unchanged");target.Text="192.0.2.1";Screenshot(f,"bat-branches.png");branchStage=5;Find<Button>(f).Single(b=>b.Text=="确定添加 / 保存").PerformClick();}
                    };
                    timer.Start();Find<Button>(autoBody).Single(b=>b.Text=="添加动作").PerformClick();timer.Stop();
                }
                Check(actions.Skip(branchStart).Select(a=>a.Type).SequenceEqual(new[]{"如果网络可达","等待毫秒","否则","等待毫秒","结束条件"}),"Saving the two-column editor adds complete true/false branches and closing markers automatically");
                actionGrid.CurrentCell=actionGrid.Rows[branchStart].Cells[2];Find<Button>(autoBody).Single(b=>b.Text=="复制动作").PerformClick();Check(actions.Count==branchStart+10&&!Object.ReferenceEquals(actions[branchStart+1],actions[branchStart+6]),"Copying a condition duplicates both branches and delimiters independently");
                int copiedStart=branchStart+5;actionGrid.CurrentCell=actionGrid.Rows[copiedStart].Cells[2];string originalFlow=String.Join("|",actions.Select(a=>a.ToString()));bool cancelledBranch=false;
                using(var timer=new Timer{Interval=40}){timer.Tick+=delegate{var f=Application.OpenForms.Cast<Form>().LastOrDefault(w=>w.Text=="编辑自动化动作"&&w.Enabled);if(f==null)return;var left=Find<GroupBox>(f).Single(g=>g.Text=="条件成立时执行");var right=Find<GroupBox>(f).Single(g=>g.Text=="条件不成立时执行");Check(Find<ListBox>(left).Single().Items.Count==1&&Find<ListBox>(right).Single().Items.Count==1,"Reopening a copied legacy-format condition reconstructs both branch columns");Find<Button>(left).Single(b=>b.Text=="删除").PerformClick();cancelledBranch=true;f.Close();};timer.Start();Find<Button>(autoBody).Single(b=>b.Text=="编辑动作").PerformClick();timer.Stop();}
                Check(cancelledBranch&&originalFlow==String.Join("|",actions.Select(a=>a.ToString())),"Cancelling branch edits preserves the entire original main workflow");
                int physicalReads=0,physicalMoves=0;Point movedTo=Point.Empty;
                main.GetType().GetField("mousePositionReader",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(main,new Func<Point>(delegate{physicalReads++;if(physicalReads>1)throw new InvalidOperationException("模拟物理坐标读取失败");return new Point(1560,-240);}));
                main.GetType().GetField("mousePositionMover",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(main,new Action<Point>(delegate(Point target){physicalMoves++;movedTo=target;}));
                int mouseStage=0;
                using(var timer=new Timer{Interval=40})
                {
                    timer.Tick+=delegate{var f=Application.OpenForms.Cast<Form>().FirstOrDefault(w=>w.Text=="添加自动化动作");if(f==null)return;
                        var capture=Find<Button>(f).Single(b=>b.Name=="captureMouseCoordinates");var test=Find<Button>(f).Single(b=>b.Name=="testMouseCoordinates");
                        var x=Find<TextBox>(f).Single(t=>t.AccessibleName=="参数 1");var y=Find<TextBox>(f).Single(t=>t.AccessibleName=="参数 2");
                        if(mouseStage==0){mouseStage=1;capture.PerformClick();Check(physicalReads==0,"Mouse capture waits for countdown instead of recording the button-click location");}
                        else if(mouseStage==1&&capture.Enabled){Check(x.Text=="1560"&&y.Text=="-240"&&physicalReads==1,"Captured physical pixels and negative monitor coordinates are saved without DPI rescaling");mouseStage=2;test.PerformClick();}
                        else if(mouseStage==2&&test.Enabled){Check(physicalMoves==1&&movedTo==new Point(1560,-240),"Coordinate preview moves to the exact captured physical point without scaling");Screenshot(f,"bat-mouse.png");mouseStage=3;capture.PerformClick();}
                        else if(mouseStage==3&&capture.Enabled){Check(x.Text=="1560"&&y.Text=="-240"&&Find<Label>(f).Any(l=>l.Text.Contains("模拟物理坐标读取失败")),"Failed physical capture keeps existing coordinates and reports the failure");mouseStage=4;capture.PerformClick();Find<ComboBox>(f).Single(c=>c.Items.Contains("移动鼠标")).SelectedItem="等待毫秒";Check(capture.Enabled&&!capture.Visible,"Changing action cancels pending mouse capture before it can overwrite another action");f.Close();}
                    };
                    timer.Start();Call(main,"EditStep",(object)null);timer.Stop();
                }
            main.Close();var wait=System.Diagnostics.Stopwatch.StartNew();Pump(()=>wait.ElapsedMilliseconds>=3200,10000);
            Check(physicalReads==2&&physicalMoves==1,"Cancelled capture timers never run after standalone editor closes");Check(uiErrors==0,"Standalone has no unhandled UI exceptions");
        }Console.WriteLine("STANDALONE UI TOTAL PASS "+passed);return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
}
