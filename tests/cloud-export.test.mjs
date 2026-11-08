import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { createVercelExport } from '../ops/export-vercel.mjs';
const revision = 'b'.repeat(40), key = 'SyntheticServerOnlyKey_DoNotPublish_123456789';
function fixture() {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'khospace-cloud-export-'));
  const dist = path.join(root, 'dist'), output = path.join(root, 'site');
  fs.mkdirSync(path.join(dist, 'assets'), { recursive: true });
  fs.writeFileSync(path.join(dist, 'index.html'), '<!doctype html><title>KhoSpace</title>');
  fs.writeFileSync(path.join(dist, 'assets/app.js'), 'console.log("integrated account");');
  fs.writeFileSync(path.join(dist, 'khospace-deployment.json'), JSON.stringify({ mode: 'cloud-connected', sourceRevision: revision }));
  return { dist, output, revision, connection: { origin: 'https://api-example.up.railway.app', key } };
}
test('connected export injects the server-only gateway key and preserves full reserved paths', () => {
  const f = fixture(), receipt = createVercelExport(f);
  assert.equal(receipt.mode, 'cloud-connected');
  const config = JSON.parse(fs.readFileSync(path.join(f.output, '.vercel/output/config.json')));
  const route = config.routes.find(item => item.transforms);
  assert.ok(route); assert.equal(route.methods, undefined);
  for (const url of ['/api/v1/auth/me', '/health/ready', '/signin-google', '/api/payment-notifications/vnpay', '/payments/return']) {
    assert.match(url, new RegExp(route.src, 'i'));
    assert.equal(url.replace(new RegExp(route.src, 'i'), route.dest), f.connection.origin + url);
  }
  assert.deepEqual(route.transforms, [{ type: 'request.headers', op: 'set', target: { key: 'X-KhoSpace-Proxy-Key' }, args: key }]);
  assert.equal(route.headers['Cache-Control'], 'no-store');
  assert.equal(JSON.stringify(receipt).includes(key), false);
  for (const file of receipt.files) assert.equal(fs.readFileSync(path.join(f.output, '.vercel/output/static', file.path), 'utf8').includes(key), false);
});
for (const origin of ['http://api.up.railway.app', 'https://localhost', 'https://127.0.0.1', 'https://api.up.railway.app@evil.test', 'https://api.up.railway.app/path', 'https://api.up.railway.app/?token=x', 'https://api.up.railway.app:8443', 'https://10.0.0.1']) {
  test(`connected export rejects unsafe upstream ${origin}`, () => {
    const f = fixture(); f.connection.origin = origin;
    assert.throws(() => createVercelExport(f), /origin/i);
    assert.equal(fs.existsSync(f.output), false);
  });
}
test('connected export rejects a missing or short private key and mixed-mode output', () => {
  const f = fixture(); f.connection.key = '';
  assert.throws(() => createVercelExport(f), /key/i);
  f.connection.key = key;
  fs.writeFileSync(path.join(f.dist, 'khospace-deployment.json'), JSON.stringify({ mode: 'frontend-only', sourceRevision: revision }));
  assert.throws(() => createVercelExport(f), /mode/i);
});
test('connected export refuses a private key accidentally included in public assets', () => {
  const f = fixture();
  fs.appendFileSync(path.join(f.dist, 'assets/app.js'), `\n${key}`);
  assert.throws(() => createVercelExport(f), /private/i);
  assert.equal(fs.existsSync(f.output), false);
});

test('connected export accepts a single-label Render HTTPS origin without exposing private key', () => {
  const f = fixture(); f.connection.origin = 'https://fixture-api.onrender.com/';
  const receipt = createVercelExport(f);
  const config = JSON.parse(fs.readFileSync(path.join(f.output, '.vercel/output/config.json')));
  assert.equal(config.routes.find(route => route.transforms).dest, 'https://fixture-api.onrender.com$1');
  assert.equal(receipt.backendConnected, false); assert.equal(receipt.runtimeAcceptanceVerified, false);
  assert.equal(JSON.stringify(receipt).includes(key), false);
});
for (const origin of ['http://api.onrender.com', 'https://onrender.com', 'https://a.b.onrender.com',
  'https://api.onrender.com.evil.test', 'https://api.onrender.com@evil.test', 'https://api.onrender.com/path',
  'https://api.onrender.com:443', 'https://api.onrender.com?x=1', 'https://api.onrender.com#x',
  'https://-api.onrender.com', 'https://api-.onrender.com', 'https://api_onrender.com', 'https://api.onrender.com//',
  `https://${'a'.repeat(64)}.onrender.com`, 'https://api.onrender.com\n', 'https://api.up.railway.app\n']) test(`Render connected export rejects unsafe upstream ${origin}`, () => {
  const f = fixture(); f.connection.origin = origin;
  assert.throws(() => createVercelExport(f), /origin/i); assert.equal(fs.existsSync(f.output), false);
});
