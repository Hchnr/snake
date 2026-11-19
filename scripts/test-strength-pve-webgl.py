#!/usr/bin/env python3
"""Compare sampled strength-preview vectors in desktop .NET and headless Unity WebGL.

The diagnostic is injected only into a temporary project. It does not verify
Douyin devices, fairness, production readiness, or every input and seed.
"""
from functools import partial
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
import json
import hashlib
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
stable_blocked_pattern = re.compile(r'stable-blocked=([0-9a-f:,]+)')
priority_pattern = re.compile(r'priority-recovery=([0-9a-f:,]+)')
priority_fixture_pattern = re.compile(r'priority-fixture=([0-9a-f]{16})')
squeeze_pattern = re.compile(r'squeeze-escape=([0-9a-f:,]+)')
squeeze_fixture_pattern = re.compile(r'squeeze-fixture=([0-9a-f]{16})')
multi_pattern = re.compile(r'multi-bite=([0-9a-f:,]+)')
multi_fixture_pattern = re.compile(r'multi-fixture=([0-9a-f]{16})')
stable_fixture_pattern = re.compile(r'stable-fixture=([0-9a-f]{16})')
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
# Captured from unmodified v7 HEAD before introducing v8.
v7_golden = (
    '2:0:0:1800:3:1f4512487cdc1bd7,2:0:1:1800:16:278d0b68b18d785f,2:0:2:1800:0:a340305091cb08df,'
    '2:0:3:1800:0:a77264b1191855a6,2:0:4:1800:16:a957d9f2f295022c,2:0:5:1800:19:6ab51bf7be5218d9,'
    '2:0:6:309:19:d307a3537b8cc492,2:1:0:1200:0:466a99fdb1979218,2:1:1:1200:16:51552d60c5e8ac4f,'
    '2:1:2:1200:0:2951d34a2052739d,2:1:3:1200:0:12c38f1584669950,2:1:4:1200:16:e308b442e98fa9fb,'
    '2:1:5:1200:23:31bde90002d14c63,2:1:6:1200:23:d6b824fb0d5f6eb0,68:0:0:1800:7:329d890711569eb9,'
    '68:0:1:1800:16:a965e3f2d1b9a4ff,68:0:2:1800:0:2636043ad68b1720,68:0:3:1800:0:7a7863e085587fd4,'
    '68:0:4:1800:16:97b9393fd7e4b032,68:0:5:1800:16:d87467a99f2a8d0f,68:0:6:1800:23:82e88834ffaa3c2b,'
    '68:1:0:1200:0:afeb8d8f0d5ae00f,68:1:1:1200:16:73df45223462f50f,68:1:2:1200:0:d95cb0f0b291843b,'
    '68:1:3:1200:0:ff625729ec0a5096,68:1:4:1200:16:adfc1ba5ab452316,68:1:5:1200:16:22d9ccf0b291d17f,'
    '68:1:6:1200:19:17ee608dda3c7809,92:0:0:1800:7:4a737248a847cf9c,92:0:1:1800:19:b98467ee99c7f9d4,'
    '92:0:2:1800:0:950e1c8f39885109,92:0:3:1800:11:299b3915976bb988,92:0:4:1800:16:994867db1085bb7a,'
    '92:0:5:1800:16:329f1455b5e5994b,92:0:6:1800:19:27cce626179215bc,92:1:0:1200:3:4a54086509398601,'
    '92:1:1:1200:23:f7e0007a78a29bc4,92:1:2:1200:0:7d6fa949806297ef,92:1:3:1200:0:ca051284f3ae80f2,'
    '92:1:4:1200:16:701b8439171df14d,92:1:5:1200:16:e84dc3cc0c769175,92:1:6:1200:19:84f02ca42199fa4e,'
    '105:0:0:1800:0:e9e745f1960dcd38,105:0:1:1800:16:091e56f820013a0a,105:0:2:1800:0:c3ef876076f1c4c1,'
    '105:0:3:1800:0:df87f6fca9f1f008,105:0:4:1800:16:a9073dd890f07043,105:0:5:1800:16:cbdf7465176b7021,'
    '105:0:6:1800:19:3bb2c583b7dbe993,105:1:0:1200:0:368570b1fc7b889a,105:1:1:1200:16:b36e3cd97165a035,'
    '105:1:2:1200:0:920ac4540fb41089,105:1:3:1200:0:b2ab1534cdb9e46f,105:1:4:1200:16:9bc79e4e6ffb547b,'
    '105:1:5:1200:19:b1e15a9839aa8237,105:1:6:1200:23:86dc8d4b81b45fd0,192:0:0:1800:7:d61a8b383729826a,'
    '192:0:1:1800:16:5e73ddb15d2d4a11,192:0:2:1800:5:7164757c30f5e7ed,192:0:3:1800:7:3f3e90a0d084ef48,'
    '192:0:4:1800:16:7047d86083d03267,192:0:5:1800:16:d5dc012b2712354f,192:0:6:1800:16:1f82f63ea9ead3cc,'
    '192:1:0:1200:3:1fe56ae373482165,192:1:1:1200:16:7c68e203f815c70e,192:1:2:220:0:4ea32b6481f6b832,'
    '192:1:3:1200:0:255b9bace040210f,192:1:4:1200:16:ec547456ee8e3fb9,192:1:5:1200:19:7038c364671abdaf,'
    '192:1:6:1200:23:c2bd8e0b8a3427cc'
)
if expected_capped != v7_golden:
    sys.exit('MISMATCH: v7 capped-start fingerprints changed from the preserved reference.')
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

stable_reference = subprocess.run(['dotnet', 'run', '--project', str(root / 'test/v3/Snake.V3.Core.Tests.csproj'), '--', 'strength-stable-blocked-vector'],
                                  text=True, capture_output=True, check=True, timeout=180).stdout
stable_match = stable_blocked_pattern.search(stable_reference)
fixture_match = stable_fixture_pattern.search(stable_reference)
if not stable_match or len(stable_match.group(1).split(',')) != 70 or not fixture_match:
    sys.exit('Desktop v8 matrix or controlled blocked-contact fixture is incomplete.')
expected_stable = stable_match.group(1)
expected_fixture = fixture_match.group(1)
# v8 was captured from main before priority recovery; protects old replays.
v8_golden = (
    '2:0:0:1800:3:29c610e6164846f7,2:0:1:1800:16:89bc767cca856aef,2:0:2:1800:0:8267fc6051adbe6b,'
    '2:0:3:1800:0:7c06a4e92a4d0c46,2:0:4:1800:16:4bed0103726513cc,2:0:5:1800:19:5b77b9f9fcb4b3b9,'
    '2:0:6:309:19:b2298752ef1d411a,2:1:0:1200:0:83b7f5cdc9d69a78,2:1:1:1200:16:483a79460af471ff,'
    '2:1:2:1200:0:22acc6f7f8f2fc8d,2:1:3:1200:0:79c3723fc2c2b040,2:1:4:1200:16:d03394e7e94f434b,'
    '2:1:5:1200:23:b67fffe5f1e9bde3,2:1:6:1200:23:f4e64c9f92d4a1d9,68:0:0:1800:7:413c6300096c7189,'
    '68:0:1:1800:16:98c5e3ff2c57138f,68:0:2:1800:0:be30fb7b31cb6e70,68:0:3:1800:0:cc342d4c4e344694,'
    '68:0:4:1800:16:b6f31619133a2492,68:0:5:1800:16:0ee5580055089b5f,68:0:6:1800:23:15a4c328187b347b,'
    '68:1:0:1200:0:d39230d71595968f,68:1:1:1200:16:513bcafd376542cf,68:1:2:1200:0:41458c19c791d24b,'
    '68:1:3:1200:0:b615e1bb17c179c6,68:1:4:1200:16:7f2061989c573206,68:1:5:1200:16:9daea041816e8d6f,'
    '68:1:6:1200:19:d091334fad09a4b9,92:0:0:1800:7:a4e1cefe9a9eb5fc,92:0:1:1800:19:566fa45f2a4ca7a4,'
    '92:0:2:1800:0:c739b227b638a3b9,92:0:3:1800:11:c44adb45ef8d6308,92:0:4:1800:16:132e2dc4d4c4ff1a,'
    '92:0:5:1800:16:2ac0b2f004ea568b,92:0:6:1800:23:3a4eaf94c9b647d1,92:1:0:1200:3:cd84468e19c33ef1,'
    '92:1:1:1200:23:149047d7c5ee5034,92:1:2:1200:0:53db6e53690ced7f,92:1:3:1200:0:887fefff362cb482,'
    '92:1:4:1200:16:cddcf423bf212ebd,92:1:5:1200:16:8fb91a92e8cb9085,92:1:6:1200:23:9a79f174e2898f28,'
    '105:0:0:1800:0:839a6d63035c1b58,105:0:1:1800:16:c2a31e712c4ac6ea,105:0:2:1800:0:d154c13582a1dd91,'
    '105:0:3:1800:0:91943b0d47072d28,105:0:4:1800:16:7931237ba56e3473,105:0:5:1800:16:6f4f9e0338fe8bd1,'
    '105:0:6:1800:19:7091e5e215809af3,105:1:0:1200:0:0aea2daf3429420a,105:1:1:1200:16:4dafe79152e759a5,'
    '105:1:2:1200:0:11c8116447042429,105:1:3:1200:0:e4f0401eb3f47b9f,105:1:4:1200:16:b20d2cf5e4a8f1fb,'
    '105:1:5:1200:19:e017f7e0f9dd80c7,105:1:6:1200:23:44125e054f631bb0,192:0:0:1800:7:6c1598821f725caa,'
    '192:0:1:1800:16:b3ec56f876779f41,192:0:2:1800:5:a6d4a6c58452b92d,192:0:3:1800:7:714f56d36dee3568,'
    '192:0:4:1800:16:848c3a1f7b660967,192:0:5:1800:16:9c6ae7cfa9209d6f,192:0:6:1800:16:ebb0b4ceecfcf3bc,'
    '192:1:0:1200:3:c716dc688c46f855,192:1:1:1200:16:d7ce0cdf15abad2e,192:1:2:220:0:10e2aa80212f6582,'
    '192:1:3:1200:0:15b7d52c33de8c8f,192:1:4:1200:16:466e6b9c6a910409,192:1:5:1200:19:5390115cfa5c9f4f,'
    '192:1:6:1200:23:2c6283a312ab664c'
)
if expected_stable != v8_golden:
    sys.exit('MISMATCH: v8 fingerprints changed from the preserved reference.')
stable_coverage = 0
for case in expected_stable.split(','):
    fields = case.split(':')
    if len(fields) != 6:
        sys.exit('Desktop v8 matrix case is malformed.')
    stable_coverage |= int(fields[4])
if stable_coverage != 31:
    sys.exit('Desktop v8 cases did not observe steering assist, recovery, both pickups and second activation.')

priority_reference = subprocess.run(['dotnet', 'run', '--project', str(root / 'test/v3/Snake.V3.Core.Tests.csproj'), '--', 'strength-recovery-priority-vector'],
                                    text=True, capture_output=True, check=True, timeout=180).stdout
priority_match = priority_pattern.search(priority_reference)
priority_fixture_match = priority_fixture_pattern.search(priority_reference)
if not priority_match or len(priority_match.group(1).split(',')) != 96 or not priority_fixture_match:
    sys.exit('Desktop v9 recovery-priority matrix or controlled fixture is incomplete.')
expected_priority = priority_match.group(1)
expected_priority_fixture = priority_fixture_match.group(1)
# Captured from unmodified HEAD before v10; protects v9 replays.
v9_golden = (
    '2:0:0:1800:35:3eebb4e849a4ed7f,2:0:1:1800:16:4525f23d40c07adb,2:0:2:1800:0:557c2e5b519ec26b,2:0:3:1800:0:7653b'
    'f5e76088372,2:0:4:1800:16:db8afc93881bee24,2:0:5:1800:51:b7f2d4e3c9db7b35,2:0:6:309:51:82e75ac0815383bf,2:0:7:'
    '1800:16:3baaba37b5d25cc2,2:1:0:1200:0:ead4ae80e188c908,2:1:1:1200:16:b2c4e669fe99d99f,2:1:2:1200:0:120a30a355c'
    '9b785,2:1:3:1200:0:d806c806c5bf30bc,2:1:4:1200:16:fc6c052c77d9c7c7,2:1:5:1200:55:b079a5ae2a226d93,2:1:6:1200:5'
    '5:e07fbdbebc4bedd1,2:1:7:1200:16:5946b19a97e83a20,9:0:0:1800:35:335bcc81cce9361b,9:0:1:1800:16:da10be5b9380772'
    'd,9:0:2:1800:0:1caf8097841949a2,9:0:3:1800:39:01e393821cc54723,9:0:4:1800:51:547211abaae43278,9:0:5:1800:51:1e'
    '6b0afbfb460a6b,9:0:6:1800:16:f168532d086a7069,9:0:7:334:51:55369efdce35b439,9:1:0:1200:39:427e1635875760e4,9:1'
    ':1:1200:16:96b9bb05d7bd6c25,9:1:2:1200:0:39dbcd2a25d2696e,9:1:3:1200:0:2e7c5940a6dba8d6,9:1:4:1200:51:e5cda930'
    '3ff77b54,9:1:5:1200:51:733fe6a2135f20df,9:1:6:1200:16:07d207ae49387cd3,9:1:7:504:59:39e3a65700a84914,68:0:0:18'
    '00:39:16e3948202694e45,68:0:1:1800:16:b0616f21beef8037,68:0:2:1800:0:02a88581e489a35c,68:0:3:1800:0:08de47538f'
    '9a55c0,68:0:4:1800:16:966adfa3a1e3a972,68:0:5:1800:16:443283e32e20f2a3,68:0:6:1800:55:7537682f71353bff,68:0:7:'
    '1800:16:ad7da1e508c19efb,68:1:0:1200:0:82366205259ddbe7,68:1:1:1200:16:6df4330930b3f737,68:1:2:1200:0:6e972ba3'
    '325b683b,68:1:3:1200:0:d4e150aadb403d8a,68:1:4:1200:16:0c66605af41b7ede,68:1:5:1200:16:ca2d168aefd4b0df,68:1:6'
    ':1200:51:bdc3b9b6dda78785,68:1:7:1200:16:23715b028e69f5cf,92:0:0:1800:39:f561c8cbfbad5094,92:0:1:1800:51:4c54e'
    '9b506a61d05,92:0:2:1800:0:76aa26b2e2dae57d,92:0:3:1800:39:56521938530432bf,92:0:4:1800:16:18efc431a173de1e,92:'
    '0:5:1800:16:aa4f60df4cc0120b,92:0:6:1800:55:71566034fa626190,92:0:7:1800:16:b8c9d09338d28fa2,92:1:0:1200:35:7b'
    '9de819806893a5,92:1:1:1200:55:756c6f54fe990849,92:1:2:1200:0:134765bb6bf94a7b,92:1:3:1200:0:bbb81e2bceb5c5aa,9'
    '2:1:4:1200:16:4d9c2aadce8bf70d,92:1:5:1200:16:ecdddcc153338755,92:1:6:1200:55:50289139c1665f95,92:1:7:123:49:1'
    '6ee0d24c926f154,105:0:0:1800:0:5a3c8b6fece267b0,105:0:1:1800:16:2f641dd594460a26,105:0:2:1800:0:1a9e6567ea97b3'
    'b9,105:0:3:1800:0:2d7ac9555aff975c,105:0:4:1800:16:e394eb5f64d6ff43,105:0:5:1800:16:1b660d67643c9fb1,105:0:6:1'
    '800:51:50587169c4afec6f,105:0:7:1800:0:fc9c28cb30fb48e9,105:1:0:1200:0:05f3dbcb2e732ac6,105:1:1:1200:16:c0c875'
    '5dd36a15b5,105:1:2:1200:0:dfa737ce3a28d8fd,105:1:3:1200:0:62ed7aced1e48f57,105:1:4:1200:16:64409a5f2fde05fb,10'
    '5:1:5:1200:51:db9eef946e8cba6b,105:1:6:1200:55:7a5b1c92b92cae54,105:1:7:1200:0:faab8572ddb8306d,192:0:0:1800:3'
    '9:3887e3060c7cd736,192:0:1:1800:16:8663c94e3da56951,192:0:2:287:37:c5c91ea4bdaf22ee,192:0:3:1800:39:26a2508460'
    '057bdc,192:0:4:1800:16:1d375d2b7fb81e2f,192:0:5:1800:16:6277da630779ad97,192:0:6:1800:16:ebbb5a9b50d66fc8,192:'
    '0:7:168:53:4da2665305f4015c,192:1:0:1200:35:9eca42587acc0b6d,192:1:1:1200:16:c4a4088c2f3810f2,192:1:2:220:0:ac'
    '6c3e27d44c9ac2,192:1:3:1200:0:ee0ce7779cd3c05f,192:1:4:1200:16:dcf33b765f453d39,192:1:5:1200:51:1138f037e7bb88'
    '6b,192:1:6:1200:55:19a7d10be27d55cc,192:1:7:1200:16:a20a9635c96bc400'
)
if expected_priority != v9_golden or expected_priority_fixture != '165a3e0fa80e83b8':
    sys.exit('MISMATCH: v9 matrix or priority fixture changed from the preserved reference.')
priority_coverage = 0
for case in expected_priority.split(','):
    fields = case.split(':')
    if len(fields) != 6:
        sys.exit('Desktop v9 recovery-priority matrix case is malformed.')
    priority_coverage |= int(fields[4])
if priority_coverage != 63:
    sys.exit('Desktop v9 cases did not observe active priority, recovery, both pickups, second activation and opening assist.')

squeeze_reference = subprocess.run(['dotnet', 'run', '--project', str(root / 'test/v3/Snake.V3.Core.Tests.csproj'), '--', 'strength-squeeze-escape-vector'],
                                   text=True, capture_output=True, check=True, timeout=180).stdout
squeeze_match = squeeze_pattern.search(squeeze_reference)
squeeze_fixture_match = squeeze_fixture_pattern.search(squeeze_reference)
if not squeeze_match or len(squeeze_match.group(1).split(',')) != 112 or not squeeze_fixture_match:
    sys.exit('Desktop v10 squeeze matrix or lifecycle fixture is incomplete.')
expected_squeeze = squeeze_match.group(1)
expected_squeeze_fixture = squeeze_fixture_match.group(1)
# Captured from main before v11. Comparing two changed runtimes alone would
# not catch a shared regression in existing v10 recordings.
if hashlib.sha256(expected_squeeze.encode('ascii')).hexdigest() != '86aff99aca352ee5b9d5a78e997a8c156b0c7d82ed834f6ea03b7a8ea9bb6ebe' or expected_squeeze_fixture != '15f29e35ece5ba90':
    sys.exit('MISMATCH: v10 squeeze vectors changed from the preserved reference.')
multi_reference = subprocess.run(['dotnet', 'run', '--project', str(root / 'test/v3/Snake.V3.Core.Tests.csproj'), '--', 'strength-multibite-vector'],
                                 text=True, capture_output=True, check=True, timeout=180).stdout
multi_match = multi_pattern.search(multi_reference)
multi_fixture_match = multi_fixture_pattern.search(multi_reference)
if not multi_match or len(multi_match.group(1).split(',')) != 112 or not multi_fixture_match:
    sys.exit('Desktop v11 multi-bite matrix or controlled fixture is incomplete.')
expected_multi = multi_match.group(1)
expected_multi_fixture = multi_fixture_match.group(1)
squeeze_coverage = 0
for case in expected_squeeze.split(','):
    fields = case.split(':')
    if len(fields) != 6:
        sys.exit('Desktop v10 squeeze matrix case is malformed.')
    squeeze_coverage |= int(fields[4])
if squeeze_coverage & 511 != 511:
    sys.exit('Desktop v10 samples missed recovery, pickups, repeated supply, opening assist or squeeze start/active/end.')

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
    private static void Run() { if (Application.absoluteURL.Contains("multi=1")) Debug.Log("SNAKE_FIXED_VECTOR " + StrengthPveReplayVector.ComputeMultiBite()); else if (Application.absoluteURL.Contains("squeeze=1")) Debug.Log("SNAKE_FIXED_VECTOR " + StrengthPveReplayVector.ComputeSqueezeEscape()); else if (Application.absoluteURL.Contains("priority=1")) Debug.Log("SNAKE_FIXED_VECTOR " + StrengthPveReplayVector.ComputeRecoveryPriority()); else Debug.Log("SNAKE_FIXED_VECTOR " + StrengthPveReplayVector.Compute() + "|" + StrengthPveReplayVector.ComputeRecovery() + "|" + StrengthPveReplayVector.ComputeSafeRecovery() + "|" + StrengthPveReplayVector.ComputeAlignedStart() + "|" + StrengthPveReplayVector.ComputeCappedStart() + "|" + StrengthPveReplayVector.ComputeStableBlocked()); }
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
        actual_stable_match = stable_blocked_pattern.search(result.stdout)
        actual_fixture_match = stable_fixture_pattern.search(result.stdout)
        if not actual_stable_match or len(actual_stable_match.group(1).split(',')) != 70 or not actual_fixture_match:
            sys.exit('WebGL v8 matrix or controlled blocked-contact fixture is incomplete.')
        actual_stable, actual_fixture = actual_stable_match.group(1), actual_fixture_match.group(1)
        # Unity/WebGL console messages can truncate one large combined log.
        # Run v9 in a separate muted browser against the same isolated build.
        print('Running v9 priority vectors in a second muted headless browser...', flush=True)
        priority_result = subprocess.run(['node', str(root / 'scripts/fixed-pve-webgl-probe.cjs'), str(chrome),
                                          'http://127.0.0.1:' + str(server.server_port) + '/?priority=1'],
                                         capture_output=True, text=True, timeout=240, check=False)
        if priority_result.returncode:
            sys.exit('Headless v9 diagnostic failed: ' + priority_result.stderr[-3000:])
        actual_priority_match = priority_pattern.search(priority_result.stdout)
        actual_priority_fixture_match = priority_fixture_pattern.search(priority_result.stdout)
        if not actual_priority_match or len(actual_priority_match.group(1).split(',')) != 96 or not actual_priority_fixture_match:
            sys.exit('WebGL v9 recovery-priority matrix or controlled fixture is incomplete.')
        actual_priority = actual_priority_match.group(1)
        print('Running v10 squeeze vectors in a third muted headless browser...', flush=True)
        squeeze_result = subprocess.run(['node', str(root / 'scripts/fixed-pve-webgl-probe.cjs'), str(chrome),
                                         'http://127.0.0.1:' + str(server.server_port) + '/?squeeze=1'],
                                        capture_output=True, text=True, timeout=240, check=False)
        if squeeze_result.returncode:
            sys.exit('Headless v10 diagnostic failed: ' + squeeze_result.stderr[-3000:])
        actual_squeeze_match = squeeze_pattern.search(squeeze_result.stdout)
        actual_squeeze_fixture_match = squeeze_fixture_pattern.search(squeeze_result.stdout)
        if not actual_squeeze_match or len(actual_squeeze_match.group(1).split(',')) != 112 or not actual_squeeze_fixture_match:
            sys.exit('WebGL v10 squeeze matrix or lifecycle fixture is incomplete.')
        actual_squeeze = actual_squeeze_match.group(1)
        print('Running v11 multi-bite vectors in a fourth muted headless browser...', flush=True)
        multi_result = subprocess.run(['node', str(root / 'scripts/fixed-pve-webgl-probe.cjs'), str(chrome),
                                       'http://127.0.0.1:' + str(server.server_port) + '/?multi=1'],
                                      capture_output=True, text=True, timeout=240, check=False)
        if multi_result.returncode:
            sys.exit('Headless v11 diagnostic failed: ' + multi_result.stderr[-3000:])
        actual_multi_match = multi_pattern.search(multi_result.stdout)
        actual_multi_fixture_match = multi_fixture_pattern.search(multi_result.stdout)
        if not actual_multi_match or len(actual_multi_match.group(1).split(',')) != 112 or not actual_multi_fixture_match:
            sys.exit('WebGL v11 matrix or controlled multi-bite fixture is incomplete.')
        if actual_multi_match.group(1) != expected_multi or actual_multi_fixture_match.group(1) != expected_multi_fixture:
            sys.exit('MISMATCH: v11 multi-bite simulation or controlled fixture differs across runtimes.')
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
        if actual_stable != expected_stable:
            for desktop, webgl in zip(expected_stable.split(','), actual_stable.split(',')):
                if desktop != webgl:
                    print('First differing v8 case: .NET ' + desktop + ' / WebGL ' + webgl)
                    break
            sys.exit('MISMATCH: local stable-blocked simulation differs across runtimes.')
        if actual_fixture != expected_fixture:
            sys.exit('MISMATCH: controlled same-frame bite/block fixture differs across runtimes.')
        if actual_priority != expected_priority:
            for desktop, webgl in zip(expected_priority.split(','), actual_priority.split(',')):
                if desktop != webgl:
                    print('First differing v9 case: .NET ' + desktop + ' / WebGL ' + webgl)
                    break
            sys.exit('MISMATCH: local recovery-priority simulation differs across runtimes.')
        if actual_priority_fixture_match.group(1) != expected_priority_fixture:
            sys.exit('MISMATCH: controlled recovery-priority fixture differs across runtimes.')
        if actual_squeeze != expected_squeeze:
            for desktop, webgl in zip(expected_squeeze.split(','), actual_squeeze.split(',')):
                if desktop != webgl:
                    print('First differing v10 case: .NET ' + desktop + ' / WebGL ' + webgl)
                    break
            sys.exit('MISMATCH: squeeze-escape simulation differs across runtimes.')
        if actual_squeeze_fixture_match.group(1) != expected_squeeze_fixture:
            sys.exit('MISMATCH: squeeze lifecycle fixture differs across runtimes.')
        print('MATCH: 18 strength, 16 v4, 24 v5, 40 v6, 70 v7, 70 v8, 96 v9, 112 v10 and 112 v11 matches plus controlled bite/block, recovery-priority, squeeze and multi-bite fixtures; selected hidden state only; Douyin devices, arbitrary inputs and rewards remain unverified.')
    finally:
        server.shutdown(); server.server_close(); thread.join(timeout=5)
