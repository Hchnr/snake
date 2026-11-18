// Headless CDP touch test; Unity logs come from an isolated diagnostic build.
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { spawn } = require('node:child_process');
const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'snake-touch-chrome-'));
const chrome = spawn(process.argv[2], [
  '--headless=new', '--mute-audio', '--no-first-run', '--no-default-browser-check',
  '--disable-background-networking', '--enable-unsafe-swiftshader', '--use-gl=angle', '--use-angle=swiftshader',
  '--remote-debugging-port=0', '--user-data-dir=' + profile, 'about:blank'
], { stdio: 'ignore' });
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
let ws, sequence = 0, pending = new Map(), events = [], failure;
function call(method, params = {}) {
  const id = ++sequence;
  return new Promise((resolve, reject) => { pending.set(id, { resolve, reject }); ws.send(JSON.stringify({ id, method, params })); });
}
async function waitFor(predicate, description, timeout = 12000) {
  const until = Date.now() + timeout;
  while (Date.now() < until) {
    if (failure) throw Error(failure);
    const result = predicate(); if (result) return result;
    await delay(30);
  }
  throw Error('Timed out: ' + description + '; recent events: ' + events.slice(-12).join(' | '));
}
function hasSince(index, fragment) { return events.slice(index).some(e => e.includes(fragment)); }
async function touch(type, points) { await call('Input.dispatchTouchEvent', { type, touchPoints: points }); }
function point(x, y, id = 1) { return { x, y, id }; }
async function tap(x, y) {
  await touch('touchStart', [point(x, y)]); await delay(120); await touch('touchEnd', []);
}
async function drag(label, x, y, endX, endY, expected) {
  const index = events.length;
  await touch('touchStart', [point(x, y)]);
  for (let i = 1; i <= 8; i++) {
    await delay(50);
    await touch('touchMove', [point(x + (endX - x) * i / 8, y + (endY - y) * i / 8)]);
  }
  await waitFor(() => events.slice(index).some(e => new RegExp('SNAKE_TOUCH_TICK \\d+ ' + expected + ' 0(?:\\s|$)').test(e)), label + ' direction');
  await touch('touchEnd', []); await delay(300);
  if (hasSince(index, 'SNAKE_TOUCH_PAUSED 1')) throw Error(label + ' unexpectedly paused the game');
  await waitFor(() => events.slice(index).filter(e => e.includes('SNAKE_TOUCH_TICK')).length >= 2, label + ' simulation');
  console.log('PASS ' + label + ' -> heading ' + expected);
}
async function main() {
  let port;
  for (let i = 0; i < 200; i++) {
    try { port = fs.readFileSync(path.join(profile, 'DevToolsActivePort'), 'utf8').split('\n')[0]; break; }
    catch { await delay(100); }
  }
  if (!port) throw Error('Chrome did not expose DevTools');
  const tabs = await (await fetch('http://127.0.0.1:' + port + '/json')).json();
  const tab = tabs.find(t => t.type === 'page');
  ws = new WebSocket(tab.webSocketDebuggerUrl);
  await new Promise((resolve, reject) => { ws.onopen = resolve; ws.onerror = reject; });
  ws.onmessage = event => {
    const message = JSON.parse(event.data);
    if (message.id && pending.has(message.id)) {
      const p = pending.get(message.id); pending.delete(message.id);
      message.error ? p.reject(Error(message.error.message)) : p.resolve(message.result);
    }
    if (message.method === 'Runtime.exceptionThrown') failure = 'Browser exception: ' + message.params.exceptionDetails.text;
    if (message.method === 'Runtime.consoleAPICalled') {
      const text = message.params.args.map(a => a.value || a.description || '').join(' ');
      if (text.includes('SNAKE_TOUCH_')) events.push(text);
    }
  };
  await call('Page.enable'); await call('Runtime.enable');
  await call('Network.setUserAgentOverride', { userAgent: 'Mozilla/5.0 (Linux; Android 13; Pixel 7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0 Mobile Safari/537.36' });
  await call('Emulation.setDeviceMetricsOverride', { width: 540, height: 960, deviceScaleFactor: 1, mobile: true });
  await call('Emulation.setTouchEmulationEnabled', { enabled: true, maxTouchPoints: 5 });
  // CDP implementations differ on partial touchEnd arguments. Record the DOM's
  // changed and remaining IDs so a wrong release cannot produce a false pass.
  await call('Page.addScriptToEvaluateOnNewDocument', { source: `for (const type of ['touchstart','touchend','touchcancel']) document.addEventListener(type, e => console.log('SNAKE_TOUCH_DOM ' + type + ' ' + [...e.changedTouches].map(t => t.identifier).join(',') + ' ' + [...e.touches].map(t => t.identifier).join(',')), true);` });
  await call('Page.navigate', { url: process.argv[3] });
  await waitFor(() => hasSince(0, 'SNAKE_TOUCH_HOME'), 'Unity home', 50000);
  await delay(600); await tap(270, 640);
  await waitFor(() => hasSince(0, 'SNAKE_TOUCH_READY'), 'start match');
  await waitFor(() => hasSince(0, 'SNAKE_TOUCH_TICK'), 'first simulation tick');
  await drag('top-left', 10, 20, 10, 100, 192);
  await drag('top-right', 530, 20, 450, 20, 128);
  await drag('bottom-left', 10, 940, 10, 860, 64);
  await drag('bottom-right', 530, 940, 450, 940, 128);
  await drag('minimap', 450, 175, 450, 235, 192);
  await drag('pause button', 115, 160, 55, 160, 128);
  let index = events.length;
  await touch('touchStart', [point(200, 500, 1)]); await delay(150);
  await touch('touchStart', [point(200, 500, 1), point(80, 160, 2)]);
  await waitFor(() => events.slice(index).some(e => /SNAKE_TOUCH_TICK \d+ \d+ 1(?:\s|$)/.test(e)), 'second finger boosts');
  const releaseIndex = events.length;
  await touch('touchEnd', [point(80, 160, 2)]);
  await waitFor(() => hasSince(releaseIndex, 'SNAKE_TOUCH_DOM touchend 2 1'), 'actual second-finger release');
  await waitFor(() => events.slice(releaseIndex).some(e => /SNAKE_TOUCH_TICK \d+ \d+ 0(?:\s|$)/.test(e)), 'release second finger');
  await touch('touchEnd', []); await delay(200);
  if (hasSince(index, 'SNAKE_TOUCH_PAUSED 1')) throw Error('second finger unexpectedly paused game');
  console.log('PASS second finger on pause button boosts without pausing');
  index = events.length;
  await touch('touchStart', [point(250, 500, 1)]); await delay(100);
  await touch('touchMove', [point(310, 500, 1)]);
  await waitFor(() => events.slice(index).some(e => /SNAKE_TOUCH_TICK \d+ 0 0(?:\s|$)/.test(e)), 'first finger direction before handoff');
  await touch('touchStart', [point(310, 500, 1), point(80, 160, 2)]);
  await waitFor(() => events.slice(index).some(e => /SNAKE_TOUCH_TICK \d+ \d+ 1(?:\s|$)/.test(e)), 'boost before handoff');
  const primaryReleaseIndex = events.length;
  await touch('touchEnd', [point(310, 500, 1)]);
  await waitFor(() => hasSince(primaryReleaseIndex, 'SNAKE_TOUCH_DOM touchend 1 2'), 'actual primary-finger release');
  await delay(150);
  const handoffIndex = events.length;
  await touch('touchMove', [point(80, 220, 2)]);
  await waitFor(() => events.slice(handoffIndex).some(e => /SNAKE_TOUCH_TICK \d+ 192 0(?:\s|$)/.test(e)), 'remaining finger takes steering');
  await touch('touchEnd', []); await delay(200);
  if (hasSince(index, 'SNAKE_TOUCH_PAUSED 1')) throw Error('finger handoff unexpectedly paused game');
  console.log('PASS remaining finger takes steering after handoff');
  index = events.length;
  await touch('touchStart', [point(200, 500, 1)]); await delay(100);
  await touch('touchMove', [point(260, 500, 1)]);
  await waitFor(() => events.slice(index).some(e => /SNAKE_TOUCH_TICK \d+ 0 0(?:\s|$)/.test(e)), 'direction before cancellation');
  await touch('touchCancel', []); await delay(250);
  console.log('PASS drag cancellation');
  index = events.length; await tap(80, 160);
  await waitFor(() => hasSince(index, 'SNAKE_TOUCH_PAUSED 1'), 'pause tap');
  await delay(200); const pauseIndex = events.length; await delay(300);
  if (events.slice(pauseIndex).some(e => e.includes('SNAKE_TOUCH_TICK'))) throw Error('simulation advanced while paused');
  if (process.env.SNAKE_TOUCH_PAUSE_SCREENSHOT) {
    const screenshot = await call('Page.captureScreenshot', { format: 'png' });
    fs.writeFileSync(process.env.SNAKE_TOUCH_PAUSE_SCREENSHOT, Buffer.from(screenshot.data, 'base64'));
  }
  index = events.length; await tap(270, 525);
  await waitFor(() => hasSince(index, 'SNAKE_TOUCH_TICK'), 'simulation resumes');
  console.log('PASS tap pauses, freezes and resumes from the guide');
}
main().then(() => { ws.close(); chrome.kill('SIGTERM'); }).catch(async error => {
  console.error(error.message); process.exitCode = 1;
  try {
    if (ws && ws.readyState === WebSocket.OPEN) {
      const screenshot = await call('Page.captureScreenshot', { format: 'png' });
      const filename = path.join(os.tmpdir(), 'snake-touch-diagnostic-failure.png');
      fs.writeFileSync(filename, Buffer.from(screenshot.data, 'base64'));
      console.error('Failure screenshot: ' + filename);
    }
  } catch {}
  if (ws) ws.close(); chrome.kill('SIGTERM');
});
chrome.on('exit', () => { try { fs.rmSync(profile, { recursive: true, force: true, maxRetries: 10, retryDelay: 100 }); } catch {} });
