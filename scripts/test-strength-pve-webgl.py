#!/usr/bin/env python3
"""Compare sampled strength-preview vectors in desktop .NET and headless Unity WebGL.

The diagnostic is injected only into a temporary project. It does not verify
Douyin devices, fairness, production readiness, or every input and seed.
"""
from functools import partial
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import threading

root = Path(__file__).resolve().parent.parent
editor = Path(os.environ.get('UNITY_EDITOR', '/Applications/Unity/Hub/Editor/6000.0.85f2/Unity.app/Contents/MacOS/Unity'))
chrome = Path(os.environ.get('SNAKE_CHROME', '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome'))
if not editor.is_file() or not chrome.is_file():
    sys.exit('Unity Editor or Chrome not found; set UNITY_EDITOR and SNAKE_CHROME to their executable paths.')

pattern = re.compile(r'strength=([0-9a-f:,]+)')
recovery_pattern = re.compile(r'(?<![a-z-])recovery=([0-9a-f:,]+)')
safe_recovery_pattern = re.compile(r'safe-recovery=([0-9a-f:,]+)')
print('Computing the desktop .NET reference...', flush=True)
reference = subprocess.run(['dotnet', 'run', '--project', str(root / 'test/v3/Snake.V3.Core.Tests.csproj'), '--', 'strength-pve-vector'],
                           text=True, capture_output=True, check=True, timeout=180).stdout
match = pattern.search(reference)
if not match:
    sys.exit('Desktop reference vector missing.')
expected = match.group(1)
if len(expected.split(',')) != 18:
    sys.exit('Desktop strength matrix is incomplete.')
coverage = 0
for case in expected.split(','):
    fields = case.split(':')
    if len(fields) != 6:
        sys.exit('Desktop strength matrix case is malformed.')
    coverage |= int(fields[4])
if coverage != 31:
    sys.exit('Desktop cases did not observe pickup spawn, player bite, pickup buff, loop buff and level growth.')
recovery_reference = subprocess.run(['dotnet', 'run', '--project', str(root / 'test/v3/Snake.V3.Core.Tests.csproj'), '--', 'strength-recovery-vector'],
                                    text=True, capture_output=True, check=True, timeout=180).stdout
recovery_match = recovery_pattern.search(recovery_reference)
if not recovery_match or len(recovery_match.group(1).split(',')) != 16:
    sys.exit('Desktop recovery matrix is incomplete.')
expected_recovery = recovery_match.group(1)
# Captured from the v4 implementation before the safe-placement variant.
# Comparing both runtimes only with each other would miss a shared regression.
v4_golden = (
    '2:0:0:1800:3:296f9ac0e06bd105,2:0:1:1800:0:1c6d40846bb499df,'
    '2:1:0:1200:0:cf1a7b9bc4beaaa6,2:1:1:1200:0:4515b11d59e7e36c,'
    '68:0:0:1800:0:d4c2493cff8017f1,68:0:1:1800:3:0b1e63fe63d6b97f,'
    '68:1:0:1200:7:90c856bffc4af5b7,68:1:1:1200:0:6d8c4a0e72422f37,'
    '73:0:0:1800:0:1df1751bdb92d449,73:0:1:1800:0:9e19330aaf7bfb88,'
    '73:1:0:1200:0:840a7b4d3edce4b6,73:1:1:1200:3:2cb6b78ca480d66c,'
    '105:0:0:1800:0:b36ffe6c82e3aab1,105:0:1:1800:15:62ef755a2a25f3e1,'
    '105:1:0:1200:0:55ca8d74a169c331,105:1:1:1200:15:bc04c5bf941a69f6'
)
if expected_recovery != v4_golden:
    sys.exit('MISMATCH: v4 recovery fingerprints changed from the preserved reference.')
recovery_coverage = 0
for case in expected_recovery.split(','):
    fields = case.split(':')
    if len(fields) != 6:
        sys.exit('Desktop recovery matrix case is malformed.')
    recovery_coverage |= int(fields[4])
if recovery_coverage != 15:
    sys.exit('Desktop recovery matrix did not observe activation, player/AI pickup, and a second activation.')
safe_reference = subprocess.run(['dotnet', 'run', '--project', str(root / 'test/v3/Snake.V3.Core.Tests.csproj'), '--', 'strength-safe-recovery-vector'],
                                text=True, capture_output=True, check=True, timeout=180).stdout
safe_match = safe_recovery_pattern.search(safe_reference)
if not safe_match or len(safe_match.group(1).split(',')) != 24:
    sys.exit('Desktop safe-recovery matrix is incomplete.')
expected_safe = safe_match.group(1)
safe_coverage = 0
for case in expected_safe.split(','):
    fields = case.split(':')
    if len(fields) != 6:
        sys.exit('Desktop safe-recovery matrix case is malformed.')
    safe_coverage |= int(fields[4])
if safe_coverage != 15:
    sys.exit('Desktop safe-recovery matrix did not observe activation, player/AI pickup, and a second activation.')

class Handler(SimpleHTTPRequestHandler):
    def guess_type(self, path):
        path = str(path)
        if path.endswith('.gz'):
            path = path[:-3]
        return super().guess_type(path)
    def end_headers(self):
        if self.path.split('?', 1)[0].endswith('.gz'):
            self.send_header('Content-Encoding', 'gzip')
        self.send_header('Cache-Control', 'no-store')
        super().end_headers()
    def log_message(self, format, *args):
        pass

with tempfile.TemporaryDirectory(prefix='snake-strength-webgl-') as directory:
    temp = Path(directory)
    project = temp / 'project'; project.mkdir()
    for name in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(root / 'unity' / name, project / name,
                        ignore=shutil.ignore_patterns('ByteGame', 'ByteGame.meta', 'ReferenceSkins', 'ReferenceSkins.meta', '.DS_Store'))
    config_path = project / 'Assets/Resources/GameConfig.json'
    config = json.loads(config_path.read_text())
    config.update(serverUrl='', apiUrl='', allowDevLogin=False, allowReferenceSkinPreview=False, experimentalFixedPve=False)
    config_path.write_text(json.dumps(config, ensure_ascii=False, indent=2) + '\n')
    target = project / 'Assets/Snake'
    shutil.copy2(root / 'test/v3/StrengthPveReplayVector.cs', target / 'StrengthPveReplayVector.cs')
    (target / 'FixedVectorDiagnostic.cs').write_text('''using UnityEngine;
using SnakeClub.Tests;
internal static class FixedVectorDiagnostic {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Run() { Debug.Log("SNAKE_FIXED_VECTOR " + StrengthPveReplayVector.Compute() + "|" + StrengthPveReplayVector.ComputeRecovery() + "|" + StrengthPveReplayVector.ComputeSafeRecovery()); }
}
''')
    output = temp / 'webgl'; stamp = temp / 'built'; log = temp / 'build.log'
    env = dict(os.environ, SNAKE_BUILD_OUTPUT=str(output), SNAKE_BUILD_STAMP=str(stamp), SNAKE_INCLUDE_REFERENCE_ASSETS='0')
    command = [str(editor), '-batchmode', '-nographics', '-quit', '-projectPath', str(project), '-buildTarget', 'WebGL',
               '-executeMethod', 'SnakeClub.Editor.SnakeBuilder.BuildWebGL', '-logFile', str(log)]
    print('Building an isolated, non-graphical WebGL diagnostic...', flush=True)
    result = subprocess.run(command, env=env, timeout=420, check=False)
    if result.returncode or not stamp.exists():
        lines = log.read_text(errors='replace').splitlines() if log.exists() else []
        errors = [line for line in lines if 'error CS' in line or 'Exception:' in line or 'error :' in line]
        print('\n'.join(errors[-25:] or lines[-80:]), file=sys.stderr)
        sys.exit(result.returncode or 1)
    server = ThreadingHTTPServer(('127.0.0.1', 0), partial(Handler, directory=str(output)))
    thread = threading.Thread(target=server.serve_forever, daemon=True); thread.start()
    try:
        print('Running the diagnostic in muted headless Chrome...', flush=True)
        result = subprocess.run(['node', str(root / 'scripts/fixed-pve-webgl-probe.cjs'), str(chrome),
                                 'http://127.0.0.1:' + str(server.server_port) + '/'],
                                capture_output=True, text=True, timeout=240, check=False)
        if result.returncode:
            sys.exit('Headless diagnostic failed: ' + result.stderr[-3000:])
        actual_match = pattern.search(result.stdout)
        if not actual_match:
            sys.exit('WebGL integer vector missing: ' + result.stdout[-1000:])
        actual = actual_match.group(1)
        if len(actual.split(',')) != 18:
            sys.exit('WebGL strength matrix is incomplete.')
        actual_recovery_match = recovery_pattern.search(result.stdout)
        if not actual_recovery_match or len(actual_recovery_match.group(1).split(',')) != 16:
            sys.exit('WebGL recovery matrix is incomplete.')
        actual_recovery = actual_recovery_match.group(1)
        actual_safe_match = safe_recovery_pattern.search(result.stdout)
        if not actual_safe_match or len(actual_safe_match.group(1).split(',')) != 24:
            sys.exit('WebGL safe-recovery matrix is incomplete.')
        actual_safe = actual_safe_match.group(1)
        print('.NET:  ' + expected + '\nWebGL: ' + actual)
        if actual != expected:
            for desktop, webgl in zip(expected.split(','), actual.split(',')):
                if desktop != webgl:
                    print('First differing matrix case: .NET ' + desktop + ' / WebGL ' + webgl)
                    break
            sys.exit('MISMATCH: strength preview differs across runtimes; do not enable replay-based rewards.')
        print('.NET recovery:  ' + expected_recovery + '\nWebGL recovery: ' + actual_recovery)
        if actual_recovery != expected_recovery:
            for desktop, webgl in zip(expected_recovery.split(','), actual_recovery.split(',')):
                if desktop != webgl:
                    print('First differing recovery case: .NET ' + desktop + ' / WebGL ' + webgl)
                    break
            sys.exit('MISMATCH: local recovery simulation differs across runtimes.')
        print('.NET safe recovery:  ' + expected_safe + '\nWebGL safe recovery: ' + actual_safe)
        if actual_safe != expected_safe:
            for desktop, webgl in zip(expected_safe.split(','), actual_safe.split(',')):
                if desktop != webgl:
                    print('First differing safe-recovery case: .NET ' + desktop + ' / WebGL ' + webgl)
                    break
            sys.exit('MISMATCH: local safe-recovery simulation differs across runtimes.')
        print('MATCH: 18 strength, 16 v4 recovery and 24 v5 recovery matches, including selected hidden state; Douyin devices, arbitrary inputs and production rewards remain unverified.')
    finally:
        server.shutdown(); server.server_close(); thread.join(timeout=5)
