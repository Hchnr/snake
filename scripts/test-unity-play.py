#!/usr/bin/env python3
"""Exercise the actual scene in a temporary, offline Unity Play Mode project."""
from pathlib import Path
import json
import os
import shutil
import subprocess
import sys
import tempfile

root = Path(__file__).resolve().parent.parent
editor = Path(os.environ.get('UNITY_EDITOR', '/Applications/Unity/Hub/Editor/6000.0.85f2/Unity.app/Contents/MacOS/Unity'))
if not editor.is_file():
    sys.exit(f'Unity Editor not found: {editor}; set UNITY_EDITOR.')
with tempfile.TemporaryDirectory(prefix='snake-unity-play-') as directory:
    project = Path(directory) / 'project'
    project.mkdir()
    for name in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(root / 'unity' / name, project / name, ignore=shutil.ignore_patterns('ByteGame', 'ByteGame.meta', '.DS_Store'))
    shutil.copy2(root / 'test/v3/SnakePlayModeRegression.cs', project / 'Assets/Snake/Editor/SnakePlayModeRegression.cs')
    config_path = project / 'Assets/Resources/GameConfig.json'
    config = json.loads(config_path.read_text())
    config['allowDevLogin'] = False
    config_path.write_text(json.dumps(config))
    log = Path(directory) / 'unity.log'
    stamp = Path(directory) / 'passed'
    environment = {**os.environ, 'SNAKE_PLAY_TEST_STAMP': str(stamp)}
    # The scene renders procedural line geometry; Unity's null graphics device
    # can crash on it, so this Play Mode test requires the real graphics backend.
    command = [str(editor), '-batchmode', '-projectPath', str(project), '-executeMethod', 'SnakeClub.Tests.Editor.PlayModeRegression.Run', '-logFile', str(log)]
    print('Running Unity Play Mode regression in an isolated offline project...', flush=True)
    try:
        result = subprocess.run(command, env=environment, timeout=300, check=False)
    except subprocess.TimeoutExpired:
        result = None
    output = log.read_text(errors='replace') if log.exists() else ''
    if result is None or result.returncode != 0 or not stamp.exists():
        errors = [line for line in output.splitlines() if 'error CS' in line or 'FAIL Unity' in line or 'Exception:' in line]
        print('\n'.join(errors[-20:] or output.splitlines()[-100:]), file=sys.stderr)
        sys.exit(result.returncode if result is not None and result.returncode else 1)
    for line in output.splitlines():
        if line.startswith('PASS Unity '): print(line)
