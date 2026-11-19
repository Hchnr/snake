#!/usr/bin/env python3
"""Run an isolated, headless v10 escape regression using the real Core."""
from pathlib import Path
import subprocess
import tempfile
from xml.sax.saxutils import escape

root = Path(__file__).resolve().parent.parent
core = escape(str(root / 'unity/Assets/Snake/Core/*.cs'))
source = escape(str(root / 'test/squeeze/Program.cs'))
project = f'''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><Compile Include="Entry.cs"/><Compile Include="{source}"/><Compile Include="{core}"/></ItemGroup></Project>'''
with tempfile.TemporaryDirectory(prefix='snake-squeeze-test-') as directory:
    path = Path(directory) / 'Test.csproj'
    path.write_text(project, encoding='utf-8')
    (Path(directory) / 'Entry.cs').write_text('SqueezeEscapeRegression.Run();\n', encoding='utf-8')
    subprocess.run(['dotnet', 'run', '--project', str(path), '-c', 'Release'], check=True)
