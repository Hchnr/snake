#!/usr/bin/env python3
"""Compare first eligible body-loss recovery in two offline rule variants.

Policies are scripted, not human input. After the variants diverge, random
streams and the later actions of the pursuit policies may diverge as well.
"""
import argparse
from pathlib import Path
import subprocess
import tempfile
from xml.sax.saxutils import escape

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--seeds', type=int, default=100, help='consecutive seeds starting at 1')
args = parser.parse_args()
if not 1 <= args.seeds <= 10000:
    parser.error('--seeds must be between 1 and 10000')
core = escape(str(Path(__file__).resolve().parent.parent / 'unity/Assets/Snake/Core/*.cs'))
project = f'''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><Compile Include="Program.cs"/><Compile Include="{core}"/></ItemGroup></Project>'''
program = r'''using SnakeClub.Core;
int samples = int.Parse(args[0]);
Console.WriteLine($"seeds=1..{samples} firstRun=false boost=false; up to 10 seconds after first eligible damage");
for (int policy = 0; policy < 4; policy++)
{
    int eligible = 0, oldRestored = 0, newRestored = 0, better = 0, worse = 0, oldDeaths = 0, newDeaths = 0;
    int oldAttacked = 0, newAttacked = 0, oldCensored = 0, newCensored = 0, supplies = 0, picked = 0, stolen = 0;
    long oldLost = 0, newLost = 0, oldEaten = 0, newEaten = 0;
    for (int seed = 1; seed <= samples; seed++)
    {
        var old = Evaluate(seed, policy, false);
        var current = Evaluate(seed, policy, true);
        if (old.first != current.first || old.target != current.target)
            throw new Exception($"Variants differed before the first eligible hit: seed={seed}, policy={policy}");
        if (old.first < 0) continue;
        eligible++;
        if (old.restored) oldRestored++; if (current.restored) newRestored++;
        if (!old.restored && current.restored) better++; if (old.restored && !current.restored) worse++;
        if (old.dead) oldDeaths++; if (current.dead) newDeaths++;
        if (old.attacked) oldAttacked++; if (current.attacked) newAttacked++;
        if (old.censored) oldCensored++; if (current.censored) newCensored++;
        oldLost += old.lost; newLost += current.lost; oldEaten += old.eaten; newEaten += current.eaten;
        supplies += current.supplies; picked += current.picked; stolen += current.stolen;
    }
    string name = policy == 0 ? "heading_0" : policy == 1 ? "heading_64" : policy == 2 ? "nearest_tail" : "nearest_fruit";
    Console.WriteLine($"policy={name} eligible={eligible} restored_old={oldRestored} restored_new={newRestored} restored_better={better} restored_worse={worse} deaths_old={oldDeaths} deaths_new={newDeaths} attacked_old={oldAttacked} attacked_new={newAttacked} ended_early_old={oldCensored} ended_early_new={newCensored} lost_old={oldLost} lost_new={newLost} eaten_old={oldEaten} eaten_new={newEaten} supply_triggers={supplies} picked_by_player={picked} picked_by_ai={stolen}");
}

static (int first, int target, bool restored, bool dead, bool attacked, bool censored, int lost, int eaten, int supplies, int picked, int stolen) Evaluate(int seed, int policy, bool recovery)
{
    var world = new PveFixedWorld(seed, false, null, true, localAdvantage: true, localOpeningFood: true, localOpeningEncounter: true, localRecoverySupply: recovery);
    int first = -1, target = 0, deadline = 0, priorLost = 0, priorEaten = 0, eatenAtLoss = 0;
    bool restored = false, attacked = false; byte heading = (byte)(policy == 1 ? 64 : 0);
    while (!world.Finished && (first < 0 || world.Tick < deadline))
    {
        if (policy >= 2 && world.Tick % 8 == 0)
        {
            var own = world.Fighters[0].motion; long best = long.MaxValue; int dx = 0, dy = 0;
            if (policy == 2)
            {
                foreach (var opponent in world.Fighters)
                {
                    if (opponent.id == "you" || !opponent.alive || opponent.motion.Length <= PveFixedMotion.MinimumLength) continue;
                    var tail = opponent.motion.Segments[opponent.motion.Length - 1];
                    int x = tail.x - own.X, y = tail.y - own.Y; long distance = (long)x * x + (long)y * y;
                    if (distance < best) { best = distance; dx = x; dy = y; }
                }
            }
            else
            {
                foreach (var fruit in world.Fruit)
                {
                    int x = fruit.position.x - own.X, y = fruit.position.y - own.Y; long distance = (long)x * x + (long)y * y;
                    if (distance < best) { best = distance; dx = x; dy = y; }
                }
            }
            if (best < long.MaxValue) heading = (byte)(((int)Math.Round(Math.Atan2(dy, dx) * 128 / Math.PI) + 256) & 255);
        }
        int lengthBefore = world.Fighters[0].motion.Length, lostBefore = world.PlayerSegmentsLostToBites, eatenBefore = world.PlayerSegmentsEaten;
        world.Step(heading, false);
        if (first < 0 && world.PlayerSegmentsLostToBites > lostBefore && world.Fighters[0].alive && !world.Finished &&
            world.Fighters[0].motion.Length <= PveFixedWorld.RecoverySupplyLengthLimit)
        {
            first = world.Tick; target = lengthBefore; deadline = first + 10 * PveFixedWorld.TickRate;
            priorLost = lostBefore; priorEaten = eatenBefore; eatenAtLoss = world.PlayerSegmentsEaten;
        }
        if (first >= 0)
        {
            if (world.Fighters[0].alive && world.Fighters[0].motion.Length >= target) restored = true;
            if (world.PlayerSegmentsEaten > eatenAtLoss) attacked = true;
        }
    }
    return (first, target, restored, first >= 0 && !world.Fighters[0].alive, attacked,
        first >= 0 && world.Fighters[0].alive && world.Tick < deadline,
        world.PlayerSegmentsLostToBites - priorLost, world.PlayerSegmentsEaten - priorEaten,
        world.RecoverySuppliesUsed, world.RecoverySupplyCollectedByPlayer, world.RecoverySupplyCollectedByOpponents);
}'''
with tempfile.TemporaryDirectory(prefix='snake-recovery-eval-') as directory:
    path = Path(directory)
    (path / 'Evaluate.csproj').write_text(project, encoding='utf-8')
    (path / 'Program.cs').write_text(program, encoding='utf-8')
    print('Paired scripted inputs, not a human or device study; later random streams may diverge.', flush=True)
    raise SystemExit(subprocess.run(['dotnet', 'run', '--project', str(path / 'Evaluate.csproj'), '-c', 'Release', '--verbosity', 'quiet', '--', str(args.seeds)], check=False).returncode)
