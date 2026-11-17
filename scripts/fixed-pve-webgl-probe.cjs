// Internal CDP probe for test-fixed-pve-webgl.py; opens no visible window.
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { spawn } = require('node:child_process');
const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'snake-fixed-chrome-'));
const chrome = spawn(process.argv[2], [
  '--headless=new', '--mute-audio', '--no-first-run', '--no-default-browser-check',
  '--disable-background-networking', '--enable-unsafe-swiftshader', '--use-gl=angle', '--use-angle=swiftshader',
  '--remote-debugging-port=0', '--user-data-dir=' + profile, 'about:blank'
], { stdio: 'ignore' });
let ws;
let finished = false;
function done(code, message) {
  if (finished) return;
  finished = true;
  if (message) (code ? console.error : console.log)(message);
  if (ws) ws.close();
  const cleanup = () => {
    try { fs.rmSync(profile, { recursive: true, force: true, maxRetries: 10, retryDelay: 100 }); }
    catch (error) { console.error('Browser profile cleanup failed: ' + error.message); process.exit(1); }
    process.exit(code);
  };
  if (chrome.exitCode !== null || chrome.signalCode !== null) { cleanup(); return; }
  // Browser profile is disposable; allow Chrome to close before removing it.
  chrome.once('exit', cleanup);
  chrome.kill('SIGTERM');
  setTimeout(() => { chrome.kill('SIGKILL'); cleanup(); }, 3000).unref();
}
chrome.on('error', e => done(1, e.message));
chrome.on('exit', code => { if (!finished) done(1, 'Chrome exited: ' + code); });
setTimeout(() => done(1, 'Timed out waiting for WebGL vector'), 210000).unref();
(async () => {
  let port;
  for (let i = 0; i < 200; i++) {
    try { port = fs.readFileSync(path.join(profile, 'DevToolsActivePort'), 'utf8').split('\n')[0]; break; }
    catch { await new Promise(r => setTimeout(r, 100)); }
  }
  if (!port) throw Error('Chrome did not expose DevTools');
  const tabs = await (await fetch('http://127.0.0.1:' + port + '/json')).json();
  const tab = tabs.find(t => t.type === 'page');
  if (!tab) throw Error('No browser page');
  ws = new WebSocket(tab.webSocketDebuggerUrl);
  await new Promise((resolve, reject) => { ws.onopen = resolve; ws.onerror = reject; });
  let id = 0;
  function send(method, params = {}) { ws.send(JSON.stringify({ id: ++id, method, params })); }
  ws.onmessage = e => {
    const event = JSON.parse(e.data);
    if (event.method === 'Runtime.consoleAPICalled') {
      const text = event.params.args.map(a => a.value || a.description || '').join(' ');
      if (text.includes('SNAKE_FIXED_VECTOR ')) done(0, text.slice(text.indexOf('SNAKE_FIXED_VECTOR ') + 19));
    }
    if (event.method === 'Runtime.exceptionThrown') done(1, 'Browser exception: ' + event.params.exceptionDetails.text);
  };
  send('Runtime.enable'); send('Page.enable'); send('Page.navigate', { url: process.argv[3] });
})().catch(e => done(1, String(e)));
