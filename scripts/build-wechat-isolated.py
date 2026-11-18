#!/usr/bin/env python3
"""Convert a disposable offline practice project with the pinned official WXSDK.

This creates local files only. A real AppID, configured HTTPS CDN and the WeChat
Developer Tools are required separately to run or preview the converted game.
"""
import argparse
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
from urllib.parse import urlsplit
from unity_defines import enable_webgl_define

ROOT = Path(__file__).resolve().parent.parent
SDK_REVISION = '915d4bf4f60ef0cd1c29316a8d67257185d609a7'
DEFAULT_EDITOR = '/Applications/Unity/Hub/Editor/6000.0.85f2/Unity.app/Contents/MacOS/Unity'


def validate_settings(appid, cdn, diagnostic):
    if not re.fullmatch(r'wx[0-9a-fA-F]{16}', appid):
        raise ValueError('AppID must be wx followed by 16 hexadecimal characters')
    url = urlsplit(cdn)
    if (not re.fullmatch(r'https://[A-Za-z0-9.-]+(?::[0-9]+)?(?:/[A-Za-z0-9._~!$&()*+,;=:@%/-]*)?', cdn)
            or url.scheme != 'https' or not url.hostname or url.username is not None or url.password is not None or url.query or url.fragment):
        raise ValueError('CDN must be an HTTPS base URL without credentials, query or fragment')
    if url.port is not None and not 1 <= url.port <= 65535:
        raise ValueError('CDN port is invalid')
    if not diagnostic and (appid.lower() == 'wx0000000000000000' or url.hostname.lower().endswith('.invalid')):
        raise ValueError('Placeholder AppID or CDN requires --diagnostic')


def verify_sdk(sdk):
    package = sdk / 'package.json'
    if not package.is_file() or json.loads(package.read_text()).get('name') != 'com.qq.weixin.minigame':
        raise ValueError('SDK path is not the official WXSDK package root')
    try:
        top = subprocess.check_output(['git', '-C', str(sdk), 'rev-parse', '--show-toplevel'], text=True, stderr=subprocess.DEVNULL).strip()
        revision = subprocess.check_output(['git', '-C', str(sdk), 'rev-parse', 'HEAD'], text=True, stderr=subprocess.DEVNULL).strip()
        dirty = subprocess.check_output(['git', '-C', str(sdk), 'status', '--porcelain', '--untracked-files=all'], text=True, stderr=subprocess.DEVNULL).strip()
    except (OSError, subprocess.CalledProcessError) as error:
        raise ValueError('SDK must be a clean Git checkout of the documented revision') from error
    if Path(top).resolve() != sdk or revision != SDK_REVISION or dirty:
        raise ValueError(f'SDK must be clean and checked out exactly at {SDK_REVISION}')
    return json.loads(package.read_text()).get('version', '')


def export_sdk(sdk, destination):
    # Build only committed files; ignored local files in the SDK checkout must
    # not leak into the isolated Unity project or its output.
    destination.mkdir()
    with subprocess.Popen(['git', '-C', str(sdk), 'archive', '--format=tar', SDK_REVISION], stdout=subprocess.PIPE) as archive:
        extracted = subprocess.run(['tar', '-xf', '-', '-C', str(destination)], stdin=archive.stdout, check=False)
        archive.stdout.close()
        archive_status = archive.wait()
    if extracted.returncode or archive_status:
        raise RuntimeError('Could not export the pinned SDK checkout')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--sdk', required=True, help='clean local checkout of the documented WXSDK revision')
    parser.add_argument('--appid', required=True, help='WeChat mini-game AppID')
    parser.add_argument('--cdn', required=True, help='HTTPS base URL for the exported WebGL resources')
    parser.add_argument('--output', required=True, help='new output directory; never overwritten')
    parser.add_argument('--strength-pve-preview', action='store_true', help='enable offline experimental strength practice')
    parser.add_argument('--diagnostic', action='store_true', help='allow placeholder AppID and CDN for a non-playable local conversion probe')
    args = parser.parse_args()
    sdk = Path(args.sdk).expanduser().resolve()
    output = Path(args.output).expanduser().resolve()
    editor = Path(os.environ.get('UNITY_EDITOR', DEFAULT_EDITOR)).expanduser().resolve()
    try:
        validate_settings(args.appid, args.cdn, args.diagnostic)
        if output.exists(): raise ValueError(f'Output already exists: {output}; choose a new path')
        version = verify_sdk(sdk)
        if not editor.is_file(): raise ValueError(f'Unity Editor not found: {editor}')
    except (ValueError, OSError, json.JSONDecodeError) as error:
        sys.exit(str(error))

    with tempfile.TemporaryDirectory(prefix='snake-wechat-build-') as directory:
        temp = Path(directory)
        project = temp / 'project'; project.mkdir()
        exclude = shutil.ignore_patterns('ByteGame', 'ByteGame.meta', 'ReferenceSkins', 'ReferenceSkins.meta', '.DS_Store')
        for name in ('Assets', 'Packages', 'ProjectSettings'):
            shutil.copytree(ROOT / 'unity' / name, project / name, ignore=exclude)
        export_sdk(sdk, project / 'Packages/com.qq.weixin.minigame')
        settings = project / 'ProjectSettings/ProjectSettings.asset'
        try:
            settings.write_text(enable_webgl_define(settings.read_text(), 'SNAKE_WECHAT_WXSDK'))
        except ValueError as error:
            sys.exit(f'Cannot configure WeChat SDK in the isolated project: {error}')
        config_path = project / 'Assets/Resources/GameConfig.json'
        config = json.loads(config_path.read_text())
        config.update(serverUrl='', apiUrl='', rewardedAdUnitId='', allowDevLogin=False, allowReferenceSkinPreview=False,
                      experimentalFixedPve=args.strength_pve_preview, experimentalStrengthPve=args.strength_pve_preview,
                      experimentalFixedPveServer=False, experimentalStrengthPveServer=False, experimentalComparison=False)
        config_path.write_text(json.dumps(config, ensure_ascii=False, indent=2) + '\n')
        shutil.copy2(ROOT / 'scripts/WeChatIsolatedBuild.cs', project / 'Assets/Snake/Editor/WeChatIsolatedBuild.cs')
        log = temp / 'build.log'; stamp = temp / 'built'
        env = dict(os.environ, SNAKE_WECHAT_OUTPUT=str(output), SNAKE_WECHAT_APPID=args.appid,
                   SNAKE_WECHAT_CDN=args.cdn, SNAKE_WECHAT_STAMP=str(stamp))
        command = [str(editor), '-batchmode', '-nographics', '-projectPath', str(project), '-buildTarget', 'WebGL',
                   '-executeMethod', 'WeChatIsolatedBuild.Run', '-logFile', str(log)]
        print('Building an isolated WeChat project with offline gameplay; no upload will be performed.', flush=True)
        try:
            result = subprocess.run(command, env=env, timeout=900, check=False)
        except subprocess.TimeoutExpired:
            if log.exists():
                output.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(log, output / 'snake-unity-build.log')
            sys.exit('WXSDK build timed out; inspect the output directory before retrying.')
        lines = log.read_text(errors='replace').splitlines() if log.exists() else []
        if log.exists():
            output.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(log, output / 'snake-unity-build.log')
        if result.returncode or not stamp.exists():
            errors = [line for line in lines if 'error CS' in line or 'SNAKE_WECHAT_BUILD_FAILED' in line or 'error :' in line]
            print('\n'.join(errors[-20:] or lines[-30:]), file=sys.stderr)
            sys.exit(result.returncode or 1)
        game = output / 'minigame'
        if not (game / 'game.json').is_file() or not (game / 'project.config.json').is_file() or not (game / 'game.js').is_file() or not (output / 'webgl').is_dir():
            sys.exit('WXSDK reported success but the expected local artifacts are incomplete.')
        try:
            project_config = json.loads((game / 'project.config.json').read_text())
            game_config = json.loads((game / 'game.json').read_text())
            if (project_config.get('appid') != args.appid or project_config.get('compileType') != 'game' or
                    game_config.get('deviceOrientation') != 'portrait' or args.cdn not in (game / 'game.js').read_text() or
                    not list(game.rglob('*.wasm.br')) or not list((output / 'webgl').glob('*.data.unityweb.bin.txt'))):
                raise ValueError('AppID, CDN, orientation, wasm or data does not match the requested build')
        except (ValueError, OSError, json.JSONDecodeError) as error:
            sys.exit(f'WXSDK output validation failed: {error}')
        version_file = (ROOT / 'unity/ProjectSettings/ProjectVersion.txt').read_text()
        version_match = re.search(r'^m_EditorVersion:\s*(\S+)', version_file, re.MULTILINE)
        metadata = {'sdk_commit': SDK_REVISION, 'sdk_version': version,
                    'project_unity_version': version_match.group(1) if version_match else '', 'editor_path': str(editor),
                    'diagnostic': args.diagnostic, 'offline_practice': True, 'strength_preview': args.strength_pve_preview}
        (output / 'snake-build-metadata.json').write_text(json.dumps(metadata, ensure_ascii=False, indent=2) + '\n')
        print(f'Local WeChat project: {game}')
        print(f'WebGL resources for configured CDN: {output / "webgl"}')
        if args.diagnostic: print('Placeholder configuration: this output cannot be used for real-device preview.')


if __name__ == '__main__':
    main()
