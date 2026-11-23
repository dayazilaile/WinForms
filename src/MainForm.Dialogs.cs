using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace WinForms
{
    public sealed partial class MainForm
    {
        private void NetworkConfigDialog()
        {
            using(Form f=NewDialog("有线 / 无线网卡：IP、网关、DNS、MAC 管理",800,720))
            {
                f.MinimumSize=new Size(730,675);
                ComboBox adapters=new ComboBox { Left=165,Top=20,Width=585,DropDownStyle=ComboBoxStyle.DropDownList };
                Label current=new Label { Left=25,Top=59,Width=720,Height=98,Text="正在读取网卡……" };
                ComboBox mode=new ComboBox { Left=165,Top=164,Width=255,DropDownStyle=ComboBoxStyle.DropDownList };
                mode.Items.AddRange(new object[]{"自动获取 IP（DHCP）","手动静态 IP"});mode.SelectedIndex=0;
                TextBox ip=FormText("",165,207,260),mask=FormText("255.255.255.0",165,243,260),gw=FormText("",165,279,260);
                ComboBox dnsMode=new ComboBox { Left=165,Top=315,Width=255,DropDownStyle=ComboBoxStyle.DropDownList };
                dnsMode.Items.AddRange(new object[]{"自动获取 DNS","手动 DNS"});dnsMode.SelectedIndex=0;
                TextBox dns1=FormText("",165,352,260),dns2=FormText("",165,388,260);
                Label info=new Label { Left=455,Top=203,Width=300,Height=222,Text="填写静态 IPv4、子网掩码及网关。\r\nDNS 可与 IP 模式独立选择。\r\n修改前自动保存本机当前配置至日志目录。\r\n设置后会检查命令退出码与实际 DHCP/IP 状态。\r\n失败时显示原因，远程操作可能中断连接。" };
                TextBox mac=FormText("",165,436,260);
                RichTextBox result=ReadBox(); result.Dock=DockStyle.None;result.SetBounds(20,528,740,160);
                Action refill=delegate {
                    NicSetting nic=adapters.SelectedItem as NicSetting;
                    if(nic==null)return;
                    current.Text="名称："+nic.Name+"    类型："+nic.Kind+"\r\n型号："+nic.Description+"\r\nMAC："+nic.Mac+"\r\nIPv4："+nic.IPv4+" / "+nic.Mask+"   网关："+nic.Gateway+"\r\nDNS："+nic.Dns;
                    mode.SelectedIndex=nic.Dhcp?0:1;
                    ip.Text=nic.IPv4.Split(',')[0].Trim();mask.Text=nic.Mask.Split(',')[0].Trim();gw.Text=nic.Gateway.Split(',')[0].Trim();
                    dns1.Text=nic.Dns.Split(',')[0].Trim();dns2.Text=nic.Dns.IndexOf(',')>=0?nic.Dns.Split(',')[1].Trim():"";
                    dnsMode.SelectedIndex=nic.AutoDns?0:1;
                    mac.Text=nic.Mac;
                };
                Action load=delegate {
                    NicSetting selected=adapters.SelectedItem as NicSetting;
                    string id=selected==null?"":selected.Id;
                    adapters.Items.Clear(); foreach(NicSetting a in NetworkConfigService.GetAdapters())adapters.Items.Add(a);
                    if(adapters.Items.Count>0) {
                        int idx=0;for(int i=0;i<adapters.Items.Count;i++)if(((NicSetting)adapters.Items[i]).Id==id)idx=i;
                        adapters.SelectedIndex=idx;
                    }
                    else current.Text="未找到可显示的有线或无线物理网卡。";
                };
                adapters.SelectedIndexChanged+=delegate { refill(); };
                mode.SelectedIndexChanged+=delegate {bool enabled=mode.SelectedIndex==1;ip.Enabled=mask.Enabled=gw.Enabled=enabled;};
                dnsMode.SelectedIndexChanged+=delegate {bool enabled=dnsMode.SelectedIndex==1;dns1.Enabled=dns2.Enabled=enabled;};
                Button apply=null; apply=FormButton("应用 IP / DNS",460,434,135,async delegate {
                    NicSetting n=adapters.SelectedItem as NicSetting;if(n==null||!RequireAdmin())return;
                    bool dhcp=mode.SelectedIndex==0,autoDns=dnsMode.SelectedIndex==0;
                    if(!ConfirmAction("修改网卡配置","网卡："+n.Name+"\r\n新IP模式："+(dhcp?"DHCP":"静态 "+ip.Text)+"\r\n新DNS："+(autoDns?"自动获取":dns1.Text+", "+dns2.Text)+"\r\n注意：可能立即断网，远程维护有失联风险。"))return;
                    string ipValue=ip.Text,maskValue=mask.Text,gatewayValue=gw.Text,dns1Value=dns1.Text,dns2Value=dns2.Text;
                    apply.Enabled=false;
                    try {string report=await Task.Run(delegate {return NetworkConfigService.ApplyIPv4(n,dhcp,ipValue,maskValue,gatewayValue,autoDns,dns1Value,dns2Value);});if(!result.IsDisposed){
                        result.Text=report;
                        bool ok=report.Contains("【配置结果】成功");
                        load(); // Re-read the actual adapter state; do not claim success based only on command launch.
                        if(!ok){ ip.Text=ipValue;mask.Text=maskValue;gw.Text=gatewayValue;dns1.Text=dns1Value;dns2.Text=dns2Value; }
                    } }
                    catch(Exception ex){result.Text="操作失败："+ex.Message;}finally{if(!apply.IsDisposed)apply.Enabled=true;}
                });
                Button setmac=null; setmac=FormButton("设置 MAC",605,434,144,async delegate {
                    NicSetting n=adapters.SelectedItem as NicSetting;if(n==null||!RequireAdmin())return;
                    if(!ConfirmAction("修改网卡 MAC","将 " + n.Name + " 的 MAC 改为："+mac.Text+"\r\n支持情况取决于驱动，修改期间会禁用/启用网卡，可能断网。\r\n务必确认用途符合网络管理规定。"))return;
                    string macValue=mac.Text;
                    setmac.Enabled=false;
                    try{result.Text=await Task.Run(delegate{return NetworkConfigService.ApplyMac(n,macValue,false);});load();}
                    catch(Exception ex){result.Text="操作失败："+ex.Message;}finally{if(!setmac.IsDisposed)setmac.Enabled=true;}
                });
                Button resetmac=null; resetmac=FormButton("恢复驱动默认 MAC",165,480,190,async delegate {
                    NicSetting n=adapters.SelectedItem as NicSetting;if(n==null||!RequireAdmin())return;
                    if(!ConfirmAction("恢复 MAC","恢复网卡驱动默认网络地址。可能中断网络；部分网卡不支持此属性。"))return;
                    resetmac.Enabled=false;
                    try {result.Text=await Task.Run(delegate{return NetworkConfigService.ApplyMac(n,"",true);});load();}
                    catch(Exception ex){result.Text="恢复失败："+ex.Message;}finally{if(!resetmac.IsDisposed)resetmac.Enabled=true;}
                });
                Button refresh=FormButton("刷新网卡",605,480,144,delegate {load();});
                Button restore=null;restore=FormButton("恢复最近网络配置",364,480,225,async delegate {
                    NicSetting nic=adapters.SelectedItem as NicSetting;if(nic==null||!RequireAdmin())return;
                    if(!ConfirmAction("恢复网络配置","根据该网卡最近一次操作前的备份恢复 IP 和 DNS。可能导致网络断开，请在现场操作。"))return;
                    restore.Enabled=false;
                    try {result.Text=await Task.Run(delegate {return NetworkConfigService.RestoreLatest(nic);});load();}
                    catch(Exception ex){result.Text="恢复失败："+ex.Message;}finally{if(!restore.IsDisposed)restore.Enabled=true;}
                });
                f.Controls.AddRange(new Control[]{FormLabel("选择网卡",25,20,130),adapters,current,FormLabel("IP 分配方式",25,164,130),mode,
                    FormLabel("IPv4 地址",25,206,130),ip,FormLabel("子网掩码",25,242,130),mask,FormLabel("默认网关",25,278,130),gw,
                    FormLabel("DNS 分配方式",25,314,130),dnsMode,FormLabel("首选 DNS",25,351,130),dns1,FormLabel("备用 DNS",25,387,130),dns2,info,
                    FormLabel("网卡 MAC",25,435,130),mac,apply,setmac,resetmac,restore,refresh,result});
                load();f.ShowDialog(this);
            }
        }
        private void RdpSettingsDialog()
        {
            using(Form f=NewDialog("远程桌面端口管理",640,490))
            {
                Label state=FormLabel(SystemOps.RdpStatus(),22,20,585);state.Height=103;
                TextBox port=FormText(SystemOps.RdpPort().ToString(),175,144,185);
                RichTextBox output=ReadBox();output.Dock=DockStyle.None;output.SetBounds(22,280,585,180);
                Button apply=null; apply=FormButton("修改端口",380,139,190,delegate {
                    int n;if(!Int32.TryParse(port.Text,out n)||n<1||n>65535){MessageBox.Show(f,"端口必须在 1～65535 范围内（不含 0）。");return;}
                    if(!RequireAdmin()||!ConfirmAction("RDP 端口修改","原端口："+SystemOps.RdpPort()+"\r\n新端口："+n+
                        (n<1024?"\r\n提醒：这是知名端口范围（1～1023），请特别核对系统服务冲突。":"")+"\r\n不会自动修改防火墙，也不会强制重启远程桌面服务。\r\n如果当前通过 RDP 连接，改动可能在重启后使你无法连接！"))return;
                    try{output.Text=SystemOps.SetRdpPort(n);state.Text=SystemOps.RdpStatus();}catch(Exception ex){output.Text="失败："+ex.Message;}
                });
                Button back=FormButton("恢复默认 3389",175,189,185,delegate {port.Text="3389";apply.PerformClick();});
                Button settings=FormButton("打开远程桌面设置",380,189,190,delegate{CommandService.OpenTool("ms-settings:remotedesktop","");});
                Button firewall=FormButton("为当前端口添加防火墙规则",22,190,335,delegate {
                    int n;if(!Int32.TryParse(port.Text,out n)||n<1||n>65535){MessageBox.Show(f,"请输入合法端口（1～65535）。");return;}
                    if(!RequireAdmin()||!ConfirmAction("新增 RDP 入站规则","即将针对 TCP 和 UDP 端口 "+n+" 新增仅用于专用网络/域网络的入站允许规则。\r\n不会修改公用网络规则；请确认此电脑允许远程桌面连接。"))return;
                    try {output.Text=SystemOps.AddRdpFirewallRules(n);} catch(Exception ex) {output.Text="失败："+ex.Message;}
                });
                back.Top=227;settings.Top=227;
                f.Controls.AddRange(new Control[]{state,FormLabel("RDP 新端口",22,144,145),port,apply,back,settings,firewall,output});f.ShowDialog(this);
            }
        }
        private void UpdateSettingsDialog()
        {
            using(Form f=NewDialog("Windows 更新管理",700,550))
            {
                RichTextBox output=ReadBox();output.Dock=DockStyle.None;output.SetBounds(20,115,655,400);
                Button check=FormButton("检查更新状态",20,18,155,delegate {Button b=new Button();DoJob(output,b,SystemOps.UpdateStatus);});
                Button disable=FormButton("禁用更新服务",188,18,155,delegate {
                    if(!RequireAdmin()||!ConfirmAction("停止 Windows Update","停止并禁用 wuauserv 服务，可能影响安全补丁及组织合规。系统维护可能恢复该服务；保存原启动类型以备还原。"))return;
                    Button b=new Button();DoJob(output,b,delegate{return SystemOps.ToggleUpdates(true);});
                });
                Button restore=FormButton("恢复更新服务",356,18,155,delegate {
                    if(!RequireAdmin()||!ConfirmAction("恢复 Windows Update","根据首次禁用前备份的启动类型恢复 wuauserv；其他更新组件或组策略可能仍需检查。"))return;
                    Button b=new Button();DoJob(output,b,delegate{return SystemOps.ToggleUpdates(false);});
                });
                Button settings=FormButton("系统更新设置",20,62,155,delegate {CommandService.OpenTool("ms-settings:windowsupdate","");});
                f.Controls.AddRange(new Control[]{check,disable,restore,settings,output});check.PerformClick();f.ShowDialog(this);
            }
        }
        private void SecuritySettingsDialog()
        {
            using (Form f=NewDialog("Windows 安全防护与组件检测",800,610))
            {
                Label title=new Label {Left=22,Top=14,Width=748,Height=34,Text="Windows 安全防护诊断",Font=new Font("DengXian",15F,FontStyle.Bold),ForeColor=Navy};
                Label note=new Label {Left=23,Top=53,Width=742,Height=65,
                    Text="兼容安全中心组件不完整、Defender 接口不可用及第三方杀毒软件环境。查询不到不等于防护已关闭。\r\n"+
                    "关闭防护会增加风险；不卸载、修补或删除系统组件，不绕过组织策略或篡改防护。",
                    ForeColor=Color.FromArgb(160,75,55)};
                RichTextBox output=ReadBox();output.Dock=DockStyle.None;output.SetBounds(23,198,752,385);
                Button check=FormButton("检测组件与实际状态",23,132,204,delegate {
                    Button b=new Button();DoJob(output,b,SystemOps.SecurityStatus);
                });
                Button disable=FormButton("尝试临时关闭防护",240,132,210,delegate {
                    if(!RequireAdmin()||!ConfirmAction("安全防护操作确认",
                        "将对当前系统可用且可备份的 Defender 与 Windows 防火墙项目提交关闭请求。\r\n"+
                        "未安装/不可管理的组件将跳过，不会篡改组件或绕过保护。\r\n"+
                        "关闭防护可能导致网络和电脑受到攻击，维护完成请尽快恢复。"))return;
                    Button b=new Button();DoJob(output,b,delegate {return SystemOps.SecurityChange(true);});
                });
                Button restore=FormButton("按原始备份恢复",464,132,184,delegate {
                    if(!RequireAdmin()||!ConfirmAction("恢复安全配置",
                        "将尝试按首次操作前的备份恢复可管理的保护项目。\r\n"+
                        "如果不存在备份，将不会猜测原状态或批量覆盖。"))return;
                    Button b=new Button();DoJob(output,b,delegate {return SystemOps.SecurityChange(false);});
                });
                Button center=FormButton("Windows 安全设置",23,169,204,delegate {
                    CommandService.OpenTool("ms-settings:windowsdefender", "");
                });
                Label tip=new Label {Left=239,Top=169,Width=520,Height=27,
                    Text="检查结果会显示：可用 / 不可用 / 状态未知，不会用退出代码判断是否关闭。",
                    ForeColor=Color.FromArgb(96,108,126)};
                f.Controls.AddRange(new Control[]{title,note,check,disable,restore,center,tip,output});
                f.Shown+=delegate {check.PerformClick();};
                f.ShowDialog(this);
            }
        }
        private void IconCacheDialog()
        {
            using(Form f=NewDialog("Windows 图标缓存修复",690,460))
            {
                Label desc=new Label {Left=22,Top=20,Width=630,Height=80,Text="桌面图标变白、快捷方式图标异常时，先尝试刷新图标。\r\n完整重建会结束 Explorer 进程、清理本用户图标缓存并重新启动 Explorer，文件管理器窗口可能被关闭。",ForeColor=Navy};
                RichTextBox output=ReadBox();output.Dock=DockStyle.None;output.SetBounds(22,161,632,260);
                Button refresh=FormButton("轻量刷新图标",22,111,170,delegate {SHChangeNotify(0x08000000,0,IntPtr.Zero,IntPtr.Zero);output.Text="已发送 Windows 图标关联刷新通知。若仍异常，尝试重建图标缓存。";});
                Button rebuild=FormButton("重建图标缓存",208,111,170,delegate {
                    if(!ConfirmAction("图标缓存重建","即将关闭并重启资源管理器 Explorer；打开的资源管理器窗口可能消失。不会删除用户文档或桌面快捷方式。"))return;
                    Button b=new Button();DoJob(output,b,delegate {
                        string cmd="$ErrorActionPreference='Continue'; Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue; Start-Sleep -Milliseconds 800; "+
                            "$paths=@((Join-Path $env:LOCALAPPDATA 'IconCache.db')); $folder=Join-Path $env:LOCALAPPDATA 'Microsoft\\Windows\\Explorer';"+
                            "if(Test-Path $folder){$paths+=@(Get-ChildItem $folder -Filter 'iconcache*.db' -File -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName)};"+
                            "$deleted=0; foreach($p in $paths){if(Test-Path $p){Remove-Item -LiteralPath $p -Force -ErrorAction SilentlyContinue;if(-not(Test-Path $p)){$deleted++}}};"+
                            "Start-Process explorer.exe; '清理成功文件数：'+$deleted; '请等待图标缓存重建完成。'";
                        return PowerShellService.RunScript(cmd,3);
                    });
                });
                Button explorer=FormButton("重新启动 Explorer",393,111,220,delegate {
                    if(!ConfirmAction("重启资源管理器","将关闭并重新启动 Explorer，打开的文件夹窗口可能消失。"))return;
                    Button b=new Button();DoJob(output,b,delegate {return PowerShellService.RunScript("Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue; Start-Sleep -Milliseconds 600; Start-Process explorer.exe; 'Explorer 已重新启动'",2);});
                });
                f.Controls.AddRange(new Control[]{desc,refresh,rebuild,explorer,output});f.ShowDialog(this);
            }
        }
        private void ScreenTestDialog()
        {
            using(Form choose=NewDialog("选择显示器",510,260))
            {
                ComboBox monitors=new ComboBox{Left=23,Top=25,Width=450,DropDownStyle=ComboBoxStyle.DropDownList};
                for(int i=0;i<Screen.AllScreens.Length;i++)monitors.Items.Add("显示器 "+(i+1)+" · "+Screen.AllScreens[i].DeviceName+" · "+Screen.AllScreens[i].Bounds.Width+"×"+Screen.AllScreens[i].Bounds.Height);
                monitors.SelectedIndex=0;
                Label info=new Label {Left=24,Top=74,Width=458,Height=74,Text="全屏纯色：红、绿、蓝、黄、白、黑、灰。\r\n左键/→：下一种颜色　右键/←：上一种\r\nEsc：立即退出。"};
                Button go=FormButton("开始全屏检测",155,163,200,delegate {
                    int ix=monitors.SelectedIndex;if(ix<0)return;
                    choose.Hide();
                    Screen display=Screen.AllScreens[ix];
                    using(Form test=new Form())
                    {
                        Color[] colors={Color.Red,Color.Lime,Color.Blue,Color.Yellow,Color.White,Color.Black,Color.Gray};
                        int i=0;
                        test.FormBorderStyle=FormBorderStyle.None; test.StartPosition=FormStartPosition.Manual;
                        test.Bounds=display.Bounds; test.TopMost=true; test.KeyPreview=true; test.BackColor=colors[0];
                        test.ShowInTaskbar=false;
                        Action<int> change=delegate(int delta){i=(i+delta+colors.Length)%colors.Length;test.BackColor=colors[i];test.Invalidate();};
                        test.KeyDown+=delegate(object s,KeyEventArgs e){if(e.KeyCode==Keys.Escape)test.Close();else if(e.KeyCode==Keys.Right||e.KeyCode==Keys.Space)change(1);else if(e.KeyCode==Keys.Left)change(-1);};
                        test.MouseClick+=delegate(object s,MouseEventArgs e){if(e.Button==MouseButtons.Left)change(1);else if(e.Button==MouseButtons.Right)change(-1);};
                        test.Shown+=delegate {test.Activate();test.Focus();};
                        test.ShowDialog(this);
                    }
                    choose.Show();
                });
                choose.Controls.AddRange(new Control[]{monitors,info,go});choose.ShowDialog(this);
            }
        }
        private void GenericErrorSearchDialog()
        {
            using(Form f=NewDialog("离线 Windows 错误代码查询",760,590))
            {
                TextBox query=FormText("",126,17,395);
                ListBox list=new ListBox{Left=20,Top=66,Width=710,Height=225};
                RichTextBox detail=ReadBox();detail.Dock=DockStyle.None;detail.SetBounds(20,304,710,253);
                Action refresh=delegate {
                    list.Items.Clear();
                    foreach(GenericErrorInfo x in GenericCodeCatalog.Search(query.Text))list.Items.Add(x);
                    detail.Text="离线通用故障代码："+GenericCodeCatalog.Count+" 条，支持输入代码、名称和类别筛选。\r\n"+
                        "注意：该数据库用于辅助排障，事件码、Windows错误码和蓝屏停止码属于不同命名空间。";
                };
                list.DisplayMember="Text";
                list.SelectedIndexChanged+=delegate{GenericErrorInfo item=list.SelectedItem as GenericErrorInfo;if(item!=null)detail.Text=item.Text;};
                Button search=FormButton("查询",536,15,100,delegate{refresh();});
                Button reload=FormButton("重载数据",20,264,140,delegate{GenericCodeCatalog.Reload();refresh();});
                f.Controls.AddRange(new Control[]{FormLabel("错误码 / 关键词",20,16,105),query,search,list,reload,detail});
                refresh();f.ShowDialog(this);
            }
        }
        private void DefaultAppsDialog()
        {
            DefaultAppsRepairDialog();
        }
    }
}
