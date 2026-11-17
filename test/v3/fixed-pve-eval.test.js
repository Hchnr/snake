const { test } = require('node:test');
const assert = require('node:assert/strict');
const { spawn, execFileSync } = require('node:child_process');
const { createHash, createHmac } = require('node:crypto');
const fs = require('node:fs');
const net = require('node:net');
const os = require('node:os');
const path = require('node:path');

const root = path.join(__dirname, '../..');
const serverDll = path.join(root, 'server/bin/Debug/net10.0/Snake.Server.dll');
const coreDll = path.join(root, 'test/v3/bin/Debug/net10.0/Snake.V3.Core.Tests.dll');
const key = 'fixed-pve-experiment-test-only-stable-key';
const signingKey = createHash('sha256').update(key).digest();
function sign(value) {
  const payload = Buffer.from(value).toString('base64url');
  return `${payload}.${createHmac('sha256', signingKey).update(payload).digest('base64url')}`;
}
const tokenFor = r => sign([r.id, r.playerId, r.mode, r.date, r.rules, r.seed, r.expiresAt].join(':'));

test('local fixed PvE verifier binds the snapshot and recording without issuing rewards', async () => {
  const temp = fs.mkdtempSync(path.join(os.tmpdir(), 'snake-fixed-pve-eval-'));
  let child;
  try {
    const playerId = 'fixed-test-player';
    const date = new Date(Date.now() + 8 * 3600000).toISOString().slice(0, 10);
    const snapshot = { version: 'pve-setup-1', firstRun: true, skin: 2, talents: [1,2,1,0,1,0,0,0,0] };
    const base = { playerId, mode: 'fixed-eval', date, rules: 'fixed-pve-experiment-1', seed: 12345,
      startedAt: Date.now() - 120000, expiresAt: Date.now() + 300000, completed: false, startConfig: snapshot };
    const old = { ...base, id: 'valid' };
    const fast = { ...base, id: 'too-fast', startedAt: Date.now() + 120000 };
    const unknown = { ...base, id: 'unknown', rules: 'fixed-pve-future' };
    const missing = { ...base, id: 'missing', startConfig: null };
    const expired = { ...base, id: 'expired', expiresAt: Date.now() - 1000 };
    const fixture = path.join(temp, 'data.json');
    fs.writeFileSync(fixture, JSON.stringify({ players: { [playerId]: { playerId, dailyKey: date, coins: 73, experience: 29,
      selectedSkin: 0, talents: [0,0,0,0,0,0,0,0,0], tutorialComplete: true, completedMatches: 4 },
      'other-player': { playerId: 'other-player', dailyKey: date } },
      runs: { valid: old, 'too-fast': fast, unknown, missing, expired } }));
    const port = await new Promise((resolve, reject) => {
      const socket = net.createServer(); socket.once('error', reject);
      socket.listen(0, '127.0.0.1', () => { const value = socket.address().port; socket.close(() => resolve(value)); });
    });
    child = spawn('dotnet', [serverDll], { cwd: root, env: { ...process.env, SNAKE_ENV: 'development', SNAKE_FIXED_PVE_EVAL: '1',
      SNAKE_HOST: '127.0.0.1', SNAKE_PORT: String(port), SNAKE_SESSION_KEY: key, SNAKE_DATA_FILE: fixture,
      SNAKE_SQLITE_FILE: path.join(temp, 'state.sqlite'), SNAKE_DEV_AUTH: '0', SNAKE_DEV_ADS: '0', SNAKE_RATE_LIMIT_PER_MIN: '10000' }, stdio: ['ignore', 'pipe', 'pipe'] });
    let output = ''; child.stdout.on('data', c => output += c); child.stderr.on('data', c => output += c);
    const endpoint = `http://127.0.0.1:${port}`;
    const deadline = Date.now() + 15000;
    let ready = false;
    while (Date.now() < deadline && !ready) {
      if (child.exitCode !== null) throw Error(`server exited: ${output}`);
      try { ready = (await fetch(`${endpoint}/health`)).ok; } catch { /* starting */ }
      if (!ready) await new Promise(resolve => setTimeout(resolve, 100));
    }
    assert.ok(ready, `server failed to start: ${output}`);
    const session = sign(`${playerId}:${Math.floor(Date.now() / 1000) + 3600}`);
    async function request(route, body, bearer = session) {
      const response = await fetch(`${endpoint}/api/v3/${route}`, { method: body === undefined ? 'GET' : 'POST',
        headers: { Authorization: `Bearer ${bearer}`, ...(body === undefined ? {} : { 'Content-Type': 'application/json' }) },
        ...(body === undefined ? {} : { body: JSON.stringify(body) }) });
      return { status: response.status, data: await response.json() };
    }
    const replay = JSON.parse(execFileSync('dotnet', [coreDll, 'fixed-replay', String(base.seed)], { encoding: 'utf8' }));
    const changedProfileReplay = JSON.parse(execFileSync('dotnet', [coreDll, 'fixed-replay', String(base.seed), 'default'], { encoding: 'utf8' }));
    assert.equal(changedProfileReplay.inputs, replay.inputs);
    assert.notEqual(changedProfileReplay.length, replay.length, 'fixture must distinguish the frozen snapshot from current profile');
    const payload = (run, inputs = replay.inputs) => ({ runId: run.id, token: tokenFor(run), inputs });
    assert.equal((await request('internal/fixed-pve/verify', { ...payload(old), token: 'invalid' })).status, 400);
    const otherSession = sign(`other-player:${Math.floor(Date.now() / 1000) + 3600}`);
    assert.equal((await request('internal/fixed-pve/verify', payload(old), otherSession)).status, 400);
    assert.equal((await request('internal/fixed-pve/verify', payload(expired))).status, 400);
    assert.equal((await request('internal/fixed-pve/verify', payload(old, '!invalid!'))).status, 400);
    assert.equal((await request('internal/fixed-pve/verify', payload(old, 'AA=='))).status, 400);
    const invalidBoost = Buffer.from(replay.inputs, 'base64'); invalidBoost[1] = 2;
    assert.equal((await request('internal/fixed-pve/verify', payload(old, invalidBoost.toString('base64')))).status, 400);
    const trailing = Buffer.concat([Buffer.from(replay.inputs, 'base64'), Buffer.from([0, 0])]);
    assert.equal((await request('internal/fixed-pve/verify', payload(old, trailing.toString('base64')))).status, 400);
    assert.equal((await request('internal/fixed-pve/verify', payload(old, 'A'.repeat(5000)))).status, 400);
    assert.equal((await request('internal/fixed-pve/verify', payload(fast))).status, 400);
    assert.equal((await request('internal/fixed-pve/verify', payload(unknown))).status, 400);
    assert.equal((await request('internal/fixed-pve/verify', payload(missing))).status, 400);
    const [verified, concurrent] = await Promise.all([
      request('internal/fixed-pve/verify', payload(old)), request('internal/fixed-pve/verify', payload(old))
    ]);
    assert.equal(verified.status, 200); assert.deepEqual(concurrent.data, verified.data);
    assert.deepEqual(verified.data, { ok: true, verified: true, experimental: true, rules: old.rules, ticks: replay.ticks,
      winner: replay.winner, kills: replay.kills, length: replay.length, eventPoints: replay.eventPoints, rewards: 0 });
    assert.deepEqual((await request('internal/fixed-pve/verify', payload(old, 'broken'))).data, verified.data);
    const profile = (await request('bootstrap')).data.profile;
    assert.equal(profile.coins, 73); assert.equal(profile.experience, 29); assert.equal(profile.completedMatches, 4);
    const created = await request('internal/fixed-pve/start', {});
    assert.equal(created.status, 200); assert.equal(created.data.mode, 'fixed-eval');
    assert.equal(created.data.rules, 'fixed-pve-experiment-1'); assert.equal(created.data.startConfig.skin, 0);
    assert.equal((await request('match/finish', { runId: created.data.runId, token: created.data.token, seconds: 3, actions: 1 })).status, 400);
    assert.equal((await request('internal/fixed-pve/start', {}, 'invalid')).status, 401);
  } finally {
    if (child && child.exitCode === null && child.signalCode === null) await new Promise(resolve => { child.once('exit', resolve); child.kill('SIGTERM'); });
    fs.rmSync(temp, { recursive: true, force: true });
  }
});
