PCI/USB 厂商 ID 数据库（可选，离线运行）
========================================

本工具箱自带 Database\hardware_models.tsv 和 bugcheck_codes.tsv，可以直接离线识别常见硬件和蓝屏代码。
如果你想补充数千个 PCI/USB 注册厂商和设备型号，可以自己下载两个公开的文本库：

1) pci.ids 官方： https://pci-ids.ucw.cz/
   下载 pci.ids（文本文件，不是 .gz）放到 EXE 同级的 Database 文件夹。
2) usb.ids 官方： http://www.linux-usb.org/usb-ids.html
   取得 usb.ids 原始文本，放到同一个 Database 文件夹。

这是可选的一次性离线资料准备：程序不自动下载，也不会联网。
放好后，进入“离线数据库”点击“重载数据库”，无须重新编译。
ID 库提供芯片厂商/设备名称，不代表零售品牌，也不能凭芯片 ID 推断笔记本厂商。
另外，注册 ID 的设备名称并非一定足以确认最终商品准确型号。

版权许可：原 PCI 数据库可按 BSD-3-Clause 或 GPL-2.0-or-later 分发，
详见官网 https://pci-ids.ucw.cz/ ；USB 数据库详见其官方许可说明。
本源码包没有捆绑两份完整的上游数据库，由用户自行取得。

已包含的部分公开型号验证参考：
- Microsoft Win32_PhysicalMemory（Manufacturer、PartNumber、Speed 等）
  https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-physicalmemory
- CORSAIR 内存零件号说明
  https://help.corsair.com/hc/en-us/articles/8528259685901-RAM-How-to-Read-the-CORSAIR-memory-part-number
- Kingston FURY KF432C16BB/16 产品说明
  https://www.kingston.com/unitedkingdom/en/memory/search?partid=KF432C16BB%2F16
- G.SKILL F4 DDR4 产品型号示例
  https://www.gskill.com/cn/specification/204/214/1562830240/F4-3200C16D-32GFX-Specification

注意：型号前缀只能推断品牌/家族。软件始终保存系统读到的原始制造商、零件号、设备 ID。
如果内存 OEM 没有通过 SMBIOS 报告厂商和零件号，单凭 Windows WMI 无法可靠确定条子的零售品牌；
不应把 RAM 芯片厂商直接当作内存条品牌。

V0.3.9：hardware_models_v039.tsv 是独立加载的增量硬件品牌规则库；显示器的实际型号优先读 EDID，不会用品牌代码猜测商品 SKU。
