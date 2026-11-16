const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(process.env.SNAKE_PREVIEW_ROOT || path.join(__dirname, '../dist/unity-webgl'));
const port = Number(process.env.SNAKE_PREVIEW_PORT || 8082);
const prefix = process.env.SNAKE_PREVIEW_PREFIX || '';
if (prefix && !/^\/[A-Za-z0-9_-]+\/$/.test(prefix)) throw new Error('SNAKE_PREVIEW_PREFIX must be a single path component with leading and trailing slashes');
const types = { '.html': 'text/html; charset=utf-8', '.css': 'text/css; charset=utf-8', '.js': 'application/javascript', '.json': 'application/json', '.wasm': 'application/wasm', '.data': 'application/octet-stream', '.png': 'image/png', '.ico': 'image/x-icon' };

http.createServer((request, response) => {
  let relative;
  try { relative = decodeURIComponent(new URL(request.url, 'http://localhost').pathname); }
  catch { response.writeHead(400).end('Bad request'); return; }
  if (prefix) {
    if (!relative.startsWith(prefix)) { response.writeHead(404).end('Not found'); return; }
    relative = '/' + relative.slice(prefix.length);
  }
  if (relative === '/') relative = '/index.html';
  const file = path.resolve(root, '.' + relative);
  if (!file.startsWith(root + path.sep)) { response.writeHead(403).end('Forbidden'); return; }
  const compressed = file.endsWith('.br') ? '.br' : file.endsWith('.gz') ? '.gz' : '';
  const original = compressed ? file.slice(0, -compressed.length) : file;
  const headers = { 'Content-Type': types[path.extname(original)] || 'application/octet-stream', 'Cache-Control': 'no-store', 'Referrer-Policy': 'no-referrer' };
  if (compressed) headers['Content-Encoding'] = compressed === '.br' ? 'br' : 'gzip';
  fs.stat(file, (error, info) => {
    if (error || !info.isFile()) { response.writeHead(404).end('Not found: build Unity WebGL first'); return; }
    response.writeHead(200, headers);
    fs.createReadStream(file).on('error', () => response.destroy()).pipe(response);
  });
}).listen(port, '127.0.0.1', () => console.log(`Unity WebGL 预览：http://127.0.0.1:${port}${prefix || '/'}`));
