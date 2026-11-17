#!/usr/bin/env python3
"""Measure default PvE encounters in desktop .NET; not human or device testing."""
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
foreach (int style in new[] { 0, 1 })
{
    int[] at = { 0, 60, 100, 160, 300 };
    int[] active = new int[at.Length], near = new int[at.Length], bots = new int[at.Length];
    var deaths = new SortedDictionary<string, int>();
    for (int seed = 1; seed <= samples; seed++)
    {
        var arena = Arena.CreateStandardPve(seed, true, "玩家");
        for (int n = 0; n < at.Length; n++)
        {
            while (arena.State.tick < at[n] && arena.State.phase == "running" && arena.Find("you").alive)
            {
                byte heading = style == 0 ? (byte)0 : (byte)(arena.State.tick / 30 * 6);
                arena.SetInput("you", PveRecording.Direction(heading), false);
                arena.Tick(.05f);
            }
            var me = arena.Find("you");
            if (arena.State.tick != at[n] || !me.alive) continue;
            active[n]++;
            int inView = 0, living = 0;
            foreach (var enemy in arena.State.snakes)
            {
                if (enemy == me || !enemy.alive) continue;
                living++;
                if (Math.Abs(enemy.segments[0].x - me.segments[0].x) <= 11 && Math.Abs(enemy.segments[0].y - me.segments[0].y) <= 19) inView++;
            }
            bots[n] += living;
            if (inView > 0) near[n]++;
        }
        var player = arena.Find("you");
        if (!player.alive && arena.State.tick <= 300)
        {
            string window = arena.State.tick <= 100 ? "0-5s" : arena.State.tick <= 160 ? "5-8s" : "8-15s";
            string key = window + "/" + player.eliminationReason;
            deaths[key] = deaths.GetValueOrDefault(key) + 1;
        }
    }
    Console.WriteLine(style == 0 ? "input=fixed" : "input=turning");
    for (int i = 0; i < at.Length; i++)
        Console.WriteLine($"t={at[i] / 20.0:F1}s alive={active[i]}/{samples} near={near[i]}/{active[i]} mean_bots={(active[i] == 0 ? 0 : (double)bots[i] / active[i]):F2}");
    Console.WriteLine("deaths=" + string.Join(", ", deaths.Select(p => p.Key + ":" + p.Value)));
}'''
with tempfile.TemporaryDirectory(prefix='snake-arena-pve-eval-') as directory:
    path = Path(directory)
    (path / 'Evaluation.csproj').write_text(project, encoding='utf-8')
    (path / 'Program.cs').write_text(program, encoding='utf-8')
    print('Desktop simulation, 200 seeds per input pattern; near uses an approximate camera rectangle.', flush=True)
    raise SystemExit(subprocess.run(['dotnet', 'run', '--project', str(path / 'Evaluation.csproj'), '-c', 'Release', '--verbosity', 'quiet'], check=False).returncode)
