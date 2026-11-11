using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ITDesktopToolkit
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
            using(var d=new SaveFileDialog{FileName=name,Filter=name.EndsWith(".csv")?"CSV 文件|*.csv":"文本报告|*.txt",OverwritePrompt=true})
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
        private void ConfigurationCompareDialog()
        {
            FlowLayoutPanel bar;RichTextBox report;
            using(Form f=ToolWindow("电脑配置对比",out bar,out report))
            {
                string current=null;Dictionary<string,string> left=null,right=null;
                report.Text="先在两台电脑分别采集并导出配置报告，再导入左/右报告比较。包含系统版本、网卡、IP/DNS、网络类别、相关服务与共享策略。\r\n报告包含电脑名、MAC、内网地址与共享路径，请按需分享。";
                Button collect=null; collect=ToolButton(bar,"采集本机报告",async delegate{
                    collect.Enabled=false;try{current=await Task.Run(new Func<string>(InspectionService.CollectConfiguration));if(!report.IsDisposed){report.Text="本机报告已采集，可导出。\r\n";foreach(var pair in InspectionService.Parse(current))report.AppendText(pair.Key+"："+pair.Value+"\r\n");}}catch(Exception ex){if(!report.IsDisposed)report.Text=ex.Message;}finally{if(!collect.IsDisposed)collect.Enabled=true;}
                });
                ToolButton(bar,"导出本机对比报告",delegate{if(current==null){MessageBox.Show(f,"请先采集本机报告。");return;}ExportToolReport(f,current,"配置报告_"+Environment.MachineName+".txt");});
                ToolButton(bar,"导入左侧报告",delegate{try{string s=ReadToolFile(f);if(s!=null){left=InspectionService.Parse(s);report.Text="左侧已载入 "+left.Count+" 项。请载入右侧后点击比较。";}}catch(Exception ex){MessageBox.Show(f,ex.Message);}});
                ToolButton(bar,"导入右侧报告",delegate{try{string s=ReadToolFile(f);if(s!=null){right=InspectionService.Parse(s);report.Text="右侧已载入 "+right.Count+" 项。";}}catch(Exception ex){MessageBox.Show(f,ex.Message);}});
                ToolButton(bar,"比较两份报告",delegate{if(left==null||right==null){MessageBox.Show(f,"请分别导入左右报告。");return;}report.Text=InspectionService.Compare(left,right);});
                ToolButton(bar,"导出对比结果",delegate{ExportToolReport(f,report.Text,"配置差异.txt");}); f.ShowDialog(this);
            }
        }
        private void SoftwareInspectionDialog()
        {
            FlowLayoutPanel bar;RichTextBox report;
            using(Form f=ToolWindow("软件清单与环境检查",out bar,out report))
            {
                string requirements="";report.Text="可直接读取软件清单与 .NET Framework 登记信息，或先导入自定义要求。\r\n清单格式（UTF-8）：每行 软件名称关键词<Tab>最低版本；最低版本可空。例如：\r\nGoogle Chrome\t120.0\r\nMicrosoft Visual C++\r\n\r\n关键词不区分大小写；不同架构请用更具体的关键词。";
                ToolButton(bar,"导入检查清单",delegate{try{string text=ReadToolFile(f);if(text!=null){InspectionService.CheckRequirements(new Dictionary<string,string>(),text);requirements=text;report.Text="已加载要求：\r\n"+text;}}catch(Exception ex){MessageBox.Show(f,ex.Message);}});
                Button read=null;read=ToolButton(bar,"读取并检查",delegate{string rules=requirements;DoJob(report,read,delegate{return InspectionService.SoftwareReport(rules);});});
                ToolButton(bar,"导出结果",delegate{ExportToolReport(f,report.Text,"软件环境检查.txt");});
                ToolButton(bar,"导出清单模板",delegate{ExportToolReport(f,"# 软件名称关键词<Tab>最低版本（可空）\r\nGoogle Chrome\t120.0\r\nMicrosoft Visual C++\r\n","软件检查清单.txt");}); f.ShowDialog(this);
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
        private void UsbProtectionDialog()
        {
            FlowLayoutPanel bar;RichTextBox report;
            using(Form f=ToolWindow("U 盘检查与本机防护",out bar,out report))
            {
                bar.Height=215;
                var drive=new ComboBox{Width=210,DropDownStyle=ComboBoxStyle.DropDownList,Margin=new Padding(5,9,5,4)};bar.Controls.Add(drive);
                Action reload=delegate{drive.Items.Clear();foreach(var d in DriveInfo.GetDrives())try{if(d.DriveType==DriveType.Removable&&d.IsReady)drive.Items.Add(d.RootDirectory.FullName);}catch(IOException){}if(drive.Items.Count>0)drive.SelectedIndex=0;};reload();
                ToolButton(bar,"刷新 U 盘",delegate{reload();});
                CancellationTokenSource cancel=null;bool running=false;
                report.Text="只读检查，不执行盘内文件、不恢复资料、不删除文件。检查隐藏属性、伪装扩展名和快捷方式指向；可疑不等于确诊。\r\n\r\n本机防护：关闭当前账户的所有驱动器自动播放，或开启本机 USB 存储写保护（管理员）。写保护通常需重新插拔，不能跟随 U 盘到其他电脑，也不阻止手动运行程序。请先导出结果，再更改策略。";
                Button scan=null,stop=null;scan=ToolButton(bar,"开始只读检查",async delegate{
                    if(drive.SelectedItem==null){MessageBox.Show(f,"未发现可移动 U 盘。");return;}
                    string root=drive.Text;cancel=new CancellationTokenSource();running=true;foreach(Control control in bar.Controls)control.Enabled=false;stop.Enabled=true;report.Text="正在只读检查，可点击停止……";
                    try{var result=await Task.Run(delegate{return UsbProtectionService.Scan(root,cancel.Token);});if(!report.IsDisposed)report.Text=result.Report();}catch(Exception ex){if(!report.IsDisposed)report.Text=ex.Message;}
                    finally{running=false;cancel.Dispose();cancel=null;if(!scan.IsDisposed){foreach(Control control in bar.Controls)control.Enabled=true;stop.Enabled=false;}}
                });stop=ToolButton(bar,"停止检查",delegate{if(cancel!=null)cancel.Cancel();});stop.Enabled=false;
                Button history=null;history=ToolButton(bar,"读取 Defender 历史",delegate{if(drive.SelectedItem==null){MessageBox.Show(f,"请选择 U 盘。");return;}string root=drive.Text;DoJob(report,history,delegate{return UsbProtectionService.DefenderHistory(root);});});
                ToolButton(bar,"导入其他杀毒报告",delegate{try{string text=ReadToolFile(f);if(text!=null)report.Text="【外部报告原文，未自动判定/验证】\r\n"+text;}catch(Exception ex){MessageBox.Show(f,ex.Message);}});
                ToolButton(bar,"导出当前报告",delegate{ExportToolReport(f,report.Text,"U盘检查_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+".txt");});
                ToolButton(bar,"读取本机防护状态",delegate{try{report.Text=UsbProtectionService.PolicyStatus();}catch(Exception ex){report.Text=ex.Message;}});
                foreach(bool wpValue in new bool[]{false,true})foreach(bool restoreValue in new bool[]{false,true})
                {
                    bool wp=wpValue,restore=restoreValue;string label=wp?(restore?"还原 USB 写保护策略":"开启本机 USB 写保护"):(restore?"还原自动播放策略":"关闭当前账户自动播放");
                    ToolButton(bar,label,delegate{
                        if(wp&&!RequireAdmin())return;
                        if(!ConfirmAction(label,(restore?"恢复首次保存的策略原值。":"修改前会保存原值。")+(wp?"此设置影响本机 USB 存储写入，可能影响其他 U 盘；请重新插拔后验证。":"此设置影响当前账户所有驱动器的自动播放；可能需重新登录。")))return;
                        try{report.Text=UsbProtectionService.SetPolicy(wp,restore);}catch(Exception ex){report.Text="操作失败："+ex.Message;}
                    });
                }
                f.FormClosing+=delegate(object sender,FormClosingEventArgs e){if(running){e.Cancel=true;cancel.Cancel();report.AppendText("\r\n正在停止，请稍后关闭。");}};
                f.ShowDialog(this);
            }
        }
    }
}
