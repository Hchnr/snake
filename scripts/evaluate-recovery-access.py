#!/usr/bin/env python3
"""Measure scripted collection during five seconds after first recovery supply.

Policies first chase the nearest living AI tail. Once the first v5 supply is
created, one holds its last heading and the other repeatedly aims at the
nearest remaining marked fruit. This is not a player study: policies can
change outcomes and subsequent random streams.
"""
import argparse
from pathlib import Path
import subprocess
import tempfile
from xml.sax.saxutils import escape

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--start', type=int, default=1, help='first consecutive seed')
parser.add_argument('--seeds', type=int, default=150, help='number of consecutive seeds')
args = parser.parse_args()
if not 1 <= args.seeds <= 10000:
    parser.error('--seeds must be between 1 and 10000')
if args.start < -(2 ** 31) or args.start + args.seeds - 1 >= 2 ** 31:
    parser.error('seed range must fit signed 32-bit integers')

core = escape(str(Path(__file__).resolve().parent.parent / 'unity/Assets/Snake/Core/*.cs'))
project = f'''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><Compile Include="Program.cs"/><Compile Include="{core}"/></ItemGroup></Project>'''
program = r'''using SnakeClub.Core;
int startSeed = int.Parse(args[0]), sampleCount = int.Parse(args[1]);
const int horizon = 5 * PveFixedWorld.TickRate;
for (int policy = 0; policy < 2; policy++)
{
    int events = 0, playerFruit = 0, aiFruit = 0, dead = 0, playerEvents = 0, aiEvents = 0, targetSelections = 0;
    for (int offset = 0; offset < sampleCount; offset++)
    {
        int seed = startSeed + offset;
        var world = new PveFixedWorld(seed, false, null, true, localAdvantage: true, localOpeningFood: true,
            localOpeningEncounter: true, localRecoverySupply: true, localSafeRecovery: true);
        byte heading = 0;
        while (!world.Finished && world.RecoverySuppliesUsed == 0)
        {
            if (world.Tick % 8 == 0) heading = NearestTail(world, heading);
            world.Step(heading, false);
        }
        if (world.RecoverySuppliesUsed == 0) continue;
        events++;
        int beforePlayer = world.RecoverySupplyCollectedByPlayer, beforeAi = world.RecoverySupplyCollectedByOpponents;
        int firstTick = world.Tick;
        while (!world.Finished && world.Tick - firstTick < horizon)
        {
            if (policy == 1 && world.Tick % 2 == 0)
            {
                int target = NearestRecoveryFruit(world);
                if (target >= 0)
                {
                    // The index must still identify a currently marked fruit;
                    // collection clears the mark and replaces the fruit.
                    if (target >= world.Fruit.Count || !world.IsRecoveryFruit(target)) throw new Exception("Stale recovery target");
                    targetSelections++;
                    var point = world.Fruit[target].position;
                    var own = world.Fighters[0].motion;
                    heading = Heading(point.x - own.X, point.y - own.Y);
                }
            }
            world.Step(heading, false);
        }
        int collectedPlayer = world.RecoverySupplyCollectedByPlayer - beforePlayer;
        int collectedAi = world.RecoverySupplyCollectedByOpponents - beforeAi;
        if (collectedPlayer > 0) playerEvents++;
        if (collectedAi > 0) aiEvents++;
        playerFruit += collectedPlayer; aiFruit += collectedAi;
        if (!world.Fighters[0].alive) dead++;
    }
    Console.WriteLine($"policy={(policy == 0 ? "hold_heading" : "nearest_recovery_fruit")} seeds={sampleCount} first_spawn={events} " +
        $"horizon_ticks={horizon} player_events={playerEvents} player_fruit={playerFruit} ai_events={aiEvents} ai_fruit={aiFruit} " +
        $"player_deaths={dead} recovery_target_selections={targetSelections}");
}
static byte Heading(int dx, int dy) => (byte)(((int)Math.Round(Math.Atan2(dy, dx) * 128 / Math.PI) + 256) & 255);
static byte NearestTail(PveFixedWorld world, byte fallback)
{
    var own = world.Fighters[0].motion; long best = long.MaxValue; int dx = 0, dy = 0;
    foreach (var opponent in world.Fighters)
    {
        if (opponent.id == "you" || !opponent.alive || opponent.motion.Length <= PveFixedMotion.MinimumLength) continue;
        var tail = opponent.motion.Segments[opponent.motion.Length - 1];
        int x = tail.x - own.X, y = tail.y - own.Y; long distance = (long)x * x + (long)y * y;
        if (distance < best) { best = distance; dx = x; dy = y; }
    }
    return best == long.MaxValue ? fallback : Heading(dx, dy);
}
static int NearestRecoveryFruit(PveFixedWorld world)
{
    var own = world.Fighters[0].motion; long best = long.MaxValue; int target = -1;
    for (int i = 0; i < world.Fruit.Count; i++)
    {
        if (!world.IsRecoveryFruit(i)) continue;
        int x = world.Fruit[i].position.x - own.X, y = world.Fruit[i].position.y - own.Y;
        long distance = (long)x * x + (long)y * y;
        if (distance < best) { best = distance; target = i; }
    }
    return target;
}'''
with tempfile.TemporaryDirectory(prefix='snake-recovery-access-eval-') as directory:
    path = Path(directory)
    (path / 'Evaluate.csproj').write_text(project, encoding='utf-8')
    (path / 'Program.cs').write_text(program, encoding='utf-8')
    print('Scripted policies only, not a player/device study; decisions and random streams may diverge after supply.', flush=True)
    print('First recovery supply only; stop after 5 seconds or match end; no boost, firstRun=false.', flush=True)
    raise SystemExit(subprocess.run(['dotnet', 'run', '--project', str(path / 'Evaluate.csproj'), '-c', 'Release', '--verbosity', 'quiet', '--', str(args.start), str(args.seeds)], check=False).returncode)
