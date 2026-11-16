const { test, before, after } = require('node:test');
const assert = require('node:assert/strict');
const { spawn } = require('node:child_process');
const net = require('node:net');
const path = require('node:path');
const fs = require('node:fs');
const vm = require('node:vm');

let server;
let base;
let output = '';
const clients = new Set();

before(async () => {
  const port = await new Promise((resolve, reject) => {
    const listener = net.createServer();
    listener.once('error', reject);
    listener.listen(0, '127.0.0.1', () => {
      const port = listener.address().port;
      listener.close(() => resolve(port));
    });
  });
  base = `http://127.0.0.1:${port}`;
  server = spawn('dotnet', [path.join(__dirname, '../../server/bin/Debug/net10.0/Snake.Server.dll')], {
    env: { ...process.env, SNAKE_HOST: '127.0.0.1', SNAKE_PORT: String(port), DOTNET_CLI_TELEMETRY_OPTOUT: '1' },
    stdio: ['ignore', 'pipe', 'pipe']
  });
  server.stdout.on('data', chunk => { output += chunk; });
  server.stderr.on('data', chunk => { output += chunk; });
  const deadline = Date.now() + 15000;
  while (Date.now() < deadline) {
    if (server.exitCode !== null) throw new Error(`server exited: ${output}`);
    try { if ((await fetch(`${base}/health`)).ok) return; } catch { /* starting */ }
    await new Promise(resolve => setTimeout(resolve, 100));
  }
  throw new Error(`server failed to start: ${output}`);
});

after(() => {
  for (const client of clients) client.close();
  if (server && server.exitCode === null) server.kill('SIGTERM');
});

async function connect() {
  const socket = new WebSocket(`${base.replace('http:', 'ws:')}/ws`);
  clients.add(socket);
  const messages = [];
  const waiters = new Set();
  socket.addEventListener('message', event => {
    const msg = JSON.parse(event.data);
    for (const waiter of waiters) {
      if (!waiter.match(msg)) continue;
      waiters.delete(waiter);
      clearTimeout(waiter.timer);
      waiter.resolve(msg);
      return;
    }
    messages.push(msg);
    if (messages.length > 100) messages.shift();
  });
  await new Promise((resolve, reject) => {
    socket.addEventListener('open', resolve, { once: true });
    socket.addEventListener('error', reject, { once: true });
  });
  return {
    send: value => socket.send(JSON.stringify(value)),
    close: () => { clients.delete(socket); socket.close(); },
    wait: (match, timeout = 5000) => {
      const index = messages.findIndex(match);
      if (index >= 0) return Promise.resolve(messages.splice(index, 1)[0]);
      return new Promise((resolve, reject) => {
        const waiter = { match, resolve, timer: setTimeout(() => {
          waiters.delete(waiter);
          reject(new Error(`timed out waiting for message; received ${messages.map(m => m.type).join(', ')}`));
        }, timeout) };
        waiters.add(waiter);
      });
    }
  };
}

test('PvP room waits for a second real player and shares state', async () => {
  const a = await connect(); const b = await connect();
  try {
    a.send({ type: 'create', mode: 'pvp', name: 'Alice' });
    const joinedA = await a.wait(m => m.type === 'joined');
    assert.match(joinedA.room, /^[A-Z2-9]{5}$/);
    const waiting = await a.wait(m => m.type === 'state' && m.state.phase === 'waiting');
    assert.equal(waiting.state.snakes.length, 1);
    assert.equal(waiting.fruitsIncluded, true);
    assert.equal(waiting.state.fruits.length, 200);
    b.send({ type: 'join', mode: 'pvp', room: joinedA.room, name: 'Bob' });
    const joinedB = await b.wait(m => m.type === 'joined');
    assert.equal(joinedB.room, joinedA.room);
    assert.notEqual(joinedB.playerId, joinedA.playerId);
    const state = await a.wait(m => m.type === 'state' && m.state.phase === 'running');
    assert.equal(state.state.snakes.length, 2);
    assert.ok(state.state.snakes.every(s => !s.bot));
    const incremental = await b.wait(m => m.type === 'state' && m.state.phase === 'running' && !m.fruitsIncluded);
    assert.equal(incremental.state.fruits, undefined);
    const refresh = await b.wait(m => m.type === 'state' && m.fruitsIncluded);
    assert.equal(refresh.state.fruits.length, 200);
    a.send({ type: 'input', x: 0, y: 1, boost: true });
    const updated = await b.wait(m => m.type === 'state' && m.state.tick > refresh.state.tick + 4);
    assert.equal(updated.state.snakes.find(s => s.id === joinedA.playerId).boosting, true);
  } finally { a.close(); b.close(); }
});

test('mixed room includes distinct humans and AI, and rejects a wrong mode', async () => {
  const a = await connect(); const b = await connect(); const bad = await connect();
  try {
    a.send({ type: 'create', mode: 'mixed', name: 'Alice' });
    const joinedA = await a.wait(m => m.type === 'joined');
    bad.send({ type: 'join', mode: 'pvp', room: joinedA.room, name: 'Wrong' });
    assert.match((await bad.wait(m => m.type === 'error')).error, /模式/);
    b.send({ type: 'join', mode: 'mixed', room: joinedA.room, name: 'Bob' });
    const joinedB = await b.wait(m => m.type === 'joined');
    const state = await b.wait(m => m.type === 'state' && m.state.snakes.filter(s => !s.bot).length === 2);
    assert.equal(state.state.snakes.filter(s => s.bot).length, 7);
    assert.deepEqual(new Set(state.state.snakes.filter(s => !s.bot).map(s => s.id)), new Set([joinedA.playerId, joinedB.playerId]));
  } finally { a.close(); b.close(); bad.close(); }
});

test('the WebGL bridge can create a room on the real local server', async () => {
  const library = {};
  const module = {};
  let resolveOpen;
  let resolveJoined;
  const opened = new Promise(resolve => { resolveOpen = resolve; });
  const joined = new Promise(resolve => { resolveJoined = resolve; });
  const source = fs.readFileSync(path.join(__dirname, '../../unity/Assets/Plugins/WebGL/SnakeSocket.jslib'), 'utf8');
  vm.runInNewContext(source, {
    LibraryManager: { library }, mergeInto: (target, value) => Object.assign(target, value),
    UTF8ToString: value => value, Module: module, WebSocket,
    SendMessage: (object, method, raw) => {
      assert.equal(object, 'Snake Club');
      assert.equal(method, 'OnSocketEvent');
      const event = JSON.parse(raw);
      if (event.type === 'open') resolveOpen();
      if (event.type === 'message') {
        const message = JSON.parse(event.data);
        if (message.type === 'joined') resolveJoined(message);
      }
    }
  });
  async function withTimeout(promise) {
    let timer;
    try {
      return await Promise.race([promise, new Promise((_, reject) => {
        timer = setTimeout(() => reject(new Error('WebGL bridge timed out')), 5000);
      })]);
    } finally { clearTimeout(timer); }
  }
  try {
    library.SnakeSocketOpen('Snake Club', `${base.replace('http:', 'ws:')}/ws`);
    await withTimeout(opened);
    library.SnakeSocketSend(JSON.stringify({ type: 'create', mode: 'mixed', name: 'Bridge' }));
    const result = await withTimeout(joined);
    assert.match(result.room, /^[A-Z2-9]{5}$/);
    assert.ok(result.playerId);
  } finally { library.SnakeSocketClose(); }
});
