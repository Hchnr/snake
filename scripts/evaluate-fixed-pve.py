#!/usr/bin/env python3
"""Measure deterministic first encounters; this is not human playtesting."""
from pathlib import Path
import os
import subprocess
import tempfile
from xml.sax.saxutils import escape

root = Path(__file__).resolve().parent.parent
sources = escape(str(Path(os.environ.get('SNAKE_EVAL_CORE', str(root / 'unity/Assets/Snake/Core'))) / '*.cs'))
project = f'''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><Compile Include="Program.cs"/><Compile Include="{sources}"/></ItemGroup></Project>'''
program = '''using SnakeClub.Core;
const int samples = 200;
foreach (int style in new[] { 0, 1 })
{
    int[] at = { 0, 60, 100, 160, 300 };
    int[] active = new int[at.Length], near = new int[at.Length], bots = new int[at.Length];
    for (int seed = 1; seed <= samples; seed++)
    {
        var world = new PveFixedWorld(seed, true, new PlayerProgress());
        for (int n = 0; n < at.Length; n++)
        {
            while (world.Tick < at[n] && !world.Finished)
                world.Step(style == 0 ? (byte)0 : (byte)(world.Tick / 30 * 6), false);
            if (world.Tick != at[n] || !world.Fighters[0].alive) continue;
            active[n]++;
            var me = world.Fighters[0].motion;
            int inView = 0, living = 0;
            for (int i = 1; i < world.Fighters.Count; i++)
            {
                var enemy = world.Fighters[i]; if (!enemy.alive) continue;
                living++;
                if (Math.Abs(enemy.motion.X - me.X) <= 11000 && Math.Abs(enemy.motion.Y - me.Y) <= 19000) inView++;
            }
            bots[n] += living;
            if (inView > 0) near[n]++;
        }
    }
    Console.WriteLine(style == 0 ? "input=fixed" : "input=turning");
    for (int i = 0; i < at.Length; i++)
        Console.WriteLine($"t={at[i] / 20.0:F1}s alive={active[i]}/{samples} near={near[i]}/{active[i]} mean_bots={(active[i] == 0 ? 0 : (double)bots[i] / active[i]):F2}");
}
'''
with tempfile.TemporaryDirectory(prefix='snake-fixed-pve-eval-') as directory:
    path = Path(directory)
    (path / 'Evaluation.csproj').write_text(project, encoding='utf-8')
    (path / 'Program.cs').write_text(program, encoding='utf-8')
    print('Desktop simulation, 200 seeds per input pattern; near uses an approximate camera rectangle.', flush=True)
    raise SystemExit(subprocess.run(['dotnet', 'run', '--project', str(path / 'Evaluation.csproj'), '-c', 'Release', '--verbosity', 'quiet'], check=False).returncode)
