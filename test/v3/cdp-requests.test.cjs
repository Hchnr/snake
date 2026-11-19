const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { spawn, spawnSync } = require('node:child_process');
const { CdpRequests } = require('../../scripts/cdp-requests.cjs');

test('CDP requests settle on response, timeout, closure and send failure', async () => {
  const sent = [];
  const requests = new CdpRequests({ send: value => sent.push(JSON.parse(value)) }, 20);
  const success = requests.call('Page.enable');
  assert.equal(sent[0].method, 'Page.enable');
  requests.receive({ id: sent[0].id, result: { ok: true } });
  assert.deepEqual(await success, { ok: true });
  const protocolError = requests.call('Bad.method');
  requests.receive({ id: sent[1].id, error: { message: 'bad request' } });
  await assert.rejects(protocolError, /bad request/);
  await assert.rejects(requests.call('Never.responds'), /timed out: Never.responds/);
  assert.equal(requests.pending.size, 0);
  const a = requests.call('Pending.a'), b = requests.call('Pending.b');
  requests.fail(Error('closed'));
  await Promise.all([assert.rejects(a, /closed/), assert.rejects(b, /closed/)]);
  assert.equal(requests.pending.size, 0);
  await assert.rejects(requests.call('After.close'), /closed/);
  const broken = new CdpRequests({ send() { throw Error('send failed'); } }, 20);
  await assert.rejects(broken.call('Broken'), /send failed/);
  assert.equal(broken.pending.size, 0);
});

test('failed Chrome spawn reports failure and cleans its temporary profile', () => {
  const temporary = fs.mkdtempSync(path.join(os.tmpdir(), 'snake-spawn-check-'));
  try {
    const probe = path.resolve(__dirname, '../../scripts/strength-touch-replay-probe.cjs');
    const result = spawnSync(process.execPath, [probe, path.join(temporary, 'missing-chrome'), 'http://127.0.0.1:1/', '60'],
      { env: { ...process.env, TMPDIR: temporary }, encoding: 'utf8', timeout: 10000 });
    assert.equal(result.status, 1);
    assert.match(result.stderr, /Chrome failed to start/);
    assert.deepEqual(fs.readdirSync(temporary), []);
  } finally { fs.rmSync(temporary, { recursive: true, force: true }); }
});

test('interrupted probe terminates Chrome and removes its profile', { skip: process.platform === 'win32' }, async () => {
  const temporary = fs.mkdtempSync(path.join(os.tmpdir(), 'snake-signal-check-'));
  try {
    const fakeChrome = path.join(temporary, 'chrome');
    fs.writeFileSync(fakeChrome, '#!/bin/sh\nexec sleep 60\n', { mode: 0o755 });
    const probe = path.resolve(__dirname, '../../scripts/strength-touch-replay-probe.cjs');
    const child = spawn(process.execPath, [probe, fakeChrome, 'http://127.0.0.1:1/', '60'],
      { env: { ...process.env, TMPDIR: temporary }, stdio: ['ignore', 'ignore', 'pipe'] });
    let stderr = ''; child.stderr.on('data', data => { stderr += data; });
    await new Promise(resolve => setTimeout(resolve, 300));
    child.kill('SIGTERM');
    const status = await new Promise((resolve, reject) => {
      const timer = setTimeout(() => { child.kill('SIGKILL'); reject(Error('probe did not exit')); }, 5000);
      child.once('close', code => { clearTimeout(timer); resolve(code); });
    });
    assert.notEqual(status, 0);
    assert.match(stderr, /Interrupted: SIGTERM/);
    assert.deepEqual(fs.readdirSync(temporary), ['chrome']);
  } finally { fs.rmSync(temporary, { recursive: true, force: true }); }
});

test('interrupted probe kills a Chrome process that ignores termination', { skip: process.platform === 'win32' }, async () => {
  const temporary = fs.mkdtempSync(path.join(os.tmpdir(), 'snake-stubborn-check-'));
  try {
    const fakeChrome = path.join(temporary, 'chrome');
    const ready = path.join(temporary, 'ready');
    fs.writeFileSync(fakeChrome, '#!/usr/bin/env python3\nimport os,signal,time\nsignal.signal(signal.SIGTERM, signal.SIG_IGN)\nopen(os.environ["SNAKE_FAKE_CHROME_READY"],"w").close()\ntime.sleep(60)\n', { mode: 0o755 });
    const probe = path.resolve(__dirname, '../../scripts/strength-touch-replay-probe.cjs');
    const child = spawn(process.execPath, [probe, fakeChrome, 'http://127.0.0.1:1/', '60'],
      { env: { ...process.env, TMPDIR: temporary, SNAKE_FAKE_CHROME_READY: ready }, stdio: ['ignore', 'ignore', 'pipe'] });
    child.stderr.resume();
    const deadline = Date.now() + 5000;
    while (!fs.existsSync(ready) && Date.now() < deadline) await new Promise(resolve => setTimeout(resolve, 25));
    assert.ok(fs.existsSync(ready), 'fake Chrome did not install its SIGTERM handler');
    const started = Date.now();
    child.kill('SIGTERM');
    const status = await new Promise((resolve, reject) => {
      const timer = setTimeout(() => { child.kill('SIGKILL'); reject(Error('probe did not exit')); }, 7000);
      child.once('close', code => { clearTimeout(timer); resolve(code); });
    });
    assert.notEqual(status, 0);
    assert.ok(Date.now() - started >= 2500, 'stubborn Chrome exited before the forced-kill timeout');
    assert.deepEqual(fs.readdirSync(temporary).sort(), ['chrome', 'ready']);
  } finally { fs.rmSync(temporary, { recursive: true, force: true }); }
});
