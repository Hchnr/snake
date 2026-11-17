#!/usr/bin/env python3
"""Compare PvE simulation fingerprints in .NET and Unity Editor, without a game window.

This checks one editor/runtime/architecture only. It does not validate WebGL,
platform devices, gameplay fairness or production reward verification.
"""
from pathlib import Path
import os
import shutil
import subprocess
import sys
import tempfile

root = Path(__file__).resolve().parent.parent
editor = Path(os.environ.get('UNITY_EDITOR', '/Applications/Unity/Hub/Editor/6000.0.85f2/Unity.app/Contents/MacOS/Unity'))
if not editor.is_file():
    sys.exit(f'Unity Editor not found: {editor}; set UNITY_EDITOR.')
expected = subprocess.run(['dotnet', 'run', '--project', str(root / 'test/v3/Snake.V3.Core.Tests.csproj'), '--', 'pve-vector'],
                          check=True, capture_output=True, text=True).stdout.strip().splitlines()[-1]
with tempfile.TemporaryDirectory(prefix='snake-pve-runtime-') as directory:
    temp = Path(directory)
    project = temp / 'project'; project.mkdir()
    for name in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(root / 'unity' / name, project / name, ignore=shutil.ignore_patterns('ByteGame', 'ByteGame.meta', '.DS_Store'))
    target = project / 'Assets/Snake/Editor'
    for name in ('PveReplayVector.cs', 'PveVectorUnity.cs'):
        shutil.copy2(root / 'test/v3' / name, target / name)
    log = temp / 'unity.log'; output = temp / 'vector'
    env = dict(os.environ, SNAKE_PVE_VECTOR_OUTPUT=str(output))
    command = [str(editor), '-batchmode', '-nographics', '-quit', '-projectPath', str(project),
               '-executeMethod', 'SnakeClub.Tests.Editor.PveVectorUnity.Run', '-logFile', str(log)]
    print('Comparing .NET with an isolated, non-graphical Unity Editor...', flush=True)
    try:
        result = subprocess.run(command, env=env, timeout=300, check=False)
    except subprocess.TimeoutExpired:
        sys.exit('Unity comparison timed out.')
    contents = log.read_text(errors='replace') if log.exists() else ''
    if result.returncode != 0 or not output.exists():
        print('\n'.join(contents.splitlines()[-80:]), file=sys.stderr)
        sys.exit(result.returncode or 1)
    actual = output.read_text().strip()
    print(f'.NET: {expected}\nUnity Editor: {actual}')
    if actual != expected:
        sys.exit('MISMATCH: PvE simulation is not bit-identical in these environments; do not enable replay-based rewards.')
    print('MATCH: tested editor fingerprint only; WebGL and target devices remain unverified.')
