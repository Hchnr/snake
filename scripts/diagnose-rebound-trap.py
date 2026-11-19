#!/usr/bin/env python3
"""Reproduce the scripted v9 seed 35 rebound trap without modifying the game.

A nearest-tail policy runs at 20 Hz and updates its heading every eight steps.
At step 465, eight branches replace that policy with fixed screen directions.
The original policy is retained as a ninth branch. This is not a human-input,
mobile, continuous-geometry, or general gameplay assessment; it checks one
previously observed regression against the current v9 implementation.
Requires .NET 10 SDK. No editor, graphics or network are used.
"""
from pathlib import Path
import subprocess
import tempfile
from xml.sax.saxutils import escape

root = Path(__file__).resolve().parent.parent
core = escape(str(root / 'unity/Assets/Snake/Core/*.cs'))
source = escape(str(root / 'scripts/rebound-trap-diagnostic/Program.cs'))
project = f'''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>disable</Nullable><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><Compile Include="{source}"/><Compile Include="{core}"/></ItemGroup></Project>'''
with tempfile.TemporaryDirectory(prefix='snake-rebound-diagnostic-') as directory:
    path = Path(directory) / 'Diagnostic.csproj'
    path.write_text(project, encoding='utf-8')
    subprocess.run(['dotnet', 'run', '--project', str(path), '-c', 'Release'], check=True)
