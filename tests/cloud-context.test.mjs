import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { exportCloudContext } from '../ops/export-cloud.mjs';
function fixture() {
  const repository = fs.mkdtempSync(path.join(os.tmpdir(), 'khospace-context-source-'));
  const inputs = {
    'global.json': '{}', 'backend/Directory.Build.props': '<Project/>',
    'backend/WarehouseRent.Api/Program.cs': '// committed fixture',
    'backend/WarehouseRent.Api/CloudHosting.cs': '// fixture',
    'backend/WarehouseRent.Api/CloudDatabaseSetup.cs': '// fixture',
    'backend/WarehouseRent.Api/WarehouseRent.Api.csproj': '<Project/>',
    'backend/WarehouseRent.Api/packages.lock.json': '{}',
    'backend/WarehouseRent.Infrastructure/WarehouseRent.Infrastructure.csproj': '<Project/>',
    'backend/WarehouseRent.Infrastructure/packages.lock.json': '{}',
    'backend/WarehouseRent.Infrastructure/Persistence/SqlConfiguration.cs': '// fixture',
    'backend/WarehouseRent.Infrastructure/Persistence/PostgresMigrations/001_initial.sql': '-- frozen fixture migration',
    'ops/cloud/Dockerfile': 'FROM fixture', 'ops/cloud/start-api.sh': '#!/bin/sh\n',
    'ops/cloud/railway.json': '{}', 'ops/cloud/sql/Dockerfile': 'ENV MSSQL_PID=Express',
    'ops/cloud/sql/start-sql.sh': '#!/bin/sh\n',
    'ops/cloud/render/Dockerfile': 'FROM render-fixture',
    'ops/cloud/render/Dockerfile.dockerignore': '**/.env*\n',
    'ops/cloud/render/start-api.sh': '#!/bin/sh\n',
    'ops/cloud/render/certificates/supabase-prod-ca-2021.crt': 'PUBLIC_SYNTHETIC_CA_FIXTURE',
    'ops/cloud/render/certificates/arbitrary.crt': 'EXCLUDED_ARBITRARY_CERT',
    'ops/cloud/render/render.yaml': 'services:\n  - type: web\n    plan: free\n',
    '.env': 'not-for-export', '.qa/private.json': 'not-for-export',
    'backend/WarehouseRent.Tests/Fake.cs': '// excluded',
    'backend/WarehouseRent.Api/appsettings.Local.json': '{"secret":"fixture"}',
    'backend/WarehouseRent.Api/obj/Fake.cs': '// generated', 'frontend/app.js': '// excluded',
  };
  for (const [name, text] of Object.entries(inputs)) {
    const target = path.join(repository, name);
    fs.mkdirSync(path.dirname(target), { recursive: true }); fs.writeFileSync(target, text);
  }
  const git = (...args) => execFileSync('git', ['-C', repository, ...args], { stdio: 'pipe' });
  git('init', '--quiet'); git('add', '--', ...Object.keys(inputs));
  git('-c', 'user.name=Fixture', '-c', 'user.email=fixture@example.test', 'commit', '-qm', 'synthetic export fixture');
  return { repository, output: path.join(fs.mkdtempSync(path.join(os.tmpdir(), 'khospace-context-test-')), 'context') };
}
test('cloud context exports only committed API build inputs and SQL service inputs', () => {
  const f = fixture(), { output } = f;
  const receipt = exportCloudContext(f);
  assert.equal(receipt.dockerExecutionVerified, false);
  assert.ok(receipt.files.some(file => file.path === 'backend/WarehouseRent.Api/Program.cs'));
  assert.ok(receipt.files.some(file => file.path === 'backend/WarehouseRent.Infrastructure/Persistence/SqlConfiguration.cs'));
  assert.ok(receipt.files.some(file => file.path === 'backend/WarehouseRent.Infrastructure/Persistence/PostgresMigrations/001_initial.sql'));
  for (const file of receipt.files) assert.doesNotMatch(file.path, /(^|\/)(\.qa|\.git|node_modules|frontend|WarehouseRent.Tests|bin|obj|uploads)(\/|$)|\.env|appsettings\.Local/i);
  assert.match(fs.readFileSync(path.join(output, 'ops/cloud/sql/Dockerfile'), 'utf8'), /MSSQL_PID=Express/);
  assert.equal(fs.readFileSync(path.join(output, 'source-revision.txt'), 'utf8').trim(), receipt.sourceRevision);
  assert.throws(() => exportCloudContext(f), /exists/i);
});
test('cloud context refuses dirty source rather than silently uploading it', () => {
  const f = fixture(); fs.appendFileSync(path.join(f.repository, 'backend/WarehouseRent.Api/Program.cs'), '\n// dirty');
  assert.throws(() => exportCloudContext(f), /commit/i);
  assert.equal(fs.existsSync(f.output), false);
});
test('cloud context refuses repository-contained output', () => {
  const f = fixture(); f.output = path.join(f.repository, 'export');
  assert.throws(() => exportCloudContext(f), /outside/i);
});

test('Render context selects only committed Render container and manifest plus shared API inputs', () => {
  const f = fixture();
  const receipt = exportCloudContext({ ...f, provider: 'render' });
  assert.equal(receipt.provider, 'render');
  for (const name of ['Dockerfile', 'Dockerfile.dockerignore', 'start-api.sh', 'render.yaml'])
    assert.ok(receipt.files.some(file => file.path === `ops/cloud/render/${name}`));
  assert.ok(receipt.files.some(file => file.path === 'backend/WarehouseRent.Api/Program.cs'));
  assert.equal(receipt.files.some(file => file.path.startsWith('ops/cloud/sql/') || file.path === 'ops/cloud/railway.json'), false);
  for (const file of receipt.files) assert.doesNotMatch(file.path, /(^|\/)(\.qa|\.git|node_modules|frontend|WarehouseRent.Tests|bin|obj|uploads)(\/|$)|\.env|appsettings\.Local/i);
  assert.equal(receipt.dockerExecutionVerified, false);
});
test('cloud context rejects unsupported provider before creating export', () => {
  const f = fixture();
  assert.throws(() => exportCloudContext({ ...f, provider: 'arbitrary' }), /provider/i);
  assert.equal(fs.existsSync(f.output), false);
});

test('Render exports only the exact committed public Supabase CA, not arbitrary certificates',()=>{
  const f=fixture();const receipt=exportCloudContext({...f,provider:'render'});
  assert.ok(receipt.files.some(x=>x.path==='ops/cloud/render/certificates/supabase-prod-ca-2021.crt'));
  assert.ok(!receipt.files.some(x=>x.path.endsWith('arbitrary.crt')));
});
