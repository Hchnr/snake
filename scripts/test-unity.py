#!/usr/bin/env python3
"""Run Unity client regressions in an isolated copy, alongside an open editor."""
from pathlib import Path
import os
import shutil
import subprocess
import sys
import tempfile

root = Path(__file__).resolve().parent.parent
editor = Path(os.environ.get('UNITY_EDITOR', '/Applications/Unity/Hub/Editor/6000.0.85f2/Unity.app/Contents/MacOS/Unity'))
if not editor.is_file():
    sys.exit(f'Unity Editor not found: {editor}; set UNITY_EDITOR to the executable path.')
with tempfile.TemporaryDirectory(prefix='snake-unity-regression-') as directory:
    project = Path(directory) / 'project'
    project.mkdir()
    excluded = ['ByteGame', 'ByteGame.meta', '.DS_Store']
    if os.environ.get('SNAKE_TEST_EXCLUDE_REFERENCES') == '1':
        excluded.extend(['ReferenceSkins', 'ReferenceSkins.meta'])
    for name in ('wenda', 'raddy'):
        if os.environ.get('SNAKE_TEST_EXCLUDE_' + name.upper() + '_LAYERS') == '1':
            excluded.extend([name + '_layers', name + '_layers.meta'])
    for name in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(root / 'unity' / name, project / name, ignore=shutil.ignore_patterns(*excluded))
    shutil.copy2(root / 'test/v3/SnakeClientRegression.cs', project / 'Assets/Snake/Editor/SnakeClientRegression.cs')
    log = Path(directory) / 'unity.log'
    stamp = Path(directory) / 'passed'
    environment = os.environ.copy()
    environment['SNAKE_UNITY_TEST_STAMP'] = str(stamp)
    command = [str(editor), '-batchmode', '-nographics', '-quit', '-projectPath', str(project), '-executeMethod', 'SnakeClub.Tests.Editor.ClientRegression.Run', '-logFile', str(log)]
    print('Running Unity client regressions in an isolated project...', flush=True)
    try:
        result = subprocess.run(command, env=environment, timeout=300, check=False)
    except subprocess.TimeoutExpired:
        print('Unity client regression timed out.', file=sys.stderr)
        result = None
    output = log.read_text(errors='replace') if log.exists() else ''
    if result is None or result.returncode != 0 or not stamp.exists():
        print('\n'.join(output.splitlines()[-100:]), file=sys.stderr)
        sys.exit(result.returncode if result is not None and result.returncode else 1)
    for line in output.splitlines():
        if line.startswith('PASS Unity '):
            print(line)
