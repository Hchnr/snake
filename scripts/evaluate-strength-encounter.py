#!/usr/bin/env python3
"""Compare offline opening variants using deterministic scripted inputs.

Neither straight-line input nor the simple tail-chasing policy represents real
players. Different events may cause the variants' random streams to diverge.
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
Console.WriteLine($"seeds=1..{samples} firstRun=false seconds=15 boost=false; random streams may diverge");
for (int pattern = 0; pattern < 5; pattern++)
foreach (bool encounter in new[] { false, true })
{
    int bite = 0, dead = 0, deadWithoutBite = 0, early = 0, firstSum = 0, near10 = 0;
    long eaten = 0, lost = 0;
    for (int seed = 1; seed <= samples; seed++)
    {
        var world = new PveFixedWorld(seed, false, null, true, localAdvantage: true, localOpeningFood: true, localOpeningEncounter: encounter);
        int first = -1; byte heading = (byte)(pattern * 64); bool near = false;
        while (!world.Finished && world.Tick < 15 * PveFixedWorld.TickRate)
        {
            if (pattern == 4 && world.Tick % 8 == 0)
            {
                var own = world.Fighters[0].motion; long best = long.MaxValue; int dx = 0, dy = 0;
                foreach (var fighter in world.Fighters)
                {
                    if (fighter.id == "you" || !fighter.alive || fighter.motion.Length <= PveFixedMotion.MinimumLength) continue;
                    var tail = fighter.motion.Segments[fighter.motion.Length - 1];
                    int x = tail.x - own.X, y = tail.y - own.Y; long distance = (long)x * x + (long)y * y;
                    if (distance < best) { best = distance; dx = x; dy = y; }
                }
                if (best < long.MaxValue) heading = (byte)(((int)Math.Round(Math.Atan2(dy, dx) * 128 / Math.PI) + 256) & 255);
            }
            world.Step(heading, false);
            if (first < 0 && world.PlayerSegmentsEaten > 0) first = world.Tick;
            if (world.Tick == 10 * PveFixedWorld.TickRate && world.Fighters[0].alive)
            {
                var player = world.Fighters[0].motion;
                foreach (var fighter in world.Fighters)
                {
                    if (fighter.id == "you" || !fighter.alive) continue;
                    long x = fighter.motion.X - player.X, y = fighter.motion.Y - player.Y;
                    if (x * x + y * y < 20000L * 20000L) near = true;
                }
            }
        }
        if (first >= 0) { bite++; firstSum += first; }
        if (!world.Fighters[0].alive) { dead++; if (first < 0) deadWithoutBite++; if (world.Tick <= 200) early++; }
        if (near) near10++;
        eaten += world.PlayerSegmentsEaten; lost += world.PlayerSegmentsLostToBites;
    }
    string policy = pattern == 4 ? "nearest_tail_every_8_ticks" : $"heading_{pattern * 64}";
    Console.WriteLine($"policy={policy} opening_encounter={encounter} matches={samples} bite_matches={bite} first_bite_tick_sum={firstSum} deaths15={dead} deaths_without_bite={deadWithoutBite} deaths10={early} near10={near10} eaten={eaten} lost={lost}");
}'''
with tempfile.TemporaryDirectory(prefix='snake-opening-encounter-eval-') as directory:
    path = Path(directory)
    (path / 'Evaluate.csproj').write_text(project, encoding='utf-8')
    (path / 'Program.cs').write_text(program, encoding='utf-8')
    print('Scripted input comparison only; not a real-player or device test.', flush=True)
    raise SystemExit(subprocess.run(['dotnet', 'run', '--project', str(path / 'Evaluate.csproj'), '-c', 'Release', '--verbosity', 'quiet', '--', str(args.seeds)], check=False).returncode)
