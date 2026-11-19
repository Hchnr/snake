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
aligned_start_pattern = re.compile(r'aligned-start=([0-9a-f:,]+)')
capped_start_pattern = re.compile(r'capped-start=([0-9a-f:,]+)')
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
# Captured before the aligned-start variant was introduced.
v5_golden = (
    '2:0:0:1800:3:1e1cff0d3046a639,2:0:1:1800:0:46fd9d29756febdb,2:0:2:1800:0:6694c1bdc138cf1d,'
    '2:1:0:1200:0:154de1e3c5e0e912,2:1:1:1200:0:76639ebc3a175cbc,2:1:2:1139:0:7d1b1665eee31359,'
    '68:0:0:1800:0:75de0c59b6a082d9,68:0:1:1800:3:357d54f9dcde1cc7,68:0:2:1800:0:bd35afb861a929d1,'
    '68:1:0:1200:5:405205a2ea487ebc,68:1:1:1200:0:04ca865c114c186b,68:1:2:1200:0:84e8e78cfcdcb7b5,'
    '105:0:0:1800:0:7a164c767ae5ca89,105:0:1:1800:15:81587e17ffc9dced,105:0:2:1800:0:3559eb722f26dfcf,'
    '105:1:0:1200:0:fad7cdc40873e0e9,105:1:1:1200:15:fc66c92f1afee3ba,105:1:2:1200:0:2ea9c9e60ba116c0,'
    '192:0:0:1800:7:a5a5b36090d5b26f,192:0:1:1800:0:07d294a2ec65cec5,192:0:2:1800:1:c0ba158c90af445c,'
    '192:1:0:1200:0:a16c07a36418ec4d,192:1:1:1200:0:87979811ef464326,192:1:2:227:3:cb9285bf6a385465'
)
if expected_safe != v5_golden:
    sys.exit('MISMATCH: v5 safe-recovery fingerprints changed from the preserved reference.')
safe_coverage = 0
for case in expected_safe.split(','):
    fields = case.split(':')
    if len(fields) != 6:
        sys.exit('Desktop safe-recovery matrix case is malformed.')
    safe_coverage |= int(fields[4])
if safe_coverage != 15:
    sys.exit('Desktop safe-recovery matrix did not observe activation, player/AI pickup, and a second activation.')
aligned_reference = subprocess.run(['dotnet', 'run', '--project', str(root / 'test/v3/Snake.V3.Core.Tests.csproj'), '--', 'strength-aligned-start-vector'],
                                   text=True, capture_output=True, check=True, timeout=180).stdout
aligned_match = aligned_start_pattern.search(aligned_reference)
if not aligned_match or len(aligned_match.group(1).split(',')) != 40:
    sys.exit('Desktop aligned-start matrix is incomplete.')
expected_aligned = aligned_match.group(1)
# Captured from the v6 main-branch implementation before v7 was introduced.
v6_golden = (
    '2:0:0:1800:3:d8e2da8f4b75086b,2:0:1:1800:0:af6e451dd029cf2f,2:0:2:1800:0:a241bebc22b4522b,2:0:3:1800:0:00c1b7ecb820cd6e,'
    '2:1:0:1200:0:c09041fa9fb89b58,2:1:1:1200:0:6fdf436316f99443,2:1:2:1200:0:af0aaea7a5ee0575,2:1:3:1200:0:9502927b285977a0,'
    '68:0:0:1800:7:f50af9c22b924061,68:0:1:1800:0:bfbe22b8dbd879bb,68:0:2:1800:0:a5344776d8be1d40,68:0:3:1800:0:dfcf3a6daaafcf34,'
    '68:1:0:1200:0:564603937d101c33,68:1:1:1200:0:9c69cfa6f5ffeb7b,68:1:2:1200:0:0d57a99a69282a63,68:1:3:1200:0:1533a647170b2402,'
    '92:0:0:1800:7:4c09a863075694a4,92:0:1:1800:3:bdd4440596c9add8,92:0:2:1800:0:95c015abca746945,92:0:3:1800:11:59958fc01f52f25c,'
    '92:1:0:1200:3:1cd2a1eaea003ab1,92:1:1:1200:7:fce20006b9a7b598,92:1:2:1200:0:3a3a102981fb3d47,92:1:3:1200:0:36d6f7725dc6dc92,'
    '105:0:0:1800:0:5c3169b7c05ae7c4,105:0:1:1800:0:cd0b722ae4d0a932,105:0:2:1800:0:abd888bfa0fd0b41,105:0:3:1800:0:6e12909b538bc7e8,'
    '105:1:0:1200:0:62b67eb11acf0ada,105:1:1:1200:0:a77fbf17c2dcf47d,105:1:2:1200:0:eb7fffdf85972f79,105:1:3:1200:0:0b55180bb41d5e77,'
    '192:0:0:1800:7:4f75282e5f395c16,192:0:1:1800:0:7a00a9c4766d9d2d,192:0:2:1800:5:bb94bf8f8238da61,192:0:3:1800:7:7d979f9354b5ee2c,'
    '192:1:0:1200:3:5211e7f59e3ef025,192:1:1:1200:0:92bf24e44aed57da,192:1:2:220:0:76394c4c1429c526,192:1:3:1200:0:ea03cc61d111064b'
)
if expected_aligned != v6_golden:
    sys.exit('MISMATCH: v6 aligned-start fingerprints changed from the preserved reference.')
aligned_coverage = 0
for case in expected_aligned.split(','):
    fields = case.split(':')
    if len(fields) != 6:
        sys.exit('Desktop aligned-start matrix case is malformed.')
    aligned_coverage |= int(fields[4])
if aligned_coverage != 15:
    sys.exit('Desktop aligned-start matrix did not observe activation, player/AI pickup, and a second activation.')
capped_reference = subprocess.run(['dotnet', 'run', '--project', str(root / 'test/v3/Snake.V3.Core.Tests.csproj'), '--', 'strength-capped-start-vector'],
                                  text=True, capture_output=True, check=True, timeout=180).stdout
capped_match = capped_start_pattern.search(capped_reference)
if not capped_match or len(capped_match.group(1).split(',')) != 70:
    sys.exit('Desktop capped-start matrix is incomplete.')
expected_capped = capped_match.group(1)
capped_coverage = 0
for case in expected_capped.split(','):
    fields = case.split(':')
    if len(fields) != 6:
        sys.exit('Desktop capped-start matrix case is malformed.')
    flags = int(fields[4]); capped_coverage |= flags
    if int(fields[2]) in (4, 5, 6) and not flags & 16:
        sys.exit('Capped-start reversal did not exercise the actual opening assist.')
if capped_coverage != 31:
    sys.exit('Desktop capped-start matrix did not observe steering assist, recovery, player/AI pickup and second activation.')

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
    private static void Run() { Debug.Log("SNAKE_FIXED_VECTOR " + StrengthPveReplayVector.Compute() + "|" + StrengthPveReplayVector.ComputeRecovery() + "|" + StrengthPveReplayVector.ComputeSafeRecovery() + "|" + StrengthPveReplayVector.ComputeAlignedStart() + "|" + StrengthPveReplayVector.ComputeCappedStart()); }
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
        actual_aligned_match = aligned_start_pattern.search(result.stdout)
        if not actual_aligned_match or len(actual_aligned_match.group(1).split(',')) != 40:
            sys.exit('WebGL aligned-start matrix is incomplete.')
        actual_aligned = actual_aligned_match.group(1)
        actual_capped_match = capped_start_pattern.search(result.stdout)
        if not actual_capped_match or len(actual_capped_match.group(1).split(',')) != 70:
            sys.exit('WebGL capped-start matrix is incomplete.')
        actual_capped = actual_capped_match.group(1)
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
        print('.NET aligned start:  ' + expected_aligned + '\nWebGL aligned start: ' + actual_aligned)
        if actual_aligned != expected_aligned:
            for desktop, webgl in zip(expected_aligned.split(','), actual_aligned.split(',')):
                if desktop != webgl:
                    print('First differing aligned-start case: .NET ' + desktop + ' / WebGL ' + webgl)
                    break
            sys.exit('MISMATCH: local aligned-start simulation differs across runtimes.')
        print('.NET capped start:  ' + expected_capped + '\nWebGL capped start: ' + actual_capped)
        if actual_capped != expected_capped:
            for desktop, webgl in zip(expected_capped.split(','), actual_capped.split(',')):
                if desktop != webgl:
                    print('First differing capped-start case: .NET ' + desktop + ' / WebGL ' + webgl)
                    break
            sys.exit('MISMATCH: local capped-start simulation differs across runtimes.')
        print('MATCH: 18 strength, 16 v4, 24 v5, 40 v6 and 70 v7 matches, including selected hidden state; Douyin devices, arbitrary inputs and production rewards remain unverified.')
    finally:
        server.shutdown(); server.server_close(); thread.join(timeout=5)
