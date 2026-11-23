using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WinForms
{
    public sealed partial class MainForm
    {
        private sealed class DialogOperation : IDisposable
        {
            private readonly FlowLayoutPanel bar;private readonly RichTextBox report;private readonly Button stop;
            private CancellationTokenSource cancel;public bool Running{get;private set;}
            public DialogOperation(Form window,FlowLayoutPanel bar,RichTextBox report,Button stop)
            {
                this.bar=bar;this.report=report;this.stop=stop;stop.Enabled=false;
                stop.Click+=delegate{if(cancel!=null)cancel.Cancel();};
                window.FormClosing+=delegate(object sender,FormClosingEventArgs e){if(Running){e.Cancel=true;cancel.Cancel();}};
            }
            public async Task Execute(Func<CancellationToken,Task<string>> work)
            {
                if(Running)return;Running=true;cancel=new CancellationTokenSource();var states=new Dictionary<Control,bool>();
                foreach(Control c in bar.Controls){states[c]=c.Enabled;c.Enabled=false;}stop.Enabled=true;report.Text="正在检查，可点击停止。关闭窗口也会先请求停止……";
                try{string text=await work(cancel.Token);if(!report.IsDisposed)report.Text=text;}
                catch(OperationCanceledException){if(!report.IsDisposed)report.AppendText("\r\n已停止。已获得的结果仍可导出，未完成目标没有检测结论。");}
                catch(Exception ex){if(!report.IsDisposed)report.AppendText("\r\n检查未完成："+ex.Message);}
                finally{Running=false;cancel.Dispose();cancel=null;foreach(var pair in states)if(!pair.Key.IsDisposed)pair.Key.Enabled=pair.Value;if(!stop.IsDisposed)stop.Enabled=false;}
            }
            public void Dispose(){if(cancel!=null)cancel.Cancel();}
        }
        private void NetworkInformationDialog(int mode)
        {
            if(mode!=1)throw new ArgumentOutOfRangeException("mode");
            FlowLayoutPanel bar;RichTextBox report;
            using(Form f=ToolWindow("DHCP 与网络服务",out bar,out report))
            {
                bar.Height=180;Button read=ToolButton(bar,"读取信息",delegate{}),stop=ToolButton(bar,"停止",delegate{});
                using(var operation=new DialogOperation(f,bar,report,stop))
                {
                    read.Click+=async delegate{await operation.Execute(delegate(CancellationToken token){return Task.Run(new Func<string>(delegate{return NetworkSystemService.Dhcp(token);}));});};
                    ToolButton(bar,"导出普通报告",delegate{ExportToolReport(f,report.Text,"DHCP与网络服务.txt");});
                    TextBox host=ToolInput(bar,"DNS / TCP 目标","",190),port=ToolInput(bar,"端口","443",55),url=ToolInput(bar,"HTTP URL","https://",230);
                    ToolButton(bar,"检查 DNS",async delegate{string name=host.Text.Trim();await operation.Execute(delegate(CancellationToken token){return Task.Run(new Func<string>(delegate{return NetworkSystemService.DnsCheck(name,token);}));});});
                    ToolButton(bar,"检查 TCP",async delegate{int p;if(!Int32.TryParse(port.Text,out p)||p<1||p>65535){MessageBox.Show(f,"端口须为 1–65535。");return;}string name=host.Text.Trim();await operation.Execute(async delegate(CancellationToken token){IPAddress ip=await NetworkDiscoveryService.Resolve(name,token);PortProbe r=await NetworkDiscoveryService.Probe(ip,p,3000,token);return r.ToString()+"\r\n仅验证 TCP 握手，不能证明业务正常。";});});
                    ToolButton(bar,"检查 HTTP",async delegate{string value=url.Text.Trim();await operation.Execute(delegate(CancellationToken token){return NetworkSystemService.HttpCheck(value,token);});});
                    report.Text="只读检查，不修改网卡、路由、DHCP 或服务设置。";f.ShowDialog(this);
                }
            }
        }
        private sealed class LocalInterface
        {
            public string Name;public IPAddress Address;
            public override string ToString(){return Name+" / "+Address;}
        }
        private void CameraDiscoveryDialog()
        {
            FlowLayoutPanel bar;RichTextBox report;
            using(Form f=ToolWindow("ONVIF 摄像头 / 设备发现",out bar,out report))
            {
                var interfaces=new ComboBox{Width=400,DropDownStyle=ComboBoxStyle.DropDownList};bar.Controls.Add(interfaces);
                Action reload=delegate{
                    interfaces.Items.Clear();try{foreach(var nic in NetworkInterface.GetAllNetworkInterfaces())if(nic.OperationalStatus==OperationalStatus.Up)foreach(var a in nic.GetIPProperties().UnicastAddresses)if(a.Address.AddressFamily==AddressFamily.InterNetwork&&!IPAddress.IsLoopback(a.Address))interfaces.Items.Add(new LocalInterface{Name=nic.Name,Address=a.Address});if(interfaces.Items.Count>0)interfaces.SelectedIndex=0;}
                    catch(NetworkInformationException ex){report.Text="网卡读取失败："+ex.Message;}
                };reload();ToolButton(bar,"刷新网卡",delegate{reload();});
                Button start=ToolButton(bar,"发现设备（5 秒）",delegate{}),stop=ToolButton(bar,"停止",delegate{});
                using(var operation=new DialogOperation(f,bar,report,stop))
                {
                    start.Click+=async delegate{var nic=interfaces.SelectedItem as LocalInterface;if(nic==null){MessageBox.Show(f,"请选择局域网网卡。");return;}await operation.Execute(async delegate(CancellationToken token){CameraDiscoveryResult r=await CameraDiscoveryService.Discover(nic.Address,token);return r+"\r\n没有结果也可能是设备不支持 ONVIF 发现、组播被阻止、设备位于其他 VLAN，或本机防火墙限制。";});};
                    ToolButton(bar,"导出设备声明",delegate{ExportToolReport(f,report.Text,"ONVIF发现.txt");});
                    report.Text="在选定网卡上发送一轮 WS-Discovery 组播探测，等待 5 秒。仅显示设备声明的地址与类型，不尝试登录或播放视频。";f.ShowDialog(this);
                }
            }
        }
    }
}
