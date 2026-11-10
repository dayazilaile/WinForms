using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ITDesktopToolkit
{
    public sealed partial class MainForm
    {
        private void ShareAccessModeDialog()
        {
            using(Form f=NewDialog("共享模式 · Win10/Win11",850,560))
            {
                Label note=new Label {Left=18,Top=16,Width=810,Height=108,
                    Text="在当前电脑设置 SMB 的访问方和共享方。两台电脑要免账号互访，需分别在两台设备上操作。\r\n"+
                    "账号共享：要求身份验证，默认推荐；免账号共享：兼容旧设备/Guest，会降低 SMB 安全性。\r\n"+
                    "注意：这三个按钮不更改具体共享文件夹权限；请另用『管理共享权限』检查共享权限和 NTFS 安全权限。",
                    ForeColor=Color.FromArgb(135,75,27)};
                RichTextBox report=ReadBox();report.Dock=DockStyle.None;report.SetBounds(18,205,809,319);
                Button auth=null,guest=null,restore=null,read=null;
                auth=FormButton("账号共享",20,131,168,delegate{
                    RunPreset(auth,report,"切换到账号共享",
                        "将本机 SMB 客户端设为拒绝不安全 Guest，服务端使用经典账号模式，并启用强制签名；\r\n将禁用本机 Guest 账户。这会影响现有来宾会话。",
                        delegate{return ShareModePresetService.Apply(false);});
                });
                guest=FormButton("免账号共享",201,131,168,delegate{
                    RunPreset(guest,report,"启用免账号共享（高风险）",
                        "将修改本机 SMB 客户端与服务器的 Guest 兼容、签名、Guest 本地账户，并设置专用网络本地子网 SMB 445 入站规则。\r\n"+
                        "仅建议隔离可信局域网；可能导致中间人攻击和敏感文件暴露。仍须检查具体共享文件夹的 SMB/NTFS 权限。",
                        delegate{return ShareModePresetService.Apply(true);});
                });
                restore=FormButton("恢复原本配置",382,131,168,delegate{
                    RunPreset(restore,report,"恢复首次保存的共享配置",
                        "恢复工具箱首次修改前保存的 SMB 客户端、服务器、Guest 账户设置，并移除工具箱创建的专用防火墙规则。不会修改文件夹本身的权限。",
                        ShareModePresetService.Restore);
                });
                read=FormButton("刷新真实状态",563,131,251,delegate{
                    DoJob(report,read,ShareModePresetService.ReadStatus);
                });
                Label caution=new Label {Left=18,Top=173,Width=810,Height=28,
                    Text="无法操作域成员/不可信公用网络；若组策略阻止 Guest，报告会指出失败位置，不会伪称设置成功。",
                    ForeColor=Color.FromArgb(135,75,27)};
                f.Controls.AddRange(new Control[]{note,auth,guest,restore,read,caution,report});
                f.Shown+=delegate{read.PerformClick();};
                f.ShowDialog(this);
            }
        }
        private async void RunPreset(Button button,RichTextBox report,string title,string warning,Func<string> job)
        {
            if(!RequireAdmin() || !ConfirmAction(title,warning))return;
            button.Enabled=false;
            report.Text="正在执行并核对配置，请查看最终返回代码及验证结果……";
            try
            {
                string result=await Task.Run(job);
                string status=await Task.Run(new Func<string>(ShareModePresetService.ReadStatus));
                if(!report.IsDisposed)report.Text="【执行结果】\r\n"+result+"\r\n\r\n【当前状态】\r\n"+status;
            }
            catch(Exception ex){if(!report.IsDisposed)report.Text="失败："+ex.Message;}
            finally{if(!button.IsDisposed)button.Enabled=true;}
        }
    }
}
