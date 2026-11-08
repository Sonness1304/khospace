import crypto from 'node:crypto';
import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
const directory = 'ops/cloud/render';
test('PostgreSQL cloud startup warms the EF model before starting the database I/O deadline',()=>{
  const source=fs.readFileSync('backend/WarehouseRent.Api/CloudHosting.cs','utf8');
  const start=source.indexOf('GetService<PostgresDataProtectionRepository>()');
  const warm=source.indexOf('_ = db.Model;',start);
  const deadline=source.indexOf('new CancellationTokenSource(TimeSpan.FromSeconds(5))',start);
  assert.ok(warm>start && warm<deadline,'Cold model initialization must not consume the five-second database I/O budget');
  assert.match(source,/PostgreSQL model initialized in/);
});
const shell = process.platform === 'win32' ? 'C:/Program Files/Git/bin/bash.exe' : 'sh';
function start(overrides = {}) {
  const fixture = fs.mkdtempSync(path.join(os.tmpdir(), 'khospace-render-start-'));
  fs.writeFileSync(path.join(fixture, 'setpriv'), '#!/bin/sh\nprintf "args=%s\\n" "$*" >> "$RECEIPT"\nprintf "host=%s health=%s bootstrap=%s/%s store=%s\\n" "$CloudHosting__BackendHost" "$CloudHosting__HealthCheckHost" "${CloudHosting__OperatorEmail:-unset}" "${CloudHosting__OperatorPassword:-unset}" "$CloudHosting__DataProtectionStore" >> "$RECEIPT"\nprintf "migrationOwner=%s\\n" "${ConnectionStrings__WarehouseRentMigration:+present}" >> "$RECEIPT"\ncase "$*" in *--initialize-cloud*) exit "${FIXTURE_INITIALIZER_EXIT:-0}" ;; esac\n', { mode: 0o755 });
  const receipt = path.join(fixture, 'receipt.txt');
  const result = spawnSync(shell, ['-c', 'export PATH="$(cd "$1" && pwd):$PATH"; export RECEIPT="$(cd "$1" && pwd)/receipt.txt"; exec sh "$2"', 'fixture', fixture.replaceAll('\\', '/'), path.resolve(directory, 'start-api.sh').replaceAll('\\', '/')], {
    encoding: 'utf8', env: { ...process.env, ASPNETCORE_ENVIRONMENT: 'Production', CloudHosting__Enabled: 'true', PORT: '10000',
      RENDER_EXTERNAL_HOSTNAME: 'fixture-api.onrender.com', CloudHosting__DataProtectionStore: 'SqlServer',
      CloudHosting__DataProtectionEncryptionKey: Buffer.alloc(32, 7).toString('base64'),
      ConnectionStrings__WarehouseRent: 'Server=fixture.database.windows.net;Database=WarehouseRent_Cloud_Fixture;Encrypt=True;TrustServerCertificate=False',
      KHOSPACE_INITIALIZE_CLOUD_ON_START: '0', CloudHosting__OperatorEmail: 'fixture@example.test',
      CloudHosting__OperatorPassword: 'SYNTHETIC_ONLY_PASSWORD_123', CloudHosting__BackendHost: '', CloudHosting__HealthCheckHost: '', ...overrides },
  });
  return { ...result, receipt: fs.existsSync(receipt) ? fs.readFileSync(receipt, 'utf8') : '' };
}
test('Render blueprint selects free Docker only, with no Render database or persistent disk', () => {
  const yaml = fs.readFileSync(`${directory}/render.yaml`, 'utf8');
  assert.match(yaml, /runtime: docker/); assert.match(yaml, /plan: free/);
  assert.match(yaml, /dockerfilePath: ops\/cloud\/render\/Dockerfile/);
  assert.match(yaml, /healthCheckPath: \/health\/live/); assert.match(yaml, /autoDeployTrigger: off/);
  assert.doesNotMatch(yaml, /^\s*(?:disk|databases|preDeployCommand|initialDeployHook):/m);
  assert.match(yaml, /key: CloudHosting__DataProtectionStore\s+value: PostgreSQL/);
  assert.match(yaml, /key: Database__Provider\s+value: PostgreSQL/);
  for (const key of ['ConnectionStrings__WarehouseRent', 'CloudHosting__DataProtectionEncryptionKey', 'CloudHosting__SharedProxyKey', 'CloudHosting__PublicOrigin'])
    assert.match(yaml, new RegExp(`key: ${key}\\s+sync: false`));
  assert.match(yaml, /key: KHOSPACE_INITIALIZE_CLOUD_ON_START\s+value: "0"/);
});
test('Render image is pinned, production only and uses no secret build arguments or volume', () => {
  const docker = fs.readFileSync(`${directory}/Dockerfile`, 'utf8');
  assert.equal((docker.match(/@sha256:[a-f0-9]{64}/g) ?? []).length, 2);
  assert.match(docker, /ENV ASPNETCORE_ENVIRONMENT=Production/);
  assert.match(docker, /ENV CloudHosting__DataProtectionStore=SqlServer/);
  assert.doesNotMatch(docker, /\b(?:ARG|VOLUME)\b|DataProtectionPath|seed-demo|Environment=Demo/);
  const ignore = fs.readFileSync(`${directory}/Dockerfile.dockerignore`, 'utf8');
  for (const pattern of ['**/.env*', '**/obj', '**/bin', '**/*.pem', '**/*.key', '**/secrets*']) assert.ok(ignore.includes(pattern));
});
test('Render entrypoint syntax/LF and normal start drops privileges with private bootstrap removed', () => {
  const syntax = spawnSync(shell, ['-n', `${directory}/start-api.sh`], { encoding: 'utf8' });
  assert.equal(syntax.status, 0, syntax.stderr);
  assert.equal(fs.readFileSync(`${directory}/start-api.sh`).includes(13), false);
  const result = start(); assert.equal(result.status, 0, result.stderr);
  assert.match(result.receipt, /--reuid app --regid app --init-groups dotnet \/app\/WarehouseRent.Api.dll --urls http:\/\/0.0.0.0:10000/);
  assert.equal((result.receipt.match(/args=/g) ?? []).length, 1);
  assert.match(result.receipt, /host=fixture-api.onrender.com health=fixture-api.onrender.com bootstrap=unset\/unset store=SqlServer/);
});
test('Render initializes only explicitly then clears credentials before long-running API', () => {
  const result = start({ KHOSPACE_INITIALIZE_CLOUD_ON_START: '1', PORT: '12345' });
  assert.equal(result.status, 0, result.stderr);
  assert.equal((result.receipt.match(/args=/g) ?? []).length, 2);
  assert.match(result.receipt, /--initialize-cloud/); assert.doesNotMatch(result.receipt, /--seed-demo/);
  assert.match(result.receipt, /bootstrap=fixture@example.test\/SYNTHETIC_ONLY_PASSWORD_123/);
  assert.match(result.receipt, /bootstrap=unset\/unset/); assert.match(result.receipt, /0.0.0.0:12345/);
});
test('failed explicit initializer never starts API or retries initialization', () => {
  const result = start({ KHOSPACE_INITIALIZE_CLOUD_ON_START: '1', FIXTURE_INITIALIZER_EXIT: '17' });
  assert.equal(result.status, 17);
  assert.equal((result.receipt.match(/args=/g) ?? []).length, 1);
  assert.match(result.receipt, /--initialize-cloud/); assert.doesNotMatch(result.receipt, /--urls/);
});
test('PostgreSQL Render starts only with a matching durable key store and clears migration owner credential',()=>{
  const result=start({ Database__Provider:'PostgreSQL',CloudHosting__DataProtectionStore:'PostgreSQL',ConnectionStrings__WarehouseRentMigration:'SYNTHETIC_MIGRATOR_ONLY' });
  assert.equal(result.status,0,result.stderr);
  assert.match(result.receipt,/store=PostgreSQL/);
  assert.doesNotMatch(result.receipt,/migrationOwner=present/);
});
for (const [key, value] of [
  ['CloudHosting__Enabled', 'false'],
  ['CloudHosting__DataProtectionStore', 'FileSystem'], ['CloudHosting__DataProtectionEncryptionKey', ''],
  ['ConnectionStrings__WarehouseRent', ''], ['RENDER_EXTERNAL_HOSTNAME', ''],
  ['RENDER_EXTERNAL_HOSTNAME', 'a.b.onrender.com'], ['RENDER_EXTERNAL_HOSTNAME', 'evil.onrender.com@evil.test'],
  ['RENDER_EXTERNAL_HOSTNAME', 'fixture-api.onrender.com\nevil.test'],
  ['CloudHosting__HealthCheckHost', '*.onrender.com'], ['CloudHosting__BackendHost', 'other.onrender.com'],
  ['PORT', '0'], ['PORT', '65536'], ['PORT', '80;echo'], ['ASPNETCORE_ENVIRONMENT', 'Demo'],
  ['KHOSPACE_INITIALIZE_CLOUD_ON_START', 'true'],
]) test(`Render rejects unsafe ${key}=${value} before initializer/runtime`, () => {
  const result = start({ [key]: value, KHOSPACE_INITIALIZE_CLOUD_ON_START: key === 'KHOSPACE_INITIALIZE_CLOUD_ON_START' ? value : '1' });
  assert.notEqual(result.status, 0); assert.equal(result.receipt, '');
  assert.equal(result.stderr.includes('SYNTHETIC_ONLY_PASSWORD_123'), false);
});


test('Render bundles the official pinned Supabase CA without weakening TLS or the system trust store',()=>{
  const certificate=fs.readFileSync(`${directory}/certificates/supabase-prod-ca-2021.crt`);
  const digest=crypto.createHash('sha256').update(certificate).digest('hex');
  assert.equal(digest,'700723581420dd1ac98fd7e9ac529f0ef210eadcaf87fc868a3ad7d114c2f3b7');
  const cert=new crypto.X509Certificate(certificate);assert.equal(cert.ca,true);
  assert.equal(cert.fingerprint.replaceAll(':',''),'A4518A0933AF6949482CCA3014C007C369DF9F6F');
  const docker=fs.readFileSync(`${directory}/Dockerfile`,'utf8');
  assert.match(docker,/COPY ops\/cloud\/render\/certificates\/supabase-prod-ca-2021\.crt \/app\/certificates\/supabase-prod-ca-2021\.crt/);
  assert.ok(docker.includes(digest));assert.match(docker,/sha256sum -c/);
  assert.doesNotMatch(docker,/update-ca-certificates|SSL.*(?:Disable|Require)|TrustServerCertificate=true/);
});
