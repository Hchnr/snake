#!/usr/bin/env python3
"""Measure desktop .NET daily-simulation allocations; this is not a device benchmark."""
from pathlib import Path
import subprocess
import sys
import tempfile
from xml.sax.saxutils import escape

root = Path(__file__).resolve().parent.parent
sources = escape(str(root / 'unity/Assets/Snake/Core/*.cs'))
project = f'''<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework>
<ImplicitUsings>enable</ImplicitUsings><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>
<ItemGroup><Compile Include="Program.cs"/><Compile Include="{sources}"/></ItemGroup></Project>'''
program = '''using SnakeClub.Core;
for (int pass = 0; pass < 2; pass++)
{
    long ticks = 0, before = GC.GetAllocatedBytesForCurrentThread();
    var timer = System.Diagnostics.Stopwatch.StartNew();
    for (int seed = 1; seed <= 20; seed++)
    {
        var match = new DailyMatch(seed);
        while (!match.Finished && match.State.tick < 600)
        {
            int tick = match.State.tick;
            match.Step((byte)((tick / 30 * 13 + seed * 7) & 255), tick % 90 > 70);
            ticks++;
        }
    }
    timer.Stop();
    long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
    Console.WriteLine($"{(pass == 0 ? "warmup" : "measured")}: ticks={ticks} bytes={bytes} elapsed_ms={timer.ElapsedMilliseconds}");
}'''
with tempfile.TemporaryDirectory(prefix='snake-daily-benchmark-') as directory:
    path = Path(directory)
    (path / 'Benchmark.csproj').write_text(project, encoding='utf-8')
    (path / 'Program.cs').write_text(program, encoding='utf-8')
    print('Desktop .NET benchmark; includes match setup and excludes Unity rendering.', flush=True)
    sys.exit(subprocess.run(['dotnet', 'run', '--project', str(path / 'Benchmark.csproj'), '-c', 'Release', '--verbosity', 'quiet'], check=False).returncode)
