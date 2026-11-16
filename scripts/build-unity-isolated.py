#!/usr/bin/env python3
"""Build a local Unity player without closing an already-open project."""
import argparse
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

root = Path(__file__).resolve().parent.parent
parser = argparse.ArgumentParser()
parser.add_argument('target', choices=('mac', 'webgl'))
parser.add_argument('--output', required=True, help='new player output path')
args = parser.parse_args()
editor = Path(os.environ.get('UNITY_EDITOR', '/Applications/Unity/Hub/Editor/6000.0.85f2/Unity.app/Contents/MacOS/Unity'))
if not editor.is_file():
    sys.exit(f'Unity Editor not found: {editor}; set UNITY_EDITOR.')
output = Path(args.output).expanduser().resolve()
if output.exists():
    sys.exit(f'Output already exists: {output}; choose a new path to preserve it.')
target = 'StandaloneOSX' if args.target == 'mac' else 'WebGL'
method = 'SnakeClub.Editor.SnakeBuilder.BuildMac' if args.target == 'mac' else 'SnakeClub.Editor.SnakeBuilder.BuildWebGL'
with tempfile.TemporaryDirectory(prefix='snake-unity-build-') as directory:
    project = Path(directory) / 'project'
    project.mkdir()
    for name in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(root / 'unity' / name, project / name, ignore=shutil.ignore_patterns('ByteGame', 'ByteGame.meta', '.DS_Store'))
    log = Path(directory) / 'build.log'
    stamp = Path(directory) / 'built'
    environment = {**os.environ, 'SNAKE_BUILD_OUTPUT': str(output), 'SNAKE_BUILD_STAMP': str(stamp)}
    command = [str(editor), '-batchmode', '-quit', '-projectPath', str(project), '-buildTarget', target, '-executeMethod', method, '-logFile', str(log)]
    print(f'Building {target} in an isolated project: {output}', flush=True)
    try:
        result = subprocess.run(command, env=environment, timeout=420, check=False)
    except subprocess.TimeoutExpired:
        sys.exit('Unity build timed out.')
    contents = log.read_text(errors='replace') if log.exists() else ''
    if result.returncode or not stamp.exists():
        lines = contents.splitlines()
        errors = [line for line in lines if 'error CS' in line or 'Exception:' in line or 'error :' in line]
        print('\n'.join(errors[-25:] or lines[-80:]), file=sys.stderr)
        sys.exit(result.returncode or 1)
    print(f'Build completed: {output}')
