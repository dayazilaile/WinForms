using System;
using System.Collections.Generic;
namespace ITDesktopToolkit
{
    public static class PowerShellService
    {
        public static string Quote(string value) { return "'" + (value ?? "").Replace("'", "''") + "'"; }
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
    }
}
