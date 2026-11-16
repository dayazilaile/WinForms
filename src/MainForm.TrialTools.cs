using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace ITDesktopToolkit
{
    public sealed partial class MainForm
    {
        private sealed class TraceRows
        {
            public bool Reached;private readonly PageSession session;private readonly DataGridView grid;private readonly Label status;
            public TraceRows(PageSession session,DataGridView grid,Label status){this.session=session;this.grid=grid;this.status=status;}
            public void Add(TraceHop row){if(session.Alive){grid.Rows.Add(row.Cells());status.Text="已检查 "+row.Ttl+" 跳";Reached=row.Reached;}}
        }
        private void BuildDiagnosticWorkspace(string key)
        {
            bool trial=key!="系统代理诊断";
            FlowLayoutPanel bar;Label state;Panel root=Workspace(trial?key+" · 新增待试用":key,key=="ICMP路由跟踪"?"每跳一次 ICMP、最多 30 跳；需点击开始，可能被网络设备记录。":trial?"新增功能集中试用，确认有用后再决定是否保留；不会自动执行。":"只读检查本机代理配置，不下载 PAC、不更改设置；点击后读取。",out bar,out state);
            string[] headers=key=="DNS记录查询"?new[]{"名称","类型","返回内容","TTL（秒）","区段"}:key=="ICMP路由跟踪"?new[]{"跳数","响应地址","状态","单跳耗时（ms）"}:key=="本机连接与监听"?new[]{"协议","本地地址","本地端口","远端地址","远端端口","状态","PID","进程"}:key=="系统代理诊断"?new[]{"来源","设置项","已隐藏敏感内容的值"}:new[]{"网卡","核对项","结果","依据 / 说明"};
            DataGridView grid=Table(headers);root.Controls["pageBody"].Controls.Add(grid);grid.BringToFront();
            if(key=="系统代理诊断"){grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill;grid.Columns[0].FillWeight=20;grid.Columns[1].FillWeight=25;grid.Columns[2].FillWeight=55;grid.DefaultCellStyle.WrapMode=DataGridViewTriState.True;grid.AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.AllCells;}
            if(key=="网络配置核对")
            {
                grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill;
                int[] minimum={140,120,90,260};float[] weight={22,18,14,46};
                for(int i=0;i<4;i++){grid.Columns[i].MinimumWidth=minimum[i];grid.Columns[i].FillWeight=weight[i];}
                grid.Columns[3].DefaultCellStyle.WrapMode=DataGridViewTriState.True;
                grid.AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.DisplayedCells;
            }
            TextBox target=null,server=null,hops=null;ComboBox type=null;CheckBox listen=null;
            if(key=="DNS记录查询")
            {
                target=WorkspaceInput(bar,"DNS 名称 / PTR 地址","",240);server=WorkspaceInput(bar,"DNS 服务器 IP（可空）","",190);
                type=new ComboBox{Width=95,DropDownStyle=ComboBoxStyle.DropDownList,Margin=new Padding(0,23,12,0),AccessibleName="DNS 记录类型"};type.Items.AddRange(TrialNetworkService.DnsTypes);type.SelectedIndex=0;bar.Controls.Add(type);
            }
            if(key=="ICMP路由跟踪"){target=WorkspaceInput(bar,"目标主机名 / IPv4","",240);hops=WorkspaceInput(bar,"最多跳数（1–30）","16",130);}
            if(key=="本机连接与监听"){listen=new CheckBox{Text="仅列出 TCP 监听项",Checked=true,Width=180,Height=34,Margin=new Padding(0,23,12,0)};bar.Controls.Add(listen);}
            Button start=WorkspaceButton(bar,key=="ICMP路由跟踪"?"开始跟踪":key=="DNS记录查询"?"查询记录":"读取本机信息",true),stop=WorkspaceButton(bar,"停止",false),export=WorkspaceButton(bar,"导出 CSV",false);
            var session=new PageSession(root,bar,stop,state);export.Click+=delegate{ExportToolReport(this,TableCsv(grid),key+".csv");};state.Text=(trial?"待试用":"就绪")+" · 点击执行后读取；结果仅供排查，不自动更改系统配置";
            start.Click+=async delegate{
                string host=target==null?"":target.Text.Trim(),dns=server==null?"":server.Text.Trim(),record=type==null?"":type.Text;bool listeners=listen!=null&&listen.Checked;int max=16;
                if(key=="ICMP路由跟踪"&&(!Int32.TryParse(hops.Text,out max)||max<1||max>30)){state.Text="跳数须为 1–30。";return;}
                grid.Rows.Clear();var traceRows=new TraceRows(session,grid,state);Action<TraceHop> onHop=new Action<TraceHop>(traceRows.Add);await session.Run(async delegate(CancellationToken token){
                    if(key=="ICMP路由跟踪")
                    {
                        await TrialNetworkService.Trace(host,max,1200,token,onHop);
                        if(session.Alive)state.Text=traceRows.Reached?"本次 ICMP 跟踪已到达目标":"已完成本次跳数范围，未确认到达目标；超时也可能是设备不回复 ICMP";
                    }
                    else
                    {
                        TrialRows rows=await Task.Run(new Func<TrialRows>(delegate{switch(key){case "DNS记录查询":return TrialNetworkService.DnsQuery(host,record,dns,token);case "本机连接与监听":return TrialNetworkService.Connections(listeners,token);case "系统代理诊断":return TrialNetworkService.Proxy(token);default:return TrialNetworkService.Audit(token);}}));
                        if(!session.Alive)return;foreach(var row in rows.Rows)grid.Rows.Add(row);state.Text="已显示 "+rows.Rows.Count+" 项 · "+rows.Note;
                    }
                });
            };
        }
    }
}
