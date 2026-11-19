#!/usr/bin/env python3
"""Compare isolated v8 recovery thresholds; not a player or device study.

For each seed, the scripted player heads to the nearest fruit or nearest living
opponent tail (without checking future strength or shielding), recalculating
at eight-step intervals, with results separated by policy. Identify the first actual body loss in the first 15s,
then observe up to 10s or the match ending. Fruit relocation changes future
inputs and random state. Death ends observation early (censoring). Returned
length is measured against immediately before first damage, including any
same-step gains; later loss excludes that triggering damage. Comparisons are
exploratory, not estimates of human win rate or causal guarantees.

hitSamples counts matches with a qualifying first loss, firstTrigger is a
supply generated on that step, and anyTrigger includes later triggers in the
follow-up. player/aiCollectedCases counts matches with at least one supply
fruit eaten by that side; Fruit counts actual fruit. restored counts matches
where the living player reaches pre-hit length at least once, dead is player
death, full10s and censored count complete and early-ended observations.
postLost/postEaten exclude first-hit combat and count body segments. The final
length change sums current length minus length just after first-hit resolution;
it combines fruit, bites, losses and other growth rather than isolating supply.
Paired counts compare the same seed, with larger and smaller outcomes tallied
separately; all-pair outcomes include censored observations, and not observing
recovery before censoring does not prove failure. A second pair line restricts
both variants to the full 10-second window. First-damage ticks must match.

Before evaluating candidates, compare each step of full original-v8 matches
against the isolated 16-segment copy across two policies and both first-run
states; the regression fingerprints include selected hidden state, fighter
motions, fruit, recovery state, input and terminal digest. This is not proof
for all possible inputs or a cryptographic integrity check. Candidates 20, 24,
and 999 alter only the threshold in a temporary Core copy; 999 is effectively
unlimited for the simulated snake size, not a proposed release setting.
"""
import argparse
import filecmp
from pathlib import Path
import shutil
import subprocess
import tempfile
from xml.sax.saxutils import escape

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--seeds', type=int, default=200, help='consecutive seeds beginning with 1')
args = parser.parse_args()
if not 1 <= args.seeds <= 1000:
    parser.error('--seeds must be between 1 and 1000')
root = Path(__file__).resolve().parent.parent
source = root / 'unity/Assets/Snake/Core'
program = root / 'scripts/recovery-thresholds/Program.cs'
vector = root / 'test/v3/StrengthPveReplayVector.cs'
old = 'public const int RecoverySupplyLimit = 2, RecoverySupplyCooldownTicks = 15 * TickRate, RecoverySupplyCount = 4, RecoverySupplyLengthLimit = 16;'
new = 'public const int RecoverySupplyLimit = 2, RecoverySupplyCooldownTicks = 15 * TickRate, RecoverySupplyCount = 4; public static int RecoverySupplyLengthLimit = 16;'

with tempfile.TemporaryDirectory(prefix='snake-recovery-threshold-') as temp:
    temp = Path(temp)
    dlls = []
    for name in ('original', 'candidate'):
        work = temp / name
        work.mkdir()
        shutil.copytree(source, work / 'core')
        if name == 'candidate':
            core = work / 'core/PveFixedWorld.cs'
            data = core.read_text(encoding='utf-8')
            if data.count(old) != 1:
                raise RuntimeError('Recovery threshold declaration changed; review this evaluator')
            core.write_text(data.replace(old, new), encoding='utf-8')
        define = '<DefineConstants>EXPERIMENTAL</DefineConstants>' if name == 'candidate' else ''
        project = f'''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><EnableDefaultCompileItems>false</EnableDefaultCompileItems>{define}</PropertyGroup><ItemGroup><Compile Include="{escape(str(program))}"/><Compile Include="{escape(str(vector))}"/><Compile Include="core/*.cs"/></ItemGroup></Project>'''
        (work / 'Evaluate.csproj').write_text(project, encoding='utf-8')
        subprocess.run(['dotnet', 'build', str(work / 'Evaluate.csproj'), '-c', 'Release', '--verbosity', 'quiet'], check=True, stdout=subprocess.DEVNULL)
        dlls.append(work / 'bin/Release/net10.0/Evaluate.dll')
    trace_seeds = min(args.seeds, 16)
    traces = []
    for index, dll in enumerate(dlls):
        trace = temp / f'trace-{index}.txt'
        with trace.open('w', encoding='utf-8') as stream:
            subprocess.run(['dotnet', str(dll), 'trace', str(trace_seeds)], check=True, stdout=stream)
        traces.append(trace)
    if not filecmp.cmp(*traces, shallow=False):
        with traces[0].open() as a, traces[1].open() as b:
            for number, (left, right) in enumerate(zip(a, b), 1):
                if left != right:
                    raise RuntimeError(f'16-segment candidate differs at trace line {number}: {left.strip()} vs {right.strip()}')
        raise RuntimeError('16-segment candidate trace length differs')
    triggered, player_picked, ai_picked = set(), set(), set()
    with traces[0].open(encoding='utf-8') as stream:
        for line in stream:
            fields = line.split()
            match = tuple(fields[:3])
            if int(fields[7]): triggered.add(match)
            if int(fields[8]): player_picked.add(match)
            if int(fields[9]): ai_picked.add(match)
    print(f'Baseline MATCH: original v8 vs temporary 16 threshold, {trace_seeds} seeds × 2 first-run states × 2 policies, each step through terminal; recovery-trigger matches={len(triggered)}, player-pick matches={len(player_picked)}, AI-pick matches={len(ai_picked)}.', flush=True)
    result = subprocess.run(['dotnet', str(dlls[1]), 'eval', str(args.seeds)], check=True, text=True, capture_output=True)
    rows = {}
    for line in result.stdout.splitlines():
        parts = line.split()
        if not parts or parts[0] != 'ROW':
            print(line)
            continue
        _, first, policy, threshold, seed, tick, duration, alive, restored, lost, eaten, player, ai, growth = parts
        rows[(first, int(policy), int(threshold), int(seed))] = (int(tick), int(duration), alive == 'True', restored == 'True', int(lost), int(eaten), int(player), int(ai), int(growth))
    for first in ('False', 'True'):
        for policy in (0, 1):
            for threshold in (20, 24, 999):
                seeds = [seed for f, p, t, seed in rows if f == first and p == policy and t == 16]
                if not all((first, policy, threshold, s) in rows and rows[(first, policy, 16, s)][0] == rows[(first, policy, threshold, s)][0] for s in seeds):
                    raise RuntimeError('First-damage sample or tick differed between candidates')
                pairs = [(rows[(first, policy, 16, s)], rows[(first, policy, threshold, s)]) for s in seeds]
                for scope, selected in (('all_including_censored', pairs), ('both_full10s', [(a,b) for a,b in pairs if a[1] == 200 and b[1] == 200])):
                    print(f'paired scope={scope} first={first} policy={policy} threshold={threshold} n={len(selected)} '
                          f'restored_better={sum(not a[3] and b[3] for a,b in selected)} restored_worse={sum(a[3] and not b[3] for a,b in selected)} '
                          f'dead_to_alive={sum(not a[2] and b[2] for a,b in selected)} alive_to_dead={sum(a[2] and not b[2] for a,b in selected)} '
                          f'lost_increased={sum(b[4]>a[4] for a,b in selected)} lost_decreased={sum(b[4]<a[4] for a,b in selected)} '
                          f'length_increased={sum(b[8]>a[8] for a,b in selected)} length_decreased={sum(b[8]<a[8] for a,b in selected)}')
