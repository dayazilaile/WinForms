using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace WinForms
{
    public sealed partial class MainForm
    {
        private void SharePermissionsDialog(RichTextBox mainOutput)
        {
            using(Form f=NewDialog("已有共享：权限管理",850,680))
            {
                Label chooseTitle=FormLabel("选择已有共享",20,19,145);
                ComboBox list=new ComboBox {Left=168,Top=20,Width=490,DropDownStyle=ComboBoxStyle.DropDownList};
                Button refresh=FormButton("刷新共享",672,16,143,delegate {});
                Label path=new Label {Left=20,Top=65,Width=798,Height=30,Text="共享目录：待选择",ForeColor=Navy};
                Label subjectLabel=FormLabel("授权对象",20,111,140);
                ComboBox subject=new ComboBox {Left=168,Top=111,Width=215,DropDownStyle=ComboBoxStyle.DropDownList};
                subject.Items.Add("Everyone（所有用户）");subject.Items.Add("自定义账户 / 用户组");subject.SelectedIndex=0;
                TextBox custom=FormText("",395,113,420);custom.Enabled=false;
                Label permTitle=FormLabel("共享访问权限",20,159,143);
                ComboBox access=new ComboBox {Left=168,Top=160,Width=215,DropDownStyle=ComboBoxStyle.DropDownList};
                access.Items.Add("只读（Read）");access.Items.Add("读写（Change）");access.Items.Add("完全控制（Full）");access.SelectedIndex=0;
                Label warning=new Label {Left=20,Top=207,Width=790,Height=70,ForeColor=Color.FromArgb(145,85,12),
                    Text="共享权限与 NTFS 文件夹权限是两套独立规则。Everyone 不等于匿名访问；只读/读写仅修改 SMB 共享权限，不会自动提升 NTFS 权限。更改可能中断正在访问的用户。"};
                RichTextBox report=ReadBox();report.Dock=DockStyle.None;report.SetBounds(20,357,795,290);
                Button grant=FormButton("添加 / 更改权限",168,289,195,delegate {});
                Button revoke=FormButton("移除此主体授权",375,289,200,delegate {});
                Button reload=FormButton("读取当前权限",590,289,205,delegate {});
                Button ntfsGrant=FormButton("同步设置 NTFS 权限",168,329,220,delegate{});
                Button ntfsRestore=FormButton("恢复此文件夹 NTFS 原权限",405,329,230,delegate{});
                Label ntfsNote=new Label{Left=20,Top=389,Width=775,Height=30,
                    Text="NTFS修改前备份首次 ACL；仅修改所选普通共享文件夹，需确认，不会批量修改整个磁盘。",ForeColor=Color.FromArgb(145,85,12)};
                report.SetBounds(20,429,795,218);
                f.Controls.AddRange(new Control[]{chooseTitle,list,refresh,path,subjectLabel,subject,custom,permTitle,access,warning,grant,revoke,reload,ntfsGrant,ntfsRestore,ntfsNote,report});
                subject.SelectedIndexChanged+=delegate {
                    custom.Enabled=subject.SelectedIndex!=0;
                    ShareService.ShareAccountEntry selectedAccount=subject.SelectedItem as ShareService.ShareAccountEntry;
                    if(selectedAccount!=null) {
                        custom.Text=selectedAccount.Name;
                        if(selectedAccount.Rights=="Read")access.SelectedIndex=0;
                        else if(selectedAccount.Rights=="Change")access.SelectedIndex=1;
                        else if(selectedAccount.Rights=="Full")access.SelectedIndex=2;
                    }
                };
                Func<string> account=delegate {return subject.SelectedIndex==0?"Everyone":custom.Text.Trim();};
                Action readRights=delegate {
                    ShareService.ShareEntry selected=list.SelectedItem as ShareService.ShareEntry;
                    if(selected==null)return;
                    path.Text="共享目录："+selected.Folder;
                    try {
                        report.Text=ShareService.ReadPermissions(selected.Name);
                        subject.Items.Clear();
                        subject.Items.Add("Everyone（所有用户）");subject.Items.Add("自定义账户 / 用户组");
                        foreach(ShareService.ShareAccountEntry principal in ShareService.ListShareAccounts(selected.Name))subject.Items.Add(principal);
                        subject.SelectedIndex=0;
                    }catch(Exception ex){report.Text="查询失败："+ex.Message;}
                };
                Action readShares=delegate {
                    try {
                        string original=list.SelectedItem is ShareService.ShareEntry?((ShareService.ShareEntry)list.SelectedItem).Name:"";
                        string raw; List<ShareService.ShareEntry> items=ShareService.ListOrdinaryShares(out raw);
                        list.Items.Clear();foreach(ShareService.ShareEntry item in items)list.Items.Add(item);
                        if(list.Items.Count==0){report.Text="没有找到普通 SMB 共享。\r\n"+raw;path.Text="共享目录：无";return;}
                        int choose=0;for(int i=0;i<list.Items.Count;i++)if(((ShareService.ShareEntry)list.Items[i]).Name==original){choose=i;break;}
                        list.SelectedIndex=choose;
                    }catch(Exception ex){report.Text="读取共享失败："+ex.Message;}
                };
                list.SelectedIndexChanged+=delegate {readRights();};
                refresh.Click+=delegate {readShares();};
                reload.Click+=delegate {readRights();};
                grant.Click+=delegate {
                    ShareService.ShareEntry target=list.SelectedItem as ShareService.ShareEntry;
                    if(target==null)return;
                    string acc=account();if(String.IsNullOrWhiteSpace(acc)){MessageBox.Show(f,"请填写用户/用户组名。");return;}
                    string right=access.SelectedIndex==0?"Read":access.SelectedIndex==1?"Change":"Full";
                    if(!RequireAdmin())return;
                    if(MessageBox.Show(f,"共享："+target.Name+"\r\n授权对象："+acc+"\r\n权限："+access.Text+"\r\n\r\n仅调整 SMB 共享权限，不改 NTFS。确认应用吗？",
                        "确认修改共享权限",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)return;
                    try {report.Text=ShareService.ChangePermissions(target.Name,acc,right);readRights();mainOutput.Text=ShareService.ReadShares();}
                    catch(Exception ex){report.Text="更新失败："+ex.Message;}
                };
                revoke.Click+=delegate {
                    ShareService.ShareEntry target=list.SelectedItem as ShareService.ShareEntry;
                    if(target==null)return;
                    string acc=account();if(String.IsNullOrWhiteSpace(acc)){MessageBox.Show(f,"请填写用户/用户组名。");return;}
                    if(!RequireAdmin())return;
                    if(MessageBox.Show(f,"确认移除共享「"+target.Name+"」中「"+acc+"」的授权？\r\n这可能导致其他用户立刻无法访问。",
                        "确认撤销授权",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)return;
                    try {report.Text=ShareService.RevokePermissions(target.Name,acc);readRights();}
                    catch(Exception ex){report.Text="撤销失败："+ex.Message;}
                };
                ntfsGrant.Click+=delegate {
                    ShareService.ShareEntry target=list.SelectedItem as ShareService.ShareEntry;
                    if(target==null)return;
                    string principal=account();
                    if(String.IsNullOrWhiteSpace(principal)){MessageBox.Show(f,"请选择用户或用户组。 ");return;}
                    string rights=access.SelectedIndex==0?"ReadAndExecute":access.SelectedIndex==1?"Modify":"FullControl";
                    if(!RequireAdmin())return;
                    if(MessageBox.Show(f,"仅对以下目录设置 NTFS 安全权限：\r\n"+target.Folder+"\r\n对象："+principal+"\r\n权限："+rights+
                        "\r\n\r\n首次 ACL 会备份。将影响文件夹及继承权限的内容，确定吗？",
                        "确认更改 NTFS 权限",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
                    try{report.Text=ShareNtfsPermissionService.Apply(target.Name,target.Folder,principal,rights);}
                    catch(Exception ex){report.Text="NTFS 权限设置失败："+ex.Message;}
                };
                ntfsRestore.Click+=delegate {
                    ShareService.ShareEntry target=list.SelectedItem as ShareService.ShareEntry;
                    if(target==null||!RequireAdmin())return;
                    if(MessageBox.Show(f,"按工具箱首次保存的 ACL 备份恢复该文件夹：\r\n"+target.Folder,
                        "恢复 NTFS 权限",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
                    try{report.Text=ShareNtfsPermissionService.Restore(target.Name,target.Folder);}
                    catch(Exception ex){report.Text="NTFS 恢复失败："+ex.Message;}
                };
                readShares();f.ShowDialog(this);
            }
        }
        private void ShareSecurityDialog()
        {
            using(Form f=NewDialog("密码保护共享与 SMB 来宾兼容检查",790,550))
            {
                Label info=new Label {Left=20,Top=12,Width=742,Height=89,
                    Text="Windows 的『密码保护共享』与 SMB 服务器认证属性、Everyone 权限、客户端来宾策略分别管理。此处可检查 SMB 状态、修改系统支持的认证用户共享属性；原生密码保护共享设置提供直达入口。不会绕过 Win11 SMB 签名或来宾限制。"};
                RichTextBox state=ReadBox();state.Dock=DockStyle.None;state.SetBounds(20,266,744,253);
                ComboBox choice=new ComboBox {Left=185,Top=166,Width=270,DropDownStyle=ComboBoxStyle.DropDownList};
                choice.Items.Add("启用 SMB 认证用户共享");choice.Items.Add("禁用 SMB 认证用户共享");
                Action readSecurity=delegate {
                    state.Text=ShareService.ShareSecurityState();
                    if(state.Text.Contains("SERVERAUTH|True"))choice.SelectedIndex=0;
                    else if(state.Text.Contains("SERVERAUTH|False"))choice.SelectedIndex=1;
                    else choice.SelectedIndex=-1;
                };
                Button scan=FormButton("读取真实 SMB 状态",20,211,210,delegate {readSecurity();});
                Button change=FormButton("应用并验证",475,164,165,delegate {
                    if(choice.SelectedIndex<0){MessageBox.Show(f,"请先读取状态并选择配置。", "提示");return;}
                    if(!RequireAdmin())return;
                    bool enabled=choice.SelectedIndex==0;
                    if(MessageBox.Show(f,(enabled?"启用":"禁用")+" SMB 的 EnableAuthenticateUserSharing 属性。\r\n注意：此属性不等同于 Windows『密码保护共享』总开关，免密码访问还受到其他安全策略限制。\r\n确认执行吗？",
                        "SMB 共享认证设置",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)return;
                    try {string result=ShareService.SetAuthenticatedUserSharing(enabled);readSecurity();state.AppendText("\r\n\r\n【应用结果】\r\n"+result);}catch(Exception ex){state.Text="执行失败："+ex.Message;}
                });
                Button native=FormButton("打开 Windows 共享设置",20,116,220,delegate {
                    CommandService.OpenTool("ms-settings:network-advancedsettings","");
                });
                f.Controls.AddRange(new Control[]{info,FormLabel("SMB 认证属性",20,165,160),choice,change,scan,state,native});
                // Keep selection separate from verification: unknown policies must not be displayed as success.
                readSecurity();
                f.ShowDialog(this);
            }
        }
    }
}
