using System;
using System.Collections.Generic;
using System.Globalization;
namespace WinForms
{
    public static class CommonPorts
    {
        private const string Map="20:FTP数据;21:FTP;22:SSH/SFTP;23:Telnet;25:SMTP;53:DNS(TCP);80:HTTP;81:HTTP备用;88:Kerberos;110:POP3;111:RPC;135:Windows RPC;139:NetBIOS;143:IMAP;389:LDAP;443:HTTPS;445:SMB;465:SMTPS;502:Modbus TCP;514:Syslog(TCP);515:LPD打印;548:AFP;554:RTSP;587:邮件提交;631:IPP打印;636:LDAPS;873:rsync;902:VMware;989:FTPS数据;990:FTPS;993:IMAPS;995:POP3S;1025:RPC/应用;1080:SOCKS;1433:SQL Server;1521:Oracle;1723:PPTP;1883:MQTT;2049:NFS;2082:cPanel;2083:cPanel HTTPS;2086:WHM;2087:WHM HTTPS;2181:ZooKeeper;2375:Docker;2376:Docker TLS;3000:应用服务;3128:HTTP代理;3260:iSCSI;3268:AD全局目录;3269:AD目录TLS;3306:MySQL;3389:RDP;3690:SVN;4369:EPMD;5000:应用服务;5001:应用服务TLS;5432:PostgreSQL;5601:Kibana;5671:AMQP TLS;5672:AMQP;5900:VNC;5901:VNC;5902:VNC;5903:VNC;5985:WinRM HTTP;5986:WinRM HTTPS;6000:X11;6379:Redis;6443:Kubernetes API;6667:IRC;7001:WebLogic;7002:WebLogic TLS;7199:Cassandra JMX;8000:HTTP应用;8008:HTTP备用;8009:AJP;8080:HTTP代理/应用;8081:HTTP应用;8086:InfluxDB;8088:应用服务;8090:HTTP应用;8161:ActiveMQ管理;8443:HTTPS备用;8500:Consul;8530:WSUS;8531:WSUS TLS;8834:Nessus;8883:MQTT TLS;8888:HTTP应用;9000:应用服务;9001:应用服务;9042:Cassandra;9090:管理/监控;9092:Kafka;9100:打印/监控;9200:Elasticsearch HTTP;9300:Elasticsearch节点;9418:Git;9999:应用服务;10000:Webmin;11211:Memcached;15672:RabbitMQ管理;27017:MongoDB;27018:MongoDB;27019:MongoDB;28017:MongoDB旧管理;61613:STOMP;61616:ActiveMQ;179:BGP;464:Kerberos密码;593:RPC/HTTP;1494:Citrix;2121:FTP备用;2222:SSH备用;2379:etcd;2380:etcd集群;2525:SMTP备用;2598:Citrix;4899:Radmin;5060:SIP(TCP);5061:SIP TLS;5357:WSD;5938:TeamViewer;11434:Ollama";
        private static readonly SortedDictionary<int,string> ports=Load();
        private static SortedDictionary<int,string> Load(){var p=new SortedDictionary<int,string>();foreach(string row in Map.Split(';')){int at=row.IndexOf(':');p.Add(Int32.Parse(row.Substring(0,at),CultureInfo.InvariantCulture),row.Substring(at+1));}return p;}
        public static string DefaultText {get{return String.Join(",",new List<int>(ports.Keys).ConvertAll(p=>p.ToString(CultureInfo.InvariantCulture)).ToArray());}}
        public static int Count {get{return ports.Count;}}
        public static string Service(int port){string value;return ports.TryGetValue(port,out value)?value:"自定义";}
    }
}
