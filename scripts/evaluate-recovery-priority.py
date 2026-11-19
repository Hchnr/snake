#!/usr/bin/env python3
"""Compare v8 with v9 using scripted first-damage recovery, not human play.

Each policy heads to the nearest fruit or nearest opponent tail, reselecting
once per eight steps without boost. Count the first body loss in the first
15 seconds; follow that seed for up to 10 seconds or match end. Early endings
censor the window. Supply changes can fork AI decisions, player direction and
random state. Comparisons are exploratory and not estimates of player outcomes.
Both builds use the repository Core unchanged; only local rule flags differ.
"""
import argparse
from pathlib import Path
import subprocess
import tempfile
from xml.sax.saxutils import escape

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--seeds', type=int, default=200)
args = parser.parse_args()
if not 1 <= args.seeds <= 1000:
    parser.error('--seeds must be between 1 and 1000')
root = Path(__file__).resolve().parent.parent
source = (root / 'scripts/recovery-thresholds/Program.cs').read_text()
old = '''#if EXPERIMENTAL
    PveFixedWorld.RecoverySupplyLengthLimit = maxLength;
#else
    throw new InvalidOperationException("Evaluation requires isolated candidate core");
#endif'''
if source.count(old) != 1 or source.count('localStableBlocked: true);') != 2 or source.count('new[] { 16, 20, 24, 999 }') != 1:
    raise RuntimeError('Recovery evaluation template changed; review its structure before comparing versions')
source = source.replace(old, '').replace('new[] { 16, 20, 24, 999 }', 'new[] { 16 }')
core = escape(str(root / 'unity/Assets/Snake/Core/*.cs'))
vector = escape(str(root / 'test/v3/StrengthPveReplayVector.cs'))
project = f'''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><Compile Include="Program.cs"/><Compile Include="{core}"/><Compile Include="{vector}"/></ItemGroup></Project>'''
all_rows = []
with tempfile.TemporaryDirectory(prefix='snake-recovery-priority-') as directory:
    directory = Path(directory)
    for name, enabled in [('v8', False), ('v9', True)]:
        work = directory / name
        work.mkdir()
        text = source.replace('localStableBlocked: true);', 'localStableBlocked: true, localRecoveryPriority: ' + str(enabled).lower() + ');')
        (work / 'Program.cs').write_text(text)
        (work / 'Evaluation.csproj').write_text(project)
        subprocess.run(['dotnet', 'build', str(work / 'Evaluation.csproj'), '-c', 'Release', '--verbosity', 'quiet'], check=True, stdout=subprocess.DEVNULL)
        output = subprocess.run(['dotnet', str(work / 'bin/Release/net10.0/Evaluation.dll'), 'eval', str(args.seeds)], check=True, text=True, capture_output=True).stdout
        rows = {}
        for line in output.splitlines():
            fields = line.split()
            if fields and fields[0] == 'ROW':
                _, first, policy, threshold, seed, tick, duration, alive, restored, lost, eaten, player, ai, growth = fields
                rows[(first, policy, int(seed))] = (int(tick), int(duration), alive == 'True', restored == 'True', int(lost), int(eaten), int(player), int(ai), int(growth))
            elif line.startswith('first='):
                print(name, line, flush=True)
        all_rows.append(rows)
for first in ('False', 'True'):
    for policy in ('0', '1'):
        keys = [key for key in all_rows[0] if key[:2] == (first, policy)]
        if len(keys) != sum(key[:2] == (first, policy) for key in all_rows[1]) or any(key not in all_rows[1] or all_rows[0][key][0] != all_rows[1][key][0] for key in keys):
            raise RuntimeError('First injury sample or tick differs before the candidate can take effect')
        pairs = [(all_rows[0][key], all_rows[1][key]) for key in keys]
        for scope, sample in [('all_including_censored', pairs), ('both_full10s', [(a, b) for a, b in pairs if a[1] >= 200 and b[1] >= 200])]:
            print(f'paired scope={scope} first={first} policy={policy} n={len(sample)} '
                  f'player_more={sum(b[6] > a[6] for a,b in sample)} player_less={sum(b[6] < a[6] for a,b in sample)} '
                  f'ai_more={sum(b[7] > a[7] for a,b in sample)} ai_less={sum(b[7] < a[7] for a,b in sample)} '
                  f'restored_better={sum(not a[3] and b[3] for a,b in sample)} restored_worse={sum(a[3] and not b[3] for a,b in sample)} '
                  f'lost_more={sum(b[4] > a[4] for a,b in sample)} lost_less={sum(b[4] < a[4] for a,b in sample)} '
                  f'dead_to_alive={sum(not a[2] and b[2] for a,b in sample)} alive_to_dead={sum(a[2] and not b[2] for a,b in sample)}')
