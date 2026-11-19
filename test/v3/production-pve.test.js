const { test } = require('node:test');
const assert = require('node:assert/strict');
const { spawn, execFileSync } = require('node:child_process');
const { createHash, createHmac } = require('node:crypto');
const fs = require('node:fs');
const http = require('node:http');
const net = require('node:net');
const os = require('node:os');
const path = require('node:path');

const root = path.join(__dirname, '../..');
const serverDll = path.join(root, 'server/bin/Debug/net10.0/Snake.Server.dll');
const coreDll = path.join(root, 'test/v3/bin/Debug/net10.0/Snake.V3.Core.Tests.dll');
const key = 'production-test-only-stable-key-at-least-32-characters';
const signingKey = createHash('sha256').update(key).digest();
const sign = value => {
  const payload = Buffer.from(value).toString('base64url');
  return `${payload}.${createHmac('sha256', signingKey).update(payload).digest('base64url')}`;
};
const tokenFor = run => sign([run.id, run.playerId, run.mode, run.date, run.rules, run.seed, run.expiresAt].join(':'));

// Exercise an actual WebSocket handshake, not just an ordinary HTTP GET.
function upgradeStatus(endpoint) {
  return new Promise((resolve, reject) => {
    const request = http.request(`${endpoint}/ws`, { headers: {
      Connection: 'Upgrade', Upgrade: 'websocket', 'Sec-WebSocket-Version': '13',
      'Sec-WebSocket-Key': 'dGhlIHNhbXBsZSBub25jZQ=='
    }});
    request.on('response', response => { response.resume(); resolve(response.statusCode); });
    request.on('upgrade', (response, socket) => { socket.destroy(); resolve(response.statusCode); });
    request.on('error', reject);
    request.setTimeout(5000, () => request.destroy(new Error('WebSocket handshake timed out')));
    request.end();
  });
}

test('production disables development rooms and unverified PvE while preserving daily and receipts', async () => {
  const temp = fs.mkdtempSync(path.join(os.tmpdir(), 'snake-production-pve-'));
  let child;
  try {
    const playerId = 'production-fixture-player';
    const date = new Date(Date.now() + 8 * 3600000).toISOString().slice(0, 10);
    const base = { playerId, mode: 'pve', date, rules: 'daily-3', seed: 1, startedAt: Date.now() - 120000, expiresAt: Date.now() + 300000 };
    const pending = { ...base, id: 'pending', completed: false };
    const completed = { ...base, id: 'completed', completed: true, expiresAt: Date.now() - 60000, coins: 35, experience: 20 };
    const daily = { ...base, id: 'daily', mode: 'daily', rules: 'daily-4', completed: false };
    const fixture = path.join(temp, 'legacy.json');
    fs.writeFileSync(fixture, JSON.stringify({ players: { [playerId]: { playerId, dailyKey: date, coins: 73, experience: 29 } }, runs: { pending, completed, daily } }));
    const port = await new Promise((resolve, reject) => {
      const socket = net.createServer(); socket.once('error', reject);
      socket.listen(0, '127.0.0.1', () => { const value = socket.address().port; socket.close(() => resolve(value)); });
    });
    const env = { ...process.env, SNAKE_ENV: 'production', SNAKE_HOST: '127.0.0.1', SNAKE_PORT: String(port), SNAKE_SESSION_KEY: key,
      SNAKE_DATA_FILE: fixture, SNAKE_SQLITE_FILE: path.join(temp, 'state.sqlite'), SNAKE_DEV_AUTH: '0', SNAKE_DEV_ADS: '0', SNAKE_FIXED_PVE_EVAL: '1', SNAKE_STRENGTH_EVAL: '1', SNAKE_RATE_LIMIT_PER_MIN: '10000' };
    child = spawn('dotnet', [serverDll], { cwd: root, env, stdio: ['ignore', 'pipe', 'pipe'] });
    let output = ''; child.stdout.on('data', chunk => output += chunk); child.stderr.on('data', chunk => output += chunk);
    const endpoint = `http://127.0.0.1:${port}`;
    const deadline = Date.now() + 15000;
    let ready = false;
    while (Date.now() < deadline && !ready) {
      if (child.exitCode !== null) throw new Error(`server exited: ${output}`);
      try { ready = (await fetch(`${endpoint}/health`)).ok; } catch { /* starting */ }
      if (!ready) await new Promise(resolve => setTimeout(resolve, 100));
    }
    assert.ok(ready, `server failed to start: ${output}`);
    assert.equal(await upgradeStatus(endpoint), 404, 'production accepted an unsupported realtime room connection');
    const session = sign(`${playerId}:${Math.floor(Date.now() / 1000) + 3600}`);
    async function request(route, body, bearer = session) {
      const response = await fetch(`${endpoint}/api/v3/${route}`, { method: body === undefined ? 'GET' : 'POST',
        headers: { Authorization: `Bearer ${bearer}`, ...(body === undefined ? {} : { 'Content-Type': 'application/json' }) },
        ...(body === undefined ? {} : { body: JSON.stringify(body) }) });
      return { status: response.status, data: await response.json() };
    }
    assert.equal((await fetch(`${endpoint}/api/v3/internal/fixed-pve/start`, { method: 'POST' })).status, 404);
    assert.equal((await fetch(`${endpoint}/api/v3/internal/strength-pve/start`, { method: 'POST' })).status, 404);
    assert.equal((await fetch(`${endpoint}/api/v3/internal/strength-pve/verify`, { method: 'POST' })).status, 404);
    assert.equal((await request('match/start', {})).status, 503);
    const finish = run => ({ runId: run.id, token: tokenFor(run), seconds: 3, actions: 1 });
    assert.equal((await request('match/finish', finish(pending))).status, 503);
    assert.equal((await request('match/finish', { ...finish(completed), token: 'invalid' })).status, 400);
    const receipt = await request('match/finish', finish(completed));
    assert.equal(receipt.status, 200); assert.equal(receipt.data.coins, 35);
    const profile = await request('bootstrap');
    assert.equal(profile.data.profile.coins, 73); assert.equal(profile.data.profile.experience, 29);
    assert.equal((await request('challenge/start?rules=daily-4', {})).status, 200);
    assert.equal((await request('match/start', {}, 'invalid')).status, 401);
    const replay = JSON.parse(execFileSync('dotnet', [coreDll, 'replay', String(daily.seed), '0', daily.rules], { encoding: 'utf8' }));
    const settled = await request('challenge/submit', { runId: daily.id, token: tokenFor(daily), score: replay.score, inputs: replay.inputs, actions: 1 });
    assert.equal(settled.status, 200); assert.equal(settled.data.verified, true);
    assert.equal(settled.data.profile.tutorialComplete, true);
    assert.equal(settled.data.profile.completedMatches, 1);
    assert.ok(settled.data.profile.coins > 73);
  } finally {
    if (child && child.exitCode === null && child.signalCode === null) await new Promise(resolve => { child.once('exit', resolve); child.kill('SIGTERM'); });
    fs.rmSync(temp, { recursive: true, force: true });
  }
});
