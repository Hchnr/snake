const { test, before, after } = require('node:test');
const assert = require('node:assert/strict');
const { spawn, spawnSync } = require('node:child_process');
const { createHash } = require('node:crypto');
const fs = require('node:fs');
const net = require('node:net');
const os = require('node:os');
const path = require('node:path');

const root = path.join(__dirname, '../..');
const serverDll = path.join(root, 'server/bin/Debug/net10.0/Snake.Server.dll');
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
const idFor = device => createHash('sha256').update(`dev:${device}`).digest('hex').slice(0, 24);
const day = () => new Date(Date.now() + 8 * 3600000).toISOString().slice(0, 10);
let temp, legacy, database, servers = [], user, endpoints = [];

async function freePort() {
  return new Promise((resolve, reject) => {
    const socket = net.createServer(); socket.once('error', reject);
    socket.listen(0, '127.0.0.1', () => { const port = socket.address().port; socket.close(() => resolve(port)); });
  });
}

async function start(port, limit = '10000', databaseFile = database, legacyFile = legacy) {
  const child = spawn('dotnet', [serverDll], { cwd: root, env: {
    ...process.env, SNAKE_ENV: 'development', SNAKE_HOST: '127.0.0.1', SNAKE_PORT: String(port), SNAKE_DEV_AUTH: '1', SNAKE_DEV_ADS: '0',
    SNAKE_DATA_FILE: legacyFile, SNAKE_SQLITE_FILE: databaseFile, SNAKE_SESSION_KEY: 'sqlite-isolated-test-key', SNAKE_RATE_LIMIT_PER_MIN: limit, DOTNET_CLI_TELEMETRY_OPTOUT: '1'
  }, stdio: ['ignore', 'pipe', 'pipe'] });
  let output = '';
  child.stdout.on('data', chunk => { output += chunk; });
  child.stderr.on('data', chunk => { output += chunk; });
  const endpoint = `http://127.0.0.1:${port}`;
  const deadline = Date.now() + 15000;
  while (Date.now() < deadline) {
    if (child.exitCode !== null) throw new Error(`server exited: ${output}`);
    try { if ((await fetch(`${endpoint}/health`)).ok) return { child, endpoint }; } catch { /* starting */ }
    await sleep(100);
  }
  child.kill('SIGTERM');
  throw new Error(`server did not start: ${output}`);
}

async function stop(server) {
  if (!server || server.child.exitCode !== null || server.child.signalCode !== null) return;
  await new Promise(resolve => { server.child.once('exit', resolve); server.child.kill('SIGTERM'); });
}

async function request(endpoint, route, token, body) {
  const reply = await fetch(`${endpoint}/api/v3/${route}`, {
    method: body === undefined ? 'GET' : 'POST',
    headers: { ...(token ? { Authorization: `Bearer ${token}` } : {}), ...(body === undefined ? {} : { 'Content-Type': 'application/json' }) },
    ...(body === undefined ? {} : { body: JSON.stringify(body) })
  });
  return { status: reply.status, data: await reply.json() };
}

before(async () => {
  temp = fs.mkdtempSync(path.join(os.tmpdir(), 'snake-sqlite-test-'));
  legacy = path.join(temp, 'legacy.json'); database = path.join(temp, 'game.sqlite');
  const playerId = idFor('sqlite-player'), otherId = idFor('sqlite-other');
  const base = { mode: 'daily', date: day(), completed: true, expiresAt: Date.now() - 1, finishedAt: Date.now() - 10000 };
  fs.writeFileSync(legacy, JSON.stringify({
    players: {
      [playerId]: { playerId, displayName: '玩家甲', tutorialComplete: true, experience: 200, coins: 100, ownedSkins: [0, 2], selectedSkin: 2, dailyKey: day() },
      [otherId]: { playerId: otherId, displayName: '玩家乙', tutorialComplete: true }
    },
    runs: {
      old: { ...base, id: 'old', playerId, rules: 'daily-1', score: 900 },
      first: { ...base, id: 'first', playerId: otherId, rules: 'daily-2', score: 120 },
      second: { ...base, id: 'second', playerId, rules: 'daily-2', score: 80 }
    }
  }));
  const a = await freePort(); let b = await freePort(); while (b === a) b = await freePort();
  servers = [await start(a), await start(b)]; endpoints = servers.map(s => s.endpoint);
  const login = await request(endpoints[0], 'auth/dev', null, { deviceId: 'sqlite-player' });
  assert.equal(login.status, 200); user = login.data;
});

after(async () => {
  await Promise.all(servers.map(stop));
  if (temp) fs.rmSync(temp, { recursive: true, force: true });
});

test('legacy JSON imports once and a versioned leaderboard reads the shared database', async () => {
  const profile = await request(endpoints[1], 'bootstrap', user.session);
  assert.equal(profile.status, 200); assert.equal(profile.data.profile.coins, 100);
  assert.deepEqual(profile.data.profile.ownedSkins, [0, 2]);
  const board = await request(endpoints[1], 'challenge/leaderboard?rules=daily-2', user.session);
  assert.equal(board.data.total, 2); assert.equal(board.data.rank, 2); assert.equal(board.data.gap, 40);
  assert.deepEqual(board.data.rows.map(row => row.score), [120, 80]);
  const oldBoard = await request(endpoints[1], 'challenge/leaderboard', user.session);
  assert.equal(oldBoard.data.total, 1); assert.equal(oldBoard.data.rows[0].score, 900);
});

test('concurrent servers settle each run exactly once and preserve both updates', async () => {
  const first = (await request(endpoints[0], 'match/start', user.session, {})).data;
  const second = (await request(endpoints[1], 'match/start', user.session, {})).data;
  await sleep(1300);
  const payload = run => ({ runId: run.runId, token: run.token, seconds: 3, actions: 3 });
  const results = await Promise.all(Array.from({ length: 12 }, (_, i) =>
    request(endpoints[i % 2], 'match/finish', user.session, payload(i % 3 === 0 ? second : first))));
  assert.ok(results.every(result => result.status === 200));
  assert.equal(new Set(results.map((result, i) => result.data.unlockedSkin === 1 ? (i % 3 === 0 ? second.runId : first.runId) : null).filter(Boolean)).size, 1);
  const profile = (await request(endpoints[0], 'bootstrap', user.session)).data.profile;
  assert.equal(profile.coins, 210); assert.equal(profile.experience, 260); assert.equal(profile.completedMatches, 3);
  assert.deepEqual(profile.ownedSkins, [0, 2, 1]);
});

test('online backup restores persisted accounts and rankings without touching the live database', async () => {
  const snapshot = path.join(temp, 'verified-backup.sqlite');
  const script = path.join(root, 'scripts/backup-sqlite.py');
  assert.ok(fs.existsSync(database + '-wal') && fs.statSync(database + '-wal').size > 0, 'test requires a live WAL');
  const backup = spawnSync('python3', [script, database, snapshot], { encoding: 'utf8' });
  assert.equal(backup.status, 0, backup.stderr);
  assert.equal(fs.statSync(snapshot).mode & 0o077, 0, 'backup is readable by other users');
  const before = fs.readFileSync(snapshot);
  const refused = spawnSync('python3', [script, database, snapshot], { encoding: 'utf8' });
  assert.notEqual(refused.status, 0); assert.deepEqual(fs.readFileSync(snapshot), before);
  const invalid = path.join(temp, 'invalid.sqlite'); fs.writeFileSync(invalid, 'not a SQLite database');
  const failed = path.join(temp, 'failed-backup.sqlite');
  assert.notEqual(spawnSync('python3', [script, invalid, failed]).status, 0);
  assert.equal(fs.existsSync(failed), false);
  assert.deepEqual(fs.readdirSync(temp).filter(name => name.startsWith('.snake-backup-')), []);

  // Start an independent server from the snapshot while the original remains live.
  const restored = await start(await freePort(), '10000', snapshot, path.join(temp, 'no-import.json'));
  try {
    const profile = (await request(restored.endpoint, 'bootstrap', user.session)).data.profile;
    assert.equal(profile.coins, 210); assert.equal(profile.experience, 260);
    assert.deepEqual(profile.ownedSkins, [0, 2, 1]);
    const board = await request(restored.endpoint, 'challenge/leaderboard?rules=daily-2', user.session);
    assert.equal(board.data.total, 2); assert.equal(board.data.rank, 2); assert.equal(board.data.gap, 40);
    const run = (await request(restored.endpoint, 'match/start', user.session, {})).data;
    await sleep(1300);
    const payload = { runId: run.runId, token: run.token, seconds: 3, actions: 3 };
    const first = await request(restored.endpoint, 'match/finish', user.session, payload);
    const duplicate = await request(restored.endpoint, 'match/finish', user.session, payload);
    assert.equal(first.status, 200); assert.equal(duplicate.status, 200);
    assert.equal((await request(restored.endpoint, 'bootstrap', user.session)).data.profile.coins, 245);
    assert.equal((await request(endpoints[0], 'bootstrap', user.session)).data.profile.coins, 210);
  } finally { await stop(restored); }
});

test('database survives restart without importing the old file again', async () => {
  fs.writeFileSync(legacy, JSON.stringify({ players: {} }));
  const port = Number(new URL(endpoints[1]).port);
  await stop(servers[1]); servers[1] = await start(port);
  const profile = (await request(servers[1].endpoint, 'bootstrap', user.session)).data.profile;
  assert.equal(profile.coins, 210); assert.equal(profile.experience, 260);
  const board = await request(servers[1].endpoint, 'challenge/leaderboard?rules=daily-2', user.session);
  assert.equal(board.data.total, 2); assert.equal(board.data.rank, 2);
});

test('rate limiting returns a usable error while health remains available', async () => {
  const isolated = await start(await freePort(), '3');
  try {
    for (let i = 0; i < 3; i++) assert.equal((await request(isolated.endpoint, 'bootstrap', user.session)).status, 200);
    const limited = await request(isolated.endpoint, 'bootstrap', user.session);
    assert.equal(limited.status, 429); assert.equal(limited.data.ok, false); assert.match(limited.data.error, /频繁/);
    assert.equal((await fetch(`${isolated.endpoint}/health`)).status, 200);
  } finally { await stop(isolated); }
});
