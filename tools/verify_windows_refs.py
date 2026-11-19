#!/usr/bin/env python3
"""Check a shipped PE's TypeRef owners against Microsoft .NET Framework metadata."""
import argparse
import hashlib
import json
from pathlib import Path


def verify(assembly, references):
    import dnfile
    assembly, references = Path(assembly), Path(references)
    cache, errors, used, stacks = {}, [], set(), []
    with dnfile.dnPE(str(assembly), clr_lazy_load=True) as pe:
        if not pe.net:
            raise ValueError('Not a managed .NET executable')
        for row in pe.net.mdtables.TypeRef:
            scope = row.ResolutionScope.row
            if not isinstance(scope, dnfile.mdtable.AssemblyRefRow):
                continue  # Nested TypeRef parents are checked on their own row.
            owner = str(scope.Name)
            if owner not in cache:
                path = references / (owner + '.dll')
                if not path.is_file():
                    raise ValueError('Missing Microsoft framework reference: ' + str(path))
                with dnfile.dnPE(str(path), clr_lazy_load=True) as framework:
                    names = set()
                    for table_name in ('TypeDef', 'ExportedType'):
                        table = getattr(framework.net.mdtables, table_name, None)
                        if table:
                            names.update((str(t.TypeNamespace), str(t.TypeName)) for t in table)
                    cache[owner] = names
            key = (str(row.TypeNamespace), str(row.TypeName))
            if key not in cache[owner]:
                errors.append('.'.join(key) + ' -> ' + owner + '.dll')
            used.add(owner)
            if key == ('System.Collections.Generic', 'Stack`1'):
                stacks.append(owner)
    if errors:
        raise ValueError('Windows framework cannot resolve TypeRef owner(s):\n' + '\n'.join(errors))
    return {'assembly': assembly.name, 'sha256': hashlib.sha256(assembly.read_bytes()).hexdigest(),
            'reference_profile': 'Microsoft .NET Framework 4.8',
            'framework_assemblies': sorted(used), 'stack_owners': sorted(set(stacks)),
            'type_reference_check': 'passed'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('assembly', type=Path)
    parser.add_argument('--references', type=Path, required=True)
    parser.add_argument('--report', type=Path)
    args = parser.parse_args()
    try:
        result = verify(args.assembly, args.references)
        if args.report:
            args.report.write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n')
        print(json.dumps(result, ensure_ascii=False))
    except (ValueError, ImportError) as ex:
        parser.exit(1, str(ex) + '\n')


if __name__ == '__main__':
    main()
