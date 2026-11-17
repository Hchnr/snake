#!/usr/bin/env python3
"""Exercise the real client queue against an isolated fake API, without a visible window.

A synthetic saved run gets a transient 503 and then an accepted mock receipt.
This does not validate server replay, real rewards, platform storage or devices.
"""
from functools import partial
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
import json
import os
import shutil
import subprocess
import sys
import tempfile
import threading

root = Path(__file__).resolve().parent.parent
editor = Path(os.environ.get('UNITY_EDITOR', '/Applications/Unity/Hub/Editor/6000.0.85f2/Unity.app/Contents/MacOS/Unity'))
chrome = Path(os.environ.get('SNAKE_CHROME', '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome'))
if not editor.is_file() or not chrome.is_file():
    sys.exit('Unity Editor or Chrome not found; set UNITY_EDITOR and SNAKE_CHROME.')

class Handler(SimpleHTTPRequestHandler):
    attempts = []
    def log_message(self, fmt, *args): pass
    def end_headers(self):
        self.send_header('Access-Control-Allow-Origin', '*')
        self.send_header('Access-Control-Allow-Headers', 'Authorization, Content-Type')
        self.send_header('Access-Control-Allow-Methods', 'GET, POST, OPTIONS')
        self.send_header('Cache-Control', 'no-store')
        if self.path.split('?', 1)[0].endswith('.gz'):
            self.send_header('Content-Encoding', 'gzip')
        super().end_headers()
    def guess_type(self, path):
        path = str(path)
        return super().guess_type(path[:-3] if path.endswith('.gz') else path)
    def respond(self, status, data):
        body = json.dumps(data, ensure_ascii=False).encode()
        self.send_response(status); self.send_header('Content-Type', 'application/json; charset=utf-8')
        self.send_header('Content-Length', str(len(body))); self.end_headers(); self.wfile.write(body)
    def do_OPTIONS(self):
        self.send_response(204); self.end_headers()
    def do_POST(self):
        body = json.loads(self.rfile.read(int(self.headers.get('Content-Length', '0'))) or b'{}')
        if self.path == '/api/v3/auth/dev':
            self.respond(200, {'ok': True, 'session': 'fixture-session', 'profile': {'playerId': 'fixture-player'}})
        elif self.path == '/api/v3/challenge/submit':
            self.attempts.append(body)
            if len(self.attempts) == 1: self.respond(503, {'ok': False, 'error': '回放服务暂不可用'})
            else: self.respond(200, {'ok': True, 'verified': True, 'coins': 0, 'experience': 0})
        else: self.respond(404, {'ok': False, 'error': 'unexpected request'})
    def do_GET(self):
        if self.path == '/api/v3/bootstrap':
            self.respond(200, {'ok': True, 'profile': {'playerId': 'fixture-player', 'tutorialComplete': True}})
        elif self.path.startswith('/api/'):
            self.respond(404, {'ok': False, 'error': 'unexpected request'})
        else: super().do_GET()

with tempfile.TemporaryDirectory(prefix='snake-pending-webgl-') as directory:
    temp = Path(directory); output = temp / 'webgl'
    server = ThreadingHTTPServer(('127.0.0.1', 0), partial(Handler, directory=str(output)))
    thread = threading.Thread(target=server.serve_forever, daemon=True); thread.start()
    try:
        project = temp / 'project'; project.mkdir()
        for name in ('Assets', 'Packages', 'ProjectSettings'):
            shutil.copytree(root / 'unity' / name, project / name,
                            ignore=shutil.ignore_patterns('ByteGame', 'ByteGame.meta', 'ReferenceSkins', 'ReferenceSkins.meta', '.DS_Store'))
        config_path = project / 'Assets/Resources/GameConfig.json'
        config = json.loads(config_path.read_text())
        config.update(serverUrl='', apiUrl=f'http://127.0.0.1:{server.server_port}', allowDevLogin=True,
                      allowReferenceSkinPreview=False, experimentalFixedPve=False, experimentalFixedPveServer=False)
        config_path.write_text(json.dumps(config, ensure_ascii=False, indent=2) + '\n')
        front = project / 'Assets/Snake/SnakeGameFront.cs'; code = front.read_text()
        def replace_once(old, new):
            global code
            if code.count(old) != 1: sys.exit('Client fixture anchor changed: ' + old)
            code = code.replace(old, new, 1)
        replace_once('        private bool busy, resultRecorded, flushing, adShowing, connecting, verifiedDaily;',
                     '        private bool fixturePreserved;\n        private bool busy, resultRecorded, flushing, adShowing, connecting, verifiedDaily;')
        replace_once('                    pendingRetry.Reset();\n                    FlushPending();', '''                    pendingRetry.Reset();
                    var fixture = new PendingChallenges();
                    fixture.entries.Add(new PendingChallenge { playerId = "fixture-player", runId = "fixture-run", token = "fixture-token", mode = "daily", inputs = "AQ==", actions = 1 });
                    SavePending(fixture);
                    FlushPending();''')
        replace_once('                    settlementRetry.Fail(entry.playerId, entry.runId, Time.unscaledTime);', '''                    fixturePreserved = reply.httpStatus == 503 && ReadPending().entries.Exists(item => item != null && item.runId == "fixture-run");
                    settlementRetry.Fail(entry.playerId, entry.runId, Time.unscaledTime);''')
        anchor = '                    var latest = ReadPending(); latest.entries.RemoveAll(item => item != null && item.runId == entry.runId && PendingRetrySchedule.OwnedBy(item.playerId, entry.playerId)); SavePending(latest);'
        replace_once(anchor, anchor + '''
                    Debug.Log("SNAKE_FIXED_VECTOR " + (fixturePreserved && reply.ok && !ReadPending().entries.Exists(item => item != null && item.runId == "fixture-run") ? "PASS" : "FAIL"));''')
        front.write_text(code)
        stamp = temp / 'built'; log = temp / 'build.log'
        env = dict(os.environ, SNAKE_BUILD_OUTPUT=str(output), SNAKE_BUILD_STAMP=str(stamp), SNAKE_INCLUDE_REFERENCE_ASSETS='0')
        command = [str(editor), '-batchmode', '-nographics', '-quit', '-projectPath', str(project), '-buildTarget', 'WebGL',
                   '-executeMethod', 'SnakeClub.Editor.SnakeBuilder.BuildWebGL', '-logFile', str(log)]
        print('Building isolated WebGL client...', flush=True)
        result = subprocess.run(command, env=env, timeout=420, check=False)
        if result.returncode or not stamp.exists():
            lines = log.read_text(errors='replace').splitlines() if log.exists() else []
            errors = [line for line in lines if 'error CS' in line or 'Exception:' in line or 'error :' in line]
            sys.exit('\n'.join(errors[-25:] or lines[-80:]))
        print('Running transient failure and recovery in muted headless Chrome...', flush=True)
        probe = subprocess.run(['node', str(root / 'scripts/fixed-pve-webgl-probe.cjs'), str(chrome),
                                f'http://127.0.0.1:{server.server_port}/'], text=True, capture_output=True, timeout=240, check=False)
        if probe.returncode or probe.stdout.strip() != 'PASS':
            sys.exit('Client recovery failed: ' + probe.stdout[-1000:] + probe.stderr[-1000:])
        if len(Handler.attempts) != 2 or Handler.attempts[0] != Handler.attempts[1] or Handler.attempts[0].get('runId') != 'fixture-run':
            sys.exit('Retry did not submit the same synthetic run exactly twice.')
        print('PASS: 503 preserved the saved run; the same request succeeded on retry and removed it.')
        print('Scope: synthetic run and mock receipt; no real reward, device, restart or platform storage verification.')
    finally:
        server.shutdown(); server.server_close(); thread.join(timeout=5)
