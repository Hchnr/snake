// Headless actual-touch match and local replay; logs come from an isolated diagnostic build.
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { spawn } = require('node:child_process');
// Keep existing probe callers on the original upward drag unless opted in.
const openingLeft = process.env.SNAKE_STRENGTH_OPENING_LEFT === '1';
// Default remains strict: recovery must occur in this natural round.
const requireRecovery = process.env.SNAKE_STRENGTH_REQUIRE_RECOVERY !== '0';
const requireProtectionCue = process.env.SNAKE_STRENGTH_REQUIRE_PROTECTION_CUE !== '0';
const matchTimeoutSeconds = Number(process.argv[4] || 600);
if (!Number.isInteger(matchTimeoutSeconds) || matchTimeoutSeconds < 60 || matchTimeoutSeconds > 900) {
  throw Error('Match timeout must be an integer from 60 to 900 seconds');
}
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
async function capture(name) {
  const directory = process.env.SNAKE_STRENGTH_CAPTURE_DIR;
  if (!directory) return;
  fs.mkdirSync(directory, { recursive: true });
  const result = await call('Page.captureScreenshot', { format: 'png' });
  fs.writeFileSync(path.join(directory, name + '.png'), Buffer.from(result.data, 'base64'));
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
      if (text.includes('SNAKE_TOUCH_') || text.includes('SNAKE_STRENGTH_E2E ')) events.push(text);
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
  await delay(600); await capture('home'); await tap(270, 640);
  await waitFor(() => hasSince(0, 'SNAKE_TOUCH_READY'), 'start match');
  await waitFor(() => hasSince(0, 'SNAKE_TOUCH_TICK'), 'first simulation tick');
  await waitFor(() => events.some(e => e.includes('SNAKE_TOUCH_CUE') && e.includes('暂不能与对手互吃')), 'visible protection explanation');
  // Let a few real ticks elapse so the seeded touch path is reproducible.
  await waitFor(() => events.some(e => { const m = e.match(/SNAKE_TOUCH_TICK (\d+) /); return m && Number(m[1]) >= 6; }), 'opening ticks');
  let index = events.length;
  // The explanation is not a control pad: drag from inside it.
  await touch('touchStart', [point(250, 680, 1)]); await delay(100);
  const steeredPoint = openingLeft ? point(170, 680, 1) : point(250, 600, 1);
  await touch('touchMove', [steeredPoint]);
  const expectedHeading = openingLeft ? 128 : 64;
  await waitFor(() => events.slice(index).some(e => new RegExp(`SNAKE_TOUCH_TICK \\d+ ${expectedHeading} 0(?:\\s|$)`).test(e)), 'actual steering');
  if (openingLeft) {
    const line = await waitFor(() => events.slice(index).find(e => /SNAKE_TOUCH_OPENING \d+ 128 1 \d+/.test(e)), 'actual capped opening assist');
    const m = line.match(/SNAKE_TOUCH_OPENING (\d+) 128 1 (\d+)/);
    const tick = Number(m[1]), angle = Number(m[2]);
    const remaining = Math.abs(((128 * 256 - angle + 98304) & 65535) - 32768);
    if (tick > 40 || remaining < 10000 || remaining > 16384)
      throw Error('First left input did not retain the capped opening turn: ' + line);
    console.log('PASS actual left drag triggered the capped opening turn');
  }
  index = events.length;
  await touch('touchStart', [steeredPoint, point(350, 500, 2)]);
  await waitFor(() => events.slice(index).some(e => /SNAKE_TOUCH_TICK \d+ \d+ 1(?:\s|$)/.test(e)), 'actual boost');
  await touch('touchEnd', [point(350, 500, 2)]); await delay(150); await touch('touchEnd', []);
  await capture('protection'); await capture('battle');
  if (!requireProtectionCue) {
    console.log('SKIP protection-expiry text assertion (explicit opt-out)');
  } else {
    await waitFor(() => events.some(e => e.includes('SNAKE_TOUCH_CUE') && e.includes('保护结束')), 'visible protection expiry', 60000);
    await capture('protection-ended');
  }
  if (requireRecovery) {
    await waitFor(() => events.some(e => /SNAKE_TOUCH_RECOVERY \d+ marked=[1234] .*回追补给/.test(e)), 'visible recovery supply after an actual bite', 120000);
    await capture('recovery-supply');
  } else {
    console.log('SKIP recovery assertion: this run verifies steering and replay only');
  }
  await waitFor(() => hasSince(0, 'SNAKE_STRENGTH_E2E '), 'complete actual-touch match and local replay', matchTimeoutSeconds * 1000);
  await delay(1000); await capture('result');
  const line = events.find(e => e.includes('SNAKE_STRENGTH_E2E '));
  const match = line.match(/SNAKE_STRENGTH_E2E (?:MATCH|FAIL) -?\d+ [01] \d+ s1-[0-9a-f]{16} [A-Za-z0-9+/=]+/);
  if (!match) throw Error('Malformed strength replay result');
  console.log(match[0]);
}
main().then(() => { ws.close(); chrome.kill('SIGTERM'); }).catch(async error => {
  console.error(error.message); process.exitCode = 1;
  let lastInput = '';
  const changes = [];
  for (const event of events) {
    const match = event.match(/SNAKE_TOUCH_TICK (\d+) (\d+) ([01])/);
    if (match && match[2] + ':' + match[3] !== lastInput) {
      lastInput = match[2] + ':' + match[3]; changes.push(match[1] + ':' + lastInput);
    }
  }
  console.error('Observed input changes (tick:heading:boost): ' + changes.slice(0, 30).join(', '));
  console.error('Recovery notices: ' + events.filter(e => e.includes('SNAKE_TOUCH_RECOVERY')).slice(0, 12).join(' | '));
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
