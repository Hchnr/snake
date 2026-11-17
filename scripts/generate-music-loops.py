#!/usr/bin/env python3
"""Render the repository's original placeholder loops as mono PCM WAV files."""
from pathlib import Path
import subprocess
import tempfile
from xml.sax.saxutils import escape

root = Path(__file__).resolve().parent.parent
sources = escape(str(root / 'unity/Assets/Snake/Core/*.cs'))
output = root / 'unity/Assets/Resources/Music'
project = f'''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><Compile Include="Program.cs"/><Compile Include="{sources}"/></ItemGroup></Project>'''
program = '''using SnakeClub.Core;
using System.Diagnostics;
using System.Text;
Directory.CreateDirectory(args[0]);
foreach (bool battle in new[] { false, true })
{
    var timer = Stopwatch.StartNew();
    float[] samples = MusicComposer.Create(battle);
    timer.Stop();
    string path = Path.Combine(args[0], battle ? "Battle.wav" : "Home.wav");
    using var file = File.Create(path);
    using var writer = new BinaryWriter(file, Encoding.ASCII);
    int dataLength = samples.Length * 2;
    writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + dataLength);
    writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((short)1); writer.Write((short)1);
    writer.Write(MusicComposer.SampleRate); writer.Write(MusicComposer.SampleRate * 2);
    writer.Write((short)2); writer.Write((short)16); writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(dataLength);
    foreach (float sample in samples) writer.Write((short)Math.Round(Math.Clamp(sample, -1f, 1f) * short.MaxValue));
    Console.WriteLine($"{Path.GetFileName(path)}: {samples.Length} samples, source synthesis {timer.ElapsedMilliseconds} ms");
}'''
with tempfile.TemporaryDirectory(prefix='snake-music-render-') as directory:
    path = Path(directory)
    (path / 'Music.csproj').write_text(project, encoding='utf-8')
    (path / 'Program.cs').write_text(program, encoding='utf-8')
    subprocess.run(['dotnet', 'run', '--project', str(path / 'Music.csproj'), '-c', 'Release', '--verbosity', 'quiet', '--', str(output)], check=True)
