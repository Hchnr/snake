const { test, before, after } = require('node:test');
const assert = require('node:assert/strict');
const { spawn, execFileSync } = require('node:child_process');
const { createHash } = require('node:crypto');
const fs = require('node:fs');
const net = require('node:net');
const os = require('node:os');
const path = require('node:path');

const root = path.join(__dirname, '../..');
const serverDll = path.join(root, 'server/bin/Debug/net10.0/Snake.Server.dll');
const coreDll = path.join(root, 'test/v3/bin/Debug/net10.0/Snake.V3.Core.Tests.dll');
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
const idFor = device => createHash('sha256').update(`dev:${device}`).digest('hex').slice(0, 24);
const chinaDay = () => new Date(Date.now() + 8 * 3600000).toISOString().slice(0, 10);
let temp, dataFile, port, base, server, output = '', alice, bob, challengeA, challengeB, recording;

async function startServer() {
  server = spawn('dotnet', [serverDll], {
    cwd: root,
    env: { ...process.env, SNAKE_HOST: '127.0.0.1', SNAKE_PORT: String(port), SNAKE_DEV_AUTH: '1', SNAKE_DEV_ADS: '0', SNAKE_DATA_FILE: dataFile, SNAKE_SESSION_KEY: 'v3-isolated-test-key', DOTNET_CLI_TELEMETRY_OPTOUT: '1' },
    stdio: ['ignore', 'pipe', 'pipe']
  });
  server.stdout.on('data', chunk => { output += chunk; });
  server.stderr.on('data', chunk => { output += chunk; });
  const deadline = Date.now() + 15000;
  while (Date.now() < deadline) {
    if (server.exitCode !== null) throw new Error(`server exited: ${output}`);
    try { if ((await fetch(`${base}/health`)).ok) return; } catch { /* starting */ }
    await sleep(100);
  }
  throw new Error(`server failed to start: ${output}`);
}
async function stopServer() {
  if (!server || server.exitCode !== null || server.signalCode !== null) return;
  await new Promise(resolve => { server.once('exit', resolve); server.kill('SIGTERM'); });
}
async function request(route, { token, body, method = body === undefined ? 'GET' : 'POST' } = {}) {
  const response = await fetch(`${base}/api/v3/${route}`, {
    method,
    headers: { ...(body === undefined ? {} : { 'Content-Type': 'application/json' }), ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    ...(body === undefined ? {} : { body: JSON.stringify(body) })
  });
  return { status: response.status, data: await response.json() };
}
async function login(device) {
  const reply = await request('auth/dev', { body: { deviceId: device } });
  assert.equal(reply.status, 200); assert.equal(reply.data.ok, true); return reply.data;
}
function replay(seed, variant = 0) {
  return JSON.parse(execFileSync('dotnet', [coreDll, 'replay', String(seed), String(variant)], { encoding: 'utf8' }));
}

before(async () => {
  temp = fs.mkdtempSync(path.join(os.tmpdir(), 'snake-v3-test-'));
  dataFile = path.join(temp, 'progress.json');
  const caveId = idFor('v3-cave');
  fs.writeFileSync(dataFile, JSON.stringify({
    players: { [caveId]: { playerId: caveId, displayName: '洞穴测试', dailyKey: chinaDay(), expedition: { id: 'expedition-fixture', cave: 2, durationMinutes: 480, startedAt: Date.now() - 12 * 3600000, bonusPercent: 7 } } }
  }));
  port = await new Promise((resolve, reject) => {
    const listener = net.createServer(); listener.once('error', reject);
    listener.listen(0, '127.0.0.1', () => { const value = listener.address().port; listener.close(() => resolve(value)); });
  });
  base = `http://127.0.0.1:${port}`;
  await startServer();
  alice = await login('v3-alice'); bob = await login('v3-bob');
  challengeA = (await request('challenge/start', { token: alice.session, body: {} })).data;
  challengeB = (await request('challenge/start', { token: bob.session, body: {} })).data;
  assert.equal(challengeA.seed, challengeB.seed);
  recording = replay(challengeA.seed);
});

after(async () => {
  await stopServer();
  if (temp) fs.rmSync(temp, { recursive: true, force: true });
});

test('authentication, skin inventory and talent points use server authority', async () => {
  assert.equal((await request('bootstrap')).status, 401);
  const bootstrap = await request('bootstrap', { token: alice.session });
  assert.equal(bootstrap.status, 200); assert.equal(bootstrap.data.profile.coins, 0);
  assert.equal(bootstrap.data.adEnabled, false);
  assert.equal((await request('skin/buy', { token: alice.session, body: { index: 1 } })).status, 400);
  assert.equal((await request('skin/equip', { token: alice.session, body: { index: 1 } })).status, 400);
  assert.equal((await request('talent/allocate', { token: alice.session, body: { index: 0 } })).status, 400);
});

test('PvE settlement is bound to the player and pays once, including daily objective', async () => {
  const runA = (await request('match/start', { token: alice.session, body: {} })).data;
  const runB = (await request('match/start', { token: alice.session, body: {} })).data;
  const finish = run => ({ runId: run.runId, token: run.token, seconds: 3, actions: 5 });
  assert.equal((await request('match/finish', { token: bob.session, body: finish(runA) })).status, 400);
  assert.equal((await request('match/finish', { token: alice.session, body: { ...finish(runA), token: runA.token + 'x' } })).status, 400);
  await sleep(1300);
  assert.equal((await request('match/finish', { token: alice.session, body: { ...finish(runA), actions: 0 } })).status, 400);
  const replies = await Promise.all(Array.from({ length: 3 }, () => request('match/finish', { token: alice.session, body: finish(runA) })));
  for (const reply of replies) {
    assert.equal(reply.status, 200); assert.equal(reply.data.coins, 35); assert.equal(reply.data.experience, 20);
    assert.equal(reply.data.profile.coins, 35); assert.equal(reply.data.profile.experience, 20);
  }
  const second = await request('match/finish', { token: alice.session, body: finish(runB) });
  assert.equal(second.status, 200); assert.equal(second.data.questCoins, 40); assert.equal(second.data.questExperience, 20);
  assert.equal(second.data.profile.coins, 110); assert.equal(second.data.profile.experience, 60);
  const allocated = await request('talent/allocate', { token: alice.session, body: { index: 0 } });
  assert.equal(allocated.status, 200); assert.equal(allocated.data.profile.talents[0], 1);
  assert.equal((await request('talent/allocate', { token: alice.session, body: { index: 0 } })).status, 400);
  const reset = await request('talent/reset', { token: alice.session, body: {} });
  assert.equal(reset.data.profile.talents[0], 0);
  const prepared = await request('ad/prepare', { token: alice.session, body: { sourceId: runA.runId, kind: 'match' } });
  assert.equal(prepared.status, 200);
  const disabled = await request('ad/complete', { token: alice.session, body: { id: prepared.data.id } });
  assert.equal(disabled.status, 503);
  assert.equal((await request('bootstrap', { token: alice.session })).data.profile.coins, 110);
});

test('offline expedition is capped at eight hours and its claim is idempotent', async () => {
  const cave = await login('v3-cave');
  const claim = () => request('expedition/claim', { token: cave.session, body: { id: 'expedition-fixture' } });
  const first = await claim();
  assert.equal(first.status, 200); assert.equal(first.data.baseCoins, 240); assert.equal(first.data.coins, 256);
  assert.equal(first.data.experience, 20); assert.equal(first.data.fragments, 1);
  assert.equal(first.data.profile.coins, 256); assert.equal(first.data.profile.caveFragments[2], 1);
  const second = await claim();
  assert.equal(second.data.profile.coins, 256); assert.equal(second.data.profile.experience, 20); assert.equal(second.data.profile.caveFragments[2], 1);
  const next = await request('expedition/start', { token: cave.session, body: { index: 0 } });
  assert.equal(next.status, 200);
  assert.equal((await request('expedition/start', { token: cave.session, body: { index: 1 } })).status, 400);
  const early = await request('expedition/claim', { token: cave.session, body: { id: next.data.profile.expedition.id } });
  assert.equal(early.data.coins, 0); assert.equal(early.data.experience, 0); assert.equal(early.data.fragments, 0);
});

test('daily replay rejects tampering, records real users once and ranks equal scores', async () => {
  const payload = run => ({ runId: run.runId, token: run.token, inputs: recording.inputs, score: recording.score, actions: 10 });
  assert.equal((await request('challenge/submit', { token: bob.session, body: payload(challengeA) })).status, 400);
  const malformed = Buffer.from(recording.inputs, 'base64'); malformed[1] = 2;
  const badInput = await request('challenge/submit', { token: alice.session, body: { ...payload(challengeA), inputs: malformed.toString('base64') } });
  assert.equal(badInput.status, 400); assert.match(badInput.data.error, /回放/);
  const badScore = await request('challenge/submit', { token: alice.session, body: { ...payload(challengeA), score: recording.score + 1 } });
  assert.equal(badScore.status, 400); assert.match(badScore.data.error, /成绩与回放/);
  const until = challengeA.serverTime + recording.ticks * 50 - 1700 - Date.now();
  if (until > 0) await sleep(until);
  const a = await request('challenge/submit', { token: alice.session, body: payload(challengeA) });
  assert.equal(a.status, 200); assert.equal(a.data.verified, true); assert.equal(a.data.score, recording.score);
  const duplicate = await request('challenge/submit', { token: alice.session, body: payload(challengeA) });
  assert.equal(duplicate.data.profile.coins, a.data.profile.coins);
  const b = await request('challenge/submit', { token: bob.session, body: payload(challengeB) });
  assert.equal(b.status, 200); assert.equal(b.data.score, recording.score);
  const boardA = await request('challenge/leaderboard', { token: alice.session });
  const boardB = await request('challenge/leaderboard', { token: bob.session });
  assert.equal(boardA.data.total, 2); assert.equal(boardA.data.rank, 1); assert.equal(boardB.data.rank, 2); assert.equal(boardB.data.gap, 0);
  assert.equal(boardA.data.rows.length, 2); assert.equal(boardA.data.rows[0].score, recording.score);
});

test('progress and signed session survive restart with a stable key', async () => {
  const before = await request('bootstrap', { token: alice.session });
  await stopServer(); await startServer();
  const after = await request('bootstrap', { token: alice.session });
  assert.equal(after.status, 200); assert.equal(after.data.profile.coins, before.data.profile.coins);
  assert.equal(after.data.profile.experience, before.data.profile.experience);
  const board = await request('challenge/leaderboard', { token: alice.session });
  assert.equal(board.data.total, 2); assert.equal(board.data.rank, 1);
});
