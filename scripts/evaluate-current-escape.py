#!/usr/bin/env python3
"""Measure v16 escape and later blocking with scripted strategies, not people.

Runs the unchanged Core in a temporary .NET 10 project. Death or the end of
the match can truncate observation; blocking streaks may involve different opponents.
"""
import argparse
from pathlib import Path
import subprocess
import tempfile
from xml.sax.saxutils import escape

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--seeds', type=int, default=100, help='consecutive seeds starting at 1')
args = parser.parse_args()
if not 1 <= args.seeds <= 1000:
    parser.error('--seeds must be between 1 and 1000')
root = Path(__file__).resolve().parent.parent
core = escape(str(root / 'unity/Assets/Snake/Core/*.cs'))
source = escape(str(root / 'scripts/escape-current-evaluation/Program.cs'))
project = f'''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><Compile Include="{source}"/><Compile Include="{core}"/></ItemGroup></Project>'''
with tempfile.TemporaryDirectory(prefix='snake-squeeze-eval-') as directory:
    path = Path(directory) / 'Evaluate.csproj'
    path.write_text(project, encoding='utf-8')
    subprocess.run(['dotnet', 'run', '--project', str(path), '-c', 'Release', '--', str(args.seeds)], check=True)
