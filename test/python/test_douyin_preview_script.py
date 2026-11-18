import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]


class PreviewScriptTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        scripts = self.root / 'scripts'
        scripts.mkdir()
        for name in ('build-douyin-preview.py', 'unity_defines.py', 'DouyinPreviewBuild.cs'):
            shutil.copy2(ROOT / 'scripts' / name, scripts / name)
        unity = self.root / 'unity'
        for folder in ('Assets/Resources', 'Assets/Snake/Editor', 'Assets/Editor/ReferenceSkins',
                       'Assets/Plugins/ByteGame/com.bytedance.starksdk', 'Packages', 'ProjectSettings'):
            (unity / folder).mkdir(parents=True, exist_ok=True)
        (unity / 'Assets/Editor/ReferenceSkins/wenda.png').write_bytes(b'local reference fixture')
        (unity / 'Assets/Plugins/ByteGame/com.bytedance.starksdk/package.json').write_text('{}')
        (unity / 'Assets/Resources/GameConfig.json').write_text(json.dumps({
            'serverUrl': 'ws://localhost', 'apiUrl': 'http://localhost',
            'allowDevLogin': True, 'allowReferenceSkinPreview': True
        }))
        (unity / 'ProjectSettings/ProjectSettings.asset').write_text('  scriptingDefineSymbols: {}\n  scriptingBackend: {}\n')
        self.editor = self.root / 'fake-editor'
        self.editor.write_text('''#!''' + sys.executable + '''
import json, os, pathlib, sys
project = pathlib.Path(sys.argv[sys.argv.index('-projectPath') + 1])
if os.environ.get('SNAKE_TEST_FAIL') == '1':
    pathlib.Path(sys.argv[sys.argv.index('-logFile') + 1]).write_text('native failure fixture\\nfull diagnostic tail\\n')
    sys.exit(7)
capture = pathlib.Path(os.environ['SNAKE_TEST_CAPTURE'])
capture.write_text(json.dumps({
    'settings': (project / 'ProjectSettings/ProjectSettings.asset').read_text(),
    'config': json.loads((project / 'Assets/Resources/GameConfig.json').read_text()),
    'referencePresent': (project / 'Assets/Editor/ReferenceSkins/wenda.png').exists(),
    'referenceOptIn': os.environ['SNAKE_DOUYIN_REFERENCE_PREVIEW'],
    'noGraphics': '-nographics' in sys.argv,
    'guard': '#error Douyin preview' in (project / 'Assets/Snake/Editor/DouyinPreviewBuild.cs').read_text()
}))
artifact = pathlib.Path(os.environ['SNAKE_DOUYIN_PREVIEW_OUTPUT']) / 'tt-minigame'
artifact.mkdir(parents=True)
(artifact / 'game.json').write_text('{}')
(artifact / 'project.config.json').write_text('{}')
pathlib.Path(os.environ['SNAKE_DOUYIN_PREVIEW_STAMP']).write_text(str(artifact))
''')
        self.editor.chmod(0o755)

    def tearDown(self):
        self.temp.cleanup()

    def build(self, include_reference=False, fixed_pve=False, no_graphics=False, graphics=False):
        capture = self.root / 'capture.json'
        output = self.root / ('internal' if include_reference else 'normal')
        command = [sys.executable, str(self.root / 'scripts/build-douyin-preview.py'),
                   '--appid', 'test-appid', '--output', str(output)]
        if include_reference:
            command.append('--include-reference-skins')
        if fixed_pve:
            command.append('--fixed-pve-preview')
        if no_graphics:
            command.append('--no-graphics')
        if graphics:
            command.append('--graphics')
        env = {**os.environ, 'UNITY_EDITOR': str(self.editor), 'SNAKE_TEST_CAPTURE': str(capture)}
        completed = subprocess.run(command, capture_output=True, text=True, env=env)
        self.assertEqual(completed.returncode, 0, completed.stdout + completed.stderr)
        return json.loads(capture.read_text())

    def test_default_is_offline_sdk_enabled_and_excludes_references(self):
        result = self.build()
        self.assertIn('WebGL: SNAKE_DOUYIN_TTSDK', result['settings'])
        self.assertTrue(result['guard'])
        self.assertEqual(result['config']['apiUrl'], '')
        self.assertEqual(result['config']['serverUrl'], '')
        self.assertFalse(result['config']['allowDevLogin'])
        self.assertFalse(result['config']['experimentalFixedPve'])
        self.assertFalse(result['config']['allowReferenceSkinPreview'])
        self.assertFalse(result['referencePresent'])
        self.assertEqual(result['referenceOptIn'], '0')
        self.assertTrue(result['noGraphics'])
        self.assertIn('scriptingDefineSymbols: {}', (self.root / 'unity/ProjectSettings/ProjectSettings.asset').read_text())

    def test_fixed_pve_preview_is_explicit_and_offline(self):
        result = self.build(fixed_pve=True)
        self.assertTrue(result['config']['experimentalFixedPve'])
        self.assertEqual(result['config']['apiUrl'], '')
        self.assertEqual(result['config']['serverUrl'], '')
        self.assertFalse(result['config']['allowDevLogin'])
        self.assertFalse(result['referencePresent'])

    def test_reference_art_requires_explicit_internal_opt_in(self):
        result = self.build(include_reference=True)
        self.assertTrue(result['referencePresent'])
        self.assertTrue(result['config']['allowReferenceSkinPreview'])
        self.assertEqual(result['referenceOptIn'], '1')
        self.assertIn('WebGL: SNAKE_DOUYIN_TTSDK', result['settings'])

    def test_no_graphics_is_forwarded_to_unity(self):
        result = self.build(no_graphics=True)
        self.assertTrue(result['noGraphics'])
        self.assertFalse(result['referencePresent'])

    def test_failed_build_preserves_private_log(self):
        env = {**os.environ, 'UNITY_EDITOR': str(self.editor), 'SNAKE_TEST_FAIL': '1', 'TMPDIR': str(self.root)}
        completed = subprocess.run([sys.executable, str(self.root / 'scripts/build-douyin-preview.py'),
                                    '--appid', 'test-appid', '--output', str(self.root / 'failed')],
                                   capture_output=True, text=True, env=env)
        self.assertEqual(completed.returncode, 7)
        logs = list(self.root.glob('snake-douyin-failure-*.log'))
        self.assertEqual(len(logs), 1)
        self.assertIn('full diagnostic tail', logs[0].read_text())
        self.assertIn(str(logs[0]), completed.stderr)
        self.assertEqual(logs[0].stat().st_mode & 0o077, 0)

    def test_graphics_requires_explicit_opt_in(self):
        result = self.build(graphics=True)
        self.assertFalse(result['noGraphics'])


if __name__ == '__main__':
    unittest.main()
