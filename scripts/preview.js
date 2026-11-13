'use strict';

const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');

const directory = path.join(__dirname, '..', 'dist', 'browser');
if (!fs.existsSync(path.join(directory, 'index.html'))) {
  console.error('请先执行 npm run build。');
  process.exit(1);
}
const files = { '/': ['index.html', 'text/html; charset=utf-8'], '/index.html': ['index.html', 'text/html; charset=utf-8'], '/bundle.js': ['bundle.js', 'text/javascript; charset=utf-8'] };
const port = Number(process.env.PORT || 8080);
http.createServer((request, response) => {
  const pathname = new URL(request.url, 'http://localhost').pathname;
  const file = files[pathname];
  if (!file) { response.writeHead(404); response.end('Not found'); return; }
  fs.readFile(path.join(directory, file[0]), (error, contents) => {
    if (error) { response.writeHead(500); response.end('Read error'); return; }
    response.writeHead(200, { 'Content-Type': file[1], 'Cache-Control': 'no-store' });
    response.end(contents);
  });
}).listen(port, '127.0.0.1', () => console.log(`开发预览：http://127.0.0.1:${port}`));
