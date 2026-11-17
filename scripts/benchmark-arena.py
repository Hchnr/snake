#!/usr/bin/env python3
"""Measure local Arena simulation allocations on desktop .NET, not device FPS."""
from pathlib import Path
import subprocess
import tempfile
from xml.sax.saxutils import escape

root = Path(__file__).resolve().parent.parent
sources = escape(str(root / 'unity/Assets/Snake/Core/*.cs'))
project = f'''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><Compile Include="Program.cs"/><Compile Include="{sources}"/></ItemGroup></Project>'''
program = '''using SnakeClub.Core;
for (int pass = 0; pass < 2; pass++)
{
    long ticks = 0, before = GC.GetAllocatedBytesForCurrentThread();
    var timer = System.Diagnostics.Stopwatch.StartNew();
    for (int seed = 1; seed <= 20; seed++)
    {
        var arena = new Arena(seed, fruitCount: 0, roundSeconds: 60f, enableCaptures: true);
        for (int i = 0; i < 8; i++)
        {
            var snake = arena.AddSnake("p" + i, "player", false);
            snake.shield = 0; snake.angle = 0;
            for (int j = 0; j < snake.segments.Count; j++) snake.segments[j] = new Point(-j * Arena.SegmentSpacing, -70 + i * 20);
            arena.SetInput(snake.id, new Point(1, 0), false);
        }
        arena.DisableStar(); arena.Start();
        while (arena.State.phase == "running" && arena.State.tick < 600) { arena.Tick(.05f); ticks++; }
    }
    timer.Stop();
    long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
    Console.WriteLine($"{(pass == 0 ? "warmup" : "measured")}: ticks={ticks} bytes={bytes} bytes_per_tick={(ticks == 0 ? 0 : bytes / ticks)} elapsed_ms={timer.ElapsedMilliseconds}");
}'''
with tempfile.TemporaryDirectory(prefix='snake-arena-benchmark-') as directory:
    path = Path(directory)
    (path / 'Benchmark.csproj').write_text(project, encoding='utf-8')
    (path / 'Program.cs').write_text(program, encoding='utf-8')
    print('Desktop .NET benchmark; includes match setup and excludes Unity rendering.', flush=True)
    raise SystemExit(subprocess.run(['dotnet', 'run', '--project', str(path / 'Benchmark.csproj'), '-c', 'Release', '--verbosity', 'quiet'], check=False).returncode)
