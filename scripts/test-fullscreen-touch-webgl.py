#!/usr/bin/env python3
"""Exercise real WebGL touch controls without a visible window or audio.

Temporary instrumentation reports actual simulation inputs and pause changes;
protection is extended only in the disposable build to keep the test running.
This does not replace device, Douyin or multitouch hardware validation.
Set SNAKE_TOUCH_VIEWPORT_WIDTH and SNAKE_TOUCH_VIEWPORT_HEIGHT to test another
viewport; coordinates are mapped to the centered UI and physical corners are
also tested when the viewport differs from the 540 x 960 reference.
Set SNAKE_TOUCH_SKIN_CHECK=1 to exercise selecting a skin and returning home.
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
        sys.exit('Input diagnostic hook changed; inspect and update the fixture: ' + repr(before[:120]))
    return code.replace(before, after, 1)

with tempfile.TemporaryDirectory(prefix='snake-touch-webgl-') as directory:
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
    if os.environ.get('SNAKE_TOUCH_SKIN_CHECK') == '1':
        before = '        [NonSerialized] private bool initializedInDomain;'
        code = inject_once(code, before, '        private string touchDiagnosticFront;\n' + before)
        before = '            bool homeVisible = !inGame && (frontPage == "home" || frontPage == "skins") && !developerRooms;'
        after = '''            if (!inGame && frontPage != touchDiagnosticFront)
            { touchDiagnosticFront = frontPage; Debug.Log("SNAKE_TOUCH_FRONT " + frontPage + " skin=" + EffectiveSkin); }
''' + before
        code = inject_once(code, before, after)
    before = '                    if (resultVisible)\n                    {\n                        SnakeState own = state.snakes.Find(s => s.id == playerId);'
    after = '                    if (homeVisible && frontPage == "home" && !touchDiagnosticHome) { touchDiagnosticHome = true; Debug.Log("SNAKE_TOUCH_HOME"); }\n' + before
    code = inject_once(code, before, after)
    before = '            state = fixedPve.RenderState();\n            strengthProtectionNotice.Reset(); strengthProtectionNotice.Observe(fixedPve, state, playerId);'
    after = '            foreach (var fighter in fixedPve.Fighters) fighter.shieldTicks = 100000;\n' + before + '\n            Debug.Log("SNAKE_TOUCH_READY");'
    code = inject_once(code, before, after)
    before = '                fixedPve.Step(heading, pointerBoost, recordedSteering);\n                runInputs.Add(heading);'
    after = '                fixedPve.Step(heading, pointerBoost, recordedSteering);\n                Debug.Log("SNAKE_TOUCH_TICK " + fixedPve.Tick + " " + heading + " " + (pointerBoost ? 1 : 0));\n                runInputs.Add(heading);'
    if os.environ.get('SNAKE_GROWTH_CAPTURE_DIR'):
        after = after.replace('                runInputs.Add(heading);', '''                if (fixedPve.Tick == 40) { fixedPve.Fighters[0].motion.Grow(32); Debug.Log("SNAKE_TOUCH_GROWN"); }
                if (fixedPve.Tick == 1 || fixedPve.Tick == 100) Debug.Log("SNAKE_TOUCH_GROWTH_ZOOM " + fixedPve.Tick + " " + Camera.main.orthographicSize.ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
                runInputs.Add(heading);''')
    code = inject_once(code, before, after)
    before = '                else { paused = !paused; if (paused) feedback.SuspendAudio(); }'
    after = '                else { paused = !paused; Debug.Log("SNAKE_TOUCH_PAUSED " + (paused ? 1 : 0)); if (paused) feedback.SuspendAudio(); }'
    code = inject_once(code, before, after)
    if os.environ.get('SNAKE_TOUCH_VARY_LABELS') == '1':
        before = '            if (Button(GamePauseButton, online ? "退出" : (paused ? "继续" : "暂停"), new Color(.10f, .17f, .25f, .92f)))'
        after = '            bool diagnosticPauseRelease = Event.current.type == EventType.MouseUp && GamePauseButton.Contains(Event.current.mousePosition);\n' + before
        code = inject_once(code, before, after)
        before = '            string strengthNotice = StrengthContactNotice.Text(fixedPve, state != null ? state.tick : -1, out bool strengthWarning);'
        after = '''            // Temporary diagnostic: emulate a dynamic HUD control appearing
            // between pointer down and up without drawing on screen.
            if (diagnosticPauseRelease)
            {
                GUI.Label(new Rect(0, 0, 1, 1), "");
                Debug.Log("SNAKE_TOUCH_LAYOUT_VARIATION " + Time.frameCount);
            }
''' + before
        code = inject_once(code, before, after)
    if os.environ.get('SNAKE_TOUCH_TRACE') == '1':
        before = '            activeTouchIds.Clear();'
        after = '''            for (int ti = 0; ti < Input.touchCount; ti++)
                Debug.Log("SNAKE_TOUCH_TRACE_PHASE " + Time.frameCount + " " + Input.GetTouch(ti).fingerId + " " + Input.GetTouch(ti).phase + " active=" + dragSteering.Active + " moved=" + dragSteering.Moved);
''' + before
        code = inject_once(code, before, after)
        before = '                bool mouseUp = Event.current.type == EventType.MouseUp;'
        after = '''                Debug.Log("SNAKE_TOUCH_TRACE_GUI " + Time.frameCount + " " + Event.current.type + " pos=" + Event.current.mousePosition + " hot=" + GUIUtility.hotControl + " touches=" + activeTouches + " active=" + dragSteering.Active + " moved=" + dragSteering.Moved + " pending=" + suppressPendingDragUp + " release=" + suppressPointerUpFrame);
''' + before
        code = inject_once(code, before, after)
        before = '            bool pressed = GUI.Button(rect, text, style);'
        after = '''            bool traceButton = text == "暂停" && (Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseUp);
            if (traceButton) Debug.Log("SNAKE_TOUCH_TRACE_BUTTON_BEFORE " + Time.frameCount + " " + Event.current.type + " pos=" + Event.current.mousePosition + " hot=" + GUIUtility.hotControl);
''' + before + '''
            if (traceButton) Debug.Log("SNAKE_TOUCH_TRACE_BUTTON_AFTER " + Time.frameCount + " " + Event.current.type + " hot=" + GUIUtility.hotControl + " pressed=" + pressed);'''
        code = inject_once(code, before, after)
    app_path.write_text(code)
    front_path = project / 'Assets/Snake/SnakeGameFront.cs'
    front = front_path.read_text()
    if os.environ.get('SNAKE_TOUCH_SKIN_CHECK') == '1':
        before = '                index => previewSkin = index, EquipPreviewSkin, TrialPreviewSkin);'
        after = '                index => { previewSkin = index; Debug.Log("SNAKE_TOUCH_SKIN_SELECTED " + index); }, index => { EquipPreviewSkin(index); Debug.Log("SNAKE_TOUCH_SKIN_EQUIPPED " + EffectiveSkin); }, TrialPreviewSkin);'
        front = inject_once(front, before, after)
    before = '            viewPreference = GrowthView.NextPreference(viewPreference);'
    after = before + '\n            Debug.Log("SNAKE_TOUCH_VIEW " + viewPreference);'
    front_path.write_text(inject_once(front, before, after))
    renderer_path = project / 'Assets/Snake/ArenaRenderer.cs'
    renderer = renderer_path.read_text()
    before = '            else view.orthographicSize = Mathf.Lerp(view.orthographicSize, targetSize, 1f - Mathf.Exp(-2.5f * zoomDelta));'
    after = before + '\n                if (MotionPaused && adjustingPausedView) Debug.Log("SNAKE_TOUCH_ZOOM " + view.orthographicSize.ToString("F3", System.Globalization.CultureInfo.InvariantCulture));'
    renderer_path.write_text(inject_once(renderer, before, after))
    output = temp / 'webgl'; stamp = temp / 'built'; log = temp / 'build.log'
    env = dict(os.environ, SNAKE_BUILD_OUTPUT=str(output), SNAKE_BUILD_STAMP=str(stamp), SNAKE_INCLUDE_REFERENCE_ASSETS='0')
    command = [str(editor), '-batchmode', '-nographics', '-quit', '-projectPath', str(project), '-buildTarget', 'WebGL',
               '-executeMethod', 'SnakeClub.Editor.SnakeBuilder.BuildWebGL', '-logFile', str(log)]
    print('Building an isolated, non-graphical touch diagnostic...', flush=True)
    result = subprocess.run(command, env=env, timeout=420, check=False)
    if result.returncode or not stamp.exists():
        lines = log.read_text(errors='replace').splitlines() if log.exists() else []
        errors = [line for line in lines if 'error CS' in line or 'Exception:' in line or 'error :' in line]
        print('\n'.join(errors[-25:] or lines[-80:]), file=sys.stderr)
        sys.exit(result.returncode or 1)
    server = ThreadingHTTPServer(('127.0.0.1', 0), partial(Handler, directory=str(output)))
    thread = threading.Thread(target=server.serve_forever, daemon=True); thread.start()
    try:
        print('Testing actual touch inputs in muted headless Chrome...', flush=True)
        for attempt in range(2):
            result = subprocess.run(['node', str(root / 'scripts/fullscreen-touch-webgl-probe.cjs'), str(chrome),
                                     'http://127.0.0.1:' + str(server.server_port) + '/'],
                                    capture_output=True, text=True, timeout=240 if os.environ.get('SNAKE_TOUCH_EXHAUSTED_SCREENSHOT') or os.environ.get('SNAKE_TOUCH_CANCEL_LOOPS') else 120, check=False)
            if result.returncode:
                error = result.stderr if len(result.stderr) <= 5000 else result.stderr[:1000] + '\n...\n' + result.stderr[-4000:]
                sys.exit('Touch diagnostic run ' + str(attempt + 1) + ' failed:\n' + result.stdout[-2000:] + error)
            print('Run ' + str(attempt + 1) + ':\n' + result.stdout.strip(), flush=True)
    finally:
        server.shutdown(); server.server_close(); thread.join(timeout=5)
