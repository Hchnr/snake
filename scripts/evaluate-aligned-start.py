#!/usr/bin/env python3
"""Compare v5 and v6 offline opening behavior under fixed directional input.

One paired seed can diverge after different movement or collisions. Straight
lines are not human controls; results are evidence about these samples only.
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
foreach (byte heading in new byte[] { 0, 64, 128, 192 })
foreach (bool aligned in new[] { false, true })
{
    int back1 = 0, back10 = 0, back20 = 0, earlyFruit = 0, bite = 0, dead = 0, deadWithoutBite = 0;
    long firstFruitTicks = 0, firstBiteTicks = 0, eaten = 0, lost = 0;
    for (int seed = 1; seed <= samples; seed++)
    {
        var w = new PveFixedWorld(seed, false, null, true, localAdvantage: true, localOpeningFood: true,
            localOpeningEncounter: true, localRecoverySupply: true, localSafeRecovery: true, localAlignedStart: aligned);
        var player = w.Fighters[0].motion; int x = player.X, y = player.Y, firstFruit = -1, firstBite = -1;
        while (!w.Finished && w.Tick < 15 * PveFixedWorld.TickRate)
        {
            // The policy supplies an actual control immediately, including
            // when the chosen direction is the default rightward.
            w.Step(heading, false, aligned);
            long forward = (long)(player.X - x) * PveFixedMotion.DirectionX(heading) + (long)(player.Y - y) * PveFixedMotion.DirectionY(heading);
            if (w.Tick == 1 && forward < 0) back1++;
            if (w.Tick == 10 && forward < 0) back10++;
            if (w.Tick == 20 && forward < 0) back20++;
            if (firstFruit < 0 && w.TryGetStrengthLevelProgress("you", out int level, out int xp, out _) && (level > 0 || xp > 0)) firstFruit = w.Tick;
            if (firstBite < 0 && w.PlayerSegmentsEaten > 0) firstBite = w.Tick;
        }
        if (firstFruit >= 0 && firstFruit <= 2 * PveFixedWorld.TickRate) { earlyFruit++; firstFruitTicks += firstFruit; }
        if (firstBite >= 0) { bite++; firstBiteTicks += firstBite; }
        if (!w.Fighters[0].alive) { dead++; if (firstBite < 0) deadWithoutBite++; }
        eaten += w.PlayerSegmentsEaten; lost += w.PlayerSegmentsLostToBites;
    }
    Console.WriteLine($"heading={heading} variant={(aligned ? "v6" : "v5")} seeds={samples} back1={back1} back10={back10} back20={back20} " +
        $"fruit2s={earlyFruit} fruit_tick_sum={firstFruitTicks} bite15s={bite} bite_tick_sum={firstBiteTicks} " +
        $"dead15s={dead} dead_without_bite={deadWithoutBite} eaten={eaten} lost={lost}");
}
// Compare a first real left drag at different times to one that follows an
// earlier deliberate rightward input; tick 41 is just outside the window.
foreach (int change in new[] { 0, 10, 20, 39, 40, 60 })
foreach (bool alreadySteered in new[] { false, true })
{
    if (change == 0 && alreadySteered) continue; // no time for an earlier input
    int backward1 = 0, backward10 = 0, assists = 0;
    for (int seed = 1; seed <= samples; seed++)
    {
        var w = new PveFixedWorld(seed, false, null, true, localAdvantage: true, localOpeningFood: true,
            localOpeningEncounter: true, localRecoverySupply: true, localSafeRecovery: true, localAlignedStart: true);
        var player = w.Fighters[0].motion; int xAtChange = 0;
        while (!w.Finished && w.Tick < change + 10)
        {
            int tick = w.Tick;
            if (tick == change) xAtChange = player.X;
            w.Step(tick < change ? (byte)0 : (byte)128, false, alreadySteered || tick >= change);
            if (w.Tick == change + 1 && player.X > xAtChange) backward1++;
            if (w.Tick == change + 10 && player.X > xAtChange) backward10++;
        }
        if (w.OpeningTurnAssistUsed) assists++;
    }
    Console.WriteLine($"delayed_left_tick={change + 1} prior_right_input={alreadySteered} seeds={samples} " +
        $"assisted={assists} back1={backward1} back10={backward10}");
}'''
with tempfile.TemporaryDirectory(prefix='snake-aligned-start-eval-') as directory:
    path = Path(directory)
    (path / 'Evaluate.csproj').write_text(project, encoding='utf-8')
    (path / 'Program.cs').write_text(program, encoding='utf-8')
    print('Scripted input only, firstRun=false, no boost; RNG streams may diverge.', flush=True)
    raise SystemExit(subprocess.run(['dotnet', 'run', '--project', str(path / 'Evaluate.csproj'), '-c', 'Release', '--verbosity', 'quiet', '--', str(args.seeds)], check=False).returncode)
