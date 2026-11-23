#!/usr/bin/env python3
"""Cross-compile using Microsoft reference assemblies, never Mono's runtime DLLs."""
import argparse
import hashlib
import io
import os
import shutil
from pathlib import Path
import subprocess
import urllib.request
import zipfile
from verify_windows_refs import verify

PACKAGE_URL = ('https://api.nuget.org/v3-flatcontainer/microsoft.netframework.referenceassemblies.net45/'
               '1.0.3/microsoft.netframework.referenceassemblies.net45.1.0.3.nupkg')
PACKAGE_SHA256 = '23a9f94ea3e2cb88cd8341af75b811c6fb5cb82516fc696e95ed4620279128e3'


def download_references(destination):
    with urllib.request.urlopen(PACKAGE_URL, timeout=60) as response:
        package = response.read()
    if hashlib.sha256(package).hexdigest() != PACKAGE_SHA256:
        raise ValueError('Microsoft reference package hash mismatch')
    with zipfile.ZipFile(io.BytesIO(package)) as archive:
        for name in archive.namelist():
            if name.startswith('build/.NETFramework/v4.5/') and name.endswith('.dll'):
                relative = Path(name).relative_to('build/.NETFramework/v4.5')
                if '..' in relative.parts:
                    raise ValueError('Invalid reference archive path')
                target = destination / relative
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(archive.read(name))
    (destination / 'package.sha256').write_text(PACKAGE_SHA256 + '\n')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--project', type=Path, default=Path(__file__).resolve().parent.parent)
    parser.add_argument('--references', type=Path, required=True)
    parser.add_argument('--mono-root', type=Path, default=Path('/workspace/cloud-setup/mono'))
    parser.add_argument('--output', type=Path)
    parser.add_argument('--standalone', action='store_true')
    parser.add_argument('--download-references', action='store_true')
    args = parser.parse_args()
    project, refs, mono = args.project.resolve(), args.references.resolve(), args.mono_root.resolve()
    template = None
    if not args.standalone:
        template = project / 'Scripts' / 'SoftwareCleanup.ps1'
        if not template.is_file():
            parser.error('Missing Scripts/SoftwareCleanup.ps1; extract the complete source package')
    if args.download_references and not (refs / 'mscorlib.dll').is_file():
        download_references(refs)
    required = ['mscorlib', 'System', 'System.Core', 'System.Drawing', 'System.Windows.Forms']
    if not args.standalone:
        required += ['System.Management', 'Microsoft.CSharp', 'System.Xml']
    for name in required:
        if not (refs / (name + '.dll')).is_file():
            parser.error('Missing Microsoft reference ' + name + '; use --download-references')
    output = (args.output or project / 'build' / ('WinForms-BAT.exe' if args.standalone else 'WinForms.exe')).resolve()
    output.parent.mkdir(parents=True, exist_ok=True)
    env = dict(os.environ, MONO_PATH=str(mono / 'usr/lib/mono/4.5'), MONO_CFG_DIR=str(mono / 'etc'), MONO_GAC_PREFIX=str(mono / 'usr'))
    command = [str(mono / 'usr/bin/mono-sgen'), '--config', str(mono / 'etc/mono/config'),
               str(mono / 'usr/lib/mono/4.5/mcs.exe'), '-noconfig', '-nostdlib', '-langversion:5',
               '-nologo', '-target:winexe', '-platform:anycpu', '-optimize+', '-codepage:65001', '-out:' + str(output)]
    manifest=project/'app.manifest'
    if manifest.is_file():command += ['-win32manifest:'+str(manifest)]
    command += ['-r:' + str(refs / (name + '.dll')) for name in required]
    command += [str(path) for path in sorted((project / 'src').glob('*.cs'))]
    subprocess.run(command, env=env, check=True)
    # Script templates stay as reviewable files beside the EXE, not executable strings in it.
    if not args.standalone:
        target = output.parent / 'Scripts' / template.name
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(template, target)
    result = verify(output, refs)
    if result['reference_profile'].endswith('4.5'):
        result['reference_package_url'], result['reference_package_sha256'] = PACKAGE_URL, PACKAGE_SHA256
    else:
        result['reference_package_url']='https://api.nuget.org/v3-flatcontainer/microsoft.netframework.referenceassemblies.net48/1.0.3/microsoft.netframework.referenceassemblies.net48.1.0.3.nupkg'
        result['reference_package_sha256']='8a7e348538e7eb91351696911689f49e3d4f63f8bab517432bbe159b8b1104a2'
    result['reference_assembly_sha256'] = {name: hashlib.sha256((refs/(name+'.dll')).read_bytes()).hexdigest() for name in required}
    import json
    (output.parent / (output.stem + '.build.json')).write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n')
    print('PASS Windows reference owners:', result['framework_assemblies'], 'Stack:', result['stack_owners'])


if __name__ == '__main__':
    main()
