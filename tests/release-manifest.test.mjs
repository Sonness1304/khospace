import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { createManifest, inspectManifest } from '../ops/release-manifest.mjs';
const revision = 'a'.repeat(40);
const metadata = { sourceRevision: revision, migrations: ['20261028220406_InAppNoticeOutbox'], tools: { dotnet: '10.0.403', node: '24.19.0', npm: '11.17.0' } };
function fixture(t) {
 const root = fs.mkdtempSync(path.join(os.tmpdir(), 'WarehouseRent_Release_Test_'));
 t.after(() => fs.rmSync(root, { recursive: true, force: true }));
 function put(name, data) { const target = path.join(root, 'app', name); fs.mkdirSync(path.dirname(target), { recursive: true }); fs.writeFileSync(target, data); }
 put('WarehouseRent.Api.dll', 'synthetic api binary'); put('WarehouseRent.Infrastructure.dll', 'synthetic infrastructure binary');
 put('WarehouseRent.Api.deps.json', '{}'); put('WarehouseRent.Api.runtimeconfig.json', '{}');
 put('appsettings.json', JSON.stringify({ Logging: { LogLevel: { Default: 'Information' } }, AllowedHosts: 'localhost;127.0.0.1' }));
 put('wwwroot/index.html', '<!doctype html><script type="module" src="/assets/app.js"></script><link rel="stylesheet" href="/assets/app.css">');
 put('wwwroot/assets/app.js', 'synthetic frontend'); put('wwwroot/assets/app.css', 'body{}');
 return { root, put };
}
test('creates a deterministic inventory and verifies the expected source revision without running payload', t => {
 const { root } = fixture(t); const created = createManifest(root, metadata); const read = inspectManifest(root, revision);
 assert.equal(read.sourceRevision, revision); assert.equal(read.payloadSha256, created.payloadSha256); assert.equal(read.fileCount, 8);
 assert.equal(read.latestMigration, metadata.migrations[0]); assert.equal(fs.existsSync(path.join(root, 'app', 'release-manifest.json')), false);
});
test('refuses incomplete payload, missing SPA assets and an external or traversing asset reference', t => {
 for (const mode of ['binary', 'asset', 'external', 'traversal']) {
  const { root, put } = fixture(t);
  if (mode === 'binary') fs.unlinkSync(path.join(root, 'app/WarehouseRent.Api.dll'));
  if (mode === 'asset') fs.unlinkSync(path.join(root, 'app/wwwroot/assets/app.js'));
  if (mode === 'external') put('wwwroot/index.html', '<script src="https://external.test/a.js"></script>');
  if (mode === 'traversal') put('wwwroot/index.html', '<script src="/assets/../outside.js"></script>');
  assert.throws(() => createManifest(root, metadata));
 }
});
test('detects payload modification, missing file and additional unlisted file', t => {
 for (const mode of ['changed', 'missing', 'extra']) {
  const { root, put } = fixture(t); createManifest(root, metadata);
  if (mode === 'changed') put('WarehouseRent.Api.dll', 'old-api');
  if (mode === 'missing') fs.unlinkSync(path.join(root, 'app/wwwroot/assets/app.css'));
  if (mode === 'extra') put('unexpected.txt', 'not in manifest');
  assert.throws(() => inspectManifest(root, revision));
 }
});
test('rejects private config/material and never overwrites an existing manifest', t => {
 for (const name of ['.env', 'appsettings.Local.json', 'owner.pfx', 'old.bak']) {
  const { root, put } = fixture(t); put(name, 'synthetic private fixture'); assert.throws(() => createManifest(root, metadata));
 }
 const { root, put } = fixture(t); put('appsettings.json', JSON.stringify({ ConnectionStrings: { WarehouseRent: 'DO_NOT_EXPORT' } }));
 assert.throws(() => createManifest(root, metadata));
 const clean = fixture(t); createManifest(clean.root, metadata); assert.throws(() => createManifest(clean.root, metadata));
});
test('fails safe on a changed expected revision, malformed manifest and unsafe inventory paths', t => {
 for (const mode of ['revision', 'malformed', 'traversal', 'absolute', 'duplicate', 'hash']) {
  const { root } = fixture(t); createManifest(root, metadata); const target = path.join(root, 'release-manifest.json');
  const manifest = JSON.parse(fs.readFileSync(target, 'utf8'));
  if (mode === 'revision') { assert.throws(() => inspectManifest(root, 'b'.repeat(40))); continue; }
  if (mode === 'malformed') { fs.writeFileSync(target, '{'); assert.throws(() => inspectManifest(root)); continue; }
  if (mode === 'traversal') manifest.files[0].path = '../outside';
  if (mode === 'absolute') manifest.files[0].path = 'C:/outside';
  if (mode === 'duplicate') manifest.files.push(manifest.files[0]);
  if (mode === 'hash') manifest.files[0].sha256 = '0'.repeat(64);
  fs.writeFileSync(target, JSON.stringify(manifest)); assert.throws(() => inspectManifest(root));
 }
});
test('rejects junctions and symlinked files without following them', t => {
 const { root } = fixture(t); const outside = fs.mkdtempSync(path.join(os.tmpdir(), 'WarehouseRent_Release_Outside_')); t.after(() => fs.rmSync(outside, { recursive: true, force: true }));
 fs.writeFileSync(path.join(outside, 'private.txt'), 'not allowed');
 fs.symlinkSync(outside, path.join(root, 'app', 'linked'), process.platform === 'win32' ? 'junction' : 'dir');
 assert.throws(() => createManifest(root, metadata), /link|reparse/i);
});
