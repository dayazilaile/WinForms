using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ITDesktopToolkit
{
    public sealed partial class MainForm
    {
        private void Win11MutualShareDialog()
        {
            using(Form f=NewDialog("Windows 11 · 两台电脑免密码互访",1000,755))
            {
                f.MinimumSize=new Size(900,690);
                Label intro=new Label {Dock=DockStyle.Top,Height=89,Padding=new Padding(18,12,18,0),
                    ForeColor=Color.FromArgb(144,79,29),
                    Text="使用方法：分别在两台 Win11 上执行第①步，再分别为要开放的文件夹执行第②步；之后用 \\\\对方IP\\共享名 访问。\r\n"+
                    "来宾免账号模式会影响本机 SMB 全局安全策略，降低 SMB 签名保护。只适用于你能控制的可信、专用局域网；禁止开放系统盘根目录。\r\n"+
                    "不会自动开启 SMB1、不会关闭整个防火墙，也不会修改对方电脑。默认只共享指定文件夹的读取权限。"};
                TabControl tab=new TabControl {Dock=DockStyle.Fill};
                TabPage step1=new TabPage("① 本机访客兼容设置"), step2=new TabPage("② 共享与安全（NTFS）"),step3=new TabPage("③ 诊断与连接测试");
                tab.TabPages.AddRange(new TabPage[]{step1,step2,step3});

                Label step1info=new Label{Dock=DockStyle.Top,Height=110,Padding=new Padding(16,12,16,0),
                    Text="这一步既配置当前电脑访问别人共享的 SMB 客户端，也配置别人访问本机的 SMB 服务端。\r\n"+
                    "具体涉及：客户端 Guest 许可、两侧签名要求、服务端本地账户网络登录映射、Guest 本地账户，以及仅专用网络同子网的 TCP445 入站规则。\r\n"+
                    "请先确保相关局域网已设为“专用网络”，并确认电脑未加入单位域。改变这些全局属性可能影响其他共享。"};
                FlowLayoutPanel actions1=new FlowLayoutPanel{Dock=DockStyle.Top,Height=58,Padding=new Padding(12,4,0,0)};
                RichTextBox report1=ReadBox();report1.Dock=DockStyle.Fill;
                Button setup=null,rollback=null,read1=null,precheck=null;
                setup=FormButton("配置本机双向来宾模式",0,0,255,async delegate{
                    if(!RequireAdmin()||!ConfirmAction("启用本机 Windows 11 SMB 来宾互访（高风险）",
                        "这会在本机允许未经身份验证的 Guest SMB 登录、取消客户端/服务端强制签名、启用 Guest 账户并设置来宾映射，"+
                        "还将添加仅专用网络、同一子网、TCP 445 的入站允许规则。\r\n"+
                        "存在中间人攻击、勒索软件传播和未经授权读取文件的风险。\r\n"+
                        "仅在隔离或可信的专用局域网使用，确认继续？"))return;
                    setup.Enabled=false;report1.Text="正在逐项配置并回读系统状态……";
                    try{report1.Text=await Task.Run(new Func<string>(Win11MutualShareService.ConfigureThisPc));}
                    catch(Exception ex){report1.Text="操作失败："+ex.Message;}
                    finally{if(!setup.IsDisposed)setup.Enabled=true;}
                });
                rollback=FormButton("恢复本机之前的设置",0,0,238,async delegate{
                    if(!RequireAdmin()||!ConfirmAction("恢复来宾共享相关系统配置",
                        "将尝试恢复客户端/服务端签名、Guest 许可和账户状态、ForceGuest，以及删除本工具专用防火墙规则。\r\n"+
                        "对选定文件夹的权限恢复是第②步独立操作；已有 SMB 会话可能中断。"))return;
                    rollback.Enabled=false;
                    try{report1.Text=await Task.Run(new Func<string>(Win11MutualShareService.RestoreThisPc));}
                    catch(Exception ex){report1.Text="操作失败："+ex.Message;}
                    finally{if(!rollback.IsDisposed)rollback.Enabled=true;}
                });
                read1=FormButton("刷新本机状态",0,0,175,delegate{DoJob(report1,read1,delegate{return Win11MutualShareService.ReadStatus("","","");});});
                precheck=FormButton("执行预检（只读）",0,0,164,delegate {
                    DoJob(report1,precheck,delegate{return Win11MutualShareService.PreflightReport();});
                });
                actions1.Controls.AddRange(new Control[]{setup,rollback,read1,precheck});
                step1.Controls.Add(report1);step1.Controls.Add(actions1);step1.Controls.Add(step1info);

                Panel folderTop=new Panel{Dock=DockStyle.Top,Height=203};
                Label info2=new Label{Left=15,Top=9,Width=930,Height=54,
                    Text="只针对一个你指定的共享文件夹：同时设置 SMB 共享权限与【属性→安全】NTFS 读取权限（Everyone）。\r\n"+
                    "修改之前保存原 NTFS ACL 与共享 Everyone 权限，可恢复；不会给整个磁盘开放权限，也不会直接启用读写。"};
                TextBox folder=FormText("",149,79,610);
                TextBox shareName=FormText("SharedFiles",149,120,290);
                Button browse=FormButton("选择文件夹",771,77,168,delegate{using(FolderBrowserDialog d=new FolderBrowserDialog())
                    if(d.ShowDialog(f)==DialogResult.OK)folder.Text=d.SelectedPath;});
                Label info2b=new Label{Left=15,Top=164,Width=940,Height=39,ForeColor=Color.FromArgb(144,79,29),
                    Text="目录建议使用 D:\\PublicShare 这样的独立文件夹。不要选桌面、用户主目录、Windows、Program Files 或磁盘根目录。"};
                folderTop.Controls.AddRange(new Control[]{info2,FormLabel("本机共享目录",15,80,130),folder,
                    FormLabel("共享名称",15,122,130),shareName,browse,info2b});
                FlowLayoutPanel actions2=new FlowLayoutPanel{Dock=DockStyle.Top,Height=58,Padding=new Padding(12,5,0,0)};
                RichTextBox report2=ReadBox();report2.Dock=DockStyle.Fill;
                Button grant=null,restoreFolder=null,inspectFolder=null;
                grant=FormButton("创建/开放此文件夹（只读）",0,0,276,async delegate {
                    string error=Win11MutualShareService.ValidateFolder(folder.Text.Trim(),shareName.Text.Trim());
                    if(error.Length>0){MessageBox.Show(f,error,"目录验证未通过",MessageBoxButtons.OK,MessageBoxIcon.Warning);return;}
                    if(!RequireAdmin()||!ConfirmAction("授予指定目录免账号读取权限",
                        "目录："+folder.Text.Trim()+"\r\n共享名："+shareName.Text.Trim()+"\r\n"+
                        "这会在共享层和 NTFS 安全 ACL 中授予 Everyone 读取权限（适用于本机其他账户，也可能允许来宾）。\r\n"+
                        "修改前保存初始权限，现有的显式拒绝 ACE 不会被覆盖。\r\n只确认你希望公开的无敏感文件目录。"))return;
                    string fp=folder.Text.Trim(),sn=shareName.Text.Trim();grant.Enabled=false;
                    try{report2.Text=await Task.Run(delegate{return Win11MutualShareService.PreparePublicFolder(fp,sn);});}
                    catch(Exception ex){report2.Text="配置失败："+ex.Message;}
                    finally{if(!grant.IsDisposed)grant.Enabled=true;}
                });
                restoreFolder=FormButton("恢复此目录原权限",0,0,207,async delegate {
                    string error=Win11MutualShareService.ValidateFolder(folder.Text.Trim(),shareName.Text.Trim());
                    if(error.Length>0){MessageBox.Show(f,error);return;}
                    if(!RequireAdmin()||!ConfirmAction("恢复所选目录共享与 NTFS 权限",
                        "仅恢复工具箱首次修改时备份的目录和共享权限。\r\n注意：将覆盖备份之后对该文件夹 ACL 的其他更改；可能移除工具新建的共享。"))return;
                    string fp=folder.Text.Trim(),sn=shareName.Text.Trim();restoreFolder.Enabled=false;
                    try{report2.Text=await Task.Run(delegate{return Win11MutualShareService.RestorePublicFolder(fp,sn);});}
                    catch(Exception ex){report2.Text="恢复失败："+ex.Message;}
                    finally{if(!restoreFolder.IsDisposed)restoreFolder.Enabled=true;}
                });
                inspectFolder=FormButton("读取共享及安全权限",0,0,203,delegate {
                    string fp=folder.Text.Trim(),sn=shareName.Text.Trim();
                    DoJob(report2,inspectFolder,delegate{return Win11MutualShareService.ReadStatus(fp,sn,"");});
                });
                actions2.Controls.AddRange(new Control[]{grant,restoreFolder,inspectFolder});
                step2.Controls.Add(report2);step2.Controls.Add(actions2);step2.Controls.Add(folderTop);

                Panel diagTop=new Panel {Dock=DockStyle.Top,Height=140};
                Label info3=new Label{Left=18,Top=11,Width=920,Height=54,
                    Text="配置完成后，在电脑 A 输入电脑 B 的 IP，检查 TCP 445，然后打开 \\\\B的IP\\共享名。\r\n"+
                    "电脑 B 也需配置同样的两步。若继续弹用户名和密码，优先检查已有会话/凭据缓存、对端 Guest 权限和 SMBClient 安全事件。"};
                TextBox peer=FormText("192.168.137.1",155,82,270);
                Label peerLabel=FormLabel("另一台电脑 IP",18,82,140);
                diagTop.Controls.AddRange(new Control[]{info3,peer,peerLabel});
                FlowLayoutPanel actions3=new FlowLayoutPanel {Dock=DockStyle.Top,Height=60,Padding=new Padding(12,3,0,0)};
                RichTextBox report3=ReadBox();report3.Dock=DockStyle.Fill;
                Button test=null;
                test=FormButton("诊断本机及另一台电脑",0,0,277,delegate {
                    string host=peer.Text.Trim(),fp=folder.Text.Trim(),sn=shareName.Text.Trim();
                    DoJob(report3,test,delegate{return Win11MutualShareService.ReadStatus(fp,sn,host);});
                });
                Button visit=FormButton("用资源管理器访问对端共享",0,0,283,delegate {
                    string host=peer.Text.Trim(),sn=shareName.Text.Trim();
                    if(!System.Text.RegularExpressions.Regex.IsMatch(host,@"^[a-zA-Z0-9_.-]{1,253}$") ||
                       !System.Text.RegularExpressions.Regex.IsMatch(sn,@"^[\p{L}\p{N}_ .-]{1,50}$")){
                       MessageBox.Show(f,"请填写有效的目标 IP/计算机名及共享名。","无效路径");return;}
                    CommandService.OpenTool("explorer.exe","\"\\\\"+host+"\\"+sn+"\"");
                });
                Button disconnect=null;
                disconnect=FormButton("断开对端旧 SMB 映射",0,0,223,async delegate {
                    string host=peer.Text.Trim();
                    if(!System.Text.RegularExpressions.Regex.IsMatch(host,@"^[a-zA-Z0-9_.-]{1,253}$")){
                        MessageBox.Show(f,"请输入正确的对端 IP 或计算机名。","输入错误");return;}
                    if(!ConfirmAction("断开对端的旧共享映射",
                        "仅尝试清理当前登录会话指向 "+host+" 的 SMB 映射。可能关闭正在访问的文件；不会删除凭据管理器中的账号密码。\r\n是否继续？"))return;
                    disconnect.Enabled=false;
                    try{report3.Text=await Task.Run(delegate{return Win11MutualShareService.DisconnectPeer(host);});}
                    catch(Exception ex){report3.Text="断开失败："+ex.Message;}
                    finally{if(!disconnect.IsDisposed)disconnect.Enabled=true;}
                });
                actions3.Controls.AddRange(new Control[]{test,visit,disconnect});
                step3.Controls.Add(report3);step3.Controls.Add(actions3);step3.Controls.Add(diagTop);

                f.Controls.Add(tab);f.Controls.Add(intro);
                f.Shown+=delegate{read1.PerformClick();};
                f.ShowDialog(this);
            }
        }
    }
}
