import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
PIN = '915d4bf4f60ef0cd1c29316a8d67257185d609a7'


class WeChatIsolatedBuildTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        scripts = self.root / 'scripts'; scripts.mkdir()
        for name in ('build-wechat-isolated.py', 'WeChatIsolatedBuild.cs', 'unity_defines.py'):
            shutil.copy2(ROOT / 'scripts' / name, scripts / name)
        unity = self.root / 'unity'
        for folder in ('Assets/Resources', 'Assets/Snake/Editor', 'Assets/Editor/ReferenceSkins', 'Packages', 'ProjectSettings'):
            (unity / folder).mkdir(parents=True, exist_ok=True)
        (unity / 'Assets/Editor/ReferenceSkins/wenda.png').write_bytes(b'private fixture')
        (unity / 'Assets/Resources/GameConfig.json').write_text(json.dumps({
            'serverUrl': 'wss://example.com', 'apiUrl': 'https://example.com',
            'rewardedAdUnitId': 'private-ad-id', 'allowDevLogin': True, 'allowReferenceSkinPreview': True, 'experimentalStrengthPveServer': True,
        }))
        (unity / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 6000.0.85f2\n')
        (unity / 'ProjectSettings/ProjectSettings.asset').write_text('  scriptingDefineSymbols: {}\n  scriptingBackend: {}\n')
        self.sdk = self.root / 'sdk'; self.sdk.mkdir()
        (self.sdk / 'package.json').write_text('{"name":"com.qq.weixin.minigame","version":"fixture"}')
        subprocess.run(['git', 'init', '-q', str(self.sdk)], check=True)
        subprocess.run(['git', '-C', str(self.sdk), 'add', 'package.json'], check=True)
        subprocess.run(['git', '-C', str(self.sdk), '-c', 'user.name=Test', '-c', 'user.email=test@example.invalid', 'commit', '-qm', 'fixture'], check=True)
        revision = subprocess.check_output(['git', '-C', str(self.sdk), 'rev-parse', 'HEAD'], text=True).strip()
        (self.sdk / '.git/info/exclude').write_text('ignored.txt\n')
        (self.sdk / 'ignored.txt').write_text('must not enter the build')
        script = scripts / 'build-wechat-isolated.py'
        script.write_text(script.read_text().replace(PIN, revision))
        self.editor = self.root / 'fake-editor'
        self.editor.write_text('#!' + sys.executable + '''
import json, os, pathlib, sys
project = pathlib.Path(sys.argv[sys.argv.index('-projectPath') + 1])
pathlib.Path(sys.argv[sys.argv.index('-logFile') + 1]).write_text('fixture build log')
pathlib.Path(os.environ['SNAKE_TEST_CAPTURE']).write_text(json.dumps({
    'config': json.loads((project / 'Assets/Resources/GameConfig.json').read_text()),
    'hasSdk': (project / 'Packages/com.qq.weixin.minigame/package.json').is_file(),
    'hasIgnored': (project / 'Packages/com.qq.weixin.minigame/ignored.txt').exists(),
    'hasRefs': (project / 'Assets/Editor/ReferenceSkins').exists(),
    'settings': (project / 'ProjectSettings/ProjectSettings.asset').read_text(),
    'headless': '-batchmode' in sys.argv and '-nographics' in sys.argv,
    'appid': os.environ['SNAKE_WECHAT_APPID'], 'cdn': os.environ['SNAKE_WECHAT_CDN']
}))
out = pathlib.Path(os.environ['SNAKE_WECHAT_OUTPUT'])
(out / 'minigame').mkdir(parents=True)
(out / 'webgl').mkdir()
(out / 'minigame/game.json').write_text(json.dumps({'deviceOrientation': 'portrait'}))
(out / 'minigame/project.config.json').write_text(json.dumps({'appid': 'wrong' if os.environ.get('SNAKE_TEST_BAD_ARTIFACT') else os.environ['SNAKE_WECHAT_APPID'], 'compileType': 'game'}))
(out / 'minigame/game.js').write_text(os.environ['SNAKE_WECHAT_CDN'])
(out / 'minigame/dummy.wasm.br').write_bytes(b'fixture')
(out / 'webgl/dummy.data.unityweb.bin.txt').write_bytes(b'fixture')
pathlib.Path(os.environ['SNAKE_WECHAT_STAMP']).write_text('complete')
''')
        self.editor.chmod(0o755)
        self.capture = self.root / 'capture.json'
        self.env = dict(os.environ, UNITY_EDITOR=str(self.editor), SNAKE_TEST_CAPTURE=str(self.capture))
        self.output = self.root / 'output'

    def tearDown(self):
        self.temp.cleanup()

    def run_build(self, *extra, appid='wx0000000000000000', cdn='https://example.invalid/probe'):
        return subprocess.run([sys.executable, str(self.root / 'scripts/build-wechat-isolated.py'),
            '--sdk', str(self.sdk), '--appid', appid, '--cdn', cdn, '--output', str(self.output), *extra],
            env=self.env, capture_output=True, text=True)

    def test_headless_probe_is_isolated_and_does_not_copy_reference_art(self):
        result = self.run_build('--diagnostic', '--strength-pve-preview')
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        captured = json.loads(self.capture.read_text()); config = captured['config']
        self.assertTrue(captured['headless'] and captured['hasSdk'])
        self.assertIn('WebGL: SNAKE_WECHAT_WXSDK', captured['settings'])
        self.assertFalse(captured['hasRefs'] or captured['hasIgnored'] or config['allowDevLogin'] or config['allowReferenceSkinPreview'])
        self.assertEqual(config['apiUrl'], ''); self.assertEqual(config['serverUrl'], '')
        self.assertEqual(config['rewardedAdUnitId'], '')
        self.assertTrue(config['experimentalFixedPve'] and config['experimentalStrengthPve'])
        self.assertFalse(config['experimentalStrengthPveServer'] or config['experimentalFixedPveServer'])
        self.assertTrue(json.loads((self.output / 'snake-build-metadata.json').read_text())['diagnostic'])
        self.assertEqual(json.loads((self.root / 'unity/Assets/Resources/GameConfig.json').read_text())['apiUrl'], 'https://example.com')
        self.assertIn('scriptingDefineSymbols: {}', (self.root / 'unity/ProjectSettings/ProjectSettings.asset').read_text())

    def test_refuses_placeholders_unless_explicit_and_never_overwrites(self):
        self.assertNotEqual(self.run_build().returncode, 0)
        self.assertFalse(self.output.exists())
        self.output.mkdir(); marker = self.output / 'keep'; marker.write_text('unchanged')
        result = self.run_build('--diagnostic')
        self.assertNotEqual(result.returncode, 0); self.assertEqual(marker.read_text(), 'unchanged')
        self.assertFalse(self.capture.exists())

    def test_rejects_unsafe_cdn_and_modified_sdk(self):
        self.assertNotEqual(self.run_build('--diagnostic', cdn="https://example.invalid/a'\ncode").returncode, 0)
        (self.sdk / 'package.json').write_text('{"name":"com.qq.weixin.minigame","version":"modified"}')
        self.assertNotEqual(self.run_build('--diagnostic').returncode, 0)
        self.assertFalse(self.output.exists()); self.assertFalse(self.capture.exists())

    def test_rejects_wrong_artifact_and_preserves_build_log(self):
        self.env['SNAKE_TEST_BAD_ARTIFACT'] = '1'
        result = self.run_build('--diagnostic')
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('output validation failed', result.stderr)
        self.assertEqual((self.output / 'snake-unity-build.log').read_text(), 'fixture build log')
        self.assertFalse((self.output / 'snake-build-metadata.json').exists())


if __name__ == '__main__':
    unittest.main()
