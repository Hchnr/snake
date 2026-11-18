#!/usr/bin/env python3
"""Build a local Unity player without closing an already-open project."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

root = Path(__file__).resolve().parent.parent
parser = argparse.ArgumentParser()
parser.add_argument('target', choices=('mac', 'webgl'))
parser.add_argument('--output', required=True, help='new player output path')
parser.add_argument('--offline-practice', action='store_true', help='disable development login and reference-only characters in the copied project')
parser.add_argument('--comparison-preview', action='store_true', help='enable offline A/B/C gameplay comparison')
parser.add_argument('--fixed-pve-preview', action='store_true', help='enable experimental integer PvE in an offline practice build')
parser.add_argument('--strength-pve-preview', action='store_true', help='enable experimental strength body-eating in offline practice')
parser.add_argument('--fixed-pve-local-api', help='enable local fixed PvE verification against http://127.0.0.1:PORT')
parser.add_argument('--no-graphics', action='store_true', help='run the build with Unity -nographics (availability depends on build target)')
args = parser.parse_args()
if args.strength_pve_preview and (not args.offline_practice or args.comparison_preview or args.fixed_pve_local_api):
    parser.error('--strength-pve-preview requires offline practice and cannot be combined with comparison or a local verifier')
if args.strength_pve_preview:
    args.fixed_pve_preview = True
if args.comparison_preview and (not args.offline_practice or args.fixed_pve_preview or args.fixed_pve_local_api):
    parser.error('--comparison-preview requires --offline-practice and cannot be combined with fixed PvE flags')
if args.fixed_pve_preview and not args.offline_practice:
    parser.error('--fixed-pve-preview requires --offline-practice')
if args.fixed_pve_local_api and (not args.offline_practice or args.fixed_pve_preview):
    parser.error('--fixed-pve-local-api requires --offline-practice and cannot be combined with --fixed-pve-preview')
if args.fixed_pve_local_api:
    from urllib.parse import urlsplit
    try:
        url = urlsplit(args.fixed_pve_local_api)
        valid = url.scheme == 'http' and url.hostname == '127.0.0.1' and url.port and not url.username and not url.password and url.path in ('', '/') and not url.query and not url.fragment
    except ValueError:
        valid = False
    if not valid:
        parser.error('--fixed-pve-local-api must be http://127.0.0.1:PORT')
editor = Path(os.environ.get('UNITY_EDITOR', '/Applications/Unity/Hub/Editor/6000.0.85f2/Unity.app/Contents/MacOS/Unity'))
if not editor.is_file():
    sys.exit(f'Unity Editor not found: {editor}; set UNITY_EDITOR.')
output = Path(args.output).expanduser().resolve()
if output.exists():
    sys.exit(f'Output already exists: {output}; choose a new path to preserve it.')
target = 'StandaloneOSX' if args.target == 'mac' else 'WebGL'
method = 'SnakeClub.Editor.SnakeBuilder.BuildMac' if args.target == 'mac' else 'SnakeClub.Editor.SnakeBuilder.BuildWebGL'
with tempfile.TemporaryDirectory(prefix='snake-unity-build-') as directory:
    project = Path(directory) / 'project'
    project.mkdir()
    excluded = ['ByteGame', 'ByteGame.meta', '.DS_Store']
    if args.offline_practice:
        excluded.extend(['ReferenceSkins', 'ReferenceSkins.meta'])
    for name in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(root / 'unity' / name, project / name, ignore=shutil.ignore_patterns(*excluded))
    if args.offline_practice:
        config_path = project / 'Assets/Resources/GameConfig.json'
        config = json.loads(config_path.read_text())
        config.update(serverUrl='', apiUrl='', allowDevLogin=False, allowReferenceSkinPreview=False, experimentalFixedPve=args.fixed_pve_preview, experimentalStrengthPve=args.strength_pve_preview)
        config['experimentalComparison'] = args.comparison_preview
        config['experimentalStrengthPveServer'] = False
        if args.fixed_pve_local_api:
            config.update(apiUrl=args.fixed_pve_local_api, allowDevLogin=True, experimentalFixedPveServer=True)
        else:
            config['experimentalFixedPveServer'] = False
        config_path.write_text(json.dumps(config, ensure_ascii=False, indent=2) + '\n')
    log = Path(directory) / 'build.log'
    stamp = Path(directory) / 'built'
    environment = {**os.environ, 'SNAKE_BUILD_OUTPUT': str(output), 'SNAKE_BUILD_STAMP': str(stamp)}
    if args.offline_practice:
        environment['SNAKE_INCLUDE_REFERENCE_ASSETS'] = '0'
    command = [str(editor), '-batchmode']
    if args.no_graphics:
        command.append('-nographics')
    command.extend(['-quit', '-projectPath', str(project), '-buildTarget', target, '-executeMethod', method, '-logFile', str(log)])
    print(f'Building {target} in an isolated project: {output}', flush=True)
    try:
        result = subprocess.run(command, env=environment, timeout=420, check=False)
    except subprocess.TimeoutExpired:
        sys.exit('Unity build timed out.')
    contents = log.read_text(errors='replace') if log.exists() else ''
    if result.returncode or not stamp.exists():
        lines = contents.splitlines()
        errors = [line for line in lines if 'error CS' in line or 'Exception:' in line or 'error :' in line]
        print('\n'.join(errors[-25:] or lines[-80:]), file=sys.stderr)
        sys.exit(result.returncode or 1)
    print(f'Build completed: {output}')
