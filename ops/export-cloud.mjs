import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { fileURLToPath, pathToFileURL } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const sha = bytes => crypto.createHash('sha256').update(bytes).digest('hex');
const providerInputs = {
  railway: ['ops/cloud/Dockerfile', 'ops/cloud/start-api.sh', 'ops/cloud/railway.json',
    'ops/cloud/sql/Dockerfile', 'ops/cloud/sql/start-sql.sh'],
  render: ['ops/cloud/render/Dockerfile', 'ops/cloud/render/Dockerfile.dockerignore',
    'ops/cloud/render/start-api.sh', 'ops/cloud/render/render.yaml',
    'ops/cloud/render/certificates/supabase-prod-ca-2021.crt'],
};
const allowed = name => !/(?:^|\/)(?:bin|obj|uploads|node_modules)(?:\/|$)/i.test(name) && (name === 'global.json' || name === 'backend/Directory.Build.props'
  || /^backend\/WarehouseRent\.(?:Api|Infrastructure)\/(?:[A-Za-z0-9_-]+\/)*[A-Za-z0-9_.-]+\.cs$/.test(name)
  || /^backend\/WarehouseRent\.(?:Api|Infrastructure)\/(?:WarehouseRent\.(?:Api|Infrastructure)\.csproj|packages\.lock\.json)$/.test(name)
  || /^backend\/WarehouseRent\.Infrastructure\/Persistence\/PostgresMigrations\/[0-9]{3}_[a-z0-9_]+\.sql$/.test(name)
  || name === 'backend/WarehouseRent.Api/appsettings.json'
  || ['ops/cloud/Dockerfile', 'ops/cloud/start-api.sh', 'ops/cloud/railway.json',
    'ops/cloud/sql/Dockerfile', 'ops/cloud/sql/start-sql.sh', ...providerInputs.render].includes(name));
const inside = (parent, child) => { const r = path.relative(parent, child); return r === '' || r !== '..' && !r.startsWith(`..${path.sep}`) && !path.isAbsolute(r); };
function git(repository, ...args) {
  const result = spawnSync('git', ['-C', repository, ...args], { maxBuffer: 32 * 1024 * 1024 });
  if (result.status !== 0) throw Error('Cloud context Git inspection failed');
  return result.stdout;
}
export function exportCloudContext({ output, repository = root, provider = 'railway' }) {
  if (!Object.hasOwn(providerInputs, provider)) throw Error('Unsupported cloud provider');
  if (!path.isAbsolute(output)) throw Error('An absolute fresh output directory is required');
  output = path.resolve(output); repository = path.resolve(repository);
  if (inside(repository, output) || inside(output, repository)) throw Error('Cloud context must be outside its repository');
  if (fs.existsSync(output)) throw Error('Cloud context output already exists');
  for (let p = output; ; p = path.dirname(p)) {
    if (fs.existsSync(p) && fs.lstatSync(p).isSymbolicLink()) throw Error('Cloud context cannot use linked paths');
    if (p === path.dirname(p)) break;
  }
  if (git(repository, 'status', '--porcelain').length) throw Error('Commit reviewed source before cloud export');
  const revision = git(repository, 'rev-parse', 'HEAD').toString().trim();
  const entries = git(repository, 'ls-tree', '-r', '-z', revision).toString().split('\0').filter(Boolean)
    .map(entry => { const [record, name] = entry.split('\t'); const [mode, type, oid] = record.split(' '); return { mode, type, oid, path: name }; })
    .filter(entry => allowed(entry.path) && (!entry.path.startsWith('ops/') || providerInputs[provider].includes(entry.path)));
  for (const required of ['global.json', 'backend/Directory.Build.props', 'backend/WarehouseRent.Api/Program.cs',
    'backend/WarehouseRent.Api/CloudHosting.cs', 'backend/WarehouseRent.Api/CloudDatabaseSetup.cs',
    'backend/WarehouseRent.Api/WarehouseRent.Api.csproj', 'backend/WarehouseRent.Api/packages.lock.json',
    'backend/WarehouseRent.Infrastructure/WarehouseRent.Infrastructure.csproj',
    'backend/WarehouseRent.Infrastructure/packages.lock.json',
    'backend/WarehouseRent.Infrastructure/Persistence/PostgresMigrations/001_initial.sql',
    ...providerInputs[provider]])
    if (!entries.some(entry => entry.path === required)) throw Error(`Missing committed cloud input: ${required}`);
  if (entries.some(entry => entry.type !== 'blob' || !['100644', '100755'].includes(entry.mode))) throw Error('Cloud inputs must be regular committed files');
  const batch = spawnSync('git', ['-C', repository, 'cat-file', '--batch'], {
    input: entries.map(entry => entry.oid).join('\n') + '\n', maxBuffer: 32 * 1024 * 1024,
  });
  if (batch.status !== 0) throw Error('Cloud input read failed');
  let offset = 0;
  const files = entries.map(entry => {
    const lineEnd = batch.stdout.indexOf(10, offset);
    const header = batch.stdout.subarray(offset, lineEnd).toString().split(' ');
    const size = Number(header[2]); offset = lineEnd + 1;
    if (header[0] !== entry.oid || header[1] !== 'blob' || !Number.isSafeInteger(size) || size > 5 * 1024 * 1024)
      throw Error('Invalid committed cloud input');
    const data = batch.stdout.subarray(offset, offset + size); offset += size + 1;
    if (data.length !== size) throw Error('Truncated committed cloud input');
    return { path: entry.path, bytes: size, sha256: sha(data), data };
  });
  if (files.reduce((n, file) => n + file.bytes, 0) > 20 * 1024 * 1024) throw Error('Cloud context too large');
  files.push({ path: 'source-revision.txt', data: Buffer.from(revision + '\n') });
  fs.mkdirSync(output, { mode: 0o700 });
  for (const file of files) {
    const destination = path.join(output, file.path);
    fs.mkdirSync(path.dirname(destination), { recursive: true });
    fs.writeFileSync(destination, file.data, { flag: 'wx' });
    if (sha(fs.readFileSync(destination)) !== sha(file.data)) throw Error('Cloud export byte verification failed');
  }
  return { sourceRevision: revision, output, provider, dockerExecutionVerified: false,
    files: files.map(file => ({ path: file.path, bytes: file.data.length, sha256: sha(file.data) })) };
}
if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  const args = process.argv.slice(2);
  if (args[0] !== '--output' || ![2, 4].includes(args.length) || args.length === 4 && args[2] !== '--provider')
    throw Error('Usage: node ops/export-cloud.mjs --output ABSOLUTE_FRESH_DIRECTORY [--provider railway|render]');
  console.log(JSON.stringify(exportCloudContext({ output: args[1], provider: args.length === 4 ? args[3] : 'railway' }), null, 2));
}
