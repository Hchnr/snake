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
const key = 'strength-eval-local-test-key-not-for-production';
const signingKey = createHash('sha256').update(key).digest();
function sign(value) {
  const payload = Buffer.from(value).toString('base64url');
  return `${payload}.${createHmac('sha256', signingKey).update(payload).digest('base64url')}`;
}
const tokenFor = run => sign([run.id, run.playerId, run.mode, run.date, run.rules, run.seed, run.expiresAt].join(':'));

test('local strength verifier binds seed and snapshot without rewards or legacy crossover', async () => {
  const temp = fs.mkdtempSync(path.join(os.tmpdir(), 'snake-strength-eval-'));
  let child;
  try {
    const playerId = 'strength-test-player';
    const date = new Date(Date.now() + 8 * 3600000).toISOString().slice(0, 10);
    const snapshot = { version: 'strength-eval-setup-1', firstRun: true };
    const base = { playerId, mode: 'strength-eval', date, rules: 'strength-pve-dev-1', seed: 73,
      startedAt: Date.now() - 120000, expiresAt: Date.now() + 300000, completed: false, strengthStartConfig: snapshot };
    const valid = { ...base, id: 'valid' };
    const growth = { ...base, id: 'growth', strengthStartConfig: { version: 'strength-eval-setup-2', firstRun: true, accountLevel: 8, basePower: 103, hardness: 14 } };
    const tamperedGrowth = { ...growth, id: 'tampered-growth', strengthStartConfig: { ...growth.strengthStartConfig, basePower: 120 } };
    const fast = { ...base, id: 'fast', startedAt: Date.now() + 120000 };
    const unknown = { ...base, id: 'unknown', rules: 'strength-pve-dev-unknown' };
    const missing = { ...base, id: 'missing', strengthStartConfig: null };
    const badSnapshot = { ...base, id: 'bad-snapshot', strengthStartConfig: { ...snapshot, version: 'unknown' } };
    const expired = { ...base, id: 'expired', expiresAt: Date.now() - 1000 };
    const oldReceipt = { ...base, id: 'old-receipt', completed: true, expiresAt: Date.now() - 1000, score: 100, fixedWinner: 'you', fixedKills: 1, fixedLength: 20 };
    const fixture = path.join(temp, 'data.json');
    fs.writeFileSync(fixture, JSON.stringify({ players: { [playerId]: { playerId, dailyKey: date, coins: 73, experience: 29,
      tutorialComplete: true, completedMatches: 4 }, 'other-player': { playerId: 'other-player', dailyKey: date } },
      runs: { valid, growth, 'tampered-growth': tamperedGrowth, fast, unknown, missing, 'bad-snapshot': badSnapshot, expired, 'old-receipt': oldReceipt } }));
    const port = await new Promise((resolve, reject) => {
      const socket = net.createServer(); socket.once('error', reject);
      socket.listen(0, '127.0.0.1', () => { const value = socket.address().port; socket.close(() => resolve(value)); });
    });
    child = spawn('dotnet', [serverDll], { cwd: root, env: { ...process.env, SNAKE_ENV: 'development', SNAKE_STRENGTH_EVAL: '1', SNAKE_FIXED_PVE_EVAL: '1',
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
    const replay = JSON.parse(execFileSync('dotnet', [coreDll, 'strength-replay', String(base.seed), '1'], { encoding: 'utf8' }));
    const changedFirstRun = JSON.parse(execFileSync('dotnet', [coreDll, 'strength-replay', String(base.seed), '0'], { encoding: 'utf8' }));
    assert.notEqual(changedFirstRun.ticks, replay.ticks, 'fixture must distinguish the saved first-run snapshot');
    const payload = (run, inputs = replay.inputs) => ({ runId: run.id, token: tokenFor(run), inputs });
    assert.equal((await request('internal/strength-pve/verify', { ...payload(valid), token: 'invalid' })).status, 400);
    assert.equal((await request('internal/strength-pve/verify', { ...payload(valid), token: tokenFor({ ...valid, seed: valid.seed + 1 }) })).status, 400);
    assert.equal((await request('internal/fixed-pve/verify', payload(valid))).status, 400);
    assert.deepEqual((await request('internal/strength-pve/verify', payload(oldReceipt, 'broken'))).data,
      { ok: true, verified: true, experimental: true, rules: base.rules, ticks: 100, winner: 'you', kills: 1, length: 20, stateDigest: '', rewards: 0 });
    const other = sign(`other-player:${Math.floor(Date.now() / 1000) + 3600}`);
    assert.equal((await request('internal/strength-pve/verify', payload(valid), other)).status, 400);
    for (const run of [fast, unknown, missing, badSnapshot, tamperedGrowth, expired])
      assert.equal((await request('internal/strength-pve/verify', payload(run))).status, 400);
    assert.equal((await request('internal/strength-pve/verify', payload(valid, '!invalid!'))).status, 400);
    assert.equal((await request('internal/strength-pve/verify', payload(valid, 'AA=='))).status, 400);
    const invalidBoost = Buffer.from(replay.inputs, 'base64'); invalidBoost[1] = 2;
    assert.equal((await request('internal/strength-pve/verify', payload(valid, invalidBoost.toString('base64')))).status, 400);
    const trailing = Buffer.concat([Buffer.from(replay.inputs, 'base64'), Buffer.from([0, 0])]);
    assert.equal((await request('internal/strength-pve/verify', payload(valid, trailing.toString('base64')))).status, 400);
    assert.equal((await request('internal/strength-pve/verify', payload(valid, 'A'.repeat(5000)))).status, 400);
    const [verified, concurrent] = await Promise.all([
      request('internal/strength-pve/verify', payload(valid)), request('internal/strength-pve/verify', payload(valid))
    ]);
    assert.equal(verified.status, 200); assert.deepEqual(concurrent.data, verified.data);
    assert.deepEqual(verified.data, { ok: true, verified: true, experimental: true, rules: base.rules, ticks: replay.ticks,
      winner: replay.winner, kills: replay.kills, length: replay.length, stateDigest: replay.stateDigest, rewards: 0 });
    assert.deepEqual((await request('internal/strength-pve/verify', payload(valid, 'broken'))).data, verified.data);
    const growthReplay = JSON.parse(execFileSync('dotnet', [coreDll, 'strength-growth-replay', String(base.seed), '700', '1'], { encoding: 'utf8' }));
    const growthVerified = await request('internal/strength-pve/verify', payload(growth, growthReplay.inputs));
    assert.deepEqual(growthVerified.data, { ok: true, verified: true, experimental: true, rules: base.rules, ticks: growthReplay.ticks,
      winner: growthReplay.winner, kills: growthReplay.kills, length: growthReplay.length, stateDigest: growthReplay.stateDigest, rewards: 0 });
    const profile = (await request('bootstrap')).data.profile;
    assert.equal(profile.coins, 73); assert.equal(profile.experience, 29); assert.equal(profile.completedMatches, 4);
    const created = await request('internal/strength-pve/start', {});
    assert.equal(created.status, 200); assert.equal(created.data.mode, 'strength-eval');
    assert.equal(created.data.rules, 'strength-pve-dev-1');
    assert.equal(created.data.strengthStartConfig.version, 'strength-eval-setup-2');
    assert.equal(created.data.strengthStartConfig.firstRun, false);
    assert.equal(created.data.strengthStartConfig.accountLevel, 1);
    assert.equal(created.data.strengthStartConfig.basePower, 100);
    assert.equal(created.data.strengthStartConfig.hardness, 0);
    assert.equal(created.data.strengthStartConfig.supported, true);
    assert.equal((await request('match/finish', { runId: created.data.runId, token: created.data.token, seconds: 3, actions: 1 })).status, 400);
    const fixed = await request('internal/fixed-pve/start', {});
    assert.equal(fixed.status, 200);
    assert.equal((await request('internal/strength-pve/verify', { runId: fixed.data.runId, token: fixed.data.token, inputs: replay.inputs })).status, 400);
    assert.equal((await request('internal/strength-pve/start', {}, 'invalid')).status, 401);
  } finally {
    if (child && child.exitCode === null && child.signalCode === null) await new Promise(resolve => { child.once('exit', resolve); child.kill('SIGTERM'); });
    fs.rmSync(temp, { recursive: true, force: true });
  }
});
