import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]


class IsolatedBuildTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        (self.root / 'scripts').mkdir()
        shutil.copy2(ROOT / 'scripts/build-unity-isolated.py', self.root / 'scripts/build-unity-isolated.py')
        for name in ('Assets/Resources', 'Assets/Editor/ReferenceSkins', 'Packages', 'ProjectSettings'):
            (self.root / 'unity' / name).mkdir(parents=True, exist_ok=True)
        (self.root / 'unity/Assets/Editor/ReferenceSkins/local.png').write_bytes(b'fixture')
        self.config = self.root / 'unity/Assets/Resources/GameConfig.json'
        self.config.write_text(json.dumps({'serverUrl': 'ws://local', 'apiUrl': 'http://local', 'allowDevLogin': True}))
        self.editor = self.root / 'fake-editor'
        self.editor.write_text('#!' + sys.executable + '''
import json, os, pathlib, sys
project = pathlib.Path(sys.argv[sys.argv.index('-projectPath') + 1])
pathlib.Path(os.environ['SNAKE_TEST_CAPTURE']).write_text(json.dumps({
    'args': sys.argv[1:],
    'config': json.loads((project / 'Assets/Resources/GameConfig.json').read_text()),
    'reference': (project / 'Assets/Editor/ReferenceSkins/local.png').exists()
}))
pathlib.Path(os.environ['SNAKE_BUILD_STAMP']).write_text('ok')
''')
        self.editor.chmod(0o755)

    def tearDown(self):
        self.temp.cleanup()

    def test_fixed_preview_requires_offline_practice(self):
        result = subprocess.run([sys.executable, str(self.root / 'scripts/build-unity-isolated.py'), 'webgl',
                                 '--output', str(self.root / 'out'), '--fixed-pve-preview'], capture_output=True, text=True)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('requires --offline-practice', result.stderr)

    def test_headless_fixed_preview_is_offline_and_does_not_modify_source(self):
        capture = self.root / 'capture.json'
        env = {**os.environ, 'UNITY_EDITOR': str(self.editor), 'SNAKE_TEST_CAPTURE': str(capture)}
        result = subprocess.run([sys.executable, str(self.root / 'scripts/build-unity-isolated.py'), 'webgl',
                                 '--output', str(self.root / 'out'), '--offline-practice', '--fixed-pve-preview', '--no-graphics'],
                                env=env, capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        data = json.loads(capture.read_text())
        self.assertIn('-nographics', data['args'])
        self.assertEqual(data['config']['apiUrl'], '')
        self.assertEqual(data['config']['serverUrl'], '')
        self.assertFalse(data['config']['allowDevLogin'])
        self.assertTrue(data['config']['experimentalFixedPve'])
        self.assertFalse(data['reference'])
        self.assertEqual(json.loads(self.config.read_text())['apiUrl'], 'http://local')

    def test_local_strength_requires_isolation_and_excludes_other_modes(self):
        base = [sys.executable, str(self.root / 'scripts/build-unity-isolated.py'), 'webgl', '--output', str(self.root / 'out')]
        option = ['--local-strength-pve-api', 'http://127.0.0.1:2568']
        for extra in ([], ['--offline-practice', '--fixed-pve-preview'],
                      ['--offline-practice', '--strength-pve-preview'], ['--offline-practice', '--comparison-preview'],
                      ['--offline-practice', '--fixed-pve-local-api', 'http://127.0.0.1:2569']):
            with self.subTest(extra=extra):
                result = subprocess.run(base + option + extra, capture_output=True, text=True)
                self.assertNotEqual(result.returncode, 0)
                self.assertIn('--local-strength-pve-api requires', result.stderr)

    def test_local_strength_rejects_non_loopback_or_non_origin_addresses(self):
        base = [sys.executable, str(self.root / 'scripts/build-unity-isolated.py'), 'webgl', '--output', str(self.root / 'out'), '--offline-practice', '--local-strength-pve-api']
        for address in ('http://localhost:2568', 'https://127.0.0.1:2568', 'http://127.0.0.2:2568',
                        'http://user@127.0.0.1:2568', 'http://127.0.0.1:2568/path',
                        'http://127.0.0.1:2568/?x=1', 'http://127.0.0.1:2568/#x', 'http://127.0.0.1', 'http://127.0.0.1:70000'):
            with self.subTest(address=address):
                result = subprocess.run(base + [address], capture_output=True, text=True)
                self.assertNotEqual(result.returncode, 0)
                self.assertIn('must be http://127.0.0.1:PORT', result.stderr)

    def test_local_strength_config_is_explicit_and_does_not_modify_source(self):
        capture = self.root / 'capture-local.json'
        env = {**os.environ, 'UNITY_EDITOR': str(self.editor), 'SNAKE_TEST_CAPTURE': str(capture)}
        result = subprocess.run([sys.executable, str(self.root / 'scripts/build-unity-isolated.py'), 'webgl',
                                 '--output', str(self.root / 'out'), '--offline-practice', '--no-graphics',
                                 '--local-strength-pve-api', 'http://127.0.0.1:2568'], env=env, capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        data = json.loads(capture.read_text())
        self.assertEqual(data['config']['apiUrl'], 'http://127.0.0.1:2568')
        self.assertEqual(data['config']['serverUrl'], '')
        self.assertTrue(data['config']['allowDevLogin'])
        self.assertTrue(data['config']['experimentalLocalStrengthPveServer'])
        for key in ('experimentalFixedPve', 'experimentalStrengthPve', 'experimentalFixedPveServer', 'experimentalStrengthPveServer', 'experimentalComparison'):
            self.assertFalse(data['config'][key], key)
        self.assertFalse(data['reference'])
        self.assertIn('-nographics', data['args'])
        self.assertEqual(json.loads(self.config.read_text())['apiUrl'], 'http://local')


if __name__ == '__main__':
    unittest.main()
