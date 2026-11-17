import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'scripts'))
from unity_defines import enable_webgl_define


class UnityDefineTests(unittest.TestCase):
    def test_empty_map_and_other_settings(self):
        source = 'PlayerSettings:\n  scriptingDefineSymbols: {}\n  additionalCompilerArguments: {}\n'
        result = enable_webgl_define(source, 'SNAKE_DOUYIN_TTSDK')
        self.assertEqual(result, 'PlayerSettings:\n  scriptingDefineSymbols:\n    WebGL: SNAKE_DOUYIN_TTSDK\n  additionalCompilerArguments: {}\n')
        self.assertEqual(enable_webgl_define(result, 'SNAKE_DOUYIN_TTSDK'), result)

    def test_preserves_existing_defines_and_other_platforms(self):
        source = '  scriptingDefineSymbols:\n    Android: OTHER\n    WebGL: "FOO;BAR"\n  scriptingBackend: {}\n'
        result = enable_webgl_define(source, 'SNAKE_DOUYIN_TTSDK')
        self.assertIn('    Android: OTHER\n', result)
        self.assertIn('    WebGL: "FOO;BAR;SNAKE_DOUYIN_TTSDK"\n', result)
        self.assertEqual(enable_webgl_define(result, 'SNAKE_DOUYIN_TTSDK'), result)

    def test_new_webgl_entry_and_invalid_inputs(self):
        source = '  scriptingDefineSymbols:\n    Android: OTHER\n  scriptingBackend: {}\n'
        result = enable_webgl_define(source, 'SNAKE_DOUYIN_TTSDK')
        self.assertIn('    WebGL: SNAKE_DOUYIN_TTSDK\n    Android: OTHER', result)
        for bad in ('', '  scriptingDefineSymbols: {WebGL: FOO}\n', '  scriptingDefineSymbols:\n    WebGL: [FOO]\n  x: 1\n'):
            with self.assertRaises(ValueError):
                enable_webgl_define(bad, 'SNAKE_DOUYIN_TTSDK')
        with self.assertRaises(ValueError):
            enable_webgl_define(source, 'INVALID;OTHER')

    def test_actual_project_settings_are_supported(self):
        settings = Path(__file__).resolve().parents[2] / 'unity/ProjectSettings/ProjectSettings.asset'
        original = settings.read_text()
        changed = enable_webgl_define(original, 'SNAKE_DOUYIN_TTSDK')
        self.assertIn('    WebGL: SNAKE_DOUYIN_TTSDK', changed)
        self.assertEqual(settings.read_text(), original)


if __name__ == '__main__':
    unittest.main()
