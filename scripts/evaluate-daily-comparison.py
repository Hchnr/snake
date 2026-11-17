#!/usr/bin/env python3
"""Measure opening deaths in offline A/B/C rules; this is not player research."""
from pathlib import Path
import os
import subprocess
import tempfile
from xml.sax.saxutils import escape

root = Path(__file__).resolve().parent.parent
core = Path(os.environ.get('SNAKE_EVAL_CORE', str(root / 'unity/Assets/Snake/Core')))
sources = escape(str(core / '*.cs'))
project = f'''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><Compile Include="Program.cs"/><Compile Include="{sources}"/></ItemGroup></Project>'''
program = '''using SnakeClub.Core;
const int samples = 200;
foreach (int variant in new[] { 0, 1, 2 })
foreach (int style in new[] { 0, 1 })
{
    int dead5 = 0, dead10 = 0, finishedAlive = 0;
    for (int seed = 1; seed <= samples; seed++)
    {
        var match = variant == 0 ? new DailyMatch(seed, DailyMatch.PreviousRulesVersion) :
            variant == 1 ? new DailyMatch(seed, DailyMatch.RulesVersion) : DailyMatch.CreateNoCaptureComparison(seed);
        while (!match.Finished && match.State.tick < 200)
        {
            byte heading = style == 0 ? (byte)0 : (byte)(match.State.tick / 30 * 6);
            match.Step(heading, false);
        }
        if (!match.State.snakes[0].alive)
        {
            if (match.State.tick <= 100) dead5++;
            if (match.State.tick <= 200) dead10++;
        }
        else if (match.Finished) finishedAlive++;
    }
    Console.WriteLine($"variant={(char)('A' + variant)} input={(style == 0 ? "fixed" : "turning")} deaths_by_5s={dead5}/{samples} deaths_by_10s={dead10}/{samples} finished_alive_before_10s={finishedAlive}/{samples}");
}'''
with tempfile.TemporaryDirectory(prefix='snake-comparison-opening-') as directory:
    path = Path(directory)
    (path / 'Evaluation.csproj').write_text(project, encoding='utf-8')
    (path / 'Program.cs').write_text(program, encoding='utf-8')
    print('Desktop .NET, 200 seeds per variant and input; fixed scripts are not real-player behavior.', flush=True)
    raise SystemExit(subprocess.run(['dotnet', 'run', '--project', str(path / 'Evaluation.csproj'), '-c', 'Release', '--verbosity', 'quiet'], check=False).returncode)
