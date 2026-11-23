using System;
using System.Drawing;
using System.Windows.Forms;

namespace WinForms
{
    public sealed partial class MainForm
    {
        private void ComputerNameDialog()
        {
            using(Form f=NewDialog("修改计算机名称",720,455))
            {
                Label warning=new Label {Left=20,Top=17,Width=660,Height=53,
                    Text="仅修改本机名称，不自动重启。改名可能影响网络共享、映射、远程桌面和资产管理。\r\n已加入域的电脑需通过域管理员流程处理；请先通知相关人员。",
                    ForeColor=Color.FromArgb(138,78,24)};
                RichTextBox state=ReadBox();state.Dock=DockStyle.None;state.SetBounds(20,81,660,135);
                TextBox name=FormText(Environment.MachineName,165,237,285);
                RichTextBox result=ReadBox();result.Dock=DockStyle.None;result.SetBounds(20,322,660,108);
                Button refresh=FormButton("刷新当前状态",471,231,195,delegate{state.Text=ComputerNameService.Status();});
                Button apply=null; apply=FormButton("提交计算机名修改",165,276,228,async delegate {
                    string newName=name.Text.Trim();
                    string error=ComputerNameService.Validate(newName);
                    if(error.Length>0){MessageBox.Show(f,error,"名称无效",MessageBoxButtons.OK,MessageBoxIcon.Warning);return;}
                    if(!RequireAdmin())return;
                    if(!ConfirmAction("修改计算机名称","当前名称："+Environment.MachineName+"\r\n新名称："+newName+
                        "\r\n变更需要重启后生效；不会自动重启。域设备将拒绝自动修改。"))return;
                    apply.Enabled=false;
                    try {result.Text=await System.Threading.Tasks.Task.Run(delegate {return ComputerNameService.Rename(newName);}); state.Text=ComputerNameService.Status();}
                    catch(Exception ex){result.Text="失败："+ex.Message;}
                    finally {if(!apply.IsDisposed)apply.Enabled=true;}
                });
                Button system=FormButton("系统属性（原生）",409,277,257,delegate{CommandService.OpenTool("sysdm.cpl","");});
                f.Controls.AddRange(new Control[]{warning,state,FormLabel("新计算机名称",20,236,140),name,refresh,apply,system,result});
                refresh.PerformClick();f.ShowDialog(this);
            }
        }
        private void EventDetailsDialog(FaultEntry entry)
        {
            if(entry==null)return;
            using(Form f=NewDialog("事件详情 · "+entry.DisplayKind+" · ID "+entry.EventId,900,660))
            {
                f.MinimumSize=new Size(700,480);
                Label note=new Label {Text="日志："+entry.LogName+"   事件 ID："+entry.EventId+"   记录编号："+(entry.RecordId>0?entry.RecordId.ToString():"未知")+
                    "\r\n完整事件内容可滚动浏览和复制，不因列表摘要截断而丢失。",Dock=DockStyle.Top,Height=64,Padding=new Padding(22,10,0,0)};
                RichTextBox details=ReadBox();details.Dock=DockStyle.Fill;details.WordWrap=true;details.Text=entry.Details();
                FlowLayoutPanel actions=new FlowLayoutPanel {Dock=DockStyle.Bottom,Height=58,Padding=new Padding(16,6,0,0)};
                Button copy=FormButton("复制全部事件信息",0,0,200,delegate {
                    try{Clipboard.SetText(entry.Details());MessageBox.Show(f,"完整事件已复制。","提示");}
                    catch(Exception ex){MessageBox.Show(f,"无法复制到剪贴板："+ex.Message);}
                });
                Button eventvwr=FormButton("打开事件查看器",0,0,190,delegate {
                    // Event Viewer cannot reliably select an exact record by its public command-line switches.
                    CommandService.OpenTool("eventvwr.msc", "");
                    MessageBox.Show(f,"请在『"+entry.LogName+"』日志中按记录编号 "+(entry.RecordId>0?entry.RecordId.ToString():"未知")+
                        " 或事件 ID "+entry.EventId+" 查找对应原始事件。", "事件定位说明");
                });
                Button close=FormButton("关闭",0,0,110,delegate{f.Close();});
                copy.Margin=new Padding(0,0,12,0);eventvwr.Margin=new Padding(0,0,12,0);
                actions.Controls.AddRange(new Control[]{copy,eventvwr,close});
                Panel content=new Panel {Dock=DockStyle.Fill,Padding=new Padding(22,4,22,4)};content.Controls.Add(details);
                f.Controls.Add(content);f.Controls.Add(actions);f.Controls.Add(note);
                f.ShowDialog(this);
            }
        }
    }
}
