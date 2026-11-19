#!/usr/bin/env python3
"""Compare two offline opening rules with fixed straight-line inputs.

The first two seconds are fully protected in both variants, so experience
in that window comes from real fruit, not body combat or elimination. These
are deterministic scripts, not player studies or guaranteed gameplay.
"""
import argparse
from pathlib import Path
import subprocess
import tempfile
from xml.sax.saxutils import escape

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--seeds', type=int, default=100, help='consecutive seeds starting at 1')
parser.add_argument('--boost', action='store_true', help='hold boost for the entire scripted sample')
args = parser.parse_args()
if not 1 <= args.seeds <= 10000:
    parser.error('--seeds must be between 1 and 10000')
core = escape(str(Path(__file__).resolve().parent.parent / 'unity/Assets/Snake/Core/*.cs'))
project = f'''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><Compile Include="Program.cs"/><Compile Include="{core}"/></ItemGroup></Project>'''
program = r'''using SnakeClub.Core;
int samples = int.Parse(args[0]);
bool boost = args[1] == "1";
Console.WriteLine($"seeds=1..{samples} firstRun=false seconds=10 boost={boost} same seeded inputs; random streams may diverge");
foreach (byte heading in new byte[] { 0, 64, 128, 192 })
foreach (bool openingFood in new[] { false, true })
{
    int earlyFruit = 0, progress = 0, deaths = 0, ateMatches = 0; long firstProgressTicks = 0, eaten = 0, lost = 0;
    for (int seed = 1; seed <= samples; seed++)
    {
        var world = new PveFixedWorld(seed, false, null, true, localAdvantage: true, localOpeningFood: openingFood);
        int first = -1;
        while (!world.Finished && world.Tick < 10 * PveFixedWorld.TickRate)
        {
            world.Step(heading, boost);
            if (first < 0 && world.TryGetStrengthLevelProgress("you", out int level, out int xp, out _) && (level > 0 || xp > 0)) first = world.Tick;
        }
        if (first >= 0) { progress++; firstProgressTicks += first; if (first <= 2 * PveFixedWorld.TickRate) earlyFruit++; }
        if (!world.Fighters[0].alive) deaths++;
        if (world.PlayerSegmentsEaten > 0) ateMatches++;
        eaten += world.PlayerSegmentsEaten; lost += world.PlayerSegmentsLostToBites;
    }
    Console.WriteLine($"heading={heading} opening_food={openingFood} matches={samples} fruit_within_2s={earlyFruit} progress_matches={progress} first_progress_tick_sum={firstProgressTicks} deaths_within_10s={deaths} body_eating_matches={ateMatches} body_segments_eaten={eaten} body_segments_lost={lost}");
}'''
with tempfile.TemporaryDirectory(prefix='snake-opening-food-eval-') as directory:
    path = Path(directory)
    (path / 'Evaluate.csproj').write_text(project, encoding='utf-8')
    (path / 'Program.cs').write_text(program, encoding='utf-8')
    print('Straight-line scripted inputs only; not a real-player or device test.', flush=True)
    raise SystemExit(subprocess.run(['dotnet', 'run', '--project', str(path / 'Evaluate.csproj'), '-c', 'Release', '--verbosity', 'quiet', '--', str(args.seeds), '1' if args.boost else '0'], check=False).returncode)
