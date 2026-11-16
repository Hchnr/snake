const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

test('WebGL bridge forwards socket lifecycle and payloads to Unity', () => {
  const messages = [];
  const module = {};
  class Socket {
    constructor(url) { this.url = url; this.readyState = 0; }
    send(value) { this.sent = value; }
    close() { this.closed = true; }
  }
  const library = {};
  const source = fs.readFileSync(path.join(__dirname, '../../unity/Assets/Plugins/WebGL/SnakeSocket.jslib'), 'utf8');
  vm.runInNewContext(source, {
    LibraryManager: { library }, mergeInto: (target, value) => Object.assign(target, value),
    UTF8ToString: value => value, Module: module, WebSocket: Socket,
    SendMessage: (object, method, data) => messages.push({ object, method, data: JSON.parse(data) })
  });
  library.SnakeSocketOpen('Snake Club', 'ws://localhost:2567/ws');
  const socket = module.snakeRoomSocket;
  assert.equal(socket.url, 'ws://localhost:2567/ws');
  socket.readyState = 1;
  socket.onopen();
  socket.onmessage({ data: '{"type":"state"}' });
  library.SnakeSocketSend('{"type":"input"}');
  assert.equal(socket.sent, '{"type":"input"}');
  assert.deepEqual(messages.map(m => m.data), [
    { type: 'open', data: '' },
    { type: 'message', data: '{"type":"state"}' }
  ]);
  assert.ok(messages.every(m => m.object === 'Snake Club' && m.method === 'OnSocketEvent'));
  library.SnakeSocketClose();
  assert.equal(socket.closed, true);
  assert.equal(socket.onmessage, null);
  assert.equal(module.snakeRoomSocket, null);
});
