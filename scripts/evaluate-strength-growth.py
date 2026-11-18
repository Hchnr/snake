#!/usr/bin/env python3
"""Paired scripted-input diagnostics for experimental account strength, not balance validation."""
import argparse
from pathlib import Path
import subprocess
import tempfile
from xml.sax.saxutils import escape

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--seeds', type=int, default=20, help='consecutive seeds, starting at 1')
args = parser.parse_args()
if not 1 <= args.seeds <= 10000:
    parser.error('--seeds must be between 1 and 10000')
core = escape(str(Path(__file__).resolve().parent.parent / 'unity/Assets/Snake/Core/*.cs'))
project = f'''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><Compile Include="Program.cs"/><Compile Include="{core}"/></ItemGroup></Project>'''
program = r'''using SnakeClub.Core;
int samples = int.Parse(args[0]);
int[] levels = { 1, 8, 41 };
Console.WriteLine($"rules={PveFixedRecording.ExperimentalStrengthRules} snapshot={StrengthEvalStartConfig.CurrentVersion} seeds=1..{samples} patterns=2");
Console.WriteLine("Same seed and precomputed inputs per pair; simulation random streams can diverge after outcomes differ.");
for (int pattern = 0; pattern < 2; pattern++)
{
    long[][] totals = levels.Select(_ => new long[6]).ToArray();
    int[] better = new int[2], worse = new int[2], same = new int[2];
    for (int seed = 1; seed <= samples; seed++)
    {
        int[] scores = new int[3];
        for (int k = 0; k < levels.Length; k++)
        {
            int level = levels[k];
            var profile = new PlayerProgress { experience = Progression.ExperienceForLevel(level), tutorialComplete = true };
            var snapshot = StrengthEvalStartConfig.Capture(profile);
            if (!snapshot.Supported || snapshot.accountLevel != level) throw new Exception("Unexpected account snapshot");
            var world = new PveFixedWorld(seed, false, null, true, snapshot);
            while (!world.Finished)
            {
                int tick = world.Tick;
                byte heading = pattern == 0 ? (byte)(tick / 80 * 31 + seed) : (byte)(tick / 53 * 37 + seed);
                bool boost = pattern == 0 ? tick % 120 >= 100 : tick % 120 >= 95;
                world.Step(heading, boost);
            }
            var player = world.Fighters[0];
            totals[k][0] += world.Winner == "you" ? 1 : 0;
            totals[k][1] += player.alive ? 1 : 0;
            totals[k][2] += player.kills;
            totals[k][3] += world.PlayerSegmentsEaten;
            totals[k][4] += world.PlayerSegmentsLostToBites;
            totals[k][5] += world.StrengthLevel("you");
            scores[k] = world.Winner == "you" ? 2 : player.alive ? 1 : 0;
        }
        for (int k = 1; k < levels.Length; k++)
            if (scores[k] > scores[0]) better[k-1]++; else if (scores[k] < scores[0]) worse[k-1]++; else same[k-1]++;
    }
    for (int k = 0; k < levels.Length; k++)
        Console.WriteLine($"pattern={pattern} level={levels[k]} matches={samples} wins={totals[k][0]} alive={totals[k][1]} kills={totals[k][2]} eaten={totals[k][3]} lost={totals[k][4]} match_levels_sum={totals[k][5]}");
    for (int k = 1; k < levels.Length; k++)
        Console.WriteLine($"pattern={pattern} level={levels[k]} vs=1 outcome_better={better[k-1]} same={same[k-1]} worse={worse[k-1]} (outcome order: win > alive > dead)");
}'''
with tempfile.TemporaryDirectory(prefix='snake-strength-growth-eval-') as directory:
    path = Path(directory)
    (path / 'Evaluate.csproj').write_text(project, encoding='utf-8')
    (path / 'Program.cs').write_text(program, encoding='utf-8')
    print('Scripted inputs only; not representative of players, device performance, or release balance.', flush=True)
    raise SystemExit(subprocess.run(['dotnet', 'run', '--project', str(path / 'Evaluate.csproj'), '-c', 'Release', '--verbosity', 'quiet', '--', str(args.seeds)], check=False).returncode)
