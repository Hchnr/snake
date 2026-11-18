#!/usr/bin/env python3
"""Compare deterministic strength-practice samples, not real-player balance."""
import argparse
from pathlib import Path
import subprocess
import tempfile
from xml.sax.saxutils import escape

parser = argparse.ArgumentParser()
parser.add_argument('--core-dir', type=Path, default=Path(__file__).resolve().parent.parent / 'unity/Assets/Snake/Core')
args = parser.parse_args()
sources = escape(str(args.core_dir.resolve() / '*.cs'))
project = f'''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><Compile Include="Program.cs"/><Compile Include="{sources}"/></ItemGroup></Project>'''
program = '''using SnakeClub.Core;
using System.Reflection;
for (int pattern = 0; pattern < 3; pattern++)
{
    long ticks = 0, attacks = 0, damage = 0, deaths = 0, wins = 0, kills = 0, attackMatches = 0;
    for (int seed = 1; seed <= 50; seed++)
    {
        var world = new PveFixedWorld(seed, false, null, true); bool attacked = false;
        var choose = (Func<int, byte>)Delegate.CreateDelegate(typeof(Func<int, byte>), world, typeof(PveFixedWorld).GetMethod("BotHeading", BindingFlags.Instance | BindingFlags.NonPublic)!);
        var boosts = (bool[])typeof(PveFixedWorld).GetField("botBoost", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(world)!;
        while (!world.Finished)
        {
            int tick = world.Tick;
            byte heading = pattern == 0 ? (byte)(tick / 80 * 31 + seed) : pattern == 1 ? (byte)(tick / 53 * 37 + seed) : choose(0);
            bool boost = pattern == 0 ? tick % 120 >= 100 : pattern == 1 ? tick % 120 >= 95 : boosts[0];
            world.Step(heading, boost); ticks++;
            if (world.LastPlayerAttackTick == world.Tick) { attacks += world.LastPlayerAttackCount; attacked = true; }
            if (world.LastPlayerDamageTick == world.Tick) damage += world.LastPlayerDamageCount;
        }
        if (attacked) attackMatches++;
        if (!world.Fighters[0].alive) deaths++;
        if (world.Winner == "you") wins++;
        kills += world.Fighters[0].kills;
    }
    Console.WriteLine($"pattern={pattern} matches=50 ticks={ticks} player_attack_matches={attackMatches} player_segments_eaten={attacks} player_segments_lost={damage} player_deaths={deaths} player_wins={wins} player_kills={kills}");
}'''
with tempfile.TemporaryDirectory(prefix='snake-strength-ai-eval-') as directory:
    path = Path(directory)
    (path / 'Evaluate.csproj').write_text(project, encoding='utf-8')
    (path / 'Program.cs').write_text(program, encoding='utf-8')
    print('Scripted neutral-account inputs; not a real-player or mobile-device evaluation.', flush=True)
    raise SystemExit(subprocess.run(['dotnet', 'run', '--project', str(path / 'Evaluate.csproj'), '-c', 'Release', '--verbosity', 'quiet'], check=False).returncode)
