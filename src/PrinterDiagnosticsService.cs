using System;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace ITDesktopToolkit
{
    public static class PrinterDiagnosticsService
    {
        public static string Read()
        {
            return PowerShellService.RunScript(@"$ErrorActionPreference='Stop';
try {
 '=== 打印服务 ==='; Get-Service Spooler | Format-List Name,Status,StartType | Out-String -Width 240;
 '=== 打印机（含默认打印机） ==='; Get-CimInstance Win32_Printer | Select-Object Name,Default,WorkOffline,PrinterStatus,PortName,DriverName | Format-List | Out-String -Width 240;
 '=== 驱动 ==='; Get-PrinterDriver | Select-Object Name,Manufacturer,MajorVersion | Format-Table -AutoSize | Out-String -Width 240;
 '=== 端口 ==='; Get-PrinterPort | Select-Object Name,PrinterHostAddress,PortNumber,Protocol | Format-Table -AutoSize | Out-String -Width 240;
 '=== 排队任务 ==='; foreach($p in Get-Printer){ '打印机：'+$p.Name; Get-PrintJob -PrinterName $p.Name | Select-Object ID,DocumentName,JobStatus,SubmittedTime,Size | Format-Table -AutoSize | Out-String -Width 240 }
} catch { Write-Output ('[读取未完成] '+$_.Exception.Message); exit 1 }",3);
        }
        public static string ClearQueue(string printer)
        {
            if(String.IsNullOrWhiteSpace(printer))throw new ArgumentException("请输入检测报告中准确的打印机名称。");
            return PowerShellService.RunScript("$ErrorActionPreference='Stop';try {$name="+PowerShellService.Quote(printer)+
                "; $p=@(Get-Printer | Where-Object {$_.Name -eq $name}); if($p.Count -ne 1){throw '打印机名称不存在或不唯一'}; $jobs=@(Get-PrintJob -PrinterName $name); foreach($j in $jobs){Remove-PrintJob -PrinterName $name -ID $j.ID}; $remaining=@(Get-PrintJob -PrinterName $name); '本次请求取消任务数：'+$jobs.Count; '当前剩余任务数：'+$remaining.Count; if($remaining.Count -gt 0){'仍有任务（也可能为刚提交的新任务），请重新诊断。';exit 2}}catch {Write-Output $_.Exception.Message;exit 1}",2);
        }
        private static void ObserveLateFault(Task task)
        { task.ContinueWith(t=>{var ignored=t.Exception;},TaskContinuationOptions.OnlyOnFaulted); }
        public static async Task<string> TestPort(string host,int port)
        {
            NetworkMonitorService.Targets(host); if(host.IndexOfAny(new char[]{',',';',' ','\r','\n'})>=0||port<1||port>65535)throw new ArgumentException("请输入单个主机名/IP 和 1–65535 的端口。");
            using(var client=new TcpClient())
            {
                try
                {
                    Task connect=client.ConnectAsync(host,port);
                    if(await Task.WhenAny(connect,Task.Delay(3000))!=connect)
                    {
                        // Observe a late connection failure after disposing the socket.
                        ObserveLateFault(connect);
                        return host+":"+port+" 连接超时。TCP 不通可能由设备离线、端口配置或防火墙造成。";
                    }
                    await connect; return host+":"+port+" TCP 可连接（未发送打印数据，不代表打印机能正常出纸）。";
                }
                catch(Exception ex){return host+":"+port+" 连接失败："+ex.Message;}
            }
        }
    }
}
