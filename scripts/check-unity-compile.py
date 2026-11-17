#!/usr/bin/env python3
"""Compile runtime sources against local Unity assemblies without starting Unity."""
from pathlib import Path
from xml.sax.saxutils import escape
import os
import subprocess
import sys
import tempfile

root = Path(__file__).resolve().parent.parent
editor = Path(os.environ.get('UNITY_EDITOR', '/Applications/Unity/Hub/Editor/6000.0.85f2/Unity.app/Contents/MacOS/Unity'))
engine = editor.parent.parent / 'Managed' / 'UnityEngine'
ui = root / 'unity/Library/ScriptAssemblies/UnityEngine.UI.dll'
if not engine.is_dir() or not ui.is_file():
    sys.exit('Unity reference assemblies or the imported UnityEngine.UI.dll are missing; set UNITY_EDITOR and import the project first.')
references = sorted(engine.glob('*.dll')) + [ui]
douyin = '--douyin' in sys.argv[1:]
if any(arg != '--douyin' for arg in sys.argv[1:]):
    sys.exit('Usage: check-unity-compile.py [--douyin]')
if douyin:
    ttsdk = root / 'unity/Library/ByteGame-BGDT/com.bytedance.starksdk/ttsdk.dll'
    if not ttsdk.is_file():
        sys.exit('TTSDK reference is missing; import TTSDK into the Unity project first.')
    litjson = root / 'unity/Library/ScriptAssemblies/TTLitJson.dll'
    if not litjson.is_file():
        sys.exit('TTSDK LitJson reference is missing; import TTSDK into the Unity project first.')
    references.extend([ttsdk, litjson])
items = '\n'.join(f'<Reference Include="{escape(path.stem)}"><HintPath>{escape(str(path))}</HintPath></Reference>' for path in references)
source = escape(str(root / 'unity/Assets/Snake'))
project = f'''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>netstandard2.1</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems><LangVersion>latest</LangVersion><DefineConstants>{"SNAKE_DOUYIN_TTSDK;UNITY_WEBGL" if douyin else ""}</DefineConstants><NoWarn>0618;0649</NoWarn></PropertyGroup>
  <ItemGroup><Compile Include="{source}/*.cs"/><Compile Include="{source}/Core/*.cs"/>{items}</ItemGroup>
</Project>'''
with tempfile.TemporaryDirectory(prefix='snake-unity-compile-') as directory:
    file = Path(directory) / 'Check.csproj'
    file.write_text(project, encoding='utf-8')
    print('Compiling Unity runtime C# without launching the editor or game...', flush=True)
    sys.exit(subprocess.run(['dotnet', 'build', str(file), '--nologo', '--verbosity', 'quiet'], check=False).returncode)
