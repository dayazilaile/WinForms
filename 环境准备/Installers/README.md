# 把微软官方安装包放在这里（Installers）

不需要编译器、SDK 或 Visual Studio。只需要系统缺少的运行组件。安装包体积较大，不随源码附带。

| 组件 | 本脚本识别的文件名 | 适用系统 |
| --- | --- | --- |
| .NET Framework 4.8 完整英文运行时 | `ndp48-x86-x64-allos-enu.exe` | Win7 SP1、Win8.1、Win10，以及对应受支持的 Server；已有 4.8/4.8.1 不安装 |
| WMF 5.1 | `Win7AndW2K8R2-KB3191566-x86.msu` | 32 位 Win7 SP1 |
| WMF 5.1 | `Win7AndW2K8R2-KB3191566-x64.msu` | 64 位 Win7 SP1 / Server 2008 R2 SP1 |
| WMF 5.1 | `W2K12-KB3191565-x64.msu` | Server 2012 |
| WMF 5.1 | `Win8.1AndW2K12R2-KB3191564-x86.msu` | 32 位 Win8.1 |
| WMF 5.1 | `Win8.1AndW2K12R2-KB3191564-x64.msu` | 64 位 Win8.1 / Server 2012 R2 |

官方下载页：

- [.NET Framework 4.8](https://dotnet.microsoft.com/zh-cn/download/dotnet-framework/net48)：选“运行时 / Runtime”的完整安装包，不是 Developer Pack，也不是 .NET 8/9/10。
- [WMF 5.1 下载中心](https://www.microsoft.com/en-us/download/details.aspx?id=54616)。Win7 / Server 2008 R2 通常下载为同名 ZIP，需要先解压出 MSU，不要把 ZIP 改名为 MSU。
- [微软 WMF 安装与配置说明](https://learn.microsoft.com/en-us/powershell/scripting/windows-powershell/wmf/setup/install-configure)。核对其先决条件、已知问题及现有服务器管理产品兼容性。

请在可以上网的电脑下载，再随整个“环境准备”文件夹复制到目标电脑。不同系统/位数的包不能混用；不要把其他文件改成这些名字。EXE 只有通过微软有效签名检查才运行；MSU 的签名及适用性由 Windows 自带的 WUSA 安装器检查。若系统时间、根证书、SP1、SHA-2 或服务堆栈更新不完整，可能仍无法安装，请按微软提示补齐，不要跳过签名校验。

Win10/11、Server 2016 及以上不使用这些旧版 WMF 包。PowerShell 5.1 缺少或损坏时，应走系统更新/组件修复。此文件夹不提供下载镜像或未经核实的安装包直链。
