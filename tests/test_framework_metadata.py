"""Cloud regression tests for the exact Windows EXE being published."""
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT/'tools'))
from verify_windows_refs import verify
REFS = Path(os.environ.get('NETFRAMEWORK_REFS', '/workspace/cloud-setup/netfx48'))
MONO = Path(os.environ.get('MONO_ROOT', '/workspace/cloud-setup/mono'))


class WindowsMetadataTests(unittest.TestCase):
    def test_published_binary_resolves_all_framework_type_owners(self):
        report = verify(ROOT/'BatAutomation.exe', REFS)
        self.assertEqual(report['stack_owners'], ['System'])

    def test_rejects_the_original_mono_stack_owner_mismatch(self):
        if not (MONO/'usr/bin/mono-sgen').is_file():
            self.skipTest('The negative Mono compiler fixture is cloud-only')
        env = dict(os.environ, MONO_PATH=str(MONO/'usr/lib/mono/4.5'), MONO_CFG_DIR=str(MONO/'etc'), MONO_GAC_PREFIX=str(MONO/'usr'))
        with tempfile.TemporaryDirectory(prefix='wrong-stack-profile-') as directory:
            src, binary = Path(directory)/'probe.cs', Path(directory)/'probe.dll'
            src.write_text('using System.Collections.Generic; public class Probe { public Stack<int> Items=new Stack<int>(); }')
            subprocess.run([str(MONO/'usr/bin/mono-sgen'), '--config', str(MONO/'etc/mono/config'), str(MONO/'usr/lib/mono/4.5/mcs.exe'), '-target:library', '-out:'+str(binary), str(src)], env=env, check=True)
            with self.assertRaisesRegex(ValueError, r'Stack`1 -> mscorlib.dll'):
                verify(binary, REFS)

    def test_build_report_matches_the_shipped_executable(self):
        report = json.loads((ROOT/'BUILD_INFO.json').read_text())
        self.assertEqual(report['sha256'], hashlib.sha256((ROOT/'BatAutomation.exe').read_bytes()).hexdigest())
        self.assertEqual(report['stack_owners'], ['System'])
        self.assertEqual(report['reference_profile'], 'Microsoft .NET Framework 4.8')

    def test_automation_sources_match_the_unchanged_toolkit_origin(self):
        origin = json.loads((ROOT/'SOURCE_PROVENANCE.json').read_text())
        self.assertEqual(origin['base_tag'], 'v0.5.19')
        for name, expected in origin['unchanged_sources_sha256'].items():
            self.assertEqual(hashlib.sha256((ROOT/name).read_bytes()).hexdigest(), expected, name)


if __name__ == '__main__':
    unittest.main(verbosity=2)
