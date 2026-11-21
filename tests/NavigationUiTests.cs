using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using ITDesktopToolkit;
class NavigationUiTests
{
    static int passed;static int uiErrors;
    static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);passed++;}
    static T Field<T>(object instance,string name){return (T)instance.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(instance);}
    static object Call(object instance,string name,params object[] args){return instance.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(instance,args);}
    static T[] Find<T>(Control root)where T:Control{return root.Controls.Cast<Control>().SelectMany(c=>new Control[]{c}.Concat(Descendants(c))).OfType<T>().ToArray();}
    static System.Collections.Generic.IEnumerable<Control> Descendants(Control root){foreach(Control c in root.Controls){yield return c;foreach(Control child in Descendants(c))yield return child;}}
    static void Navigate(MainForm main,TreeView tree,string key){foreach(TreeNode group in tree.Nodes)foreach(TreeNode node in group.Nodes)if((string)node.Tag==key){group.Expand();tree.SelectedNode=node;return;}throw new Exception("Navigation entry missing: "+key);}
    static int Leaves(TreeView tree){return tree.Nodes.Cast<TreeNode>().Sum(g=>g.Nodes.Count);}
    static void Pump(Func<bool> finished,int timeout=5000){var watch=System.Diagnostics.Stopwatch.StartNew();while(!finished()){Application.DoEvents();if(watch.ElapsedMilliseconds>timeout)throw new Exception("UI operation did not finish");System.Threading.Thread.Sleep(10);}Application.DoEvents();}
    static void Click(TreeView tree,TreeNode node){Call(tree,"OnNodeMouseClick",new TreeNodeMouseClickEventArgs(node,MouseButtons.Left,1,node.Bounds.Left+5,node.Bounds.Top+5));Application.DoEvents();}
    static void Screenshot(Form form,string name){form.Refresh();Application.DoEvents();using(var image=new Bitmap(form.ClientSize.Width,form.ClientSize.Height)){try{using(var g=Graphics.FromImage(image))g.CopyFromScreen(form.PointToScreen(Point.Empty),Point.Empty,form.ClientSize);}catch(System.Runtime.InteropServices.ExternalException){form.DrawToBitmap(image,new Rectangle(Point.Empty,form.ClientSize));Console.WriteLine("Screenshot fallback: "+name);}image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,name));}}
    static void Modal(MainForm main,string method,params object[] args)
    {
        bool opened=false;
        using(var timer=new Timer{Interval=50})
        {
            timer.Tick+=delegate{foreach(Form form in Application.OpenForms.Cast<Form>().ToArray())if(form!=main){opened=true;Check(form.Width>=780&&Find<RichTextBox>(form).Length==1,"Dialog renders: "+form.Text);form.Close();return;}};
            timer.Start();try{Call(main,method,args);}finally{timer.Stop();}
        }
        Check(opened,"Modal closed cleanly: "+method);
    }
    static void SoftwareBaselineUi(MainForm main,TreeView tree)
    {
        var inventory=new SoftwareSnapshot();var normal=new SoftwareEntry{Name="标准编辑器",Publisher="标准厂商",Version="2.0",Key="normal"};var extraA=new SoftwareEntry{Name="额外程序 A",Publisher="其他厂商",Version="1.0",Key="extra-a",HasUninstaller=true};var extraB=new SoftwareEntry{Name="额外程序 B",Publisher="其他厂商",Version="3.0",Key="extra-b"};inventory.Entries.AddRange(new[]{normal,extraA,extraB});
        System.Collections.Generic.List<SoftwareBaselineEntry> saved=null;int writes=0,reads=0,plans=0,starts=0;bool approve=false,failSave=false;SoftwareEntry planned=null;string exported="";
        main.GetType().GetField("softwareBaselineReader",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(main,new Func<System.Collections.Generic.List<SoftwareBaselineEntry>>(delegate{reads++;return saved==null?null:SoftwareBaselineService.Checked(saved);}));
        main.GetType().GetField("softwareBaselineWriter",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(main,new Action<System.Collections.Generic.List<SoftwareBaselineEntry>>(delegate(System.Collections.Generic.List<SoftwareBaselineEntry> list){if(failSave)throw new IOException("模拟标准清单写入失败");saved=SoftwareBaselineService.Checked(list);writes++;}));
        main.GetType().GetField("softwareBaselineExporter",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(main,new Action<string>(delegate(string text){exported=text;}));
        main.GetType().GetField("softwareUninstallPlanner",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(main,new Func<SoftwareEntry,SoftwareUninstallPlan>(delegate(SoftwareEntry item){plans++;planned=item;return new SoftwareUninstallPlan{Name=item.Name,FileName=@"C:\Example\uninstall.exe",Arguments="--uninstall"};}));
        main.GetType().GetField("softwareUninstallConfirmation",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(main,new Func<SoftwareUninstallPlan,bool>(delegate{return approve;}));
        main.GetType().GetField("softwareUninstallLauncher",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(main,new Action<SoftwareUninstallPlan>(delegate{starts++;}));
        main.GetType().GetField("softwareCache",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(main,inventory);Navigate(main,tree,"系统概览");Navigate(main,tree,"软件环境检查");Application.DoEvents();var body=Field<Panel>(main,"content");var grid=Find<DataGridView>(body).Single(g=>g.Name=="softwareGrid");var view=Find<ComboBox>(body).Single(c=>c.Name=="softwareView");var standard=Find<Button>(body).Single(b=>b.Text=="标准清单 ▾").ContextMenuStrip;
        Check(grid.Rows.Count==3&&grid.Rows.Cast<DataGridViewRow>().All(r=>Convert.ToString(r.Cells[0].Value)=="未建立标准"),"No standard does not label installed software as extra");view.SelectedIndex=1;Check(grid.Rows.Count==0,"Extra view stays empty until a valid standard exists");
        using(var timer=new Timer{Interval=40}){bool handled=false;timer.Tick+=delegate{var f=Application.OpenForms.Cast<Form>().SingleOrDefault(w=>w.Text=="编辑软件标准清单");if(f==null||handled)return;handled=true;var draft=Find<DataGridView>(f).Single();int before=draft.Rows.Count;draft.CurrentCell=draft.Rows[1].Cells[1];Find<Button>(f).Single(b=>b.Text=="删除所选行").PerformClick();Check(draft.Rows.Count==before-1,"Explicit remove-row button removes only the selected draft entry");draft.Rows[0].Cells[1].Value="取消的修改";Find<Button>(f).Single(b=>b.Text=="取消").PerformClick();};timer.Start();standard.Items.Cast<ToolStripItem>().Single(i=>i.Name=="captureSoftwareBaseline").PerformClick();timer.Stop();}
        Check(writes==0&&saved==null&&normal.Name=="标准编辑器","Cancelling a captured standard draft preserves saved rules and installed records");
        using(var timer=new Timer{Interval=40}){bool handled=false;timer.Tick+=delegate{var f=Application.OpenForms.Cast<Form>().SingleOrDefault(w=>w.Text=="编辑软件标准清单");if(f==null||handled)return;handled=true;var draft=Find<DataGridView>(f).Single();foreach(DataGridViewRow row in draft.Rows)if(!row.IsNewRow)row.Cells[0].Value=Convert.ToString(row.Cells[1].Value)==normal.Name;Screenshot(f,"software-standard-editor-v0516.png");Find<Button>(f).Single(b=>b.Text=="保存标准清单").PerformClick();};timer.Start();standard.Items.Cast<ToolStripItem>().Single(i=>i.Name=="captureSoftwareBaseline").PerformClick();timer.Stop();}
        Check(writes==1&&saved.Count==1&&saved[0].Name==normal.Name&&view.SelectedIndex==1&&grid.Rows.Count==2,"Reviewing capture saves only checked software and immediately shows extras");
        Check(grid.Rows.Cast<DataGridViewRow>().All(r=>Object.ReferenceEquals(r.Tag,extraA)||Object.ReferenceEquals(r.Tag,extraB)),"Extra rows retain original installed objects for registered uninstall lookup");
        Screenshot(main,"software-extra-v0516.png");grid.Sort(grid.Columns[1],System.ComponentModel.ListSortDirection.Descending);Find<TextBox>(body).Single(t=>t.AccessibleName=="搜索名称 / 版本 / 发布者").Text=extraA.Name;
        Call(grid,"OnCellMouseDown",new DataGridViewCellMouseEventArgs(0,0,0,0,new MouseEventArgs(MouseButtons.Right,1,0,0,0)));var uninstall=grid.ContextMenuStrip.Items.Cast<ToolStripItem>().Single(i=>i.Name=="uninstallSoftware");Call(grid.ContextMenuStrip,"OnOpening",new System.ComponentModel.CancelEventArgs());uninstall.PerformClick();Pump(()=>Find<Label>(body).Any(l=>l.Text.Contains("已取消卸载")));
        Check(plans==1&&starts==0&&planned==extraA&&writes==1,"Sorted and filtered extra row opens the correct registered plan; cancellation makes no changes");
        approve=true;uninstall.PerformClick();Pump(()=>starts==1);Check(plans==2&&planned==extraA&&grid.Rows.Count==1&&writes==1,"Confirmed extra uninstall invokes only its registered launcher and keeps the row until refresh");
        Find<TextBox>(body).Single(t=>t.AccessibleName=="搜索名称 / 版本 / 发布者").Text="";int noRemove=grid.Rows.Cast<DataGridViewRow>().Single(r=>r.Tag==extraB).Index;Call(grid,"OnCellMouseDown",new DataGridViewCellMouseEventArgs(0,noRemove,0,0,new MouseEventArgs(MouseButtons.Right,1,0,0,0)));Call(grid.ContextMenuStrip,"OnOpening",new System.ComponentModel.CancelEventArgs());Check(!uninstall.Enabled,"An extra entry with no registered uninstaller does not offer forced removal");
        var imported=new[]{new SoftwareBaselineEntry{Name=normal.Name,Publisher=normal.Publisher},new SoftwareBaselineEntry{Name="缺少的标准程序",Publisher=""}};main.GetType().GetField("softwareBaselineImportReader",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(main,new Func<string>(delegate{return SoftwareBaselineService.Serialize(imported);}));standard.Items.Cast<ToolStripItem>().Single(i=>i.Name=="importSoftwareBaseline").PerformClick();
        Check(writes==2&&saved.Count==2&&grid.Rows.Count==2,"Import replaces and persists the standard, then recalculates extras without a registry read");view.SelectedIndex=2;Check(grid.Rows.Count==1&&grid.Rows[0].Tag==null&&Convert.ToString(grid.Rows[0].Cells[1].Value)=="缺少的标准程序","Missing view lists standard-only entries without invented installed identities");
        Call(grid,"OnCellMouseDown",new DataGridViewCellMouseEventArgs(0,0,0,0,new MouseEventArgs(MouseButtons.Right,1,0,0,0)));Call(grid.ContextMenuStrip,"OnOpening",new System.ComponentModel.CancelEventArgs());uninstall.PerformClick();Check(!uninstall.Enabled&&plans==2&&starts==1,"Missing software can never start an uninstall plan");
        standard.Items.Cast<ToolStripItem>().Single(i=>i.Name=="exportSoftwareBaseline").PerformClick();Check(SoftwareBaselineService.Parse(exported).Count==2&&writes==2,"Exported standard is reusable and exporting does not modify saved rules");
        main.GetType().GetField("softwareBaselineImportReader",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(main,new Func<string>(delegate{return null;}));standard.Items.Cast<ToolStripItem>().Single(i=>i.Name=="importSoftwareBaseline").PerformClick();Check(writes==2&&grid.Rows.Count==1,"Cancelling standard import preserves current rules and results");
        using(var timer=new Timer{Interval=40}){bool handled=false;timer.Tick+=delegate{var f=Application.OpenForms.Cast<Form>().SingleOrDefault(w=>w.Text=="编辑软件标准清单");if(f==null||handled)return;handled=true;var draft=Find<DataGridView>(f).Single();foreach(DataGridViewRow row in draft.Rows)if(!row.IsNewRow)row.Cells[0].Value=false;Find<Button>(f).Single(b=>b.Text=="保存标准清单").PerformClick();Check(f.Visible&&writes==2&&Find<Label>(f).Single(l=>l.Name=="softwareBaselineError").Text.Length>0,"Empty reviewed standard stays in editor and cannot overwrite saved rules");Find<Button>(f).Single(b=>b.Text=="取消").PerformClick();};timer.Start();standard.Items.Cast<ToolStripItem>().Single(i=>i.Name=="editSoftwareBaseline").PerformClick();timer.Stop();}
        failSave=true;using(var timer=new Timer{Interval=40}){bool handled=false;timer.Tick+=delegate{var f=Application.OpenForms.Cast<Form>().SingleOrDefault(w=>w.Text=="编辑软件标准清单");if(f==null||handled)return;handled=true;Find<Button>(f).Single(b=>b.Text=="保存标准清单").PerformClick();Check(f.Visible&&writes==2&&Find<Label>(f).Single(l=>l.Name=="softwareBaselineError").Text.Contains("模拟"),"Failed persistence keeps the old standard and leaves an editable error instead of reporting success");Find<Button>(f).Single(b=>b.Text=="取消").PerformClick();};timer.Start();standard.Items.Cast<ToolStripItem>().Single(i=>i.Name=="editSoftwareBaseline").PerformClick();timer.Stop();}failSave=false;
        var refreshed=new SoftwareSnapshot();refreshed.Entries.Add(normal);main.GetType().GetField("softwareCache",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(main,refreshed);Navigate(main,tree,"系统概览");Navigate(main,tree,"软件环境检查");Application.DoEvents();body=Field<Panel>(main,"content");grid=Find<DataGridView>(body).Single(g=>g.Name=="softwareGrid");view=Find<ComboBox>(body).Single(c=>c.Name=="softwareView");view.SelectedIndex=1;Check(reads==2&&writes==2&&grid.Rows.Count==0&&Find<Label>(body).Any(l=>l.Text.Contains("多出 0")),"Reopening restores persisted standard and recomputes against a refreshed installed snapshot");
        refreshed.Warnings.Add("模拟登记区读取不完整");Navigate(main,tree,"系统概览");Navigate(main,tree,"软件环境检查");Application.DoEvents();body=Field<Panel>(main,"content");grid=Find<DataGridView>(body).Single(g=>g.Name=="softwareGrid");view=Find<ComboBox>(body).Single(c=>c.Name=="softwareView");view.SelectedIndex=2;Check(grid.Rows.Count==1&&Convert.ToString(grid.Rows[0].Cells[0].Value).Contains("待核对")&&Find<Label>(body).Any(l=>l.Text.Contains("读取不完整")),"Partial registry reads mark absent standard entries for review instead of claiming definite missing software");
        main.GetType().GetField("softwareBaselineReader",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(main,new Func<System.Collections.Generic.List<SoftwareBaselineEntry>>(delegate{throw new IOException("模拟标准文件损坏");}));Navigate(main,tree,"系统概览");Navigate(main,tree,"软件环境检查");Application.DoEvents();body=Field<Panel>(main,"content");grid=Find<DataGridView>(body).Single();Check(Convert.ToString(grid.Rows[0].Cells[0].Value)=="未建立标准"&&Find<RichTextBox>(body).Any(t=>t.Text.Contains("读取失败")),"Unreadable standard reports an error and never misclassifies every installed program as extra");
    }
    [STAThread]static int Main()
    {
        Application.EnableVisualStyles();Application.ThreadException+=delegate{uiErrors++;};string path=Path.Combine(Paths.UserDir,"navigation-v050.txt");byte[] original=File.Exists(path)?File.ReadAllBytes(path):null;
        try
        {
            File.WriteAllLines(path,new[]{"安全与维护","U 盘检查与防护"},Encoding.UTF8);
            using(var main=new MainForm())
            {
                var originalLoad=Field<System.Threading.Tasks.Task<DatabaseService>>(main,"databaseLoad");var gate=new System.Threading.Tasks.TaskCompletionSource<DatabaseService>();main.GetType().GetField("databaseLoad",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(main,gate.Task);
                main.Show();Application.DoEvents();Check(main.Visible&&!gate.Task.IsCompleted&&Field<string>(main,"currentPage")=="系统概览","Overview renders while database task is still pending");Pump(()=>originalLoad.IsCompleted,30000);gate.SetResult(originalLoad.GetAwaiter().GetResult());Application.DoEvents();var tree=Field<TreeView>(main,"navigationTree");var search=Field<TextBox>(main,"navigationSearch");
                Check(tree.Nodes.Count==7&&Leaves(tree)==30,"Seven categories / 30 features (including trial), no duplicate system category");
                Check(Field<string>(main,"currentPage")=="系统概览"&&tree.Nodes[0].IsExpanded,"Every launch opens system overview despite saved prior page");
                Click(tree,tree.Nodes[0]);Check(!tree.Nodes[0].IsExpanded,"Category text click collapses");Click(tree,tree.Nodes[1]);Check(tree.Nodes[1].IsExpanded&&tree.Nodes.Cast<TreeNode>().Count(n=>n.IsExpanded)==1,"Accordion expands only one group");
                var clearSearch=Find<Button>(main).Single(b=>b.Name=="navigationClear");Check(!clearSearch.Visible,"Search clear icon is hidden for empty input");
                search.Text="DNS";Application.DoEvents();Check(clearSearch.Visible&&clearSearch.Parent.Name=="navigationSearchFrame"&&clearSearch.FlatAppearance.BorderSize==0,"Clear icon stays inside the search field without a separate border");Check(Leaves(tree)==4,"Search DNS aliases including trial");search.Text="不存在的功能XYZ";Application.DoEvents();Check(Leaves(tree)==0,"Empty search result has no fake feature");clearSearch.PerformClick();Application.DoEvents();Check(search.Text==""&&!clearSearch.Visible&&Leaves(tree)==30,"Inline clear resets search and restores all navigation entries");
                var software=new SoftwareSnapshot();for(int i=0;i<500;i++)software.Entries.Add(new SoftwareEntry{Name="样例软件 "+i,Version="1.0",Publisher="测试发布者",Scope="当前用户",Architecture="64 位登记区",Location="C:\\Example",Key="sample"+i});
                main.GetType().GetField("softwareBaselineReader",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(main,new Func<System.Collections.Generic.List<SoftwareBaselineEntry>>(delegate{return null;}));
                main.GetType().GetField("softwareCache",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(main,software);
                Navigate(main,tree,"软件环境检查");Application.DoEvents();var softwareBody=Field<Panel>(main,"content");var softwareGrid=Find<DataGridView>(softwareBody).Single();
                Check(softwareGrid.Rows.Count==500&&softwareGrid.Columns.Count==7&&Application.OpenForms.Count==1,"Software inventory opens directly with a comparison column from cache");
                Find<TextBox>(softwareBody).Single(t=>t.AccessibleName=="搜索名称 / 版本 / 发布者").Text="样例软件 123";Application.DoEvents();Check(softwareGrid.Rows.Count==1,"Software search filters cached list without a new registry read");
                int uninstallPlans=0,uninstallStarts=0;string uninstallName="";bool approveUninstall=false;
                var selectedSoftware=(SoftwareEntry)softwareGrid.Rows[0].Tag;selectedSoftware.HasUninstaller=true;
                main.GetType().GetField("softwareUninstallPlanner",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(main,new Func<SoftwareEntry,SoftwareUninstallPlan>(delegate(SoftwareEntry item){uninstallPlans++;uninstallName=item.Name;return new SoftwareUninstallPlan{Name=item.Name,FileName=@"C:\Example\uninstall.exe",Arguments="--uninstall"};}));
                main.GetType().GetField("softwareUninstallConfirmation",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(main,new Func<SoftwareUninstallPlan,bool>(delegate{return approveUninstall;}));
                main.GetType().GetField("softwareUninstallLauncher",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(main,new Action<SoftwareUninstallPlan>(delegate{uninstallStarts++;}));
                Call(softwareGrid,"OnCellMouseDown",new DataGridViewCellMouseEventArgs(0,0,0,0,new MouseEventArgs(MouseButtons.Right,1,0,0,0)));var uninstallItem=softwareGrid.ContextMenuStrip.Items.Cast<ToolStripItem>().Single(i=>i.Name=="uninstallSoftware");uninstallItem.PerformClick();Pump(()=>Find<Label>(softwareBody).Any(l=>l.Text.Contains("已取消卸载")),10000);
                Check(uninstallPlans==1&&uninstallStarts==0&&uninstallName==selectedSoftware.Name&&softwareGrid.Rows.Count==1,"Right-click targets the filtered row by its object and cancellation launches no uninstaller");
                approveUninstall=true;uninstallItem.PerformClick();Pump(()=>uninstallStarts==1,10000);Check(softwareGrid.Rows.Count==1&&Find<Label>(softwareBody).Any(l=>l.Text.Contains("已启动")),"Confirmed uninstall starts the registered workflow without hiding or deleting the listed record");Screenshot(main,"software-uninstall-v057.png");
                Screenshot(main,"software-v053.png");search.Text="软件";Application.DoEvents();Screenshot(main,"search-v053.png");clearSearch.PerformClick();Application.DoEvents();
                SoftwareBaselineUi(main,tree);
                Navigate(main,tree,"启动项管理");Application.DoEvents();var startupBody=Field<Panel>(main,"content");Pump(()=>Find<Button>(startupBody).Single(b=>b.Text=="刷新启动项").Enabled,30000);var startup=Find<DataGridView>(startupBody).Single();
                Check(startup.Columns["icon"].Width>=64&&startup.ColumnHeadersDefaultCellStyle.WrapMode==DataGridViewTriState.False&&startup.Columns["command"].MinimumWidth>=280,"Startup icon header remains horizontal and command column cannot collapse");
                var actions=Field<System.Collections.Generic.List<AutoStep>>(main,"autoSteps");actions.Add(new AutoStep{Type="运行程序并等待",A="notepad.exe",B="example.txt",C="30"});Navigate(main,tree,"BAT 自动化");Application.DoEvents();var autoBody=Field<Panel>(main,"content");var actionGrid=Find<DataGridView>(autoBody).Single();
                Check(Find<CheckBox>(autoBody).Single(c=>c.Text.Contains("自动弹出 UAC")).Checked&&Find<CheckBox>(autoBody).Single(c=>c.Text.Contains("执行到末尾后删除")).Checked&&Find<CheckBox>(autoBody).Single(c=>c.Text.Contains("启用跨机房环境诊断")).Checked&&Find<CheckBox>(autoBody).Any(c=>c.Text=="隐藏运行窗口"),"All existing automation options and their explicit default selections remain available");
                Check(!Find<Button>(autoBody).Any(b=>b.Text=="上移"||b.Text=="下移"),"BAT editor removes old move buttons while retaining drag handle");
                Find<Button>(autoBody).Single(b=>b.Text=="复制动作").PerformClick();Check(actions.Count==2&&!Object.ReferenceEquals(actions[0],actions[1])&&actions[1].A==actions[0].A&&actions[1].B==actions[0].B&&actions[1].C==actions[0].C&&actionGrid.CurrentRow.Index==1,"Copy duplicates all step parameters independently immediately after selection");
                actions[1].A="other.exe";Check(actions[0].A=="notepad.exe","Editing a copy leaves the original step unchanged");
                Call(actionGrid,"OnKeyDown",new KeyEventArgs(Keys.Control|Keys.D));Check(actions.Count==3&&actionGrid.CurrentRow.Index==2,"Ctrl+D creates another editable step copy");Screenshot(main,"automation-v053.png");
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
                        else if(branchStage==4){choice.SelectedItem="如果系统版本";Check(Find<TextBox>(f).Single(t=>t.AccessibleName=="Build 上限（可空）").Visible&&Find<ListBox>(f).All(l=>l.Items.Count==1),"System condition labels its Build bounds while retaining both branch drafts");choice.SelectedItem="如果文件存在";Check(Find<TextBox>(f).Single(t=>t.AccessibleName=="文件完整路径").Visible&&Find<Button>(f).Any(b=>b.Visible&&b.Text=="浏览选择要判断的文件"),"File condition has a concrete file-path field and matching file-picker caption");choice.SelectedItem="如果网络可达";var target=Find<TextBox>(f).Single(t=>t.AccessibleName=="目标 IP / 主机名");target.Clear();Find<Button>(f).Single(b=>b.Text=="确定添加 / 保存").PerformClick();Check(f.Visible&&actions.Count==branchStart&&Find<Label>(f).Single(l=>l.Name=="conditionError").Text.Length>0,"Invalid condition stays in editor and leaves main workflow unchanged");target.Text="192.0.2.1";Screenshot(f,"automation-branches-v0510.png");branchStage=5;Find<Button>(f).Single(b=>b.Text=="确定添加 / 保存").PerformClick();}
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
                        else if(mouseStage==2&&test.Enabled){Check(physicalMoves==1&&movedTo==new Point(1560,-240),"Coordinate preview moves to the exact captured physical point without scaling");Screenshot(f,"mouse-v059.png");mouseStage=3;capture.PerformClick();}
                        else if(mouseStage==3&&capture.Enabled){Check(x.Text=="1560"&&y.Text=="-240"&&Find<Label>(f).Any(l=>l.Text.Contains("模拟物理坐标读取失败")),"Failed physical capture keeps existing coordinates and reports the failure");mouseStage=4;capture.PerformClick();Find<ComboBox>(f).Single(c=>c.Items.Contains("移动鼠标")).SelectedItem="等待毫秒";Check(capture.Enabled&&!capture.Visible,"Changing action cancels pending mouse capture before it can overwrite another action");f.Close();}
                    };
                    timer.Start();Call(main,"EditStep",(object)null);timer.Stop();
                }
                main.GetType().GetField("usbDriveReader",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(main,new Func<System.Collections.Generic.List<UsbDriveAccess>>(delegate{return new System.Collections.Generic.List<UsbDriveAccess>{new UsbDriveAccess{Root=@"G:\",FileSystem="exFAT"},new UsbDriveAccess{Root=@"H:\",FileSystem="NTFS"}};}));
                using(var timer=new Timer{Interval=40})
                {
                    timer.Tick+=delegate{var f=Application.OpenForms.Cast<Form>().FirstOrDefault(w=>w.Text=="U 盘检查与本机防护");if(f==null)return;
                        var drive=Find<ComboBox>(f).Single(c=>c.AccessibleName=="U 盘与格式");var protect=Find<Button>(f).Single(b=>b.Text=="NTFS 普通用户只读");var restore=Find<Button>(f).Single(b=>b.Text=="恢复所选 U 盘原权限");var hint=Find<Label>(f).Single(l=>l.Name=="usbPermissionHint");
                        Check(drive.Text.Contains("exFAT")&&!protect.Enabled&&!restore.Enabled&&hint.Text.Contains("不支持 NTFS")&&Find<Button>(f).Single(b=>b.Text=="开启本机 USB 写保护").Enabled,"exFAT selection disables ACL actions and explains the available local USB policy");Screenshot(f,"usb-exfat-v059.png");
                        drive.SelectedIndex=1;Check(protect.Enabled&&restore.Enabled&&hint.Text.StartsWith("NTFS"),"Selecting NTFS enables permission actions without applying them");
                        drive.SelectedIndex=0;Find<Button>(f).Single(b=>b.Text=="刷新 U 盘").PerformClick();Check(drive.Text.Contains("exFAT")&&!protect.Enabled&&!restore.Enabled,"Refreshing USB choices preserves the selected disk and unsupported permission state");f.Close();
                    };
                    timer.Start();Call(main,"UsbProtectionDialog");timer.Stop();
                }
                int prompts=0;string fake=InspectionService.Serialize(new System.Collections.Generic.Dictionary<string,string>{{"系统/名称","测试系统"},{"CPU/1/型号","测试 CPU"},{"内存/1/厂商","KINGBANK"},{"内存/1/容量","16 GiB"},{"内存/1/配置频率 MHz","3200"},{"主板/1/型号","测试主板"},{"显卡/1/型号","测试显卡"},{"网卡/样例/型号","测试网卡"}});
                main.GetType().GetField("configurationReader",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(main,new Func<System.Threading.CancellationToken,string>(delegate{return fake;}));
                main.GetType().GetField("configurationExportPrompt",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(main,new Func<Form,bool>(delegate{prompts++;return false;}));
                using(var timer=new Timer{Interval=40}){
                    bool clicked=false;timer.Tick+=delegate{foreach(Form f in Application.OpenForms.Cast<Form>().ToArray())if(f.Text=="电脑配置对比"){if(!clicked){clicked=true;Find<Button>(f).Single(b=>b.Text=="采集本机报告").PerformClick();}else if(prompts>0){Check(Find<RichTextBox>(f).Single().Text.Contains("测试 CPU")&&prompts==1,"Collect shows hardware configuration and asks once to export; No preserves the report");Screenshot(f,"configuration-v054.png");f.Close();}break;}};
                    timer.Start();Call(main,"ConfigurationCompareDialog");timer.Stop();
                }
                var sensitiveRows=new System.Collections.Generic.List<SavedWifiProfile>();int savedReads=0;
                Func<System.Threading.CancellationToken,SavedWifiInventory> fakeSaved=delegate{
                    savedReads++;var data=new SavedWifiInventory();var saved=new SavedWifiProfile{Name="样例配置（测试数据）",Ssid="样例网络",Authentication="WPA2PSK",Encryption="AES",Password="Demo-only-56!",HasPassword=true,Status="已读取",Adapter="样例无线网卡"};data.Profiles.Add(saved);sensitiveRows.Add(saved);
                    data.Profiles.Add(new SavedWifiProfile{Name="受限样例（测试数据）",Ssid="受限网络",Authentication="WPA3SAE",Encryption="AES",Password="无法读取",Status="系统未返回明文密码，可能受权限或策略限制",Adapter="样例无线网卡"});return data;
                };
                main.GetType().GetField("savedWifiReader",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(main,fakeSaved);
                Navigate(main,tree,"已保存的Wi-Fi");Application.DoEvents();var savedBody=Field<Panel>(main,"content");Pump(()=>Find<Button>(savedBody).Single(b=>b.Text=="刷新配置").Enabled,30000);var savedGrid=Find<DataGridView>(savedBody).Single();
                Check(savedReads==1&&savedGrid.Rows.Count==2&&(string)savedGrid.Rows[0].Cells[4].Value=="Demo-only-56!"&&Application.OpenForms.Count==1,"Saved Wi-Fi opens directly and automatically shows allowed plaintext without confirmation or another dialog");
                Check(savedGrid.AutoSizeColumnsMode==DataGridViewAutoSizeColumnsMode.Fill&&savedGrid.Columns[4].MinimumWidth>=180&&savedGrid.Columns[4].DefaultCellStyle.WrapMode==DataGridViewTriState.True&&savedGrid.Columns.Count==7,"Saved Wi-Fi uses a full-width seven-column grid with room for passwords");
                Check((string)savedGrid.Rows[1].Cells[4].Value=="无法读取"&&Convert.ToString(savedGrid.Rows[1].Cells[5].Value).Contains("权限"),"Protected profile keeps an explicit permission state instead of fake plaintext");Screenshot(main,"saved-wifi-v056.png");
                Find<TextBox>(savedBody).Single(t=>t.AccessibleName=="搜索配置 / SSID / 网卡").Text="受限";Application.DoEvents();Check(savedGrid.Rows.Count==1&&savedReads==1,"Saved profile search filters current rows without another password read");
                Find<Button>(savedBody).Single(b=>b.Text=="清空显示").PerformClick();Check(savedGrid.Rows.Count==0&&sensitiveRows[0].Password==""&&!sensitiveRows[0].HasPassword,"Clear removes visible passwords and releases the current inventory");
                Find<TextBox>(savedBody).Single(t=>t.AccessibleName=="搜索配置 / SSID / 网卡").Clear();var reloadSaved=Find<Button>(savedBody).Single(b=>b.Text=="刷新配置");reloadSaved.PerformClick();Pump(()=>reloadSaved.Enabled,30000);
                using(var started=new System.Threading.ManualResetEventSlim()){
                    var lateProfile=new SavedWifiProfile{Name="取消测试",Password="Demo-only-cancel",HasPassword=true};
                    main.GetType().GetField("savedWifiReader",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(main,new Func<System.Threading.CancellationToken,SavedWifiInventory>(delegate(System.Threading.CancellationToken token){started.Set();token.WaitHandle.WaitOne(5000);var result=new SavedWifiInventory();result.Profiles.Add(lateProfile);return result;}));
                    reloadSaved.PerformClick();Pump(()=>started.IsSet,10000);Check(sensitiveRows[1].Password==""&&savedGrid.Rows.Count==0,"Refresh clears previous passwords before the next read finishes");Find<Button>(savedBody).Single(b=>b.Text=="停止").PerformClick();Pump(()=>reloadSaved.Enabled,10000);Check(savedGrid.Rows.Count==0&&lateProfile.Password==""&&!lateProfile.HasPassword,"Stopped password read discards a late result instead of displaying it");
                }
                main.GetType().GetField("savedWifiReader",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(main,fakeSaved);reloadSaved.PerformClick();Pump(()=>reloadSaved.Enabled,30000);Navigate(main,tree,"端口扫描");Application.DoEvents();Check(sensitiveRows.All(x=>x.Password==""&&!x.HasPassword),"Leaving saved Wi-Fi clears password references from that page");
                foreach(string key in new[]{"端口扫描","主机发现","路由与MAC","Wi-Fi分析"})
                {
                    Navigate(main,tree,key);Application.DoEvents();var body=Field<Panel>(main,"content");
                    Check(Find<DataGridView>(body).Length>0&&!Find<Button>(body).Any(b=>b.Text.StartsWith("打开")),"Direct inline page without extra open button: "+key);
                    Check(Application.OpenForms.Count==1,"Inline navigation never launches modal: "+key);
                    if(key=="路由与MAC"||key=="Wi-Fi分析"){var button=Find<Button>(body).Single(b=>b.Text=="刷新信息");Pump(()=>button.Enabled,30000);Check(Find<Panel>(body).Count(p=>p.Name=="resultTabs")==1,"Auto-read completes with structured tables: "+key);}
                }
                var routeFixture=new RouteInventory();routeFixture.Adapters.Add(new RouteAdapterRow{Name="样例网卡",State="已连接",Mac="02:00:00:00:00:01",IPv4="192.0.2.1",Prefix="24",IPv6="2001:db8::1",Gateway4="192.0.2.254",Gateway6="fe80::1",Speed="1000"});
                routeFixture.Routes.Add(new object[]{"样例网卡","IPv4","0.0.0.0/0","192.0.2.254","10","NetMgmt"});routeFixture.Routes.Add(new object[]{"样例网卡","IPv6","::/0","fe80::1","10","NetMgmt"});
                main.GetType().GetField("routeReader",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(main,new Func<System.Threading.CancellationToken,RouteInventory>(delegate{return routeFixture;}));
                Navigate(main,tree,"路由与MAC");Application.DoEvents();var routeBody=Field<Panel>(main,"content");Pump(()=>Find<Button>(routeBody).Single(b=>b.Text=="刷新信息").Enabled,30000);var af=Find<ComboBox>(routeBody).Single(c=>c.AccessibleName=="地址族");var adapter=Find<DataGridView>(routeBody).Single(g=>g.Columns.Count==9);
                Check(adapter.Columns[3].HeaderText=="IPv4"&&adapter.Columns[4].HeaderText=="掩码"&&adapter.Columns[4].Width<=60&&(string)adapter.Rows[0].Cells[3].Value=="192.0.2.1"&&(string)adapter.Rows[0].Cells[4].Value=="24","IPv4 address and numeric mask occupy adjacent compact columns without CIDR or dotted mask");
                Check(adapter.Columns.Cast<DataGridViewColumn>().Take(7).Sum(c=>c.Width)<=870&&adapter.Columns[2].Width>=140&&adapter.Columns[3].Width>=125&&adapter.Columns[1].MinimumWidth>=65,"Compact adapter columns bring gateway closer while preserving MAC/IP and horizontal status headers");Screenshot(main,"route-v059.png");
                Check(adapter.Columns[5].HeaderText=="IPv6"&&(string)adapter.Rows[0].Cells[5].Value=="2001:db8::1","IPv6 appears beside IPv4 and its prefix on the same adapter row");
                af.SelectedIndex=1;Application.DoEvents();Check((string)adapter.Rows[0].Cells[3].Value=="192.0.2.1"&&(string)adapter.Rows[0].Cells[6].Value=="fe80::1","Family switch updates gateway without hiding either local address family");af.SelectedIndex=0;Screenshot(main,"route-v054.png");
                Find<Button>(routeBody).Single(b=>b.Text=="路由表").PerformClick();Application.DoEvents();var routeGrid=Find<DataGridView>(routeBody).Single(g=>g.Name=="routeTable");
                Check(routeGrid.Visible&&routeGrid.Columns[0].HeaderText=="目标"&&routeGrid.Columns[1].HeaderText=="掩码"&&routeGrid.Columns[2].HeaderText=="下一跳"&&(string)routeGrid.Rows[0].Cells[0].Value=="0.0.0.0"&&(int)routeGrid.Rows[0].Cells[1].Value==0,"Route/MAC table splits destination and numeric mask before next hop");Screenshot(main,"route-v055.png");var routeHeaders=routeGrid.Columns.Cast<DataGridViewColumn>().Select(c=>c.HeaderText).ToArray();
                Navigate(main,tree,"网络诊断");Application.DoEvents();var diagnosticBody=Field<Panel>(main,"content");Pump(()=>Find<Button>(diagnosticBody).Single(b=>b.Text=="刷新网卡").Enabled,30000);var routeButton=Find<Button>(diagnosticBody).Single(b=>b.Text=="查看路由表");routeButton.PerformClick();Pump(()=>routeButton.Enabled,30000);var directRoute=Find<DataGridView>(diagnosticBody).Single(g=>g.Name=="routeTable");
                Check(directRoute.Visible&&directRoute.Columns.Cast<DataGridViewColumn>().Select(c=>c.HeaderText).SequenceEqual(routeHeaders)&&(string)directRoute.Rows[0].Cells[2].Value=="192.0.2.254"&&Application.OpenForms.Count==1,"Network diagnostic route action opens the same structured inline schema without command text or extra modal");Screenshot(main,"network-route-v055.png");
                Navigate(main,tree,"系统代理诊断");Application.DoEvents();var proxyBody=Field<Panel>(main,"content");Check(tree.SelectedNode.Parent.Text=="网络诊断"&&Find<DataGridView>(proxyBody).Single().Rows.Count==0&&Find<Label>(proxyBody).Any(l=>l.Text.StartsWith("就绪"))&&!Find<Label>(proxyBody).Any(l=>l.Text.Contains("新增待试用")),"Proxy diagnostic moves into network category and remains idle on entry");
                string studentDir=Path.Combine(Path.GetTempPath(),"students-ui-"+Guid.NewGuid());Directory.CreateDirectory(studentDir);
                try {
                    var students=new System.Collections.Generic.List<StudentMachine>{new StudentMachine{Room="测试机房A",Name="学生机01",IPv4="192.0.2.11",Mac="02:00:00:00:00:11",Note="排版测试"},new StudentMachine{Room="测试机房A",Name="学生机02",IPv4="192.0.2.12",Mac="02:00:00:00:00:12"},new StudentMachine{Room="测试机房B",Name="学生机03",IPv4="192.0.2.13",Mac="02:00:00:00:00:13"}};
                    string rosterPath=Path.Combine(studentDir,"students.tsv");var network=WakeNetwork.Create("测试网卡","192.0.2.1","255.255.255.0");var sends=new System.Collections.Generic.List<string>();
                    main.GetType().GetField("studentMachines",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(main,students);main.GetType().GetField("studentStorePath",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(main,rosterPath);
                    main.GetType().GetField("wakeNetworkReader",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(main,new Func<System.Threading.CancellationToken,System.Collections.Generic.List<WakeNetwork>>(delegate{return new System.Collections.Generic.List<WakeNetwork>{network};}));
                    Func<System.Collections.Generic.IEnumerable<StudentMachine>,WakeNetwork,int,System.Threading.CancellationToken,Action<WakeResult>,System.Threading.Tasks.Task> fakeWake=async delegate(System.Collections.Generic.IEnumerable<StudentMachine> machines,WakeNetwork n,int port,System.Threading.CancellationToken token,Action<WakeResult> receive){foreach(var item in machines){await System.Threading.Tasks.Task.Delay(5,token);sends.Add(item.Mac);receive(new WakeResult{Machine=item,Packets=3});}};
                    main.GetType().GetField("wakeSender",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(main,fakeWake);
                    Navigate(main,tree,"学生机与网络唤醒");Application.DoEvents();var studentBody=Field<Panel>(main,"content");Pump(()=>Find<Button>(studentBody).Single(b=>b.Text=="刷新本机网卡").Enabled,30000);var studentGrid=Find<DataGridView>(studentBody).Single();var room=Find<ComboBox>(studentBody).Single(c=>c.AccessibleName=="机房");
                    Check(tree.SelectedNode.Parent.Text=="学生机管理"&&studentGrid.Rows.Count==3&&sends.Count==0,"Student management has an independent category and entry never sends wake packets");
                    room.SelectedIndex=1;Application.DoEvents();studentGrid.ClearSelection();studentGrid.CurrentCell=studentGrid.Rows[0].Cells[1];studentGrid.Rows[0].Selected=true;var wake=Find<Button>(studentBody).Single(b=>b.Text=="唤醒选中");wake.PerformClick();Pump(()=>wake.Enabled,30000);
                    Check(sends.Count==1&&sends[0]=="02:00:00:00:00:11"&&studentGrid.Rows.Cast<DataGridViewRow>().Any(r=>Convert.ToString(r.Cells[4].Value).Contains("未确认开机")),"Explicit wake acts on selected student and displays unconfirmed delivery status");
                    Find<TextBox>(studentBody).Single(t=>t.AccessibleName=="搜索名称 / IP / MAC").Text="01";var wakeRoom=Find<Button>(studentBody).Single(b=>b.Text=="唤醒该机房");wakeRoom.PerformClick();Pump(()=>wakeRoom.Enabled,30000);
                    Check(sends.Count==3&&sends.Skip(1).OrderBy(x=>x).SequenceEqual(students.Take(2).Select(x=>x.Mac).OrderBy(x=>x)),"Room wake includes that whole room and excludes another room despite search filtering");
                    Find<TextBox>(studentBody).Single(t=>t.AccessibleName=="搜索名称 / IP / MAC").Clear();Application.DoEvents();Screenshot(main,"students-v055.png");
                    bool saved=false;using(var timer=new Timer{Interval=40}){timer.Tick+=delegate{foreach(Form f in Application.OpenForms.Cast<Form>().ToArray())if(f.Text=="添加学生机"){Find<TextBox>(f).Single(t=>t.AccessibleName=="机房").Text="测试机房C";Find<TextBox>(f).Single(t=>t.AccessibleName=="名称").Text="学生机04";Find<TextBox>(f).Single(t=>t.AccessibleName=="IPv4（可空）").Text="192.0.2.14";Find<TextBox>(f).Single(t=>t.AccessibleName=="有线 MAC").Text="02:00:00:00:00:14";saved=true;Find<Button>(f).Single(b=>b.Text=="保存记录").PerformClick();break;}};timer.Start();Find<Button>(studentBody).Single(b=>b.Text=="添加学生机").PerformClick();timer.Stop();}
                    Check(saved&&StudentRoster.Load(rosterPath).Count==4,"Adding a student validates and persists the roster to its configured local file");
                    room.SelectedIndex=room.Items.Count-1;Application.DoEvents();studentGrid.CurrentCell=studentGrid.Rows[0].Cells[1];
                    using(var timer=new Timer{Interval=40}){timer.Tick+=delegate{foreach(Form f in Application.OpenForms.Cast<Form>().ToArray())if(f.Text=="编辑学生机"){Find<TextBox>(f).Single(t=>t.AccessibleName=="名称").Text="学生机04已编辑";Find<Button>(f).Single(b=>b.Text=="保存记录").PerformClick();break;}};timer.Start();Find<Button>(studentBody).Single(b=>b.Text=="编辑学生机").PerformClick();timer.Stop();}
                    Check(StudentRoster.Load(rosterPath).Single(x=>x.Mac=="02:00:00:00:00:14").Name=="学生机04已编辑","Editing a student updates the intended persisted MAC record without dropping other rooms");
                    int scannerCalls=0,wakeBeforeScan=sends.Count;
                    Func<string,WakeNetwork,System.Threading.CancellationToken,Action<StudentScanRow>,System.Threading.Tasks.Task<StudentScanResult>> fakeScanner=async delegate(string range,WakeNetwork source,System.Threading.CancellationToken token,Action<StudentScanRow> receive){scannerCalls++;var result=new StudentScanResult();foreach(var item in new[]{new StudentScanRow{IPv4="192.0.2.21",Mac="02:00:00:00:00:11",Name="旧设备新IP",Status="ARP 模拟结果"},new StudentScanRow{IPv4="192.0.2.15",Mac="02:00:00:00:00:15",Name="学生机05",Status="ARP 模拟结果"}}){await System.Threading.Tasks.Task.Delay(5,token);result.Rows.Add(item);receive(item);}return result;};
                    main.GetType().GetField("studentScanner",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(main,fakeScanner);
                    bool scanStarted=false,scanSaved=false;int saveStage=0;string beforeSave=File.ReadAllText(rosterPath);
                    using(var timer=new Timer{Interval=40})
                    {
                        timer.Tick+=delegate{
                            var popup=Application.OpenForms.Cast<Form>().FirstOrDefault(f=>f.Text=="保存到教室");
                            if(popup!=null)
                            {
                                var name=Find<TextBox>(popup).Single(t=>t.AccessibleName=="教室名称");
                                if(saveStage==1)
                                {
                                    Check(name.Text=="测试机房C"&&popup.AcceptButton!=null&&popup.CancelButton!=null,"Save popup defaults to selected classroom and supports confirm/cancel keys");
                                    Check(File.ReadAllText(rosterPath)==beforeSave,"Opening classroom save popup does not persist unconfirmed results");
                                    saveStage=2;Find<Button>(popup).Single(b=>b.Text=="取消").PerformClick();
                                }
                                else if(saveStage==3)
                                {
                                    name.Text="   ";Find<Button>(popup).Single(b=>b.Text=="确认保存").PerformClick();
                                    Check(popup.Visible&&File.ReadAllText(rosterPath)==beforeSave&&Find<Label>(popup).Any(l=>l.Text.Contains("保存未完成")),"Blank classroom stays in save popup without changing stored roster");
                                    name.Text=" 测试机房D ";Screenshot(popup,"student-save-v058.png");saveStage=4;Find<Button>(popup).Single(b=>b.Text=="确认保存").PerformClick();
                                }
                                return;
                            }
                            foreach(Form f in Application.OpenForms.Cast<Form>().ToArray())if(f.Text=="扫描网段并保存学生机")
                            {
                                var begin=Find<Button>(f).Single(b=>b.Text=="扫描网段");var results=Find<DataGridView>(f).Single();
                                if(!scanStarted)
                                {
                                    scanStarted=true;Check(scannerCalls==0&&!Find<TextBox>(f).Any(t=>t.AccessibleName=="保存到机房"),"Opening student scan needs no classroom name and makes no ARP query before Start");begin.PerformClick();
                                }
                                else if(begin.Enabled&&!scanSaved&&results.Rows.Count==2)
                                {
                                    scanSaved=true;Find<Button>(f).Single(b=>b.Text=="保存勾选").PerformClick();
                                    Check(!Application.OpenForms.Cast<Form>().Any(d=>d.Text=="保存到教室")&&File.ReadAllText(rosterPath)==beforeSave,"Save with no selected devices asks for selection before opening classroom popup");
                                    Find<Button>(f).Single(b=>b.Text=="勾选可用设备").PerformClick();Screenshot(f,"student-scan-v058.png");saveStage=1;f.BeginInvoke(new Action(delegate{Find<Button>(f).Single(b=>b.Text=="保存勾选").PerformClick();}));
                                }
                                else if(saveStage==2)
                                {
                                    Check(results.Rows.Count==2&&results.Rows.Cast<DataGridViewRow>().All(r=>Convert.ToBoolean(r.Cells[0].Value))&&File.ReadAllText(rosterPath)==beforeSave,"Cancel keeps scan results and checked devices without changing the roster");
                                    saveStage=3;f.BeginInvoke(new Action(delegate{Find<Button>(f).Single(b=>b.Text=="保存勾选").PerformClick();}));
                                }
                                break;
                            }
                        };
                        timer.Start();Find<Button>(studentBody).Single(b=>b.Text=="扫描网段并保存").PerformClick();timer.Stop();
                    }
                    var learned=StudentRoster.Load(rosterPath);Check(scanSaved&&scannerCalls==1&&learned.Count==5&&learned.Single(x=>x.Mac=="02:00:00:00:00:11").Name=="学生机01"&&learned.Single(x=>x.Mac=="02:00:00:00:00:11").IPv4=="192.0.2.21","Reviewed scan results persist new students and update known IPs without replacing names");
                    Check(room.SelectedItem.ToString()=="测试机房D"&&studentGrid.Rows.Count==1&&sends.Count==wakeBeforeScan,"Saving selects the chosen room and never automatically wakes discovered devices");Screenshot(main,"students-v057.png");
                    Navigate(main,tree,"端口扫描");main.GetType().GetField("studentMachines",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(main,null);Navigate(main,tree,"学生机与网络唤醒");Application.DoEvents();var persistedBody=Field<Panel>(main,"content");Pump(()=>Find<Button>(persistedBody).Single(b=>b.Text=="刷新本机网卡").Enabled,30000);var persistedRoom=Find<ComboBox>(persistedBody).Single(c=>c.AccessibleName=="机房");
                    Check(Find<DataGridView>(persistedBody).Single().Rows.Count==5&&scannerCalls==1,"Fresh student page reloads all saved rooms from disk without rescanning");
                    persistedRoom.SelectedIndex=persistedRoom.Items.Count-1;var storedWake=Find<Button>(persistedBody).Single(b=>b.Text=="唤醒该机房");storedWake.PerformClick();Pump(()=>storedWake.Enabled,30000);Check(sends.Last()=="02:00:00:00:00:15"&&scannerCalls==1,"Stored classroom wakes with its learned MAC without scanning again");
                    Navigate(main,tree,"端口扫描");
                }finally{Directory.Delete(studentDir,true);}
                foreach(string key in new[]{"DNS记录查询","ICMP路由跟踪","本机连接与监听","网络配置核对"})
                {
                    Navigate(main,tree,key);Application.DoEvents();var trial=Field<Panel>(main,"content");
                    Check(tree.SelectedNode.Parent.Text.Contains("新增")&&Find<DataGridView>(trial).Single().Rows.Count==0&&Find<Button>(trial).Single(b=>b.Text=="停止").Enabled==false&&Find<Label>(trial).Any(l=>l.Text.StartsWith("待试用")),"Trial remains separate and idle until explicit action: "+key);
                }
                var audit=Field<Panel>(main,"content");var auditGrid=Find<DataGridView>(audit).Single();Check(auditGrid.AutoSizeColumnsMode==DataGridViewAutoSizeColumnsMode.Fill&&auditGrid.Columns[3].MinimumWidth>=260,"Trial IPv4 audit table fills available width with room for explanations");var read=Find<Button>(audit).Single(b=>b.Text=="读取本机信息");read.PerformClick();Pump(()=>read.Enabled,30000);
                Check(Find<DataGridView>(audit).Single().Rows.Count>0,"Trial local IPv4 audit executes and fills table without probing network");
                Navigate(main,tree,"DNS记录查询");Application.DoEvents();var dnsPage=Field<Panel>(main,"content");Find<TextBox>(dnsPage).Single(t=>t.AccessibleName=="DNS 名称 / PTR 地址").Text="bad; exit";var query=Find<Button>(dnsPage).Single(b=>b.Text=="查询记录");query.PerformClick();Pump(()=>query.Enabled);
                Check(Find<DataGridView>(dnsPage).Single().Rows.Count==0&&Find<Label>(dnsPage).Any(l=>l.Text.Contains("格式无效")),"Trial UI displays validation error without DNS query");
                Navigate(main,tree,"ICMP路由跟踪");Application.DoEvents();Navigate(main,tree,"DNS记录查询");Application.DoEvents();Screenshot(main,"trial-v052.png");
                Navigate(main,tree,"主机发现");Application.DoEvents();Check(!Find<TextBox>(Field<Panel>(main,"content")).Any(t=>t.AccessibleName=="TCP 端口（逗号或范围）"),"Ping page has no TCP port input");
                Navigate(main,tree,"端口扫描");Application.DoEvents();var panel=Field<Panel>(main,"content");var ports=Find<TextBox>(panel).Single(t=>t.AccessibleName=="TCP 端口（逗号或范围）");Check(NetworkDiscoveryService.ParsePorts(ports.Text).Length==CommonPorts.Count,"Inline scanner defaults to complete common preset");
                var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
                try
                {
                    ports.Text=((IPEndPoint)listener.LocalEndpoint).Port.ToString();Find<TextBox>(panel).Single(t=>t.AccessibleName=="目标主机名 / IP").Text="127.0.0.1";
                    var start=Find<Button>(panel).Single(b=>b.Text=="开始扫描");start.PerformClick();Pump(()=>start.Enabled);
                    var result=Find<DataGridView>(panel).Single();Check(result.Rows.Count==1&&(string)result.Rows[0].Cells[3].Value=="开放","Inline UI runs real local TCP scan and renders result");
                    Screenshot(main,"navigation-v051.png");
                }finally{listener.Stop();}
                Navigate(main,tree,"Wi-Fi分析");Application.DoEvents();panel=Field<Panel>(main,"content");Pump(()=>Find<Button>(panel).Single(b=>b.Text=="刷新信息").Enabled,30000);
                var fixture=new WifiInventory();fixture.Adapters.Add(new WifiAdapterRow{Name="样例无线网卡（测试数据）",Mac="02:00:00:00:00:01",State="已连接",Ssid="样例网络",Bssid="02:00:00:00:00:02",Type="基础结构",Phy="802.11ac",Frequency="5180",Band="5 GHz",Channel="36",Signal="82",Rx="866.7",Tx="780"});
                fixture.Networks.Add(new WifiBssRow{Ssid="样例网络（测试数据）",Bssid="02:00:00:00:00:02",Type="基础结构",Phy="802.11ac",Frequency="5180",Band="5 GHz",Channel="36",Signal="82",Rssi="-48",BasicRates="6, 12, 24",SupportedRates="6, 9, 12, 18, 24, 36, 48, 54",Security="加密",Adapter="样例无线网卡"});
                var root=Field<Panel>(main,"content").Controls.OfType<Panel>().Single();Call(main,"BindWifi",root,fixture);
                var tables=Find<DataGridView>(panel);Check(tables.Count(g=>g.Visible)==1&&tables.Single(g=>g.Visible).Columns[0].HeaderText=="SSID","Nearby hotspot table is the default and only one table is visible");Check(tables.Length==2&&tables.All(g=>g.Columns.Count==13)&&tables.All(g=>g.Rows.Count==1),"WiFi interfaces and APs rendered as separate 13-column tables");
                Check(tables.SelectMany(g=>g.Columns.Cast<DataGridViewColumn>()).Any(c=>c.HeaderText=="基本速率 Mb")&&tables.SelectMany(g=>g.Columns.Cast<DataGridViewColumn>()).Any(c=>c.HeaderText=="接收 Mb"),"Basic and link rates have distinct columns");
                Find<Button>(panel).Single(b=>b.Text=="无线接口 / 当前连接").PerformClick();Application.DoEvents();Screenshot(main,"wifi-v054-interface.png");Find<Button>(panel).Single(b=>b.Text=="附近热点（系统缓存）").PerformClick();Application.DoEvents();Screenshot(main,"wifi-v054-hotspots.png");Check(tables.Single(g=>g.Visible).Columns[8].HeaderText=="基本速率 Mb","Basic rates keep their own compact unit header");
                Check(tables.All(g=>g.Columns[0].Width>=220)&&tables.All(g=>g.Columns.Cast<DataGridViewColumn>().All(c=>!c.HeaderText.Contains("（"))),"SSID has a wide leading column and Wi-Fi headers omit parenthesized units");
                var hotspots=tables.Single(g=>g.Columns[2].HeaderText=="RSSI");Check(hotspots.Columns[12].HeaderText=="声明速率集 Mb"&&(string)hotspots.Rows[0].Cells[3].Value=="82%","RSSI is near the front, declared rate set is last, and percent is attached to signal");
                hotspots.Rows.Add(new WifiBssRow{Ssid="弱信号样例",Signal="9",Rssi="-85"}.Cells());Call(hotspots,"OnColumnHeaderMouseClick",new DataGridViewCellMouseEventArgs(3,-1,0,0,new MouseEventArgs(MouseButtons.Left,1,0,0,0)));Check((string)hotspots.Rows[0].Cells[3].Value=="9%","Signal sorting remains numeric with a percent suffix");
                var grid=tables.First();object originalCell=grid.Rows[0].Cells[0].Value;grid.Rows[0].Cells[0].Value="=SUM(1,2)";string csv=(string)typeof(MainForm).GetMethod("TableCsv",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{grid});Check(csv.Contains("'=SUM(1,2)"),"CSV neutralizes formula injection from network-supplied names");grid.Rows[0].Cells[0].Value=originalCell;
                main.Size=new Size(1000,670);Application.DoEvents();Check(Find<Panel>(panel).Single(p=>p.Name=="resultTabs").Height>180,"Tables remain usable at minimum window size");Screenshot(main,"wifi-v054-compact.png");
                Navigate(main,tree,"路由与MAC");Navigate(main,tree,"Wi-Fi分析");Navigate(main,tree,"端口扫描");Pump(()=>true);Check(uiErrors==0,"Rapid navigation cancels auto-read without disposed-control errors");
                using(var testPage=new Panel())
                {
                    var testBar=new FlowLayoutPanel();var testStop=new Button();var testState=new Label();testPage.Controls.Add(testBar);testPage.Controls.Add(testState);testBar.Controls.Add(testStop);
                    Type sessionType=typeof(MainForm).GetNestedType("PageSession",BindingFlags.NonPublic);object operation=Activator.CreateInstance(sessionType,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new object[]{testPage,testBar,testStop,testState},null);
                    bool cancelled=false;Func<System.Threading.CancellationToken,System.Threading.Tasks.Task> waiting=async delegate(System.Threading.CancellationToken token){try{await System.Threading.Tasks.Task.Delay(30000,token);}catch(OperationCanceledException){cancelled=true;throw;}};
                    var running=(System.Threading.Tasks.Task)sessionType.GetMethod("Run").Invoke(operation,new object[]{waiting});testPage.Dispose();Pump(()=>running.IsCompleted);Check(cancelled&&!running.IsFaulted,"Disposing an active page cancels work without updating disposed controls");
                }
                Modal(main,"NetworkInformationDialog",1);Modal(main,"CameraDiscoveryDialog");
                main.Close();Application.DoEvents();Check(physicalReads==2&&physicalMoves==1,"Cancelled and closed capture timers never run later or move the mouse again");Check(uiErrors==0,"No unhandled UI exceptions");
            }
            Console.WriteLine("NAVIGATION UI TOTAL PASS "+passed);return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
        finally{if(original==null){if(File.Exists(path))File.Delete(path);}else File.WriteAllBytes(path,original);}
    }
}
