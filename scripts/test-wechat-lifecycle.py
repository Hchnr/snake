#!/usr/bin/env python3
"""Run synthetic WeChat lifecycle callbacks in a disposable headless Unity editor.

The fake API tests wiring and cleanup only; it does not emulate WeChat devices.
"""
from pathlib import Path
import os
import shutil
import subprocess
import sys
import tempfile
from unity_defines import enable_webgl_define

root = Path(__file__).resolve().parent.parent
editor = Path(os.environ.get('UNITY_EDITOR', '/Applications/Unity/Hub/Editor/6000.0.85f2/Unity.app/Contents/MacOS/Unity'))
if not editor.is_file():
    sys.exit(f'Unity Editor not found: {editor}')
with tempfile.TemporaryDirectory(prefix='snake-wechat-lifecycle-test-') as directory:
    temp = Path(directory); project = temp / 'project'; project.mkdir()
    for name in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(root / 'unity' / name, project / name,
                        ignore=shutil.ignore_patterns('ByteGame', 'ByteGame.meta', 'ReferenceSkins', 'ReferenceSkins.meta', '.DS_Store'))
    settings = project / 'ProjectSettings/ProjectSettings.asset'
    try:
        source = enable_webgl_define(settings.read_text(), 'SNAKE_WECHAT_WXSDK')
        settings.write_text(enable_webgl_define(source, 'SNAKE_WECHAT_TEST'))
    except ValueError as error:
        sys.exit(f'Cannot configure the WeChat lifecycle fixture: {error}')
    shutil.copy2(root / 'test/v3/FakeWeChatSdk.cs', project / 'Assets/Snake/FakeWeChatSdk.cs')
    shutil.copy2(root / 'test/v3/WeChatLifecycleRegression.cs', project / 'Assets/Snake/Editor/WeChatLifecycleRegression.cs')
    log = temp / 'unity.log'; stamp = temp / 'passed'
    env = dict(os.environ, SNAKE_WECHAT_TEST_STAMP=str(stamp))
    cmd = [str(editor), '-batchmode', '-nographics', '-quit', '-projectPath', str(project), '-buildTarget', 'WebGL',
           '-executeMethod', 'SnakeClub.Tests.Editor.WeChatLifecycleRegression.Run', '-logFile', str(log)]
    print('Running simulated WeChat lifecycle callbacks in headless Unity...', flush=True)
    try:
        result = subprocess.run(cmd, env=env, timeout=300, check=False)
    except subprocess.TimeoutExpired:
        sys.exit('WeChat lifecycle fixture timed out.')
    lines = log.read_text(errors='replace').splitlines() if log.exists() else []
    if result.returncode or not stamp.exists():
        errors = [line for line in lines if 'error CS' in line or 'WECHAT_LIFECYCLE_TEST_FAILED' in line]
        print('\n'.join(errors[-20:] or lines[-80:]), file=sys.stderr)
        sys.exit(result.returncode or 1)
    for line in lines:
        if line.startswith('PASS Unity '): print(line)
