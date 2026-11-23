using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WinForms
{
    public sealed class NetworkSample
    {
        public DateTimeOffset Time; public string Host, Status; public long Milliseconds;
    }
    public static class NetworkMonitorService
    {
        public static string[] Targets(string input)
        {
            var hosts=new List<string>();
            foreach(string s in input.Split(new char[]{',',';','\r','\n',' '},StringSplitOptions.RemoveEmptyEntries))
            {
                string host=s.Trim(); if(Uri.CheckHostName(host)==UriHostNameType.Unknown) throw new ArgumentException("目标应为 IP 或主机名，不含协议和端口："+host);
                if(!hosts.Contains(host))hosts.Add(host);
            }
            if(hosts.Count==0||hosts.Count>8)throw new ArgumentException("请输入 1 至 8 个检测目标。");
            return hosts.ToArray();
        }
        public static async Task Run(string[] hosts,int rounds,int intervalSeconds,CancellationToken cancel,Action<NetworkSample> receive)
        {
            if(rounds<1||rounds>86400||intervalSeconds<1||intervalSeconds>3600)throw new ArgumentException("检测次数或间隔无效。");
            for(int round=0;round<rounds&&!cancel.IsCancellationRequested;round++)
            {
                var tasks=new List<Task>();
                foreach(string target in hosts)
                {
                    string host=target;
                    tasks.Add(Task.Run(async delegate {
                        var sample=new NetworkSample {Time=DateTimeOffset.Now,Host=host,Status="未知",Milliseconds=-1};
                        using(var ping=new Ping()) try
                        {
                            PingReply reply=await ping.SendPingAsync(host,1500);
                            sample.Status=reply.Status.ToString(); if(reply.Status==IPStatus.Success)sample.Milliseconds=reply.RoundtripTime;
                        }
                        catch(Exception ex){sample.Status="检测错误："+ex.GetType().Name;}
                        if(!cancel.IsCancellationRequested)receive(sample);
                    }));
                }
                await Task.WhenAll(tasks);
                if(round+1<rounds)try{await Task.Delay(intervalSeconds*1000,cancel);}catch(OperationCanceledException){break;}
            }
        }
        public static string Summary(IList<NetworkSample> samples)
        {
            var hosts=new SortedSet<string>(); foreach(var s in samples)hosts.Add(s.Host);
            StringBuilder b=new StringBuilder("ICMP 检测汇总（无响应也可能是设备禁用 ICMP，不等同于断网）\r\n");
            foreach(string h in hosts)
            {
                int total=0,ok=0,changes=0; long sum=0,min=Int64.MaxValue,max=0,previous=-1,jitter=0;
                foreach(var s in samples)if(s.Host==h)
                {
                    total++; if(s.Status!="Success"){previous=-1;continue;} ok++;sum+=s.Milliseconds;min=Math.Min(min,s.Milliseconds);max=Math.Max(max,s.Milliseconds);
                    if(previous>=0){jitter+=Math.Abs(s.Milliseconds-previous);changes++;}previous=s.Milliseconds;
                }
                b.AppendLine(h+": 采样 "+total+"，成功 "+ok+"，未响应/错误 "+(total-ok)+"（"+((total-ok)*100.0/total).ToString("F1",CultureInfo.InvariantCulture)+"%）"+
                    (ok==0?"，无有效延迟":"，延迟 min/avg/max="+min+"/"+(sum/(double)ok).ToString("F1",CultureInfo.InvariantCulture)+"/"+max+" ms；相邻成功采样平均波动="+(changes==0?"无":(jitter/(double)changes).ToString("F1",CultureInfo.InvariantCulture))+" ms"));
            }
            return b.ToString();
        }
        private static string Csv(string s){return "\""+(s??"").Replace("\"","\"\"")+"\"";}
        public static string Export(IList<NetworkSample> samples)
        {
            StringBuilder b=new StringBuilder("时间,目标,状态,延迟毫秒\r\n");
            foreach(var s in samples)b.AppendLine(Csv(s.Time.ToString("o"))+","+Csv(s.Host)+","+Csv(s.Status)+","+(s.Milliseconds<0?"":s.Milliseconds.ToString(CultureInfo.InvariantCulture)));
            return b.ToString();
        }
    }
}
