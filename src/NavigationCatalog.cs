using System;
using System.Collections.Generic;

namespace WinForms
{
    public sealed class NavigationEntry
    {
        public readonly string Group, Key, Keywords;
        public NavigationEntry(string group,string key,string keywords){Group=group;Key=key;Keywords=keywords;}
    }
    public static class NavigationCatalog
    {
        private static readonly NavigationEntry[] entries={
            new NavigationEntry("系统与硬件","系统概览","系统信息 Windows CPU"),
            new NavigationEntry("系统与硬件","详细硬件配置","内存 显示器 EDID 磁盘"),
            new NavigationEntry("系统与硬件","软件环境检查","软件 运行库 .NET VC++"),
            new NavigationEntry("系统与硬件","启动项管理","开机 自启动"),
            new NavigationEntry("系统与硬件","系统优化","电源 睡眠 休眠"),
            new NavigationEntry("系统与硬件","蓝屏与异常关机","安全 维护 蓝屏 事件 日志 故障"),
            new NavigationEntry("系统与硬件","系统修复","安全 维护 修复 DISM SFC 默认应用 文件关联 Office 压缩包 音乐 视频"),
            new NavigationEntry("系统与硬件","功能开关","安全 维护 防火墙 远程桌面 RDP"),
            new NavigationEntry("系统与硬件","U 盘检查与防护","安全 维护 U盘 USB 写保护 自动播放"),
            new NavigationEntry("网络诊断","网络诊断","连接测试 IP DNS 网关 ping"),
            new NavigationEntry("网络诊断","网络持续检测","延迟 丢包 波动 ping"),
            new NavigationEntry("网络诊断","端口扫描","TCP port 服务"),
            new NavigationEntry("网络诊断","主机发现","局域网 网段 扫描 IP"),
            new NavigationEntry("网络诊断","路由与MAC","路由表 ARP 邻居 网卡 MAC IP"),
            new NavigationEntry("网络诊断","DHCP与网络服务","租约 DHCP DNS HTTP TCP 检测"),
            new NavigationEntry("网络诊断","系统代理诊断","代理 WinHTTP PAC 环境变量"),
            new NavigationEntry("无线与设备","Wi-Fi分析","WiFi 无线 WLAN 信号 信道"),
            new NavigationEntry("无线与设备","已保存的Wi-Fi","WiFi 无线 密码 WLAN 配置"),
            new NavigationEntry("无线与设备","摄像头发现","摄像头 ONVIF WS-Discovery IPC"),
            new NavigationEntry("共享与打印","共享文件夹","SMB NTFS 账号 Guest 权限"),
            new NavigationEntry("共享与打印","打印机与服务","打印机 队列 Spooler 驱动"),
            new NavigationEntry("学生机管理","学生机与网络唤醒","学生机 机房 WOL Wake-on-LAN 远程 开机 MAC 清单 关机 重启 电源 取消"),
            new NavigationEntry("自动化与工具","BAT 自动化","BAT 脚本 工作流 鼠标 键盘"),
            new NavigationEntry("自动化与工具","电脑配置对比","差异 对比 报告"),
            new NavigationEntry("自动化与工具","Windows 管理入口","控制面板 工具"),
            new NavigationEntry("自动化与工具","离线数据库","故障码 数据库 查询"),
            new NavigationEntry("新增 · 待试用","DNS记录查询","新增 试用 DNS A AAAA MX TXT SRV PTR"),
            new NavigationEntry("新增 · 待试用","ICMP路由跟踪","新增 试用 tracert traceroute 跳数 路径 Ping"),
            new NavigationEntry("新增 · 待试用","本机连接与监听","新增 试用 TCP UDP 本机连接 监听 进程 PID"),
            new NavigationEntry("新增 · 待试用","网络配置核对","新增 试用 IPv4 网关 DNS APIPA 子网 本机"),
        };
        public static NavigationEntry[] All {get{return (NavigationEntry[])entries.Clone();}}
        public static List<NavigationEntry> Search(string query)
        {
            string[] terms=(query??"").Split(new char[]{' ','\t','\r','\n'},StringSplitOptions.RemoveEmptyEntries);
            var matches=new List<NavigationEntry>();
            foreach(var e in entries)
            {
                string text=e.Group+" "+e.Key+" "+e.Keywords;bool match=true;
                foreach(string term in terms)if(text.IndexOf(term,StringComparison.OrdinalIgnoreCase)<0){match=false;break;}
                if(match)matches.Add(e);
            }
            return matches;
        }
        public static NavigationEntry Find(string key){foreach(var e in entries)if(e.Key==key)return e;return null;}
    }
}
