// Internal real-time touch probe for test-strength-pve-client-e2e.py.
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { spawn } = require('node:child_process');
const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'snake-strength-e2e-chrome-'));
const chrome = spawn(process.argv[2], [
  '--headless=new', '--mute-audio', '--no-first-run', '--no-default-browser-check', '--disable-background-networking',
  '--enable-unsafe-swiftshader', '--use-gl=angle', '--use-angle=swiftshader', '--remote-debugging-port=0',
  '--user-data-dir=' + profile, 'about:blank'
], { stdio: 'ignore' });
let ws, stopped = false, started = false, sentInput = false, verifySeen = false, turns = 0;
let inspectReplay = Promise.resolve(false);
const pending = new Map();
let id = 0;
function done(code, message) {
  if (stopped) return;
  stopped = true;
  if (message) (code ? console.error : console.log)(message);
  if (ws) ws.close();
  const cleanup = () => { fs.rmSync(profile, { recursive: true, force: true }); process.exit(code); };
  if (chrome.exitCode !== null || chrome.signalCode !== null) { cleanup(); return; }
  chrome.once('exit', cleanup); chrome.kill('SIGTERM');
  setTimeout(() => { chrome.kill('SIGKILL'); cleanup(); }, 3000).unref();
}
chrome.on('error', e => done(1, e.message));
chrome.on('exit', code => { if (!stopped) done(1, 'Chrome exited: ' + code); });
setTimeout(() => done(1, 'Timed out waiting for a verified local match'), 160000).unref();
function call(method, params = {}) {
  return new Promise((resolve, reject) => {
    const n = ++id; pending.set(n, { resolve, reject }); ws.send(JSON.stringify({ id: n, method, params }));
  });
}
const pause = ms => new Promise(r => setTimeout(r, ms));
async function tap(x, y) {
  await call('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [{ x, y, id: 1 }] });
  await pause(120);
  await call('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] });
}
async function steer() {
  if (stopped || !started) return;
  const angle = turns++ % 4;
  const targets = [[270, 420], [390, 540], [270, 690], [150, 540]];
  const [x, y] = targets[angle];
  await call('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [{ x: 270, y: 540, id: 1 }] });
  for (let i = 1; i <= 4; i++) {
    await pause(50);
    await call('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: [{ x: 270 + (x - 270) * i / 4, y: 540 + (y - 540) * i / 4, id: 1 }] });
  }
  // Hold a second finger while retaining the steering finger, then release
  // it separately so actual simulation input includes a boost interval.
  await call('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [{ x, y, id: 1 }, { x: 450, y: 700, id: 2 }] });
  await pause(700);
  await call('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [{ x: 450, y: 700, id: 2 }] });
  await pause(100);
  await call('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] });
  sentInput = true;
}
(async () => {
  let port;
  for (let i = 0; i < 200; i++) {
    try { port = fs.readFileSync(path.join(profile, 'DevToolsActivePort'), 'utf8').split('\n')[0]; break; }
    catch { await pause(100); }
  }
  if (!port) throw Error('Chrome did not expose DevTools');
  const tabs = await (await fetch('http://127.0.0.1:' + port + '/json')).json();
  const tab = tabs.find(t => t.type === 'page');
  if (!tab) throw Error('No browser page');
  ws = new WebSocket(tab.webSocketDebuggerUrl);
  await new Promise((resolve, reject) => { ws.onopen = resolve; ws.onerror = reject; });
  ws.onmessage = e => {
    const event = JSON.parse(e.data);
    if (event.id && pending.has(event.id)) {
      const p = pending.get(event.id); pending.delete(event.id);
      event.error ? p.reject(Error(event.error.message)) : p.resolve(event.result);
    }
    if (event.method === 'Runtime.exceptionThrown') done(1, 'Browser exception: ' + event.params.exceptionDetails.text);
    if (event.method === 'Network.requestWillBeSent' && event.params.request.method === 'POST' && event.params.request.url.endsWith('/internal/strength-pve/verify')) {
      const request = event.params.request;
      const requestId = event.params.requestId;
      inspectReplay = (async () => {
        let body = request.postData;
        if (!body && request.postDataEntries?.length)
          body = Buffer.concat(request.postDataEntries.map(entry => Buffer.from(entry.bytes || '', 'base64'))).toString('utf8');
        if (!body) body = (await call('Network.getRequestPostData', { requestId })).postData;
        const input = Buffer.from(JSON.parse(body).inputs, 'base64');
        const headings = new Set(); let boosted = false, released = false;
        for (let i = 0; i + 1 < input.length; i += 2) {
          headings.add(input[i]);
          if (input[i + 1] === 1) boosted = true;
          else if (boosted && input[i + 1] === 0) released = true;
        }
        return input.length > 0 && input.length % 2 === 0 && headings.size > 1 && boosted && released;
      })().catch(() => false);
    }
    if (event.method === 'Network.responseReceived') {
      const { url, status } = event.params.response;
      if (event.params.type === 'Preflight' || status === 204) return;
      if (url.endsWith('/api/v3/bootstrap')) {
        if (status !== 200) return done(1, 'Bootstrap failed: ' + status);
        // Wait for Unity to process the successful login and render its home screen.
        pause(2000).then(() => tap(270, 640)).catch(e => done(1, String(e)));
      }
      if (url.endsWith('/internal/strength-pve/start')) {
        if (status !== 200) return done(1, 'Experiment start failed: ' + status);
        if (!started) {
          started = true;
          (async () => {
            await pause(1000);
            while (!stopped && started) { await steer(); await pause(2500); }
          })().catch(e => { if (!stopped) done(1, String(e)); });
        }
      }
      if (url.endsWith('/internal/strength-pve/verify')) {
        if (status !== 200) return done(1, 'Experiment verification failed: ' + status);
        verifySeen = true;
      }
    }
    if (event.method === 'Runtime.consoleAPICalled') {
      const text = event.params.args.map(a => a.value || a.description || '').join(' ');
      if (text.includes('SNAKE_STRENGTH_E2E ')) {
        inspectReplay.then(requestSeen => {
          if (text.includes('SNAKE_STRENGTH_E2E MATCH') && sentInput && requestSeen && verifySeen)
            done(0, 'PASS: touch steering, boost and release produced a matching strength experimental server receipt.');
          else done(1, 'Client reported mismatch or missing real touch/replay evidence.');
        }).catch(() => done(1, 'Unable to inspect bounded replay input'));
      }
    }
  };
  await call('Page.enable'); await call('Runtime.enable'); await call('Network.enable', { maxPostDataSize: 10000 });
  await call('Network.setUserAgentOverride', { userAgent: 'Mozilla/5.0 (Linux; Android 13; Pixel 7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0 Mobile Safari/537.36' });
  await call('Emulation.setTouchEmulationEnabled', { enabled: true, maxTouchPoints: 2 });
  await call('Emulation.setDeviceMetricsOverride', { width: 540, height: 960, deviceScaleFactor: 1, mobile: true });
  await call('Page.navigate', { url: process.argv[3] });
})().catch(e => done(1, String(e)));
