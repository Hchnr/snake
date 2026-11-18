#!/usr/bin/env python3
"""Run a real-time local WebGL client/server replay experiment, with no rewards.

Only an isolated temporary Unity project receives the diagnostic log marker.
This checks one local run, not a Douyin device or release readiness.
"""
from functools import partial
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
import json
import os
import secrets
import shutil
import socket
import subprocess
import sys
import tempfile
import threading
import time
import urllib.request

root = Path(__file__).resolve().parent.parent
editor = Path(os.environ.get('UNITY_EDITOR', '/Applications/Unity/Hub/Editor/6000.0.85f2/Unity.app/Contents/MacOS/Unity'))
chrome = Path(os.environ.get('SNAKE_CHROME', '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome'))
if not editor.is_file() or not chrome.is_file():
    sys.exit('Unity Editor or Chrome not found; set UNITY_EDITOR and SNAKE_CHROME to their executable paths.')

class Handler(SimpleHTTPRequestHandler):
    def guess_type(self, path):
        path = str(path)
        return super().guess_type(path[:-3] if path.endswith('.gz') else path)
    def end_headers(self):
        if self.path.split('?', 1)[0].endswith('.gz'):
            self.send_header('Content-Encoding', 'gzip')
        self.send_header('Cache-Control', 'no-store')
        super().end_headers()
    def log_message(self, format, *args):
        pass

def free_port():
    with socket.socket() as sock:
        sock.bind(('127.0.0.1', 0))
        return sock.getsockname()[1]

with tempfile.TemporaryDirectory(prefix='snake-fixed-client-e2e-') as directory:
    temp = Path(directory)
    project = temp / 'project'; project.mkdir()
    for name in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(root / 'unity' / name, project / name,
                        ignore=shutil.ignore_patterns('ByteGame', 'ByteGame.meta', 'ReferenceSkins', 'ReferenceSkins.meta', '.DS_Store'))
    api_port = free_port()
    config_path = project / 'Assets/Resources/GameConfig.json'
    config = json.loads(config_path.read_text())
    config.update(serverUrl='', apiUrl=f'http://127.0.0.1:{api_port}', allowDevLogin=True,
                  allowReferenceSkinPreview=False, experimentalFixedPve=False, experimentalFixedPveServer=True, experimentalStrengthPveServer=False)
    config_path.write_text(json.dumps(config, ensure_ascii=False, indent=2) + '\n')
    # This marker is only in the temporary player and observes the actual UI decision.
    front = project / 'Assets/Snake/SnakeGameFront.cs'
    code = front.read_text()
    marker = '                resultNote = matches ? "本机回放一致 · 不计奖励或排行" : reply.ok ?'
    if code.count(marker) != 1:
        sys.exit('Client verification decision changed; update the diagnostic fixture.')
    code = code.replace(marker, '                Debug.Log("SNAKE_FIXED_E2E " + (matches ? "MATCH" : "FAIL"));\n' + marker, 1)
    front.write_text(code)
    output = temp / 'webgl'; stamp = temp / 'built'; buildlog = temp / 'build.log'
    env = dict(os.environ, SNAKE_BUILD_OUTPUT=str(output), SNAKE_BUILD_STAMP=str(stamp), SNAKE_INCLUDE_REFERENCE_ASSETS='0')
    command = [str(editor), '-batchmode', '-nographics', '-quit', '-projectPath', str(project), '-buildTarget', 'WebGL',
               '-executeMethod', 'SnakeClub.Editor.SnakeBuilder.BuildWebGL', '-logFile', str(buildlog)]
    print('Building the current client in an isolated, non-graphical Unity project...', flush=True)
    result = subprocess.run(command, env=env, timeout=420, check=False)
    if result.returncode or not stamp.exists():
        lines = buildlog.read_text(errors='replace').splitlines() if buildlog.exists() else []
        errors = [line for line in lines if 'error CS' in line or 'Exception:' in line or 'error :' in line]
        print('\n'.join(errors[-25:] or lines[-80:]), file=sys.stderr)
        sys.exit(result.returncode or 1)
    print('Building local API server...', flush=True)
    subprocess.run(['dotnet', 'build', str(root / 'server/Snake.Server.csproj'), '--nologo', '--verbosity', 'quiet'], check=True, timeout=180)
    web = ThreadingHTTPServer(('127.0.0.1', 0), partial(Handler, directory=str(output)))
    thread = threading.Thread(target=web.serve_forever, daemon=True); thread.start()
    data = temp / 'data.json'; backend_log = temp / 'server.log'
    server_env = dict(os.environ, SNAKE_ENV='development', SNAKE_HOST='127.0.0.1', SNAKE_PORT=str(api_port),
                      SNAKE_DEV_AUTH='1', SNAKE_DEV_ADS='0', SNAKE_FIXED_PVE_EVAL='1', SNAKE_DATA_FILE=str(data),
                      SNAKE_SESSION_KEY=secrets.token_urlsafe(32), SNAKE_ALLOWED_ORIGINS=f'http://127.0.0.1:{web.server_port}')
    server_env.pop('SNAKE_SQLITE_FILE', None)
    server_env.pop('SNAKE_RATE_LIMIT_PER_MIN', None)
    with backend_log.open('w') as log:
        backend = subprocess.Popen(['dotnet', str(root / 'server/bin/Debug/net10.0/Snake.Server.dll')], cwd=root, env=server_env,
                                   stdout=log, stderr=subprocess.STDOUT)
        try:
            deadline = time.monotonic() + 20
            while True:
                if backend.poll() is not None:
                    sys.exit('Local server exited early: ' + backend_log.read_text(errors='replace')[-2500:])
                try:
                    with urllib.request.urlopen(f'http://127.0.0.1:{api_port}/health', timeout=1) as response:
                        if response.status == 200: break
                except OSError:
                    if time.monotonic() >= deadline: sys.exit('Local server did not become ready.')
                    time.sleep(.1)
            print('Playing one real-time match in muted headless Chrome...', flush=True)
            probe = subprocess.run(['node', str(root / 'scripts/fixed-pve-client-e2e-probe.cjs'), str(chrome),
                                    f'http://127.0.0.1:{web.server_port}/'], text=True, capture_output=True, timeout=180, check=False)
            if probe.returncode:
                sys.exit('Client replay test failed: ' + probe.stderr[-2500:])
            state = json.loads(data.read_text())
            runs = list(state.get('runs', {}).values()); players = list(state.get('players', {}).values())
            if len(runs) != 1 or runs[0].get('mode') != 'fixed-eval' or not runs[0].get('completed'):
                sys.exit('Expected one verified experimental run in isolated storage.')
            if len(players) != 1 or any(players[0].get(k, 0) != 0 for k in ('coins', 'experience', 'completedMatches')) or state.get('rewards'):
                sys.exit('Experimental run unexpectedly changed rewards or progress.')
            print(probe.stdout.strip())
            print(f"PASS: {runs[0]['score']} ticks; temporary account has no rewards or completion progress.")
            print('Scope: one local WebGL run; Douyin devices, anti-cheat and production rewards remain unverified.')
        finally:
            backend.terminate()
            try: backend.wait(timeout=10)
            except subprocess.TimeoutExpired: backend.kill(); backend.wait()
            web.shutdown(); web.server_close(); thread.join(timeout=5)
