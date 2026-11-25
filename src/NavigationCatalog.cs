using System;
using System.Collections.Generic;

namespace WinForms
{
    public sealed class NavigationEntry
    {
        public readonly string Group, Key, Keywords,Since,Note;
        public NavigationEntry(string group,string key,string keywords):this(group,key,keywords,"",""){}
        public NavigationEntry(string group,string key,string keywords,string since,string note){Group=group;Key=key;Keywords=keywords;Since=since;Note=note;}
        public bool Trial {get{return Group=="新增 · 待试用";}}
        public string DisplayName {get{return Trial?Key+(Since.Length>0?"  [v"+Since+"]":"  [历史]"):Key;}}
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
            new NavigationEntry("新增 · 待试用","新增功能总览","新增 试用 版本 入口 总览", "0.6.4","集中查看试用入口、加入版本与用途。"),
            new NavigationEntry("新增 · 待试用","一键系统优化","优化 电源 高性能 睡眠 天气 搜索 扩展名 桌面 D盘 时间", "0.6.6","勾选配置、先备份后执行并核验；维护动作单独按钮，支持同机同账户恢复。"),
            new NavigationEntry("新增 · 待试用","开机临时文件清理","开机 欢迎 加载慢 Temp 临时 文件 清理", "0.6.6","单独清理当前用户Local\\Temp，占用/链接/工具目录跳过；删除不可恢复。"),
            new NavigationEntry("新增 · 待试用","跨机房唤醒","跨网段 跨机房 WOL 唤醒 广播 单播", "0.6.5","不改网络配置，尝试单播/广播；能否送达依赖现有网络和网卡，尚未实机确认。"),
            new NavigationEntry("新增 · 待试用","学生机远程电源","远程 关机 重启 学生机 电源 试用", "0.6.2","v0.6.4 移入试用区；需要目标机允许远程管理，尚未确认实机可用。"),
            new NavigationEntry("新增 · 待试用","远程电源诊断","远程 RPC SMB 53 1722 135 445 关机 排查", "0.6.4","只读检查选中目标的 MAC 与通道，导出学生机本地只读检查脚本。"),
            new NavigationEntry("新增 · 待试用","软件清单清理BAT","软件 清单 标准 卸载 清理 BAT 脚本", "0.6.4","已有批量清理能力的新入口；先建立/导入保留标准，再选择模式生成单 BAT。"),
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
                string text=e.Group+" "+e.Key+" "+e.Keywords+" "+e.Since;bool match=true;
                foreach(string term in terms)if(text.IndexOf(term,StringComparison.OrdinalIgnoreCase)<0){match=false;break;}
                if(match)matches.Add(e);
            }
            return matches;
        }
        public static NavigationEntry Find(string key){foreach(var e in entries)if(e.Key==key)return e;return null;}
    }
}
