# V0.5.2 硬件库来源与许可

获取日期：2026-11-15。文件保持上游原始文本；应用仅离线读取，不自动下载更新。

| 文件 | 版本 | 许可 | SHA-256 |
| --- | --- | --- | --- |
| pci.v052.ids | 2026.11.15 | BSD-3-Clause | c2b5f74b02e480fa38fd3fdfd6ace0a860524033d7b0cb0129a3428b4de9eee9 |
| usb.v052.ids | 2026.06.26 | GPL-3.0（上游仓库 LICENSE） | f5a48b0cc8dae1607c2f0bae6b8dc13f2ecef69dbeaeaf34b4be8e280d34dba4 |
| Sources/Identification-v052.cs.txt | 2026-11-15 获取 | MPL-2.0 | 506d1fd4a69c3342b282155a2f0ed4106c791ee1eb38e02d9faaab4f28446f42 |

来源：

- PCI：https://raw.githubusercontent.com/pciutils/pciids/master/pci.ids
- USB：https://raw.githubusercontent.com/usbids/usbids/master/usb.ids
- 主板：https://raw.githubusercontent.com/LibreHardwareMonitor/LibreHardwareMonitor/master/LibreHardwareMonitorLib/Hardware/Motherboard/Identification.cs

附带原始库加载后：PCI 2526 厂商 / 21819 设备，USB 3427 厂商 / 20537 设备，硬件规则共 572 条。计数排除子系统、接口和 class/audio/HID 段；用户覆盖或增删数据后计数会变化。

## 识别范围与优先级

- `pci.v052.ids` / `usb.v052.ids` 为附带的上游快照；原 `pci.ids` / `usb.ids` 在快照之后加载，同 ID 的用户条目优先。不会覆盖原文件。
- 只读取厂商/设备段，PCI 子系统/USB 接口和后续 class/audio/HID 等段不当作厂商，避免误认。
- PCI/USB ID 提供芯片/设备名称，不能确定显卡零售板卡品牌、显存、主板 PCB 修订或整机 SKU。
- `board_models_v052.tsv` 从所附 LibreHardwareMonitor 源码的 GetModel 名称提取 406 条精确型号规则，保留原作者/MPL 许可；不从名称猜制造商。品牌沿用系统制造商或已有可靠规则。提取脚本：`tools/build_board_catalog.py`。
- `component_models_v052.tsv` 为带来源链接的内存/SSD 前缀规则；只是家族，不推断完整规格。
- 旧五列 TSV 保持支持；扩展列为 来源、匹配方式（contains/exact/prefix）、制造商条件。型号精确命中优先，等分时保留先加载的用户规则。
- 上游数据的许可仅适用于对应数据/源文件，见 `Licenses/`；未修改原数据文件，提供可读文本与来源。

本库不可能覆盖所有硬件；未知 ID 保留系统原值，不伪造型号。
