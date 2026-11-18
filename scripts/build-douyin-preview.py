#!/usr/bin/env python3
"""Build an offline TTSDK preview in an isolated Unity project; do not upload it."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

from unity_defines import enable_webgl_define

root = Path(__file__).resolve().parent.parent
parser = argparse.ArgumentParser()
parser.add_argument('--appid', required=True, help='Douyin mini-game AppID')
parser.add_argument('--output', required=True, help='new TTSDK build output directory')
parser.add_argument('--include-reference-skins', action='store_true', help='include local reference art for internal testing only')
parser.add_argument('--fixed-pve-preview', action='store_true', help='enable experimental integer PvE in this offline preview')
parser.add_argument('--strength-pve-preview', action='store_true', help='enable experimental strength body-eating in this offline preview')
graphics = parser.add_mutually_exclusive_group()
graphics.add_argument('--no-graphics', dest='no_graphics', action='store_true', help='run Unity without graphics (default)')
graphics.add_argument('--graphics', dest='no_graphics', action='store_false', help='allow graphics if the installed TTSDK needs them')
parser.set_defaults(no_graphics=True)
args = parser.parse_args()
editor = Path(os.environ.get('UNITY_EDITOR', '/Applications/Unity/Hub/Editor/6000.0.85f2/Unity.app/Contents/MacOS/Unity'))
output = Path(args.output).expanduser().resolve()
sdk = root / 'unity/Assets/Plugins/ByteGame/com.bytedance.starksdk/package.json'
if not editor.is_file():
    sys.exit(f'Unity Editor not found: {editor}')
if not sdk.is_file():
    sys.exit('Install TTSDK with ByteGame Develop Tools before building.')
if output.exists():
    sys.exit(f'Output already exists: {output}; choose a new path to preserve it.')
references = root / 'unity/Assets/Editor/ReferenceSkins'
if args.include_reference_skins and not (references / 'wenda.png').is_file():
    sys.exit('The local reference art is missing; build without --include-reference-skins instead.')

with tempfile.TemporaryDirectory(prefix='snake-douyin-preview-') as directory:
    project = Path(directory) / 'project'
    project.mkdir()
    excluded = ['.DS_Store']
    if not args.include_reference_skins:
        excluded.extend(['ReferenceSkins', 'ReferenceSkins.meta'])
    for name in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(root / 'unity' / name, project / name, ignore=shutil.ignore_patterns(*excluded))
    settings_path = project / 'ProjectSettings/ProjectSettings.asset'
    try:
        settings_path.write_text(enable_webgl_define(settings_path.read_text(), 'SNAKE_DOUYIN_TTSDK'))
    except ValueError as error:
        sys.exit(f'Cannot configure Douyin SDK in the isolated project: {error}')
    config_path = project / 'Assets/Resources/GameConfig.json'
    config = json.loads(config_path.read_text())
    config.update(serverUrl='', apiUrl='', allowDevLogin=False, allowReferenceSkinPreview=args.include_reference_skins,
                  experimentalFixedPve=args.fixed_pve_preview or args.strength_pve_preview, experimentalStrengthPve=args.strength_pve_preview,
                  experimentalFixedPveServer=False, experimentalStrengthPveServer=False, experimentalComparison=False)
    config_path.write_text(json.dumps(config, ensure_ascii=False, indent=2) + '\n')
    shutil.copy2(root / 'scripts/DouyinPreviewBuild.cs', project / 'Assets/Snake/Editor/DouyinPreviewBuild.cs')
    log = Path(directory) / 'build.log'
    stamp = Path(directory) / 'built'
    environment = {**os.environ, 'SNAKE_DOUYIN_PREVIEW_APPID': args.appid,
                   'SNAKE_DOUYIN_PREVIEW_OUTPUT': str(output), 'SNAKE_DOUYIN_PREVIEW_STAMP': str(stamp),
                   'SNAKE_DOUYIN_REFERENCE_PREVIEW': '1' if args.include_reference_skins else '0'}
    command = [str(editor), '-batchmode']
    if args.no_graphics:
        command.append('-nographics')
    command += ['-projectPath', str(project), '-buildTarget', 'WebGL',
               '-executeMethod', 'DouyinPreviewBuild.Run', '-logFile', str(log)]
    print(f'Building an offline Douyin preview: {output}', flush=True)
    try:
        result = subprocess.run(command, env=environment, timeout=900, check=False)
    except subprocess.TimeoutExpired:
        sys.exit('TTSDK build timed out.')
    contents = log.read_text(errors='replace') if log.exists() else ''
    if result.returncode or not stamp.exists():
        errors = [line for line in contents.splitlines() if 'error CS' in line or 'DOUYIN_PREVIEW_BUILD_FAILED' in line]
        print('\n'.join(errors[-20:] or contents.splitlines()[-30:]), file=sys.stderr)
        sys.exit(result.returncode or 1)
    artifact = Path(stamp.read_text().strip())
    if not (artifact / 'game.json').is_file() or not (artifact / 'project.config.json').is_file():
        sys.exit(f'TTSDK returned an incomplete project: {artifact}')
    print(f'Douyin preview project: {artifact}')
