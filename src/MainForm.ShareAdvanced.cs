using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ITDesktopToolkit
{
    public sealed partial class MainForm
    {
        private void ShareMoreActionsDialog()
        {
            using(Form f=NewDialog("更多共享管理入口",650,365))
            {
                Label intro=new Label {Left=22,Top=18,Width=610,Height=65,
                    Text="在统一共享列表之外，还可以进入 Windows 原生共享管理、网络配置与功能管理。\r\n这些入口不会自动改变系统安全设置。"};
                Button msc=FormButton("共享文件夹管理",22,95,278,delegate{CommandService.OpenTool("fsmgmt.msc","");});
                Button center=FormButton("网络和共享中心",315,95,285,delegate{CommandService.OpenTool("control.exe","/name Microsoft.NetworkAndSharingCenter");});
                Button advanced=FormButton("Windows 高级共享设置",22,148,278,delegate{CommandService.OpenTool("control.exe","/name Microsoft.NetworkAndSharingCenter /page Advanced");});
                Button rules=FormButton("高级防火墙",315,148,285,delegate{CommandService.OpenTool("wf.msc","");});
                Button optional=FormButton("Windows SMB1 可选组件",22,201,278,delegate{CommandService.OpenTool("optionalfeatures.exe","");});
                Button credential=FormButton("Windows 凭据管理",315,201,285,delegate{CommandService.OpenTool("control.exe","/name Microsoft.CredentialManager");});
                Label warning=new Label {Left=22,Top=260,Width=610,Height=68,ForeColor=Color.FromArgb(140,82,25),
                    Text="谨慎使用 SMB1、来宾访问和关闭强制签名。\r\n域策略环境下，本机设置可能受组织策略限制。"};
                f.Controls.AddRange(new Control[]{intro,msc,center,advanced,rules,optional,credential,warning});
                f.ShowDialog(this);
            }
        }
        private void ShareAdvancedDialog()
        {
            using(Form f=NewDialog("共享文件夹 / SMB 兼容诊断与修复",960,720))
            {
                f.MinimumSize=new Size(780,590);
                TabControl tabs=new TabControl {Dock=DockStyle.Fill};
                TabPage profiles=new TabPage("网络共享开关"), smb=new TabPage("SMB 协议与认证"), diagnostic=new TabPage("共享连接诊断");
                tabs.TabPages.AddRange(new TabPage[]{profiles,smb,diagnostic});

                // Windows advanced sharing UI covers settings with no cross-version public write API.
                // The status report combines firewall profile-specific rules, related services and SMB properties.
                Label profileIntro=new Label {Dock=DockStyle.Top,Height=92,Padding=new Padding(13,11,13,0),
                    Text="【专用网络】网络发现、自动设备设置、文件和打印机共享。\r\n【公用网络】网络发现、文件和打印机共享（通常应关闭）。\r\n【所有网络】公用文件夹共享、密码保护共享、共享加密及其他选项：认证模式请在「共享认证模式」中直接读取与设置，其他选项显示真实检测结果。"};
                FlowLayoutPanel profileActions=new FlowLayoutPanel{Dock=DockStyle.Top,Height=50,Padding=new Padding(9,3,0,0)};
                Button read=null;
                RichTextBox report=ReadBox();report.Dock=DockStyle.Fill;
                read=FormButton("读取全部真实状态",0,0,182,delegate{DoJob(report,read,ShareDiagnosticsService.ReadSecurityStatus);});
                Button openAdvanced=FormButton("高级共享设置",0,0,165,delegate{CommandService.OpenTool("control.exe","/name Microsoft.NetworkAndSharingCenter /page Advanced");});
                Button firewall=FormButton("高级防火墙",0,0,140,delegate{CommandService.OpenTool("wf.msc","");});
                Button services=FormButton("相关服务",0,0,125,delegate{CommandService.OpenTool("services.msc","");});
                Button network=FormButton("网络属性",0,0,125,delegate{CommandService.OpenTool("ms-settings:network-advancedsettings","");});
                profileActions.Controls.AddRange(new Control[]{read,openAdvanced,firewall,services,network});
                profiles.Controls.Add(report);profiles.Controls.Add(profileActions);profiles.Controls.Add(profileIntro);
                Panel networkRow=new Panel{Dock=DockStyle.Top,Height=92,BackColor=Color.White};
                ComboBox networks=new ComboBox{Left=14,Top=12,Width=405,DropDownStyle=ComboBoxStyle.DropDownList};
                Button reloadProfile=FormButton("读取网卡类别",430,10,144,delegate{});
                Button privateProfile=FormButton("设为专用",584,10,122,delegate{});
                Button publicProfile=FormButton("设为公用",718,10,122,delegate{});
                Button restoreProfile=FormButton("恢复网络原类别",14,52,185,delegate{});
                Label profileCaution=new Label{Left=208,Top=54,Width=675,Height=31,ForeColor=Color.FromArgb(140,82,25),
                    Text="类别切换会改变防火墙规则范围，远程维护时可能失联；域认证网络禁止修改。"};
                reloadProfile.Click+=delegate {
                    NetworkSharingProfileService.Profile old=networks.SelectedItem as NetworkSharingProfileService.Profile;
                    int id=old==null?-1:old.Index;
                    networks.Items.Clear();
                    try {string msg;
                        foreach(NetworkSharingProfileService.Profile x in NetworkSharingProfileService.Enumerate(out msg))networks.Items.Add(x);
                        if(networks.Items.Count>0){int selected=0;for(int j=0;j<networks.Items.Count;j++)
                            if(((NetworkSharingProfileService.Profile)networks.Items[j]).Index==id)selected=j;
                            networks.SelectedIndex=selected;}
                        report.Text=msg;
                    }catch(Exception ex){report.Text="网卡读取失败："+ex.Message;}
                };
                Action<string,bool> applyProfile=async delegate(string category,bool restoring) {
                    NetworkSharingProfileService.Profile p=networks.SelectedItem as NetworkSharingProfileService.Profile;
                    if(p==null)return;
                    if(!RequireAdmin()||!ConfirmAction("更改网络配置文件",
                        "网卡："+p.Interface+"\r\n当前类别："+p.Category+"\r\n"+
                        (restoring?"恢复原始类别":"设置为："+category)+
                        "\r\n可能影响防火墙开放范围和远程连接。域认证设备不做修改。"))return;
                    privateProfile.Enabled=publicProfile.Enabled=restoreProfile.Enabled=false;
                    try {
                        string jobReport=await Task.Run(delegate{return NetworkSharingProfileService.SetProfile(p,category,restoring);});
                        reloadProfile.PerformClick();
                        report.Text=jobReport+"\r\n\r\n"+report.Text;
                    }
                    catch(Exception ex){report.Text="修改失败："+ex.Message;}
                    finally{if(!privateProfile.IsDisposed)privateProfile.Enabled=publicProfile.Enabled=restoreProfile.Enabled=true;}
                };
                privateProfile.Click+=delegate{applyProfile("Private",false);};
                publicProfile.Click+=delegate{applyProfile("Public",false);};
                restoreProfile.Click+=delegate{applyProfile("Private",true);};
                networkRow.Controls.AddRange(new Control[]{networks,reloadProfile,privateProfile,publicProfile,restoreProfile,profileCaution});
                // Share discovery/firewall controls: the selected profile is explicit and state is verified.
                Panel directShare=new Panel{Dock=DockStyle.Top,Height=113,BackColor=Color.FromArgb(245,248,251)};
                Label directLabel=new Label{Left=15,Top=8,Width=850,Height=25,Text="网络共享开关（专用 / 公用）：读取并调整 Windows 内置网络发现、文件和打印机共享防火墙规则"};
                ComboBox fwProfile=new ComboBox{Left=15,Top=38,Width=112,DropDownStyle=ComboBoxStyle.DropDownList};
                fwProfile.Items.AddRange(new object[]{"专用网络","公用网络"});fwProfile.SelectedIndex=0;
                ComboBox fwKind=new ComboBox{Left=136,Top=38,Width=214,DropDownStyle=ComboBoxStyle.DropDownList};
                fwKind.Items.AddRange(new object[]{"网络发现","文件和打印机共享"});fwKind.SelectedIndex=0;
                Button enableRule=null,disableRule=null,restoreRule=null,refreshRule=null;
                enableRule=FormButton("开启",362,37,94,delegate {
                    if(!RequireAdmin()||!ConfirmAction("开启网络共享防火墙规则","仅修改"+fwProfile.Text+"的"+fwKind.Text+"相关内置规则，可能增加网络可发现性。"))return;
                    string prof=fwProfile.SelectedIndex==0?"Private":"Public", kind=fwKind.SelectedIndex==0?"Discovery":"Files";
                    DoJob(report,enableRule,delegate{return NetworkSharingOptionsService.SetFirewall(prof,kind,true,false);});
                });
                disableRule=FormButton("关闭",461,37,94,delegate {
                    if(!RequireAdmin()||!ConfirmAction("关闭网络共享防火墙规则","仅修改"+fwProfile.Text+"的"+fwKind.Text+"相关内置规则，可能中断远程共享访问。"))return;
                    string prof=fwProfile.SelectedIndex==0?"Private":"Public", kind=fwKind.SelectedIndex==0?"Discovery":"Files";
                    DoJob(report,disableRule,delegate{return NetworkSharingOptionsService.SetFirewall(prof,kind,false,false);});
                });
                restoreRule=FormButton("恢复原值",561,37,123,delegate {
                    if(!RequireAdmin()||!ConfirmAction("恢复共享防火墙规则","按首次备份恢复当前选定项目各条规则的原始状态。"))return;
                    string prof=fwProfile.SelectedIndex==0?"Private":"Public", kind=fwKind.SelectedIndex==0?"Discovery":"Files";
                    DoJob(report,restoreRule,delegate{return NetworkSharingOptionsService.SetFirewall(prof,kind,false,true);});
                });
                refreshRule=FormButton("读取全部开关状态",692,37,172,delegate {DoJob(report,refreshRule,NetworkSharingOptionsService.ReadOptions);});
                Label fwNote=new Label{Left=15,Top=80,Width=875,Height=26,ForeColor=Color.FromArgb(140,82,25),
                    Text="规则修改仅影响本机防火墙，不等于更改共享和 NTFS 权限；某些规则横跨多种配置文件，实际应以读取结果为准。"};
                directShare.Controls.AddRange(new Control[]{directLabel,fwProfile,fwKind,enableRule,disableRule,restoreRule,refreshRule,fwNote});
                // Dock top: the last added top-docked panel is closest to the tab's top edge.
                profiles.Controls.Add(networkRow);
                profiles.Controls.Add(directShare);

                Label smbIntro=new Label {Dock=DockStyle.Top,Height=107,Padding=new Padding(13,11,13,0),
                    Text="优先使用 SMB 2/3 和具备密码的用户身份验证。以下各项是不同的 Windows SMB 配置，不能等同于『密码保护共享』。\r\n修改前保存首次原值；管理员权限下需二次确认；恢复按钮按原值还原。\r\nSMB1 风险较高，本工具只查询并提供 Windows 功能入口，不自动开启。",ForeColor=Color.FromArgb(124,71,25)};
                Panel settings=new Panel{Dock=DockStyle.Top,Height=147,BackColor=Color.White};
                Label nameLabel=FormLabel("SMB 设置项",20,18,110);
                ComboBox options=new ComboBox{Left=134,Top=18,Width=338,DropDownStyle=ComboBoxStyle.DropDownList};
                options.Items.Add("客户端：强制 SMB 签名");
                options.Items.Add("客户端：允许不安全 Guest 登录");
                options.Items.Add("服务端：强制 SMB 签名");
                options.Items.Add("服务端：认证用户共享属性");
                options.SelectedIndex=0;
                ComboBox value=new ComboBox{Left=580,Top=18,Width=170,DropDownStyle=ComboBoxStyle.DropDownList};
                value.Items.AddRange(new object[]{"开启 (True)","关闭 (False)"});value.SelectedIndex=0;
                RichTextBox smbReport=ReadBox();smbReport.Dock=DockStyle.Fill;
                Button apply=null,restore=null;
                apply=FormButton("应用所选设置",134,68,160,async delegate {
                    string[] keys={"ClientSignature","ClientGuest","ServerSignature","ServerAuthenticated"};
                    string key=keys[options.SelectedIndex];bool enabled=value.SelectedIndex==0;
                    bool risky=(key=="ClientGuest"&&enabled)||(key=="ClientSignature"&&!enabled)||(key=="ServerSignature"&&!enabled)||
                        (key=="ServerAuthenticated"&&!enabled);
                    string caution=risky?"\r\n【安全风险】该选项可能降低 SMB 安全性，只应在可信局域网临时排障时使用。":"";
                    if(!RequireAdmin() || !ConfirmAction("更改 SMB 安全属性","设置："+options.Text+"\r\n新值："+(enabled?"开启":"关闭")+caution+
                        "\r\n影响系统全局 SMB 行为，可能中断现有共享连接；首次修改前会保存原值。"))return;
                    apply.Enabled=false;
                    try{smbReport.Text=await Task.Run(delegate{return ShareDiagnosticsService.ChangeOption(key,enabled,false);});}
                    catch(Exception ex){smbReport.Text="修改失败："+ex.Message;}
                    finally{if(!apply.IsDisposed)apply.Enabled=true;}
                });
                restore=FormButton("恢复首次记录原值",309,68,183,async delegate {
                    string[] keys={"ClientSignature","ClientGuest","ServerSignature","ServerAuthenticated"};
                    string key=keys[options.SelectedIndex];
                    if(!RequireAdmin() || !ConfirmAction("恢复 SMB 原值","恢复【"+options.Text+"】的首次记录配置。\r\n只有存在有效备份才会执行。"))return;
                    restore.Enabled=false;
                    try{smbReport.Text=await Task.Run(delegate{return ShareDiagnosticsService.ChangeOption(key,false,true);});}
                    catch(Exception ex){smbReport.Text="恢复失败："+ex.Message;}
                    finally{if(!restore.IsDisposed)restore.Enabled=true;}
                });
                Button inspect=FormButton("刷新 SMB 状态",508,68,173,delegate{Button tmp=new Button();DoJob(smbReport,tmp,ShareDiagnosticsService.ReadSecurityStatus);});
                Button feature=FormButton("Windows SMB1 功能",690,68,202,delegate{CommandService.OpenTool("optionalfeatures.exe","");});
                Label note=new Label{Left=20,Top=111,Width=890,Height=29,ForeColor=Color.FromArgb(140,82,25),
                    Text="提示：签名是消息完整性保护，Guest 是匿名会话兼容设置；不要将这两项等同于禁用密码验证。"};
                settings.Controls.AddRange(new Control[]{nameLabel,options,value,apply,restore,inspect,feature,note});
                smb.Controls.Add(smbReport);smb.Controls.Add(settings);smb.Controls.Add(smbIntro);
                Panel modeBar=new Panel{Dock=DockStyle.Top,Height=53};
                Button modes=FormButton("共享模式：账号 / 免账号 / 恢复",13,7,284,delegate{ShareAccessModeDialog();});
                Button signOff=null,signOn=null,signRestore=null;
                signOff=FormButton("关闭客户端强制签名",303,7,200,delegate{
                    if(!RequireAdmin()||!ConfirmAction("关闭 SMB 客户端强制签名",
                        "对应 Set-SmbClientConfiguration -RequireSecuritySignature $false。\r\n此更改为本机全局设置，降低 SMB 完整性防护；仅在可信环境排障时使用。"))return;
                    DoJob(smbReport,signOff,delegate{return ShareDiagnosticsService.ChangeOption("ClientSignature",false,false);});
                });
                signOn=FormButton("开启客户端强制签名",510,7,198,delegate{
                    if(!RequireAdmin()||!ConfirmAction("开启 SMB 客户端强制签名",
                        "对应 Set-SmbClientConfiguration -RequireSecuritySignature $true；旧设备可能不支持。"))return;
                    DoJob(smbReport,signOn,delegate{return ShareDiagnosticsService.ChangeOption("ClientSignature",true,false);});
                });
                signRestore=FormButton("恢复签名原值",716,7,181,delegate{
                    if(!RequireAdmin()||!ConfirmAction("恢复客户端强制签名原值","只恢复首次备份的 ClientSignature 设置。"))return;
                    DoJob(smbReport,signRestore,delegate{return ShareDiagnosticsService.ChangeOption("ClientSignature",false,true);});
                });
                modeBar.Controls.AddRange(new Control[]{modes,signOff,signOn,signRestore});
                smb.Controls.Add(modeBar);

                Label targetInfo=new Label {Dock=DockStyle.Top,Height=72,Padding=new Padding(15,13,0,0),
                    Text="输入主机名、IP 地址或 UNC 路径，如 \\\\192.168.1.100 或 \\\\192.168.1.100\\share。\r\n自动检测 DNS/IP 解析、TCP 445、SMB 客户端及活动连接；不直接修改远端共享。"};
                Panel targetActions=new Panel{Dock=DockStyle.Top,Height=93};
                TextBox target=FormText(@"\\192.168.1.100",22,14,420);
                RichTextBox diagnosticReport=ReadBox();diagnosticReport.Dock=DockStyle.Fill;
                Button test=null;
                test=FormButton("诊断 SMB 连接",464,12,183,delegate {
                    string host=target.Text.Trim();
                    DoJob(diagnosticReport,test,delegate{return ShareDiagnosticsService.Diagnose(host);});
                });
                Button explorer=FormButton("资源管理器访问",663,12,217,delegate {
                    if(target.Text.Trim().StartsWith(@"\\"))CommandService.OpenTool("explorer.exe","\""+target.Text.Trim().Replace("\"","")+"\"");
                    else MessageBox.Show(f,"请输入以两个反斜杠开头的 UNC 路径（如 \\\\192.168.1.100）。");
                });
                Label diagnosticNote=new Label{Left=23,Top=55,Width=855,Height=35,Text="诊断结果无法单靠 0x80004005 判定原因；请结合对端权限、凭据、组策略和 SMB 日志。"};
                targetActions.Controls.AddRange(new Control[]{target,test,explorer,diagnosticNote});
                diagnostic.Controls.Add(diagnosticReport);diagnostic.Controls.Add(targetActions);diagnostic.Controls.Add(targetInfo);

                f.Controls.Add(tabs);
                f.Shown+=delegate{reloadProfile.PerformClick();read.PerformClick();};
                f.ShowDialog(this);
            }
        }
    }
}
