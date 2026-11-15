"""Extract factual SMBIOS board names from the included MPL-2.0 source snapshot.
No model specifications or vendor ownership are inferred. Run from repo root.
"""
import json
import re
from pathlib import Path
source = Path('Database/Sources/Identification-v052.cs.txt').read_text(encoding='utf-8-sig')
body = source.split('public static Model GetModel(string name)', 1)[1]
names_set = set()
pending = []
for line in body.splitlines():
    match = re.search(r'name\.Equals\("((?:\\.|[^"\\])*)", StringComparison\.OrdinalIgnoreCase\)', line)
    if match:
        pending.append(json.loads('"' + match.group(1) + '"'))
    returned = re.search(r'return Model\.(\w+);', line)
    if returned:
        if returned.group(1) != 'Unknown':
            names_set.update(pending)
        pending.clear()
names = sorted(names_set, key=str.casefold)
rows = [
    '# Board model-name data derived from LibreHardwareMonitor Identification.cs; MPL-2.0.',
    '# Copyright (C) LibreHardwareMonitor and Contributors; partial Copyright (C) Michael Möller and Contributors.',
    '# 类别\t关键词\t品牌\t补充名称\t说明\t来源\t匹配方式\t制造商条件',
]
for name in names:
    assert not any(c in name for c in '\t\r\n')
    rows.append('\t'.join(['主板', name, '', name, 'SMBIOS 型号名称精确匹配；品牌沿用系统制造商，不推断规格或 PCB 修订', 'https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/master/LibreHardwareMonitorLib/Hardware/Motherboard/Identification.cs', 'exact', '']))
Path('Database/board_models_v052.tsv').write_text('\n'.join(rows) + '\n', encoding='utf-8')
print('Board model names:', len(names))
