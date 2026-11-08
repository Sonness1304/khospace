import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import { spawnSync } from 'node:child_process';
for (const file of ['ops/cloud/start-api.sh', 'ops/cloud/sql/start-sql.sh']) {
  test(`${file} has portable shell syntax`, () => {
    const bash = process.platform === 'win32' ? 'C:/Program Files/Git/bin/bash.exe' : 'sh';
    const result = spawnSync(bash, ['-n', file], { encoding: 'utf8' });
    assert.equal(result.status, 0, result.stderr);
    assert.equal(fs.readFileSync(file).includes(13), false, 'Linux entrypoint must use LF');
  });
}
test('cloud images are pinned and storage/production boundaries are explicit', () => {
  const api = fs.readFileSync('ops/cloud/Dockerfile', 'utf8');
  const sql = fs.readFileSync('ops/cloud/sql/Dockerfile', 'utf8');
  assert.equal((api.match(/@sha256:[a-f0-9]{64}/g) ?? []).length, 2);
  assert.match(sql, /@sha256:[a-f0-9]{64}/); assert.match(sql, /MSSQL_PID=Express/);
  const start = fs.readFileSync('ops/cloud/start-api.sh', 'utf8');
  assert.match(start, /mountpoint -q \/data/); assert.match(start, /setpriv --reuid app/);
  assert.match(start, /KHOSPACE_INITIALIZE_CLOUD_ON_START:-0/);
  assert.match(start, /unset CloudHosting__OperatorPassword CloudHosting__OperatorEmail/);
  const config = JSON.parse(fs.readFileSync('ops/cloud/railway.json'));
  assert.equal(config.deploy.healthcheckPath, '/health/live');
});
