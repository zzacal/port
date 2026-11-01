import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
const root = '/tmp/p008/web', out = '/tmp/p008/out';
const types = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.wasm': 'application/wasm', '.jpg': 'image/jpeg' };
http.createServer((req, res) => {
  if (req.method === 'POST' && req.url.startsWith('/save/')) {
    const name = path.basename(req.url.slice(6));
    const chunks = [];
    req.on('data', c => chunks.push(c));
    req.on('end', () => { fs.writeFileSync(path.join(out, name), Buffer.concat(chunks)); res.end('ok'); });
    return;
  }
  const p = path.join(root, path.normalize(req.url === '/' ? '/index.html' : req.url));
  if (!p.startsWith(root) || !fs.existsSync(p)) { res.statusCode = 404; return res.end(); }
  res.setHeader('Content-Type', types[path.extname(p)] ?? 'application/octet-stream');
  fs.createReadStream(p).pipe(res);
}).listen(18181, '127.0.0.1');
