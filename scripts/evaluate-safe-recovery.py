#!/usr/bin/env python3
"""Compare first recovery-supply placement in local v4 and v5.

The comparison uses scripts, not players. Tail/fruit policies can choose
different directions after placement, and subsequent random streams can differ.
"""
import argparse
from pathlib import Path
import subprocess
import tempfile
from xml.sax.saxutils import escape

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--start', type=int, default=1, help='first consecutive seed')
parser.add_argument('--seeds', type=int, default=300, help='number of consecutive seeds')
args = parser.parse_args()
if not 1 <= args.seeds <= 10000:
    parser.error('--seeds must be between 1 and 10000')
if args.start < -(2 ** 31) or args.start + args.seeds - 1 >= 2 ** 31:
    parser.error('seed range must fit signed 32-bit integers')
core = escape(str(Path(__file__).resolve().parent.parent / 'unity/Assets/Snake/Core/*.cs'))
project = f'''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><Compile Include="Program.cs"/><Compile Include="{core}"/></ItemGroup></Project>'''
program = r'''using SnakeClub.Core;
int start = int.Parse(args[0]), samples = int.Parse(args[1]);
Console.WriteLine($"seeds={start}..{(long)start + samples - 1} firstRun=false boost=false; first eligible bite only");
for (int policy = 0; policy < 4; policy++)
{
    var totals = new Totals[] { new Totals(), new Totals() };
    int eligible = 0;
    for (int offset = 0; offset < samples; offset++)
    {
        int seed = start + offset;
        var old = Simulate(seed, policy, false);
        var current = Simulate(seed, policy, true);
        if (old.firstTick != current.firstTick || old.lengthBefore != current.lengthBefore)
            throw new Exception($"Variants differed before first eligible bite: seed={seed} policy={policy}");
        if (old.firstTick < 0) continue;
        eligible++;
        totals[0].Add(old); totals[1].Add(current);
    }
    string name = policy == 0 ? "heading_0" : policy == 1 ? "heading_64" : policy == 2 ? "nearest_tail" : "nearest_fruit";
    for (int variant = 0; variant < 2; variant++)
    {
        var t = totals[variant];
        Console.WriteLine($"policy={name} variant=v{4 + variant} seeds={samples} eligible={eligible} no_eligible={samples - eligible} " +
            $"spawn0={t.counts[0]} spawn1={t.counts[1]} spawn2={t.counts[2]} spawn3={t.counts[3]} spawn4={t.counts[4]} " +
            $"spawned_fruit={t.spawned} events_inside_ai_pickup={t.insideEvents} fruit_inside_ai_pickup={t.insideFruit} " +
            $"events_within_5000_of_ai={t.nearEvents} fruit_within_5000_of_ai={t.nearFruit} " +
            $"next_step_ai_events={t.aiEvents} next_step_ai_fruit={t.aiFruit} next_step_player_events={t.playerEvents} next_step_player_fruit={t.playerFruit}");
    }
}

static Result Simulate(int seed, int policy, bool safe)
{
    var world = new PveFixedWorld(seed, false, null, true, localAdvantage: true, localOpeningFood: true,
        localOpeningEncounter: true, localRecoverySupply: true, localSafeRecovery: safe);
    byte heading = (byte)(policy == 1 ? 64 : 0);
    while (!world.Finished)
    {
        heading = SelectHeading(world, policy, heading);
        int priorLost = world.PlayerSegmentsLostToBites, priorLength = world.Fighters[0].motion.Length;
        world.Step(heading, false);
        if (world.PlayerSegmentsLostToBites <= priorLost || !world.Fighters[0].alive || world.Finished ||
            world.Fighters[0].motion.Length > PveFixedWorld.RecoverySupplyLengthLimit) continue;

        var result = new Result { firstTick = world.Tick, lengthBefore = priorLength };
        for (int i = 0; i < world.Fruit.Count; i++)
        {
            if (!world.IsRecoveryFruit(i)) continue;
            result.spawned++;
            bool inside = false, near = false;
            foreach (var opponent in world.Fighters)
            {
                if (opponent.id == "you" || !opponent.alive) continue;
                long dx = (long)opponent.motion.X - world.Fruit[i].position.x;
                long dy = (long)opponent.motion.Y - world.Fruit[i].position.y;
                long distance = dx * dx + dy * dy;
                if (distance <= 1250L * 1250L) inside = true;
                if (distance <= 5000L * 5000L) near = true;
            }
            if (inside) result.insideFruit++;
            if (near) result.nearFruit++;
        }
        int beforeAi = world.RecoverySupplyCollectedByOpponents, beforePlayer = world.RecoverySupplyCollectedByPlayer;
        heading = SelectHeading(world, policy, heading);
        world.Step(heading, false);
        result.aiFruit = world.RecoverySupplyCollectedByOpponents - beforeAi;
        result.playerFruit = world.RecoverySupplyCollectedByPlayer - beforePlayer;
        return result;
    }
    return new Result { firstTick = -1 };
}

static byte SelectHeading(PveFixedWorld world, int policy, byte heading)
{
    if (policy < 2 || world.Tick % 8 != 0) return heading;
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
    return best == long.MaxValue ? heading : (byte)(((int)Math.Round(Math.Atan2(dy, dx) * 128 / Math.PI) + 256) & 255);
}

sealed class Result
{
    public int firstTick, lengthBefore, spawned, insideFruit, nearFruit, aiFruit, playerFruit;
}
sealed class Totals
{
    public readonly int[] counts = new int[PveFixedWorld.RecoverySupplyCount + 1];
    public int spawned, insideEvents, insideFruit, nearEvents, nearFruit, aiEvents, aiFruit, playerEvents, playerFruit;
    public void Add(Result r)
    {
        counts[r.spawned]++; spawned += r.spawned;
        if (r.insideFruit > 0) insideEvents++; insideFruit += r.insideFruit;
        if (r.nearFruit > 0) nearEvents++; nearFruit += r.nearFruit;
        if (r.aiFruit > 0) aiEvents++; aiFruit += r.aiFruit;
        if (r.playerFruit > 0) playerEvents++; playerFruit += r.playerFruit;
    }
}'''
with tempfile.TemporaryDirectory(prefix='snake-safe-recovery-eval-') as directory:
    path = Path(directory)
    (path / 'Evaluate.csproj').write_text(project, encoding='utf-8')
    (path / 'Program.cs').write_text(program, encoding='utf-8')
    print('Paired scripted policies, not a player or device study; behavior and RNG may diverge after placement.', flush=True)
    print('5,000 is a conservative geometry margin, not a measured pickup; next-step counts measure actual collection.', flush=True)
    raise SystemExit(subprocess.run(['dotnet', 'run', '--project', str(path / 'Evaluate.csproj'), '-c', 'Release', '--verbosity', 'quiet', '--', str(args.start), str(args.seeds)], check=False).returncode)
