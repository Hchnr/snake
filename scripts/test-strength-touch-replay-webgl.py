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
body_edge_capture = os.environ.get('SNAKE_BODY_THREAT_CAPTURE_DIR', '')
# The default recovery scenario holds the simulation at tick 9 until the
# actual touch arrives. Custom seeds and visual capture scenarios stay natural.
pin_opening = (require_recovery == '1' and opening_left == '0' and
               'SNAKE_STRENGTH_SEED' not in os.environ and not weak_edge_capture and not body_edge_capture and
               os.environ.get('SNAKE_STRENGTH_CAPTURE_FRUIT') != '1' and
               os.environ.get('SNAKE_STRENGTH_CAPTURE_FADE') != '1')

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
        before = '                else if (!DrawWeakBodyEdge(state)) DrawOpponentEdge(state);'
        after = '''                else
                {
                    bool diagnosticWeakDrawn = DrawWeakBodyEdge(state);
                    if (Event.current.type == EventType.Repaint && !diagnosticWeakDrawn && strengthDiagnosticWeakDrawn)
                    { strengthDiagnosticWeakDrawn = false; Debug.Log("SNAKE_TOUCH_WEAK_EDGE_HIDDEN " + state.tick); }
                    if (!diagnosticWeakDrawn) DrawOpponentEdge(state);
                }'''
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
        default_seed = '58' if opening_left == '1' else '1016' if pin_opening else '39'
        diagnostic_seed = int(os.environ.get('SNAKE_STRENGTH_SEED', default_seed))
    except ValueError:
        sys.exit('SNAKE_STRENGTH_SEED must be an integer.')
    if not -(2 ** 31) <= diagnostic_seed < 2 ** 31:
        sys.exit('SNAKE_STRENGTH_SEED must fit in a signed 32-bit integer.')
    after = '            strengthDiagnosticSeed = run != null ? run.seed : ' + str(diagnostic_seed) + ';\n            fixedPve = new PveFixedWorld(strengthDiagnosticSeed, firstRun, profile,'
    code = inject_once(code, before, after)
    if pin_opening:
        before = '        [NonSerialized] private bool initializedInDomain;'
        code = inject_once(code, before, '        private bool strengthDiagnosticGateLogged;\n' + before)
        before = '            while (stepTimer >= .05f && !fixedPve.Finished)\n            {\n'
        after = before + '''                if (fixedPve.Tick >= 9 && !hasSteered)
                {
                    if (!strengthDiagnosticGateLogged) { strengthDiagnosticGateLogged = true; Debug.Log("SNAKE_TOUCH_OPENING_GATE " + fixedPve.Tick); }
                    stepTimer = 0; return;
                }
'''
        code = inject_once(code, before, after)
    if os.environ.get('SNAKE_STRENGTH_CAPTURE_FADE') == '1':
        before = '        [NonSerialized] private bool initializedInDomain;'
        code = inject_once(code, before, '        private bool strengthDiagnosticFadeLogged;\n' + before)
        before = '                string opening = OpeningBoostNotice.Text(fixedPve);'
        after = before + """
                if (!strengthDiagnosticFadeLogged && Event.current.type == EventType.Repaint && fixedPve.Tick >= 250 && fixedPve.Tick <= 280)
                {
                    strengthDiagnosticFadeLogged = true;
                    Debug.Log("SNAKE_TOUCH_FADE " + fixedPve.Tick + " " + fixedPve.CurrentPlayerHardness + " " + opening);
                }"""
        code = inject_once(code, before, after)
    if os.environ.get('SNAKE_STRENGTH_CAPTURE_FRUIT') == '1':
        before = '        [NonSerialized] private bool initializedInDomain;'
        code = inject_once(code, before, '        private bool strengthDiagnosticFruitLogged;\n        private float strengthDiagnosticFruitFreezeUntil;\n' + before)
        before = '            while (stepTimer >= .05f && !fixedPve.Finished)\n            {\n                byte heading = EncodeDirection(direction);'
        after = before.replace('                byte heading', '                if (strengthDiagnosticFruitFreezeUntil > Time.realtimeSinceStartup) { stepTimer = 0; return; }\n                byte heading')
        code = inject_once(code, before, after)
        before = '                state = fixedPve.RenderState();\n                strengthProtectionNotice.Observe(fixedPve, state, playerId);'
        after = '''                state = fixedPve.RenderState();
                if (!strengthDiagnosticFruitLogged && fixedPve.LocalOpeningFruit)
                    for (int i = 0; i < fixedPve.Fruit.Count; i++)
                        if (fixedPve.IsOpeningFruitAttracted(i) && !fixedPve.IsRecoveryFruit(i))
                        {
                            strengthDiagnosticFruitLogged = true;
                            strengthDiagnosticFruitFreezeUntil = Time.realtimeSinceStartup + 2f;
                            Debug.Log("SNAKE_TOUCH_FRUIT " + fixedPve.Tick + " " + i);
                            break;
                        }
                strengthProtectionNotice.Observe(fixedPve, state, playerId);'''
        code = inject_once(code, before, after)
    if body_edge_capture:
        before = '        [NonSerialized] private bool initializedInDomain;'
        code = inject_once(code, before, '        private bool strengthDiagnosticBodyDrawn;\n        private float strengthDiagnosticBodyFreezeUntil;\n' + before)
        before = '            while (stepTimer >= .05f && !fixedPve.Finished)\n            {\n'
        after = before + '                if (strengthDiagnosticBodyFreezeUntil > Time.realtimeSinceStartup) { stepTimer = 0; return; }\n'
        code = inject_once(code, before, after)
        before = '            Panel(box, new Color(.18f, .055f, .045f, .94f));'
        after = '''            if (Event.current.type == EventType.Repaint && !strengthDiagnosticBodyDrawn)
            {
                var marked = world.snakes.Find(s => s != null && s.id == playerId);
                int part = bodyThreatEdgeTarget.segment;
                if (marked == null || !marked.alive || marked.shield > 0 || part < 1 ||
                    part >= marked.strengthThreatSegments.Count || !marked.strengthThreatSegments[part])
                    throw new InvalidOperationException("Body danger arrow lacks an actual marked player segment");
                strengthDiagnosticBodyDrawn = true;
                strengthDiagnosticBodyFreezeUntil = Time.realtimeSinceStartup + 2f;
                Debug.Log("SNAKE_TOUCH_BODY_EDGE " + world.tick + " " + part + " " +
                    box.x.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " " + box.y.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
            }
''' + before
        code = inject_once(code, before, after)
    before = '            strengthProtectionNotice.Reset(); strengthProtectionNotice.Observe(fixedPve, state, playerId);'
    after = before + '\n            if (fixedPve.RulesVersion != PveFixedRecording.LocalStrengthOpeningFruitRules) throw new InvalidOperationException("Expected v16 strength practice");\n            Debug.Log("SNAKE_TOUCH_READY");'
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
    after = '''            string visibleProtectionCue = showProtection ? protectionText :
                showRecovery && recoveryText.Contains("出生保护结束") ? "出生保护结束 · 回追并行" : "";
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
                bool valid = PveFixedRecording.TryReplayStrength(strengthDiagnosticSeed, firstRun, tape, out var replay, fixedPve.LocalStrengthAdvantage, fixedPve.LocalOpeningFood, fixedPve.LocalOpeningEncounter, fixedPve.LocalRecoverySupply, fixedPve.LocalSafeRecovery, fixedPve.LocalAlignedStart, fixedPve.LocalCappedStart, fixedPve.LocalStableBlocked, fixedPve.LocalRecoveryPriority, fixedPve.LocalSqueezeEscape, fixedPve.LocalMultiBite, fixedPve.LocalHeadTieAfterBite, fixedPve.LocalOpeningBoost, fixedPve.LocalFreeBoost, fixedPve.LocalOpeningDefense, fixedPve.LocalOpeningFruit);
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
                                         SNAKE_STRENGTH_REQUIRE_PROTECTION_CUE=require_protection_cue,
                                         SNAKE_STRENGTH_PIN_OPENING='1' if pin_opening else '0'),
                                timeout=match_timeout + 300)
        if result.returncode:
            sys.exit('Touch replay diagnostic failed:\n' + result.stdout[-2000:] + result.stderr[-4000:])
        guidance_heading = 128 if opening_left == '1' else 64
        guidance_pass = 'PASS protection guidance drag -> heading ' + str(guidance_heading)
        if guidance_pass not in result.stdout:
            sys.exit('Missing evidence of steering from the protection guidance panel.')
        print(guidance_pass, flush=True)
        if pin_opening:
            gate_pass = 'PASS recovery fixture resumed on real steering at tick 10'
            if gate_pass not in result.stdout:
                sys.exit('Missing evidence of steering through the recovery fixture gate.')
            print(gate_pass, flush=True)
        if require_recovery == '1':
            recovery_pass = next((line for line in result.stdout.splitlines() if line.startswith('PASS actual recovery:')), '')
            if not recovery_pass:
                sys.exit('Missing evidence of actual recovery supply and guidance.')
            print(recovery_pass, flush=True)
        if require_protection_cue == '1':
            if 'PASS actual protection expiry notice' not in result.stdout:
                sys.exit('Missing evidence of the protection expiry notice.')
            print('PASS actual protection expiry notice.', flush=True)
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
        if body_edge_capture:
            body_evidence = next((line for line in result.stdout.splitlines() if line.startswith('PASS body danger indicator')), '')
            if not body_evidence or 'PASS body pointer referred to a marked player segment' not in result.stdout:
                sys.exit('Missing actual player-segment or indicator-drag evidence.')
            print(body_evidence, flush=True)
            print('PASS body pointer referred to a marked player segment.', flush=True)
        if os.environ.get('SNAKE_STRENGTH_CAPTURE_FRUIT') == '1':
            fruit_evidence = next((line for line in result.stdout.splitlines() if line.startswith('PASS actual opening fruit attraction')), '')
            if not fruit_evidence:
                sys.exit('Missing evidence of the v16 actual fruit attraction.')
            print(fruit_evidence, flush=True)
        if os.environ.get('SNAKE_STRENGTH_CAPTURE_FADE') == '1':
            fade_evidence = next((line for line in result.stdout.splitlines() if line.startswith('PASS actual opening defense fade notice')), '')
            if not fade_evidence:
                sys.exit('Missing evidence of the v16 opening-defense fade notice.')
            print(fade_evidence, flush=True)
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
                              'strength-opening-fruit-replay-terminal', seed, first, tape], capture_output=True, text=True, timeout=180, check=True)
        try:
            terminal = json.loads(ref.stdout.strip().splitlines()[-1])
        except (IndexError, json.JSONDecodeError):
            sys.exit('Desktop replay result missing.')
        if terminal.get('ticks') != int(ticks) or terminal.get('hash') != expected:
            sys.exit('MISMATCH: actual input recording differs between WebGL and .NET.')
        print('MATCH: one complete actual-touch match, ' + ticks + ' ticks, steering and boost; no rewards. Real devices remain unverified.')
    finally:
        server.shutdown(); server.server_close(); thread.join(timeout=5)
