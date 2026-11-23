using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WinForms
{
    public sealed partial class MainForm
    {
        private void BuildFieldTool(string title,string description,Action open)
        {
            FlowLayoutPanel bar; Panel p=Page(title,description,out bar);
            RichTextBox box=ReadBox(); box.Text=description+"\r\n\r\n点击上方按钮进入。结果可导出，检查不会自动修复系统。";
            p.Controls["pageBody"].Controls.Add(ContentCard(box));
            Action(bar,"打开"+title,delegate{open();},true);
        }
        private Form ToolWindow(string title,out FlowLayoutPanel bar,out RichTextBox report)
        {
            Form f=NewDialog(title,1000,680); f.MinimumSize=new Size(780,540);
            bar=new FlowLayoutPanel{Dock=DockStyle.Top,Height=145,AutoScroll=true,Padding=new Padding(8),WrapContents=true};
            report=ReadBox(); report.Margin=new Padding(10); f.Controls.Add(report); f.Controls.Add(bar); return f;
        }
        private Button ToolButton(FlowLayoutPanel bar,string title,EventHandler click)
        { Button b=FormButton(title,0,0,170,click); b.Margin=new Padding(4);bar.Controls.Add(b);return b; }
        private TextBox ToolInput(FlowLayoutPanel bar,string label,string value,int width)
        {
            bar.Controls.Add(new Label{Text=label,AutoSize=true,Margin=new Padding(6,12,0,0)});
            var t=new TextBox{Text=value,Width=width,Margin=new Padding(4,9,4,4)};bar.Controls.Add(t);return t;
        }
        private void ExportToolReport(Form owner,string text,string name)
        {
            using(var d=new SaveFileDialog{InitialDirectory=Paths.BaseDir,FileName=name,Filter=name.EndsWith(".csv")?"CSV 文件|*.csv":"文本报告|*.txt",OverwritePrompt=true})
            if(d.ShowDialog(owner)==DialogResult.OK)try{File.WriteAllText(d.FileName,text,new UTF8Encoding(true));}catch(Exception ex){MessageBox.Show(owner,"导出失败："+ex.Message);}
        }
        private string ReadToolFile(Form f)
        {
            using(var d=new OpenFileDialog{Filter="文本报告/清单|*.txt;*.tsv|所有文件|*.*"})
            {
                if(d.ShowDialog(f)!=DialogResult.OK)return null;
                if(new FileInfo(d.FileName).Length>4*1024*1024)throw new IOException("请选择小于 4 MiB 的报告/清单。");
                return File.ReadAllText(d.FileName,Encoding.UTF8);
            }
        }
        private Func<CancellationToken,string> configurationReader=InspectionService.CollectConfiguration;
        private Func<Form,bool> configurationExportPrompt=delegate(Form owner){return MessageBox.Show(owner,"本机配置采集完成，是否现在导出报告？","导出配置报告",MessageBoxButtons.YesNo,MessageBoxIcon.Question)==DialogResult.Yes;};
        private void ConfigurationCompareDialog()
        {
            FlowLayoutPanel bar;RichTextBox report;
            using(Form f=ToolWindow("电脑配置对比",out bar,out report))
            {
                string current=null;Dictionary<string,string> left=null,right=null;CancellationTokenSource collectCancel=null;
                f.FormClosing+=delegate{if(collectCancel!=null)collectCancel.Cancel();};
                report.Text="先在两台电脑分别采集并导出配置报告，再导入左/右报告比较。按部件一行展示系统、CPU、主板、内存、显卡、磁盘与网卡，导出同样的紧凑格式。\r\n报告包含电脑名、MAC 与 IP 地址。";
                Button collect=null; collect=ToolButton(bar,"采集本机报告",async delegate{
                    collect.Enabled=false;current=null;report.Text="正在后台采集系统与硬件配置…";collectCancel=new CancellationTokenSource();CancellationToken token=collectCancel.Token;
                    try {
                        string captured=await Task.Run(new Func<string>(delegate{return configurationReader(token);}));
                        if(report.IsDisposed||f.IsDisposed)return;current=captured;
                        report.Text=InspectionService.ConfigurationSummary(current);
                        if(configurationExportPrompt(f))
                            ExportToolReport(f,InspectionService.ExportConfiguration(current),"配置报告_"+Environment.MachineName+".txt");
                    }catch(OperationCanceledException){if(!report.IsDisposed)report.Text="采集已取消。";}
                    catch(Exception ex){if(!report.IsDisposed)report.Text="采集失败："+ex.Message;}
                    finally{collectCancel.Dispose();collectCancel=null;if(!collect.IsDisposed)collect.Enabled=true;}
                });
                ToolButton(bar,"导出本机对比报告",delegate{if(current==null){MessageBox.Show(f,"请先采集本机报告。");return;}ExportToolReport(f,InspectionService.ExportConfiguration(current),"配置报告_"+Environment.MachineName+".txt");});
                ToolButton(bar,"导入左侧报告",delegate{try{string s=ReadToolFile(f);if(s!=null){left=InspectionService.Parse(s);report.Text="左侧已载入 "+left.Count+" 项。请载入右侧后点击比较。";}}catch(Exception ex){MessageBox.Show(f,ex.Message);}});
                ToolButton(bar,"导入右侧报告",delegate{try{string s=ReadToolFile(f);if(s!=null){right=InspectionService.Parse(s);report.Text="右侧已载入 "+right.Count+" 项。";}}catch(Exception ex){MessageBox.Show(f,ex.Message);}});
                ToolButton(bar,"比较两份报告",delegate{if(left==null||right==null){MessageBox.Show(f,"请分别导入左右报告。");return;}report.Text=InspectionService.CompareConfigurations(left,right);});
                ToolButton(bar,"导出对比结果",delegate{ExportToolReport(f,report.Text,"配置差异.txt");}); f.ShowDialog(this);
            }
        }
        private void ContinuousNetworkDialog()
        {
            FlowLayoutPanel bar;RichTextBox report;
            using(Form f=ToolWindow("网络质量持续检测",out bar,out report))
            {
                var targets=ToolInput(bar,"目标（逗号分隔）","",360);
                var rounds=ToolInput(bar,"轮数","300",65);var interval=ToolInput(bar,"间隔秒","2",50);
                var samples=new List<NetworkSample>();object guard=new object();CancellationTokenSource cancel=null;bool running=false;
                report.Text="输入网关、内网服务器或外网主机名/IP，最多 8 个。每轮并行 ICMP 检测，随后等待指定间隔。\r\n记录响应时间、未响应时间及延迟波动。ICMP 无响应不一定代表断网。停止后可导出完整逐条 CSV。";
                Action refresh=delegate{
                    List<NetworkSample> copy;lock(guard)copy=new List<NetworkSample>(samples);
                    var b=new StringBuilder(NetworkMonitorService.Summary(copy));b.AppendLine("\r\n最近 20 条（全部记录请导出 CSV）：");
                    for(int i=Math.Max(0,copy.Count-20);i<copy.Count;i++){var s=copy[i];b.AppendLine(s.Time.ToString("HH:mm:ss zzz")+" "+s.Host+" "+s.Status+(s.Milliseconds<0?"":" "+s.Milliseconds+" ms"));}
                    report.Text=b.ToString();
                };
                var timer=new System.Windows.Forms.Timer{Interval=1000};timer.Tick+=delegate{refresh();};
                Button start=null,stop=null;
                start=ToolButton(bar,"开始新一轮检测",async delegate{
                    try
                    {
                        string[] hosts=NetworkMonitorService.Targets(targets.Text);int n,seconds;
                        if(!Int32.TryParse(rounds.Text,out n)||n<1||n>10000||!Int32.TryParse(interval.Text,out seconds)||seconds<1||seconds>3600)throw new ArgumentException("轮数 1–10000，间隔 1–3600 秒。");
                        if(samples.Count>0&&!ConfirmAction("开始新检测","将清空窗口中的上一轮采样，请先导出需要保留的结果。"))return;
                        lock(guard)samples.Clear();cancel=new CancellationTokenSource();running=true;start.Enabled=false;stop.Enabled=true;targets.Enabled=rounds.Enabled=interval.Enabled=false;timer.Start();
                        await NetworkMonitorService.Run(hosts,n,seconds,cancel.Token,delegate(NetworkSample sample){lock(guard)samples.Add(sample);});
                    }
                    catch(Exception ex){if(!report.IsDisposed)MessageBox.Show(f,ex.Message);}
                    finally{timer.Stop();if(running&&!report.IsDisposed)refresh();running=false;if(cancel!=null){cancel.Dispose();cancel=null;}if(!start.IsDisposed){start.Enabled=true;stop.Enabled=false;targets.Enabled=rounds.Enabled=interval.Enabled=true;}}
                });
                stop=ToolButton(bar,"停止检测",delegate{if(cancel!=null)cancel.Cancel();});stop.Enabled=false;
                ToolButton(bar,"导出完整 CSV",delegate{List<NetworkSample> copy;lock(guard)copy=new List<NetworkSample>(samples);ExportToolReport(f,NetworkMonitorService.Export(copy),"网络检测.csv");});
                f.FormClosing+=delegate(object sender,FormClosingEventArgs e){if(running){e.Cancel=true;cancel.Cancel();report.AppendText("\r\n正在停止，请稍后关闭窗口。");}};
                try{f.ShowDialog(this);}finally{timer.Dispose();if(cancel!=null){cancel.Cancel();cancel.Dispose();}}
            }
        }
        private void PrinterDiagnosticsDialog()
        {
            FlowLayoutPanel bar;RichTextBox report;
            using(Form f=ToolWindow("打印机专项诊断",out bar,out report))
            {
                var printer=ToolInput(bar,"准确打印机名称","",300);
                var host=ToolInput(bar,"打印机 IP/主机名","",200);var port=ToolInput(bar,"端口","9100",60);
                report.Text="读取默认打印机、队列任务、驱动和端口；TCP 连通测试不发送打印数据。取消队列只影响输入名称对应的打印机。";
                Button read=null;read=ToolButton(bar,"读取完整诊断",delegate{DoJob(report,read,PrinterDiagnosticsService.Read);});
                Button clear=null;clear=ToolButton(bar,"取消所选队列全部任务",delegate{
                    string name=printer.Text.Trim();if(name.Length==0){MessageBox.Show(f,"请输入准确打印机名称。");return;}
                    if(!RequireAdmin()||!ConfirmAction("取消打印任务","将取消打印机『"+name+"』当前排队的所有任务，不能撤销。"))return;
                    DoJob(report,clear,delegate{return PrinterDiagnosticsService.ClearQueue(name);});
                });
                Button test=null;test=ToolButton(bar,"测试 TCP 端口",async delegate{
                    int value;if(!Int32.TryParse(port.Text,out value)){MessageBox.Show(f,"端口应为整数。");return;}test.Enabled=false;
                    try{string result=await PrinterDiagnosticsService.TestPort(host.Text.Trim(),value);if(!report.IsDisposed)report.Text=result;}catch(Exception ex){if(!report.IsDisposed)report.Text=ex.Message;}finally{if(!test.IsDisposed)test.Enabled=true;}
                });
                ToolButton(bar,"导出诊断结果",delegate{ExportToolReport(f,report.Text,"打印机诊断.txt");});f.ShowDialog(this);
            }
        }
        private Func<List<UsbDriveAccess>> usbDriveReader=UsbAccessService.AvailableDrives;
        private void UsbProtectionDialog()
        {
            FlowLayoutPanel bar;RichTextBox report;
            using(Form f=ToolWindow("U 盘检查与本机防护",out bar,out report))
            {
                bar.AutoSize=true;bar.AutoSizeMode=AutoSizeMode.GrowAndShrink;
                var drive=new ComboBox{Width=245,AccessibleName="U 盘与格式",DropDownStyle=ComboBoxStyle.DropDownList,Margin=new Padding(5,9,5,4)};bar.Controls.Add(drive);
                Action updateAccess=null;
                Action reload=delegate{string previous=drive.SelectedItem is UsbDriveAccess?((UsbDriveAccess)drive.SelectedItem).Root:"";drive.Items.Clear();try{foreach(var d in usbDriveReader())drive.Items.Add(d);if(drive.Items.Count>0){int index=0;for(int i=0;i<drive.Items.Count;i++)if(((UsbDriveAccess)drive.Items[i]).Root==previous)index=i;drive.SelectedIndex=index;}}catch(Exception ex){report.Text="读取 U 盘未完成："+ex.Message;}if(updateAccess!=null)updateAccess();};
                ToolButton(bar,"刷新 U 盘",delegate{reload();});
                CancellationTokenSource cancel=null;bool running=false;
                report.Text="只读检查不执行或删除盘内文件。\r\n\r\nNTFS U 盘权限：选盘后可让普通 Windows 用户只读，SYSTEM 为所有者，管理员保留写入和恢复权限。此方式不限制管理员或不遵守 NTFS 权限的系统；需要所有电脑都只读，请使用硬件写保护。\r\n\r\n本机策略只影响当前电脑。";
                Button scan=null,stop=null;scan=ToolButton(bar,"开始只读检查",async delegate{
                    if(drive.SelectedItem==null){MessageBox.Show(f,"未发现可移动 U 盘。");return;}
                    string root=((UsbDriveAccess)drive.SelectedItem).Root;cancel=new CancellationTokenSource();running=true;foreach(Control control in bar.Controls)control.Enabled=false;stop.Enabled=true;report.Text="正在只读检查，可点击停止……";
                    try{var result=await Task.Run(delegate{return UsbProtectionService.Scan(root,cancel.Token);});if(!report.IsDisposed)report.Text=result.Report();}catch(Exception ex){if(!report.IsDisposed)report.Text=ex.Message;}
                    finally{running=false;cancel.Dispose();cancel=null;if(!scan.IsDisposed){foreach(Control control in bar.Controls)control.Enabled=true;stop.Enabled=false;updateAccess();}}
                });stop=ToolButton(bar,"停止检查",delegate{if(cancel!=null)cancel.Cancel();});stop.Enabled=false;
                Button history=null;history=ToolButton(bar,"读取 Defender 历史",delegate{if(drive.SelectedItem==null){MessageBox.Show(f,"请选择 U 盘。");return;}string root=((UsbDriveAccess)drive.SelectedItem).Root;DoJob(report,history,delegate{return UsbProtectionService.DefenderHistory(root);});});
                ToolButton(bar,"导入其他杀毒报告",delegate{try{string text=ReadToolFile(f);if(text!=null)report.Text="【外部报告原文，未自动判定/验证】\r\n"+text;}catch(Exception ex){MessageBox.Show(f,ex.Message);}});
                ToolButton(bar,"导出当前报告",delegate{ExportToolReport(f,report.Text,"U盘检查_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+".txt");});
                ToolButton(bar,"读取本机防护状态",delegate{try{report.Text=UsbProtectionService.PolicyStatus();}catch(Exception ex){report.Text=ex.Message;}});
                Func<bool> canSetAcl=delegate{if(drive.SelectedItem==null){report.Text="请选择 U 盘。";return false;}try{var current=UsbAccessService.InspectDrive(((UsbDriveAccess)drive.SelectedItem).Root);if(!current.SupportsPermissions){report.Text=current.PermissionHint;return false;}return true;}catch(Exception ex){report.Text="无法核对 U 盘格式，请刷新后重试："+ex.Message;return false;}};
                Button protect=null;protect=ToolButton(bar,"NTFS 普通用户只读",delegate{
                    if(!canSetAcl())return;
                    if(!RequireAdmin())return;
                    string root=((UsbDriveAccess)drive.SelectedItem).Root;
                    if(!ConfirmAction("设置 U 盘 NTFS 只读权限","目标："+root+"\r\n将递归修改全部文件和文件夹的 NTFS 权限，并在本机保存原权限。普通用户只能读取；管理员仍可写入。请先备份重要资料。"))return;
                    DoJob(report,protect,delegate{try{return UsbAccessService.Protect(root);}catch(Exception ex){return "权限设置未完成："+ex.Message;}});
                });
                Button restoreAcl=null;restoreAcl=ToolButton(bar,"恢复所选 U 盘原权限",delegate{
                    if(!canSetAcl())return;
                    if(!RequireAdmin())return;
                    string root=((UsbDriveAccess)drive.SelectedItem).Root;
                    if(!ConfirmAction("恢复 U 盘权限","将按本机备份恢复 "+root+" 的 NTFS 权限。请核对选中的是原 U 盘。"))return;
                    DoJob(report,restoreAcl,delegate{try{return UsbAccessService.Restore(root);}catch(Exception ex){return "权限恢复未完成："+ex.Message;}});
                });
                Button folderAccess=null;folderAccess=ToolButton(bar,"取得指定文件夹权限",delegate{
                    if(!RequireAdmin())return;
                    using(var picker=new FolderBrowserDialog{Description="选择要取得权限的文件夹（含子文件夹）",ShowNewFolderButton=false})
                    {
                        if(picker.ShowDialog(f)!=DialogResult.OK)return;
                        string path=picker.SelectedPath;
                        if(!ConfirmAction("取得文件夹权限","将递归把 Administrators 设为 "+path+" 的所有者并授予完全控制。此操作会改变该文件夹及子项的权限。"))return;
                        DoJob(report,folderAccess,delegate{return UsbAccessService.TakeFolder(path);});
                    }
                });
                foreach(bool wpValue in new bool[]{false,true})foreach(bool restoreValue in new bool[]{false,true})
                {
                    bool wp=wpValue,restore=restoreValue;string label=wp?(restore?"还原 USB 写保护策略":"开启本机 USB 写保护"):(restore?"还原自动播放策略":"关闭当前账户自动播放");
                    ToolButton(bar,label,delegate{
                        if(wp&&!RequireAdmin())return;
                        if(!ConfirmAction(label,(restore?"恢复首次保存的策略原值。":"修改前会保存原值。")+(wp?"此设置影响本机 USB 存储写入，可能影响其他 U 盘；请重新插拔后验证。":"此设置影响当前账户所有驱动器的自动播放；可能需重新登录。")))return;
                        try{report.Text=UsbProtectionService.SetPolicy(wp,restore);}catch(Exception ex){report.Text="操作失败："+ex.Message;}
                    });
                }
                var accessHint=new Label{AutoSize=true,MaximumSize=new Size(900,0),Margin=new Padding(6,8,6,10),ForeColor=Navy,Name="usbPermissionHint"};
                bar.SetFlowBreak(bar.Controls[bar.Controls.Count-1],true);bar.Controls.Add(accessHint);
                updateAccess=delegate{var selected=drive.SelectedItem as UsbDriveAccess;bool supported=selected!=null&&selected.SupportsPermissions;protect.Enabled=!running&&supported;restoreAcl.Enabled=!running&&supported;accessHint.Text=selected==null?"未发现已就绪的可移动 U 盘，可插入后刷新。":selected.PermissionHint;};
                drive.SelectedIndexChanged+=delegate{updateAccess();};reload();
                f.FormClosing+=delegate(object sender,FormClosingEventArgs e){if(running){e.Cancel=true;cancel.Cancel();report.AppendText("\r\n正在停止，请稍后关闭。");}};
                f.ShowDialog(this);
            }
        }
    }
}
