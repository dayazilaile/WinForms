using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace ITDesktopToolkit
{
    public sealed partial class MainForm
    {
        private Func<CancellationToken,SavedWifiInventory> savedWifiReader=SavedWifiService.Read;
        private void BuildSavedWifiWorkspace()
        {
            FlowLayoutPanel bar;Label state;Panel root=Workspace("已保存的 Wi-Fi","直接列出本机可访问的已保存配置和密码；系统未允许读取时显示原因",out bar,out state);
            TextBox search=WorkspaceInput(bar,"搜索配置 / SSID / 网卡","",230);Button refresh=WorkspaceButton(bar,"刷新配置",true),stop=WorkspaceButton(bar,"停止",false),copy=WorkspaceButton(bar,"复制所选密码",false),clear=WorkspaceButton(bar,"清空显示",false);
            var grid=Table(new[]{"配置名称","SSID","安全类型","加密","密码","读取状态","无线网卡"});grid.Name="savedWifiGrid";grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill;
            int[] minimum={140,110,90,80,180,130,120};float[] weight={18,15,12,10,24,15,16};for(int i=0;i<minimum.Length;i++){grid.Columns[i].MinimumWidth=minimum[i];grid.Columns[i].FillWeight=weight[i];}
            var passwordFont=new System.Drawing.Font("Consolas",10F);grid.Columns[4].DefaultCellStyle.Font=passwordFont;grid.Columns[4].DefaultCellStyle.WrapMode=DataGridViewTriState.True;grid.AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.DisplayedCells;grid.RowTemplate.MinimumHeight=36;root.Disposed+=delegate{passwordFont.Dispose();};
            root.Controls["pageBody"].Controls.Add(grid);grid.BringToFront();var session=new PageSession(root,bar,stop,state);SavedWifiInventory snapshot=null;
            Action erase=delegate{grid.Rows.Clear();if(snapshot!=null){snapshot.Dispose();snapshot=null;}};
            Action render=delegate {
                grid.Rows.Clear();if(snapshot==null)return;string query=search.Text.Trim();var rows=new List<DataGridViewRow>();
                foreach(var profile in snapshot.Profiles){if(query.Length>0&&(profile.Name+" "+profile.Ssid+" "+profile.Adapter).IndexOf(query,StringComparison.OrdinalIgnoreCase)<0)continue;var row=new DataGridViewRow();row.CreateCells(grid,profile.Cells());row.Tag=profile;rows.Add(row);}if(rows.Count>0)grid.Rows.AddRange(rows.ToArray());
                state.Text="显示 "+rows.Count+" / "+snapshot.Profiles.Count+" 条 · "+(snapshot.Notices.Count>0?String.Join("；",snapshot.Notices.ToArray()):snapshot.Profiles.Count==0?"未发现可访问的已保存配置":"密码直接显示；读取状态以当前系统权限为准");
            };
            refresh.Click+=async delegate{
                erase();await session.Run(async delegate(CancellationToken token){SavedWifiInventory fresh=await Task.Run(new Func<SavedWifiInventory>(delegate{return savedWifiReader(token);}));if(token.IsCancellationRequested||!session.Alive){fresh.Dispose();token.ThrowIfCancellationRequested();return;}snapshot=fresh;render();});
            };
            search.TextChanged+=delegate{if(session.Alive&&!session.Running)render();};
            clear.Click+=delegate{erase();state.Text="已清空显示；点击刷新重新读取";};
            copy.Click+=delegate{if(grid.CurrentRow==null)return;var profile=grid.CurrentRow.Tag as SavedWifiProfile;if(profile==null||!profile.HasPassword){state.Text="所选配置没有可复制的明文密码";return;}try{Clipboard.SetText(profile.Password);state.Text="所选密码已复制到剪贴板";}catch{state.Text="复制失败，请重试";}};
            root.Disposed+=delegate{if(snapshot!=null){snapshot.Dispose();snapshot=null;}};
            root.BeginInvoke(new Action(delegate{if(session.Alive)refresh.PerformClick();}));
        }
    }
}
