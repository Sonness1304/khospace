import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { createVercelExport } from '../ops/export-vercel.mjs';

const revision = 'a'.repeat(40);
function fixture() {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'khospace-vercel-test-'));
  const dist = path.join(root, 'dist'), output = path.join(root, 'site');
  fs.mkdirSync(path.join(dist, 'assets'), { recursive: true });
  fs.writeFileSync(path.join(dist, 'index.html'), '<!doctype html><html lang="vi"><title>KhoSpace</title><script src="/assets/app.js"></script></html>');
  fs.writeFileSync(path.join(dist, 'assets/app.js'), 'console.log("frontend-only");');
  fs.writeFileSync(path.join(dist, 'khospace-deployment.json'), JSON.stringify({ mode: 'frontend-only', sourceRevision: revision }));
  return { root, dist, output, revision };
}
test('exports only inspected frontend bytes and fails closed before SPA fallback', () => {
  const f = fixture(), receipt = createVercelExport(f);
  assert.equal(receipt.sourceRevision, revision);
  assert.equal(receipt.mode, 'frontend-only');
  const config = JSON.parse(fs.readFileSync(path.join(f.output, '.vercel/output/config.json')));
  assert.equal(config.version, 3);
  const reserved = config.routes.find(route => route.status === 503);
  for (const url of ['/api', '/api/v1/auth/csrf', '/api/payment-notifications/momo', '/health', '/health/ready', '/signin-google', '/payments/return', '/API/v1/auth/google']) {
    assert.match(url, new RegExp(reserved.src, 'i'));
  }
  assert.equal(reserved.headers['Cache-Control'], 'no-store');
  assert.equal(reserved.methods, undefined);
  assert.ok(config.routes.indexOf(reserved) < config.routes.findIndex(route => route.handle === 'filesystem'));
  const fallback = config.routes.find(route => route.dest === '/index.html');
  assert.deepEqual(fallback.methods, ['GET', 'HEAD']);
  assert.ok(config.routes.some(route => route.status === 405));
  assert.equal(fs.readFileSync(path.join(f.output, '.vercel/output/static/assets/app.js'), 'utf8'), 'console.log("frontend-only");');
  assert.equal(fs.existsSync(path.join(f.output, 'backend')), false);
  assert.equal(receipt.files.length, 3);
});
test('refuses existing output without overwriting its bytes', () => {
  const f = fixture(); fs.mkdirSync(f.output); fs.writeFileSync(path.join(f.output, 'keep'), 'original');
  assert.throws(() => createVercelExport(f), /exists/i);
  assert.equal(fs.readFileSync(path.join(f.output, 'keep'), 'utf8'), 'original');
});
test('rejects an output nested in its source', () => {
  const f = fixture(); assert.throws(() => createVercelExport({ ...f, output: path.join(f.dist, 'site') }), /outside/i);
});
for (const name of ['.env', 'appsettings.json', 'secret.txt', 'assets/app.js.map', 'WarehouseRent.Api.dll']) {
  test(`rejects unexpected export file ${name} before creating output`, () => {
    const f = fixture(); fs.writeFileSync(path.join(f.dist, name), 'synthetic');
    assert.throws(() => createVercelExport(f), /unexpected/i);
    assert.equal(fs.existsSync(f.output), false);
  });
}
test('rejects incorrect build mode or revision', () => {
  const f = fixture();
  fs.writeFileSync(path.join(f.dist, 'khospace-deployment.json'), JSON.stringify({ mode: 'integrated', sourceRevision: revision }));
  assert.throws(() => createVercelExport(f), /mode/i);
  fs.writeFileSync(path.join(f.dist, 'khospace-deployment.json'), JSON.stringify({ mode: 'frontend-only', sourceRevision: 'b'.repeat(40) }));
  assert.throws(() => createVercelExport(f), /revision/i);
});
test('rejects symlinked source or output ancestors', () => {
  const f = fixture(), link = path.join(f.root, 'linked');
  fs.symlinkSync(f.dist, link, 'junction');
  assert.throws(() => createVercelExport({ ...f, dist: link }), /symlink/i);
  assert.throws(() => createVercelExport({ ...f, output: path.join(link, 'outside') }), /symlink/i);
});
