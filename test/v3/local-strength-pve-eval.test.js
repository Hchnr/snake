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
const secret = 'local-strength-v13-test-key-never-use-in-production';
const key = createHash('sha256').update(secret).digest();
const sign = value => {
  const payload = Buffer.from(value).toString('base64url');
  return `${payload}.${createHmac('sha256', key).update(payload).digest('base64url')}`;
};
const tokenFor = run => sign([run.id, run.playerId, run.mode, run.date, run.rules, run.seed, run.expiresAt,
  run.startedAt, run.localStrengthStartConfig?.version ?? 'missing',
  run.localStrengthStartConfig?.firstRun == null ? 'missing' : run.localStrengthStartConfig.firstRun ? '1' : '0'].join(':'));
const replay = (seed, first) => JSON.parse(execFileSync('dotnet', [coreDll, 'strength-local-v13-replay', String(seed), first ? '1' : '0'], { encoding: 'utf8' }));

async function startServer(temp, fixture, enabled) {
  const port = await new Promise((resolve, reject) => {
    const socket = net.createServer(); socket.once('error', reject);
    socket.listen(0, '127.0.0.1', () => { const value = socket.address().port; socket.close(() => resolve(value)); });
  });
  const database = path.join(temp, enabled ? 'enabled.sqlite' : 'disabled.sqlite');
  const child = spawn('dotnet', [serverDll], { cwd: root, env: { ...process.env,
    SNAKE_ENV: 'development', SNAKE_LOCAL_STRENGTH_EVAL: enabled ? '1' : '0', SNAKE_STRENGTH_EVAL: '1', SNAKE_FIXED_PVE_EVAL: '1',
    SNAKE_HOST: '127.0.0.1', SNAKE_PORT: String(port), SNAKE_SESSION_KEY: secret, SNAKE_DATA_FILE: fixture,
    SNAKE_SQLITE_FILE: database, SNAKE_DEV_AUTH: '0', SNAKE_DEV_ADS: '0', SNAKE_RATE_LIMIT_PER_MIN: '10000' }, stdio: ['ignore', 'pipe', 'pipe'] });
  let output = ''; child.stdout.on('data', c => output += c); child.stderr.on('data', c => output += c);
  const url = `http://127.0.0.1:${port}`, deadline = Date.now() + 20000;
  let ready = false;
  while (Date.now() < deadline && !ready) {
    if (child.exitCode !== null) throw Error(`server exited: ${output}`);
    try { ready = (await fetch(`${url}/health`)).ok; } catch { /* starting */ }
    if (!ready) await new Promise(resolve => setTimeout(resolve, 100));
  }
  assert.ok(ready, `server failed to start: ${output}`);
  return { child, url, database };
}
async function stopServer(child) {
  if (child && child.exitCode === null && child.signalCode === null)
    await new Promise(resolve => { child.once('exit', resolve); child.kill('SIGTERM'); });
}

// Read SQLite directly so verification itself does not cause the usual account
// migration, day reset, or revision bump of the bootstrap endpoint.
function profile(database, id) {
  return JSON.parse(execFileSync('python3', ['-c',
    'import sqlite3,sys; print(sqlite3.connect(sys.argv[1]).execute("SELECT payload FROM players WHERE id=?", (sys.argv[2],)).fetchone()[0])', database, id], { encoding: 'utf8' }));
}
function rowCounts(database) {
  return JSON.parse(execFileSync('python3', ['-c',
    'import sqlite3,sys,json; c=sqlite3.connect(sys.argv[1]); print(json.dumps([c.execute("SELECT COUNT(*) FROM rewards").fetchone()[0], c.execute("SELECT COUNT(*) FROM runs WHERE mode=\'daily\' AND completed=1").fetchone()[0]]))', database], { encoding: 'utf8' }));
}

test('v13 local verifier binds snapshot and signed time, enforces complete recording, and leaves progress unchanged', async () => {
  const temp = fs.mkdtempSync(path.join(os.tmpdir(), 'snake-local-strength-eval-'));
  const processes = [];
  try {
    const playerId = 'local-strength-player', otherId = 'local-strength-other';
    const date = new Date(Date.now() + 8 * 3600000).toISOString().slice(0, 10);
    const snapshot = { version: 'local-strength-eval-setup-1', firstRun: true };
    const base = { playerId, mode: 'strength-local-eval', date, rules: 'strength-local-advantage-13', seed: 73,
      startedAt: Date.now() - 120000, expiresAt: Date.now() + 300000, completed: false, localStrengthStartConfig: snapshot };
    const valid = { ...base, id: 'valid' }, normal = { ...base, id: 'normal', localStrengthStartConfig: { ...snapshot, firstRun: false } };
    const fast = { ...base, id: 'fast', startedAt: Date.now() + 120000 };
    const unknown = { ...base, id: 'unknown', rules: 'strength-local-advantage-12' };
    const missing = { ...base, id: 'missing', localStrengthStartConfig: null };
    const missingVersion = { ...base, id: 'missing-version', localStrengthStartConfig: { firstRun: true } };
    const missingFirst = { ...base, id: 'missing-first', localStrengthStartConfig: { version: snapshot.version } };
    const badVersion = { ...base, id: 'bad-version', localStrengthStartConfig: { ...snapshot, version: 'unknown' } };
    const expired = { ...base, id: 'expired', expiresAt: Date.now() - 1000 };
    const receipt = { ...base, id: 'receipt', completed: true, expiresAt: Date.now() - 1000, score: 100,
      fixedWinner: 'you', fixedKills: 1, fixedLength: 20, strengthStateDigest: 's1-abcdef' };
    const legacy = { ...base, id: 'legacy', mode: 'strength-eval', rules: 'strength-pve-dev-4' };
    const tamperedFirst = { ...base, id: 'tampered-first', localStrengthStartConfig: { ...snapshot, firstRun: false } };
    const tamperedTime = { ...base, id: 'tampered-time', startedAt: base.startedAt - 1000 };
    const runs = Object.fromEntries([valid, normal, fast, unknown, missing, missingVersion, missingFirst, badVersion, expired, receipt, legacy, tamperedFirst, tamperedTime].map(run => [run.id, run]));
    const fixture = path.join(temp, 'fixture.json');
    fs.writeFileSync(fixture, JSON.stringify({ players: {
      [playerId]: { playerId, dailyKey: '2000-01-01', coins: 73, experience: 29, tutorialComplete: true, completedMatches: 0, dailyMatches: 3, revision: 37 },
      [otherId]: { playerId: otherId, dailyKey: date, tutorialComplete: false, completedMatches: 0 }
    }, runs }));
    const disabled = await startServer(temp, fixture, false); processes.push(disabled.child);
    assert.equal((await fetch(`${disabled.url}/api/v3/internal/local-strength-pve/start`, { method: 'POST' })).status, 404);
    assert.equal((await fetch(`${disabled.url}/api/v3/internal/local-strength-pve/verify`, { method: 'POST' })).status, 404);
    await stopServer(disabled.child);
    const server = await startServer(temp, fixture, true); processes.push(server.child);
    const before = profile(server.database, playerId);
    const beforeCounts = rowCounts(server.database);
    const firstTape = replay(73, true), normalTape = replay(73, false);
    assert.notEqual(firstTape.ticks, normalTape.ticks, 'fixture must distinguish server first-run state');
    const session = sign(`${playerId}:${Math.floor(Date.now() / 1000) + 3600}`);
    async function request(route, body, bearer = session) {
      const response = await fetch(`${server.url}/api/v3/${route}`, { method: 'POST',
        headers: { Authorization: `Bearer ${bearer}`, 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
      return { status: response.status, data: await response.json() };
    }
    const payload = (run, inputs = firstTape.inputs) => ({ runId: run.id, token: tokenFor(run), inputs });
    const verify = (run, inputs) => request('internal/local-strength-pve/verify', payload(run, inputs));
    assert.equal((await verify(valid, '!invalid!')).status, 400);
    assert.equal((await verify(valid, 'AA==')).status, 400);
    assert.equal((await verify(valid, 'A'.repeat(5000))).status, 400);
    assert.equal((await request('internal/local-strength-pve/verify', { ...payload(valid), token: 'invalid' })).status, 400);
    for (const altered of [ { ...valid, seed: valid.seed + 1 }, { ...valid, startedAt: valid.startedAt - 1000 },
      { ...valid, localStrengthStartConfig: { ...snapshot, firstRun: false } }, { ...valid, localStrengthStartConfig: { ...snapshot, version: 'tampered' } } ])
      assert.equal((await request('internal/local-strength-pve/verify', { ...payload(valid), token: tokenFor(altered) })).status, 400);
    for (const run of [fast, unknown, missing, missingVersion, missingFirst, badVersion, expired, legacy])
      assert.equal((await verify(run)).status, 400, run.id);
    assert.equal((await request('internal/local-strength-pve/verify', { ...payload(tamperedFirst), token: tokenFor({ ...tamperedFirst, localStrengthStartConfig: snapshot }) })).status, 400, 'persisted first-run tampering invalidates the original token');
    assert.equal((await request('internal/local-strength-pve/verify', { ...payload(tamperedTime), token: tokenFor({ ...tamperedTime, startedAt: base.startedAt }) })).status, 400, 'persisted start-time tampering invalidates the original token');
    const otherSession = sign(`${otherId}:${Math.floor(Date.now() / 1000) + 3600}`);
    assert.equal((await request('internal/local-strength-pve/verify', payload(valid), otherSession)).status, 400);
    const unsteered = Buffer.from(firstTape.inputs, 'base64'); unsteered[0] = 17;
    assert.equal((await verify(valid, unsteered.toString('base64'))).status, 400, 'unsteered heading must remain right');
    const invalidFlags = Buffer.from(firstTape.inputs, 'base64'); invalidFlags[1] = 4;
    assert.equal((await verify(valid, invalidFlags.toString('base64'))).status, 400);
    const resetSteering = Buffer.from(firstTape.inputs, 'base64'); resetSteering[9] = 0;
    assert.equal((await verify(valid, resetSteering.toString('base64'))).status, 400);
    const trailing = Buffer.concat([Buffer.from(firstTape.inputs, 'base64'), Buffer.from([0, 2])]);
    assert.equal((await verify(valid, trailing.toString('base64'))).status, 400);
    const [done, parallel] = await Promise.all([verify(valid), verify(valid)]);
    assert.equal(done.status, 200); assert.deepEqual(parallel.data, done.data);
    assert.deepEqual(done.data, { ok: true, verified: true, experimental: true, rules: base.rules,
      ticks: firstTape.ticks, winner: firstTape.winner, kills: firstTape.kills, length: firstTape.length,
      stateDigest: firstTape.stateDigest, rewards: 0 });
    assert.deepEqual((await verify(valid, 'bad')).data, done.data, 'idempotent receipt must not depend on resubmitted tape');
    const normalDone = await verify(normal, normalTape.inputs);
    assert.equal(normalDone.status, 200); assert.equal(normalDone.data.stateDigest, normalTape.stateDigest);
    assert.deepEqual((await verify(receipt, 'bad')).data, { ok: true, verified: true, experimental: true, rules: base.rules,
      ticks: 100, winner: 'you', kills: 1, length: 20, stateDigest: 's1-abcdef', rewards: 0 });
    const created = await request('internal/local-strength-pve/start', {});
    assert.equal(created.status, 200); assert.equal(created.data.mode, base.mode); assert.equal(created.data.rules, base.rules);
    assert.equal(created.data.localStrengthStartConfig.version, snapshot.version);
    assert.equal(created.data.localStrengthStartConfig.firstRun, false, 'server determines snapshot from account');
    assert.equal(created.data.localStrengthStartConfig.firstRunCode, 2);
    const createdFirst = await request('internal/local-strength-pve/start', {}, otherSession);
    assert.equal(createdFirst.status, 200); assert.equal(createdFirst.data.localStrengthStartConfig.firstRun, true);
    assert.equal(createdFirst.data.localStrengthStartConfig.firstRunCode, 1);
    const after = profile(server.database, playerId);
    assert.deepEqual(after, before, 'experiment changed profile including daily counters, completion or revision');
    assert.deepEqual(rowCounts(server.database), beforeCounts, 'experiment created reward or leaderboard rows');
    // These are ordinary endpoints: their existing account migrations and
    // revision maintenance are deliberately outside the comparison above.
    assert.equal((await request('match/finish', { runId: created.data.runId, token: created.data.token, seconds: 3, actions: 1 })).status, 400);
    assert.equal((await request('internal/strength-pve/verify', payload(valid))).status, 400);
  } finally {
    for (const process of processes) await stopServer(process);
    fs.rmSync(temp, { recursive: true, force: true });
  }
});
