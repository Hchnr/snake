#!/usr/bin/env python3
"""Replay a real touch-controlled strength match across WebGL and desktop .NET.

Temporary instrumentation records actual simulation inputs in a disposable build.
By default an upward first drag and a fixed seed exercise recovery. Set
SNAKE_STRENGTH_OPENING_LEFT=1 to check the capped leftward opening.
This does not replace device, Douyin or multitouch hardware validation.
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
from headless_process import run_headless

root = Path(__file__).resolve().parent.parent
editor = Path(os.environ.get('UNITY_EDITOR', '/Applications/Unity/Hub/Editor/6000.0.85f2/Unity.app/Contents/MacOS/Unity'))
chrome = Path(os.environ.get('SNAKE_CHROME', '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome'))
if not editor.is_file() or not chrome.is_file():
    sys.exit('Unity Editor or Chrome not found; set UNITY_EDITOR and SNAKE_CHROME.')
try:
    match_timeout = int(os.environ.get('SNAKE_STRENGTH_MATCH_TIMEOUT_SECONDS', '600'))
except ValueError:
    sys.exit('SNAKE_STRENGTH_MATCH_TIMEOUT_SECONDS must be an integer from 60 to 900.')
if not 60 <= match_timeout <= 900:
    sys.exit('SNAKE_STRENGTH_MATCH_TIMEOUT_SECONDS must be an integer from 60 to 900.')
opening_left = os.environ.get('SNAKE_STRENGTH_OPENING_LEFT', '0')
if opening_left not in ('0', '1'):
    sys.exit('SNAKE_STRENGTH_OPENING_LEFT must be 0 or 1.')
require_recovery = os.environ.get('SNAKE_STRENGTH_REQUIRE_RECOVERY', '1')
if require_recovery not in ('0', '1'):
    sys.exit('SNAKE_STRENGTH_REQUIRE_RECOVERY must be 0 or 1.')
require_protection_cue = os.environ.get('SNAKE_STRENGTH_REQUIRE_PROTECTION_CUE', '1')
if require_protection_cue not in ('0', '1'):
    sys.exit('SNAKE_STRENGTH_REQUIRE_PROTECTION_CUE must be 0 or 1.')
weak_edge_capture = os.environ.get('SNAKE_WEAK_EDGE_CAPTURE_DIR', '')

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

def inject_once(code, before, after):
    if code.count(before) != 1:
        sys.exit('Input diagnostic hook changed; inspect and update the fixture.')
    return code.replace(before, after, 1)

with tempfile.TemporaryDirectory(prefix='snake-strength-input-') as directory:
    temp = Path(directory)
    project = temp / 'project'; project.mkdir()
    for name in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(root / 'unity' / name, project / name,
                        ignore=shutil.ignore_patterns('ByteGame', 'ByteGame.meta', 'ReferenceSkins', 'ReferenceSkins.meta', '.DS_Store'))
    config_path = project / 'Assets/Resources/GameConfig.json'
    config = json.loads(config_path.read_text())
    config.update(serverUrl='', apiUrl='', allowDevLogin=False, allowReferenceSkinPreview=False,
                  experimentalFixedPve=True, experimentalStrengthPve=True, experimentalFixedPveServer=False, experimentalStrengthPveServer=False, experimentalComparison=False)
    config_path.write_text(json.dumps(config, ensure_ascii=False, indent=2) + '\n')
    app_path = project / 'Assets/Snake/SnakeGameApp.cs'
    code = app_path.read_text()
    before = '        [NonSerialized] private bool initializedInDomain;'
    after = '        private bool touchDiagnosticHome;\n' + before
    code = inject_once(code, before, after)
    before = '                    if (resultVisible)\n                    {\n                        SnakeState own = state.snakes.Find(s => s.id == playerId);'
    after = '                    if (homeVisible && frontPage == "home" && !touchDiagnosticHome) { touchDiagnosticHome = true; Debug.Log("SNAKE_TOUCH_HOME"); }\n' + before
    code = inject_once(code, before, after)
    before = '        [NonSerialized] private bool initializedInDomain;'
    after = '        private int strengthDiagnosticSeed;\n' + before
    code = inject_once(code, before, after)
    before = '        [NonSerialized] private bool initializedInDomain;'
    after = '        private bool strengthDiagnosticOpeningLogged;\n' + before
    code = inject_once(code, before, after)
    before = '        [NonSerialized] private bool initializedInDomain;'
    after = '        private string strengthDiagnosticCue, strengthDiagnosticRecovery, strengthDiagnosticBoost;\n' + before
    code = inject_once(code, before, after)
    if weak_edge_capture:
        before = '        [NonSerialized] private bool initializedInDomain;'
        code = inject_once(code, before, '        private bool strengthDiagnosticWeakDrawn;\n' + before)
        before = '                if (!DrawWeakBodyEdge(state)) DrawOpponentEdge(state);'
        after = '''                bool diagnosticWeakDrawn = DrawWeakBodyEdge(state);
                if (Event.current.type == EventType.Repaint && !diagnosticWeakDrawn && strengthDiagnosticWeakDrawn)
                { strengthDiagnosticWeakDrawn = false; Debug.Log("SNAKE_TOUCH_WEAK_EDGE_HIDDEN " + state.tick); }
                if (!diagnosticWeakDrawn) DrawOpponentEdge(state);'''
        code = inject_once(code, before, after)
        before = '            Panel(box, new Color(.15f, .11f, .045f, .92f));'
        after = '''            if (Event.current.type == EventType.Repaint && !strengthDiagnosticWeakDrawn)
            {
                var marked = world.snakes.Find(s => s.id == weakEdgeTarget.id);
                if (marked == null || !marked.alive || marked.shield > 0 || weakEdgeTarget.segment < 1 ||
                    weakEdgeTarget.segment >= marked.strengthWeakSegments.Count || !marked.strengthWeakSegments[weakEdgeTarget.segment])
                    throw new InvalidOperationException("Weak-edge pointer lacks an actual eligible segment");
                strengthDiagnosticWeakDrawn = true;
                Debug.Log("SNAKE_TOUCH_WEAK_EDGE " + state.tick + " " + weakEdgeTarget.id + " " + weakEdgeTarget.segment + " " +
                    box.x.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " " + box.y.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
            }
''' + before
        code = inject_once(code, before, after)
    before = '            fixedPve = new PveFixedWorld(run != null ? run.seed : unchecked((int)DateTime.UtcNow.Ticks), firstRun, profile,'
    try:
        diagnostic_seed = int(os.environ.get('SNAKE_STRENGTH_SEED', '58' if opening_left == '1' else '39'))
    except ValueError:
        sys.exit('SNAKE_STRENGTH_SEED must be an integer.')
    if not -(2 ** 31) <= diagnostic_seed < 2 ** 31:
        sys.exit('SNAKE_STRENGTH_SEED must fit in a signed 32-bit integer.')
    after = '            strengthDiagnosticSeed = run != null ? run.seed : ' + str(diagnostic_seed) + ';\n            fixedPve = new PveFixedWorld(strengthDiagnosticSeed, firstRun, profile,'
    code = inject_once(code, before, after)
    before = '            strengthProtectionNotice.Reset(); strengthProtectionNotice.Observe(fixedPve, state, playerId);'
    after = before + '\n            if (fixedPve.RulesVersion != PveFixedRecording.LocalStrengthFreeBoostRules) throw new InvalidOperationException("Expected v14 strength practice");\n            Debug.Log("SNAKE_TOUCH_READY");'
    code = inject_once(code, before, after)
    before = '                fixedPve.Step(heading, pointerBoost, recordedSteering);\n                runInputs.Add(heading);'
    after = '''                fixedPve.Step(heading, pointerBoost, recordedSteering);
                if (recordedSteering && !strengthDiagnosticOpeningLogged) {
                    strengthDiagnosticOpeningLogged = true;
                    Debug.Log("SNAKE_TOUCH_OPENING " + fixedPve.Tick + " " + heading + " " + (fixedPve.OpeningTurnAssistUsed ? 1 : 0) + " " + fixedPve.Fighters[0].motion.Angle);
                }
                Debug.Log("SNAKE_TOUCH_TICK " + fixedPve.Tick + " " + heading + " " + (pointerBoost ? 1 : 0));
                runInputs.Add(heading);'''
    code = inject_once(code, before, after)
    before = '            if (showProtection || showRecovery || showStrengthIntro)\n'
    after = '''            string visibleProtectionCue = showProtection ? protectionText : "";
            if (visibleProtectionCue != strengthDiagnosticCue)
            {
                strengthDiagnosticCue = visibleProtectionCue;
                Debug.Log("SNAKE_TOUCH_CUE " + state.tick + " " + visibleProtectionCue.Replace("\\n", " | "));
            }
            string visibleRecovery = showRecovery ? recoveryText : "";
            if (visibleRecovery != strengthDiagnosticRecovery)
            {
                strengthDiagnosticRecovery = visibleRecovery;
                int marked = 0;
                for (int i = 0; i < fixedPve.Fruit.Count; i++) if (fixedPve.IsRecoveryFruit(i)) marked++;
                Debug.Log("SNAKE_TOUCH_RECOVERY " + state.tick + " marked=" + marked + " " + visibleRecovery);
            }
''' + before
    code = inject_once(code, before, after)
    before = '            string strengthBoostHint = StrengthBoostNotice.Text(fixedPve, state, playerId, pointerBoost, SimulationSuspended);'
    after = before + '''
            if (strengthBoostHint != strengthDiagnosticBoost)
            {
                strengthDiagnosticBoost = strengthBoostHint;
                Debug.Log("SNAKE_TOUCH_BOOST_NOTICE " + (state == null ? -1 : state.tick) + " " + strengthBoostHint);
            }'''
    code = inject_once(code, before, after)
    app_path.write_text(code)
    shutil.copy2(root / 'test/v3/StrengthPveReplayVector.cs', project / 'Assets/Snake/StrengthPveReplayVector.cs')
    front_path = project / 'Assets/Snake/SnakeGameFront.cs'
    front = front_path.read_text()
    before = '            resultRecorded = true;\n            if (comparisonVariant >= 0'
    after = """            resultRecorded = true;
            if (fixedPve != null && fixedPve.StrengthPreview)
            {
                byte[] tape = runInputs.ToArray();
                string live = SnakeClub.Tests.StrengthPveReplayVector.Terminal(fixedPve);
                bool valid = PveFixedRecording.TryReplayStrength(strengthDiagnosticSeed, firstRun, tape, out var replay, fixedPve.LocalStrengthAdvantage, fixedPve.LocalOpeningFood, fixedPve.LocalOpeningEncounter, fixedPve.LocalRecoverySupply, fixedPve.LocalSafeRecovery, fixedPve.LocalAlignedStart, fixedPve.LocalCappedStart, fixedPve.LocalStableBlocked, fixedPve.LocalRecoveryPriority, fixedPve.LocalSqueezeEscape, fixedPve.LocalMultiBite, fixedPve.LocalHeadTieAfterBite, fixedPve.LocalOpeningBoost, fixedPve.LocalFreeBoost);
                bool match = valid && SnakeClub.Tests.StrengthPveReplayVector.Terminal(replay) == live;
                Debug.Log("SNAKE_STRENGTH_E2E " + (match ? "MATCH" : "FAIL") + " " + strengthDiagnosticSeed + " " +
                    (firstRun ? 1 : 0) + " " + fixedPve.Tick + " " + live + " " + Convert.ToBase64String(tape));
            }
            if (comparisonVariant >= 0"""
    front = inject_once(front, before, after)
    front_path.write_text(front)
    output = temp / 'webgl'; stamp = temp / 'built'; log = temp / 'build.log'
    env = dict(os.environ, SNAKE_BUILD_OUTPUT=str(output), SNAKE_BUILD_STAMP=str(stamp), SNAKE_INCLUDE_REFERENCE_ASSETS='0')
    command = [str(editor), '-batchmode', '-nographics', '-quit', '-projectPath', str(project), '-buildTarget', 'WebGL',
               '-executeMethod', 'SnakeClub.Editor.SnakeBuilder.BuildWebGL', '-logFile', str(log)]
    print('Building an isolated, non-graphical strength replay diagnostic...', flush=True)
    result = subprocess.run(command, env=env, timeout=420, check=False)
    if result.returncode or not stamp.exists():
        lines = log.read_text(errors='replace').splitlines() if log.exists() else []
        errors = [line for line in lines if 'error CS' in line or 'Exception:' in line or 'error :' in line]
        print('\n'.join(errors[-25:] or lines[-80:]), file=sys.stderr)
        sys.exit(result.returncode or 1)
    saved_build = os.environ.get('SNAKE_STRENGTH_SAVE_BUILD')
    if saved_build:
        saved_path = Path(saved_build)
        if saved_path.exists():
            sys.exit('SNAKE_STRENGTH_SAVE_BUILD must be a new path.')
        shutil.copytree(output, saved_path)
        print('Saved diagnostic WebGL build: ' + str(saved_path), flush=True)
    server = ThreadingHTTPServer(('127.0.0.1', 0), partial(Handler, directory=str(output)))
    thread = threading.Thread(target=server.serve_forever, daemon=True); thread.start()
    try:
        print('Playing a complete strength preview using muted headless Chrome...', flush=True)
        result = run_headless(['node', str(root / 'scripts/strength-touch-replay-probe.cjs'), str(chrome),
                                 'http://127.0.0.1:' + str(server.server_port) + '/', str(match_timeout)],
                                env=dict(os.environ, SNAKE_STRENGTH_OPENING_LEFT=opening_left, SNAKE_STRENGTH_REQUIRE_RECOVERY=require_recovery,
                                         SNAKE_STRENGTH_REQUIRE_PROTECTION_CUE=require_protection_cue),
                                timeout=match_timeout + 300)
        if result.returncode:
            sys.exit('Touch replay diagnostic failed:\n' + result.stdout[-2000:] + result.stderr[-4000:])
        guidance_heading = 128 if opening_left == '1' else 64
        guidance_pass = 'PASS protection guidance drag -> heading ' + str(guidance_heading)
        if guidance_pass not in result.stdout:
            sys.exit('Missing evidence of steering from the protection guidance panel.')
        print(guidance_pass, flush=True)
        if opening_left == '1':
            if 'PASS actual left drag triggered the capped opening turn' not in result.stdout:
                sys.exit('Missing evidence of an actual capped leftward turn.')
            print('PASS actual left drag triggered the capped opening turn.', flush=True)
        if weak_edge_capture:
            if 'PASS weak body indicator -> heading 192' not in result.stdout or 'PASS pointer referred to a marked live segment' not in result.stdout:
                sys.exit('Missing marked-segment or indicator-drag evidence.')
            for line in result.stdout.splitlines():
                if line.startswith('PASS weak body indicator') or line.startswith('PASS pointer referred'):
                    print(line, flush=True)
        if require_recovery == '0':
            if 'SKIP recovery assertion:' not in result.stdout:
                sys.exit('Recovery opt-out was not acknowledged by the probe.')
            print('SKIP recovery assertion: this run verifies steering and replay only.', flush=True)
        if require_protection_cue == '0':
            if 'SKIP protection-expiry text assertion (explicit opt-out)' not in result.stdout:
                sys.exit('Protection cue opt-out was not acknowledged by the probe.')
            print('SKIP protection-expiry text assertion (explicit opt-out).', flush=True)
        lines = [line for line in result.stdout.splitlines() if line.startswith('SNAKE_STRENGTH_E2E ')]
        if len(lines) != 1:
            sys.exit('Missing or duplicate real-input replay result.')
        fields = lines[0].split()
        if len(fields) != 7 or fields[1] != 'MATCH':
            sys.exit('Unity local replay mismatch or malformed diagnostic.')
        seed, first, ticks, expected, tape = fields[2:]
        import base64
        raw = base64.b64decode(tape, validate=True)
        flags = raw[1::2]
        if (len(raw) != int(ticks) * 2 or not any(raw[i] != 0 for i in range(0, len(raw), 2)) or
            not any(flag & 1 for flag in flags) or not any(flag & 2 for flag in flags) or
            any(flag > 3 for flag in flags) or any((flags[i - 1] & 2) and not (flags[i] & 2) for i in range(1, len(flags)))):
            sys.exit('Recording did not include valid persistent steering and boost input.')
        print('Replaying the actual recorded inputs in desktop .NET...', flush=True)
        ref = subprocess.run(['dotnet', 'run', '--project', str(root / 'test/v3/Snake.V3.Core.Tests.csproj'), '--',
                              'strength-free-boost-replay-terminal', seed, first, tape], capture_output=True, text=True, timeout=180, check=True)
        try:
            terminal = json.loads(ref.stdout.strip().splitlines()[-1])
        except (IndexError, json.JSONDecodeError):
            sys.exit('Desktop replay result missing.')
        if terminal.get('ticks') != int(ticks) or terminal.get('hash') != expected:
            sys.exit('MISMATCH: actual input recording differs between WebGL and .NET.')
        print('MATCH: one complete actual-touch match, ' + ticks + ' ticks, steering and boost; no rewards. Real devices remain unverified.')
    finally:
        server.shutdown(); server.server_close(); thread.join(timeout=5)
