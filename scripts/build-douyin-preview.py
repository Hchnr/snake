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

def preserve_failure_log(source):
    # Temporary build projects are deleted at exit. Keep failed build logs
    # private and separate so native failures can actually be investigated.
    if not source.is_file():
        return
    try:
        fd, name = tempfile.mkstemp(prefix='snake-douyin-failure-', suffix='.log')
        with os.fdopen(fd, 'wb') as target, source.open('rb') as original:
            shutil.copyfileobj(original, target)
        print(f'Unity failure log preserved: {name}', file=sys.stderr)
    except OSError as error:
        print(f'Could not preserve Unity failure log: {error}', file=sys.stderr)

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
if not args.appid or args.appid != args.appid.strip():
    parser.error('--appid must be non-empty and have no surrounding whitespace')
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
                  experimentalFixedPveServer=False, experimentalStrengthPveServer=False, experimentalLocalStrengthPveServer=False, experimentalComparison=False)
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
        preserve_failure_log(log)
        sys.exit('TTSDK build timed out.')
    contents = log.read_text(errors='replace') if log.exists() else ''
    if result.returncode or not stamp.exists():
        preserve_failure_log(log)
        errors = [line for line in contents.splitlines() if 'error CS' in line or 'DOUYIN_PREVIEW_BUILD_FAILED' in line]
        print('\n'.join(errors[-20:] or contents.splitlines()[-30:]), file=sys.stderr)
        sys.exit(result.returncode or 1)
    try:
        artifact = Path(stamp.read_text(encoding='utf-8').strip()).resolve()
        try:
            artifact.relative_to(output)
        except ValueError:
            raise ValueError('artifact path is outside the requested output directory')
        game = json.loads((artifact / 'game.json').read_text(encoding='utf-8'))
        config = json.loads((artifact / 'project.config.json').read_text(encoding='utf-8'))
        entry = artifact / 'game.js'
        if not isinstance(game, dict) or game.get('deviceOrientation') != 'portrait':
            raise ValueError('game.json must describe a portrait mini-game')
        subpackages = game.get('subpackages', [])
        if not isinstance(subpackages, list):
            raise ValueError('game.json subpackages must be an array')
        for package in subpackages:
            package_root = package.get('root') if isinstance(package, dict) else None
            if not isinstance(package_root, str) or not package_root.strip() or Path(package_root).is_absolute():
                raise ValueError('game.json contains an invalid subpackage root')
            resolved = (artifact / package_root).resolve()
            try:
                resolved.relative_to(artifact)
            except ValueError:
                raise ValueError('subpackage root is outside the mini-game project')
            if resolved == artifact or not resolved.is_dir():
                raise ValueError('game.json references a missing subpackage directory')
        if not isinstance(config, dict) or config.get('appid') != args.appid:
            raise ValueError('project.config.json AppID does not match the requested AppID')
        if not entry.is_file() or entry.stat().st_size == 0:
            raise ValueError('game.js is missing or empty')
    except (OSError, UnicodeError, json.JSONDecodeError, ValueError) as error:
        preserve_failure_log(log)
        sys.exit(f'TTSDK returned an invalid project: {error}')
    print(f'Douyin preview project: {artifact}')
