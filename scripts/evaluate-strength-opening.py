#!/usr/bin/env python3
"""Compare starting-length candidates in an isolated scripted strength match.

The candidate length is injected into each freshly created world solely for
this diagnostic. It is not the shipped rule, a real-player study, or a replay
compatibility test; differing states can cause random streams to diverge.
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
using System.Reflection;
int samples = int.Parse(args[0]);
Console.WriteLine($"rules={PveFixedRecording.ExperimentalStrengthRules} seeds=1..{samples} patterns=2; same initial seed/input, random streams may diverge");
for (int pattern = 0; pattern < 2; pattern++)
for (int bonus = 0; bonus <= 12; bonus += 4)
{
    int wins = 0, deaths = 0, earlyDeaths = 0, ateMatches = 0, noProgressDeaths = 0;
    long eaten = 0, lost = 0, firstProgressTickSum = 0; int progressMatches = 0;
    for (int seed = 1; seed <= samples; seed++)
    {
        var world = new PveFixedWorld(seed, false, null, true);
        world.Fighters[0].motion.Grow(bonus);
        var xp = (int[])typeof(PveFixedWorld).GetField("strengthExperience", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(world)!;
        int firstProgress = -1;
        while (!world.Finished)
        {
            int tick = world.Tick;
            byte heading = pattern == 0 ? (byte)(tick / 80 * 31 + seed) : (byte)(tick / 53 * 37 + seed);
            bool boost = pattern == 0 ? tick % 120 >= 100 : tick % 120 >= 95;
            world.Step(heading, boost);
            if (firstProgress < 0 && xp[0] > 0) firstProgress = world.Tick;
        }
        if (world.Winner == "you") wins++;
        if (!world.Fighters[0].alive)
        {
            deaths++; if (world.Tick <= 10 * PveFixedWorld.TickRate) earlyDeaths++;
            if (firstProgress < 0) noProgressDeaths++;
        }
        if (firstProgress >= 0) { progressMatches++; firstProgressTickSum += firstProgress; }
        if (world.PlayerSegmentsEaten > 0) ateMatches++;
        eaten += world.PlayerSegmentsEaten; lost += world.PlayerSegmentsLostToBites;
    }
    Console.WriteLine($"pattern={pattern} initial_extra_segments={bonus} matches={samples} wins={wins} deaths={deaths} early_deaths={earlyDeaths} deaths_without_progress={noProgressDeaths} progress_matches={progressMatches} first_progress_tick_sum={firstProgressTickSum} body_eating_matches={ateMatches} body_segments_eaten={eaten} body_segments_lost={lost}");
}'''
with tempfile.TemporaryDirectory(prefix='snake-strength-opening-eval-') as directory:
    path = Path(directory)
    (path / 'Evaluate.csproj').write_text(project, encoding='utf-8')
    (path / 'Program.cs').write_text(program, encoding='utf-8')
    print('Fixed scripted inputs; progress means experience from fruit, body bites or eliminations. Not a human or device test.', flush=True)
    raise SystemExit(subprocess.run(['dotnet', 'run', '--project', str(path / 'Evaluate.csproj'), '-c', 'Release', '--verbosity', 'quiet', '--', str(args.seeds)], check=False).returncode)
