const { test, before, after } = require('node:test');
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
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
const idFor = device => createHash('sha256').update(`dev:${device}`).digest('hex').slice(0, 24);
const chinaDay = () => new Date(Date.now() + 8 * 3600000).toISOString().slice(0, 10);
let temp, dataFile, port, base, server, output = '', alice, bob, challengeA, challengeB, recording;

async function startServer() {
  const env = { ...process.env, SNAKE_ENV: 'development', SNAKE_HOST: '127.0.0.1', SNAKE_PORT: String(port), SNAKE_DEV_AUTH: '1', SNAKE_DEV_ADS: '0', SNAKE_DATA_FILE: dataFile, SNAKE_SESSION_KEY: 'v3-isolated-test-key', DOTNET_CLI_TELEMETRY_OPTOUT: '1' };
  delete env.SNAKE_SQLITE_FILE;
  delete env.SNAKE_RATE_LIMIT_PER_MIN;
  server = spawn('dotnet', [serverDll], {
    cwd: root,
    env,
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
function replay(seed, variant = 0, rules = 'daily-2') {
  return JSON.parse(execFileSync('dotnet', [coreDll, 'replay', String(seed), String(variant), rules], { encoding: 'utf8' }));
}

function legacyToken(run) {
  const payload = Buffer.from([run.id, run.playerId, run.mode, run.date, run.rules, run.seed, run.expiresAt].join(':')).toString('base64url');
  const key = createHash('sha256').update('v3-isolated-test-key').digest();
  return `${payload}.${createHmac('sha256', key).update(payload).digest('base64url')}`;
}

before(async () => {
  temp = fs.mkdtempSync(path.join(os.tmpdir(), 'snake-v3-test-'));
  dataFile = path.join(temp, 'progress.json');
  const caveId = idFor('v3-cave');
  const caveOvercapId = idFor('v3-cave-overcap');
  const legacyId = idFor('v3-legacy');
  const oldProfileId = idFor('v3-old-profile');
  const newId = idFor('v3-new-rules');
  const missingId = idFor('v3-missing-rules');
  const receiptId = idFor('v3-receipt');
  const pursuitId = idFor('v3-pursuit');
  const legacy = { id: 'legacy-run', playerId: legacyId, mode: 'daily', date: chinaDay(), rules: 'daily-1', seed: 1,
    startedAt: Date.now() - 120000, expiresAt: Date.now() + 300000 };
  const current = { id: 'capture-run', playerId: newId, mode: 'daily', date: chinaDay(), rules: 'daily-3', seed: 1,
    startedAt: Date.now() - 120000, expiresAt: Date.now() + 300000 };
  const missing = { id: 'missing-rules-run', playerId: missingId, mode: 'daily', date: chinaDay(), seed: 1,
    startedAt: Date.now() - 120000, expiresAt: Date.now() + 300000 };
  fs.writeFileSync(dataFile, JSON.stringify({
    players: {
      [pursuitId]: { playerId: pursuitId, displayName: '追逐测试', dailyKey: chinaDay() },
      [receiptId]: { playerId: receiptId, displayName: '回执测试', tutorialComplete: true, coins: 73, experience: 29, dailyKey: chinaDay() },
      [caveOvercapId]: { playerId: caveOvercapId, displayName: '旧探洞', dailyKey: chinaDay(), experience: 29, dailyExpeditionXp: 99, expedition: { id: 'overcap-cave', cave: 0, durationMinutes: 120, startedAt: Date.now() - 9 * 3600000, bonusPercent: 0 } },
      [caveId]: { playerId: caveId, displayName: '洞穴测试', dailyKey: chinaDay(), expedition: { id: 'expedition-fixture', cave: 2, durationMinutes: 480, startedAt: Date.now() - 12 * 3600000, bonusPercent: 7 } },
      [oldProfileId]: { playerId: oldProfileId, displayName: '老玩家', tutorialComplete: true, experience: 345, coins: 777, ownedSkins: [0, 2], selectedSkin: 2,
        talents: [1, 0, 0, 0, 0, 0, 0, 0, 0], dailyKey: chinaDay(), expedition: { id: 'old-cave', cave: 1, durationMinutes: 240, startedAt: Date.now() - 10000, bonusPercent: 2 } }
    },
    runs: { [legacy.id]: legacy, [current.id]: current, [missing.id]: missing,
      'pursuit-run': { id: 'pursuit-run', playerId: pursuitId, mode: 'daily', date: chinaDay(), rules: 'daily-4', seed: 1, startedAt: Date.now() - 120000, expiresAt: Date.now() + 300000 },
      'receipt-pve': { id: 'receipt-pve', playerId: receiptId, mode: 'pve', date: chinaDay(), rules: 'daily-3', seed: 1, startedAt: Date.now() - 120000, expiresAt: Date.now() - 60000, completed: true, coins: 35, experience: 20 },
      'receipt-daily': { id: 'receipt-daily', playerId: receiptId, mode: 'daily', date: '2000-01-01', rules: 'daily-3', seed: 1, startedAt: Date.now() - 120000, expiresAt: Date.now() - 60000, completed: true, coins: 35, experience: 20, score: 123 },
      'receipt-pending': { id: 'receipt-pending', playerId: receiptId, mode: 'pve', date: chinaDay(), rules: 'daily-3', seed: 1, startedAt: Date.now() - 120000, expiresAt: Date.now() - 60000, completed: false }
    }
  }));
  port = await new Promise((resolve, reject) => {
    const listener = net.createServer(); listener.once('error', reject);
    listener.listen(0, '127.0.0.1', () => { const value = listener.address().port; listener.close(() => resolve(value)); });
  });
  base = `http://127.0.0.1:${port}`;
  await startServer();
  alice = await login('v3-alice'); bob = await login('v3-bob');
  challengeA = (await request('challenge/start?rules=daily-2', { token: alice.session, body: {} })).data;
  challengeB = (await request('challenge/start?rules=daily-2', { token: bob.session, body: {} })).data;
  assert.equal(challengeA.seed, challengeB.seed);
  assert.equal(challengeA.rules, 'daily-2');
  recording = replay(challengeA.seed);
});

after(async () => {
  await stopServer();
  if (temp) fs.rmSync(temp, { recursive: true, force: true });
});

test('expired completed receipts recover without re-awarding, but expired unfinished runs remain invalid', async () => {
  const owner = await login('v3-receipt');
  const stored = JSON.parse(fs.readFileSync(dataFile, 'utf8')).runs;
  const payload = run => ({ runId: run.id, token: legacyToken(run), seconds: 3, actions: 3, score: 123, inputs: '' });
  const pve = await request('match/finish', { token: owner.session, body: payload(stored['receipt-pve']) });
  const daily = await request('challenge/submit', { token: owner.session, body: payload(stored['receipt-daily']) });
  assert.equal(pve.status, 200); assert.equal(pve.data.coins, 35);
  assert.equal(daily.status, 200); assert.equal(daily.data.score, 123); assert.equal(daily.data.verified, true); assert.equal(daily.data.shareToken, '');
  assert.equal((await request('match/finish', { token: owner.session, body: payload(stored['receipt-pending']) })).status, 400);
  assert.equal((await request('match/finish', { token: bob.session, body: payload(stored['receipt-pve']) })).status, 400);
  assert.equal((await request('match/finish', { token: owner.session, body: { ...payload(stored['receipt-pve']), token: 'invalid' } })).status, 400);
  const again = await request('match/finish', { token: owner.session, body: payload(stored['receipt-pve']) });
  assert.equal(again.data.profile.coins, 73); assert.equal(again.data.profile.experience, 29);
});

test('authentication, skin inventory and talent points use server authority', async () => {
  assert.equal((await request('bootstrap')).status, 401);
  const bootstrap = await request('bootstrap', { token: alice.session });
  assert.equal(bootstrap.status, 200); assert.equal(bootstrap.data.profile.coins, 0);
  assert.equal(bootstrap.data.adEnabled, false);
  assert.equal((await request('skin/buy', { token: alice.session, body: { index: 1 } })).status, 400);
  assert.equal((await request('skin/buy', { token: alice.session, body: { index: 4 } })).status, 400);
  assert.equal((await request('skin/equip', { token: alice.session, body: { index: 1 } })).status, 400);
  assert.equal((await request('talent/allocate', { token: alice.session, body: { index: 0 } })).status, 400);
});

test('old signed daily replay remains valid without entering the new leaderboard', async () => {
  const player = await login('v3-legacy');
  const oldClientRun = await request('challenge/start', { token: player.session, body: {} });
  assert.equal(oldClientRun.status, 200); assert.equal(oldClientRun.data.rules, 'daily-1');
  assert.equal((await request('challenge/start?rules=unknown', { token: player.session, body: {} })).status, 400);
  const stored = JSON.parse(fs.readFileSync(dataFile, 'utf8')).runs['legacy-run'];
  const old = replay(stored.seed, 0, stored.rules);
  assert.equal(old.score, 90);
  const result = await request('challenge/submit', { token: player.session, body: {
    runId: stored.id, token: legacyToken(stored), inputs: old.inputs, score: old.score, actions: 10
  }});
  assert.equal(result.status, 200); assert.equal(result.data.verified, true);
  const board = await request('challenge/leaderboard?rules=daily-2', { token: player.session });
  assert.equal(board.data.total, 0); assert.equal(board.data.rank, 0);
  const oldBoard = await request('challenge/leaderboard', { token: player.session });
  assert.equal(oldBoard.data.total, 1); assert.equal(oldBoard.data.rank, 1); assert.equal(oldBoard.data.rows[0].score, 90);
  assert.equal((await request('challenge/leaderboard?rules=unknown', { token: player.session })).status, 400);
});

test('daily-3 capture replay verifies and is isolated from older leaderboards', async () => {
  const player = await login('v3-new-rules');
  const stored = JSON.parse(fs.readFileSync(dataFile, 'utf8')).runs['capture-run'];
  const run = replay(stored.seed, 0, stored.rules);
  assert.ok(run.score <= 1460);
  const result = await request('challenge/submit', { token: player.session, body: {
    runId: stored.id, token: legacyToken(stored), inputs: run.inputs, score: run.score, actions: 10
  }});
  assert.equal(result.status, 200); assert.equal(result.data.verified, true);
  const share = await request('challenge/share', { token: player.session, body: { id: stored.id } });
  assert.equal(share.status, 200); assert.equal(share.data.score, run.score);
  assert.equal(result.data.shareToken, share.data.shareToken, 'verified result did not prefetch the share token');
  const duplicateResult = await request('challenge/submit', { token: player.session, body: {
    runId: stored.id, token: legacyToken(stored), inputs: run.inputs, score: run.score, actions: 10
  }});
  assert.equal(duplicateResult.status, 200); assert.equal(duplicateResult.data.shareToken, share.data.shareToken);
  const resolved = await request('challenge/shared?token=' + encodeURIComponent(share.data.shareToken), { token: bob.session });
  assert.equal(resolved.status, 200); assert.equal(resolved.data.score, run.score);
  assert.equal(resolved.data.rules, 'daily-3'); assert.equal(resolved.data.date, chinaDay());
  assert.equal((await request('challenge/share', { token: bob.session, body: { id: stored.id } })).status, 400);
  const unfinished = (await request('challenge/start?rules=daily-3', { token: player.session, body: {} })).data;
  assert.equal((await request('challenge/share', { token: player.session, body: { id: unfinished.runId } })).status, 400);
  const broken = (share.data.shareToken[0] === 'A' ? 'B' : 'A') + share.data.shareToken.slice(1);
  assert.equal((await request('challenge/shared?token=' + encodeURIComponent(broken), { token: bob.session })).status, 400);
  assert.equal((await request('challenge/shared?token=' + encodeURIComponent(player.session), { token: bob.session })).status, 400);
  assert.equal((await request('challenge/shared?token=' + encodeURIComponent(share.data.shareToken))).status, 401);
  const expiredPayload = Buffer.from('friend-v1:2000-01-01:daily-3:12').toString('base64url');
  const signingKey = createHash('sha256').update('v3-isolated-test-key').digest();
  const expiredToken = expiredPayload + '.' + createHmac('sha256', signingKey).update(expiredPayload).digest('base64url');
  assert.equal((await request('challenge/shared?token=' + expiredToken, { token: bob.session })).status, 400);
  for (const invalidPayload of [`friend-v1:${chinaDay()}:daily-2:12`, `friend-v1:${chinaDay()}:daily-3:99999`]) {
    const encoded = Buffer.from(invalidPayload).toString('base64url');
    const signed = encoded + '.' + createHmac('sha256', signingKey).update(encoded).digest('base64url');
    assert.equal((await request('challenge/shared?token=' + signed, { token: bob.session })).status, 400);
  }
  assert.equal((await request('challenge/shared?token=' + 'x'.repeat(257), { token: bob.session })).status, 400);
  const board = await request('challenge/leaderboard?rules=daily-3', { token: player.session });
  assert.equal(board.data.total, 1); assert.equal(board.data.rows[0].score, run.score);
  const previous = await request('challenge/leaderboard?rules=daily-2', { token: player.session });
  assert.equal(previous.data.total, 0);
});

test('daily-4 verifies, shares and keeps its leaderboard separate from daily-3', async () => {
  const player = await login('v3-pursuit');
  const stored = JSON.parse(fs.readFileSync(dataFile, 'utf8')).runs['pursuit-run'];
  const recording = replay(stored.seed, 0, stored.rules);
  const result = await request('challenge/submit', { token: player.session, body: {
    runId: stored.id, token: legacyToken(stored), inputs: recording.inputs, score: recording.score, actions: 10
  }});
  assert.equal(result.status, 200); assert.equal(result.data.verified, true);
  const shared = await request('challenge/shared?token=' + encodeURIComponent(result.data.shareToken), { token: bob.session });
  assert.equal(shared.status, 200); assert.equal(shared.data.rules, 'daily-4'); assert.equal(shared.data.score, recording.score);
  const current = await request('challenge/leaderboard?rules=daily-4', { token: player.session });
  const previous = await request('challenge/leaderboard?rules=daily-3', { token: player.session });
  assert.equal(current.data.total, 1); assert.equal(current.data.rows[0].score, recording.score);
  assert.equal(previous.data.rank, 0); assert.equal(previous.data.total, 1);
  const oldRun = JSON.parse(fs.readFileSync(dataFile, 'utf8')).runs['capture-run'];
  const oldShare = await request('challenge/share', { token: (await login('v3-new-rules')).session, body: { id: oldRun.id } });
  assert.equal(oldShare.status, 200); assert.equal((await request('challenge/shared?token=' + encodeURIComponent(oldShare.data.shareToken), { token: player.session })).data.rules, 'daily-3');
});

test('old account inventory, talent and active expedition survive progress migration', async () => {
  const user = await login('v3-old-profile');
  const first = (await request('bootstrap', { token: user.session })).data.profile;
  const second = (await request('bootstrap', { token: user.session })).data.profile;
  assert.equal(first.completedMatches, 1); assert.equal(second.completedMatches, 1);
  assert.equal(first.coins, 777); assert.equal(first.experience, 345);
  assert.deepEqual(first.ownedSkins, [0, 2]); assert.equal(first.selectedSkin, 2); assert.equal(first.talents[0], 1);
  assert.equal(first.expedition.id, 'old-cave'); assert.equal(first.expedition.bonusPercent, 2);
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
  assert.equal(second.data.unlockedSkin, 1); assert.deepEqual(second.data.profile.ownedSkins, [0, 1]);
  const secondAgain = await request('match/finish', { token: alice.session, body: finish(runB) });
  assert.equal(secondAgain.data.unlockedSkin, 1); assert.deepEqual(secondAgain.data.profile.ownedSkins, [0, 1]);
  const allocated = await request('talent/allocate', { token: alice.session, body: { index: 0 } });
  assert.equal(allocated.status, 200); assert.equal(allocated.data.profile.talents[0], 1);
  assert.equal((await request('talent/allocate', { token: alice.session, body: { index: 0 } })).status, 400);
  const reset = await request('talent/reset', { token: alice.session, body: {} });
  assert.equal(reset.data.profile.talents[0], 0);
  const recommended = await request('talent/recommend', { token: alice.session, body: {} });
  assert.equal(recommended.status, 200); assert.equal(recommended.data.profile.talents[0], 1);
  assert.equal((await request('talent/recommend', { token: alice.session, body: {} })).status, 400);
  assert.equal((await request('talent/reset', { token: alice.session, body: {} })).status, 200);
  const prepared = await request('ad/prepare', { token: alice.session, body: { sourceId: runA.runId, kind: 'match' } });
  assert.equal(prepared.status, 200);
  const disabled = await request('ad/complete', { token: alice.session, body: { id: prepared.data.id } });
  assert.equal(disabled.status, 503);
  assert.equal((await request('bootstrap', { token: alice.session })).data.profile.coins, 110);
});

test('over-cap legacy expedition data never subtracts experience', async () => {
  const player = await login('v3-cave-overcap');
  const claim = await request('expedition/claim', { token: player.session, body: { id: 'overcap-cave' } });
  assert.equal(claim.status, 200); assert.equal(claim.data.experience, 0);
  assert.equal(claim.data.profile.experience, 29); assert.equal(claim.data.profile.dailyExpeditionXp, 20);
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
  const boardA = await request('challenge/leaderboard?rules=daily-2', { token: alice.session });
  const boardB = await request('challenge/leaderboard?rules=daily-2', { token: bob.session });
  assert.equal(boardA.data.total, 2); assert.equal(boardA.data.rank, 1); assert.equal(boardB.data.rank, 2); assert.equal(boardB.data.gap, 0);
  assert.equal(boardA.data.rows.length, 2); assert.equal(boardA.data.rows[0].score, recording.score);
});

test('progress and signed session survive restart with a stable key', async () => {
  const before = await request('bootstrap', { token: alice.session });
  await stopServer(); await startServer();
  const after = await request('bootstrap', { token: alice.session });
  assert.equal(after.status, 200); assert.equal(after.data.profile.coins, before.data.profile.coins);
  assert.equal(after.data.profile.experience, before.data.profile.experience);
  const board = await request('challenge/leaderboard?rules=daily-2', { token: alice.session });
  assert.equal(board.data.total, 2); assert.equal(board.data.rank, 1);
});

test('an older stored run without a rules field keeps the previous default', async () => {
  const player = await login('v3-missing-rules');
  const stored = JSON.parse(fs.readFileSync(dataFile, 'utf8')).runs['missing-rules-run'];
  const previous = { ...stored, rules: 'daily-2' };
  const run = replay(previous.seed, 0, previous.rules);
  const result = await request('challenge/submit', { token: player.session, body: {
    runId: previous.id, token: legacyToken(previous), inputs: run.inputs, score: run.score, actions: 10
  }});
  assert.equal(result.status, 200); assert.equal(result.data.verified, true);
});
