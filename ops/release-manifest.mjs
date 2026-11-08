import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { pathToFileURL } from 'node:url';

const manifestName = 'release-manifest.json';
const hash = bytes => crypto.createHash('sha256').update(bytes).digest('hex');
const fail = message => { throw Error(message); };
const revisionPattern = /^[0-9a-f]{40}$/;
const migrationPattern = /^\d{14}_[A-Za-z][A-Za-z0-9]*$/;
const maxFiles = 5000, maxBytes = 512 * 1024 * 1024, maxFileBytes = 256 * 1024 * 1024;
function safePath(value) {
 if (typeof value !== 'string' || value.length > 500 || value.includes('\\') || value.includes(':') || value.includes('\0') || value.startsWith('/')
  || value.split('/').some(p => !p || p === '.' || p === '..' || /[<>"|?*\x00-\x1f]/.test(p) || /[. ]$/.test(p))) fail('Unsafe inventory path.');
 return value;
}
function noLinks(target) {
 const absolute = path.resolve(target); let current = path.parse(absolute).root;
 for (const part of absolute.slice(current.length).split(path.sep).filter(Boolean)) {
  current = path.join(current, part); const stat = fs.lstatSync(current);
  if (stat.isSymbolicLink()) fail('Symbolic link or reparse path is not allowed.');
 }
 return absolute;
}
function metadataValid(metadata) {
 if (metadata.schemaVersion !== 1 || metadata.application !== 'WarehouseRent' || !revisionPattern.test(metadata.sourceRevision)
  || metadata.sourceDirty !== false || metadata.artifactKind !== 'local-demo-candidate') fail('Invalid release identity.');
 if (!Array.isArray(metadata.migrations) || !metadata.migrations.length || metadata.migrations.length > 1000
  || metadata.migrations.some(x => typeof x !== 'string' || !migrationPattern.test(x)) || new Set(metadata.migrations).size !== metadata.migrations.length
  || [...metadata.migrations].sort().join() !== metadata.migrations.join() || metadata.latestExpectedMigration !== metadata.migrations.at(-1)) fail('Invalid expected migration inventory.');
 for (const tool of ['dotnet', 'node', 'npm']) if (!/^\d+\.\d+\.\d+$/.test(metadata.tools?.[tool])) fail('Invalid toolchain metadata.');
 if (metadata.runtimeAcceptanceVerified !== false || metadata.browserAcceptanceVerified !== false || metadata.databaseMigrated !== false) fail('Artifact metadata cannot claim runtime acceptance.');
}
function validateConfig(file, relative) {
 const name = path.posix.basename(relative).toLowerCase();
 if (name.startsWith('.env') || /\.(?:pfx|p12|key|pem|bak|db|sqlite|sqlite3|mdf|ldf)$/.test(name)
  || (name.startsWith('appsettings') && name !== 'appsettings.json') || /(?:^|[-_.])(?:secret|credential|token)(?:[-_.]|$)/.test(name)) fail('Private/configuration material is not allowed in a demo artifact.');
 if (name === 'appsettings.json') {
  const config = JSON.parse(fs.readFileSync(file, 'utf8'));
  if (Object.keys(config).some(k => !['Logging', 'AllowedHosts'].includes(k)) || config.AllowedHosts !== 'localhost;127.0.0.1'
   || Object.keys(config.Logging ?? {}).some(k => k !== 'LogLevel') || Object.values(config.Logging?.LogLevel ?? {}).some(x => !['Trace', 'Debug', 'Information', 'Warning', 'Error', 'Critical', 'None'].includes(x))) fail('Only the safe base logging/loopback configuration can be packaged.');
 }
}
function inventory(directory) {
 const root = noLinks(directory); if (!fs.statSync(root).isDirectory()) fail('Candidate directory required.');
 const files = []; let bytes = 0; const folded = new Set();
 function walk(folder, prefix = '', depth = 0) {
  if (depth > 12) fail('Payload directory depth exceeded.');
  for (const entry of fs.readdirSync(folder, { withFileTypes: true }).sort((a, b) => a.name.localeCompare(b.name, 'en'))) {
   const relative = safePath(prefix + entry.name); const target = path.join(folder, entry.name); const stat = fs.lstatSync(target);
   if (!prefix && entry.name === manifestName) { if (!stat.isFile() || stat.isSymbolicLink()) fail('Manifest must be a regular file.'); continue; }
   if (stat.isSymbolicLink()) fail('Symbolic link or reparse payload is not allowed.');
   if (stat.isDirectory()) { walk(target, relative + '/', depth + 1); continue; }
   if (!stat.isFile() || stat.nlink !== 1 || stat.size > maxFileBytes) fail('Unsupported payload file or size.');
   if (folded.has(relative.toLowerCase())) fail('Case-colliding paths are not allowed.'); folded.add(relative.toLowerCase());
   bytes += stat.size; if (bytes > maxBytes || files.length >= maxFiles) fail('Payload limit exceeded.');
   validateConfig(target, relative);
   const content = fs.readFileSync(target); const after = fs.lstatSync(target);
   if (after.isSymbolicLink() || after.ino !== stat.ino || after.size !== stat.size || after.mtimeMs !== stat.mtimeMs || content.length !== stat.size) fail('Payload changed during inspection.');
   files.push({ path: relative, bytes: content.length, sha256: hash(content) });
  }
 }
 walk(root); return files.sort((a, b) => a.path < b.path ? -1 : a.path > b.path ? 1 : 0);
}
function requiredPayload(directory, files) {
 const paths = new Set(files.map(x => x.path));
 for (const name of ['WarehouseRent.Api.dll', 'WarehouseRent.Infrastructure.dll', 'WarehouseRent.Api.deps.json', 'WarehouseRent.Api.runtimeconfig.json', 'appsettings.json', 'wwwroot/index.html'])
  if (!paths.has('app/' + name)) fail('Required published API/SPA file is missing.');
 if (files.some(x => !x.path.startsWith('app/'))) fail('Only the app payload is allowed outside the manifest.');
 const index = fs.readFileSync(path.join(directory, 'app/wwwroot/index.html'), 'utf8');
 const references = [...index.matchAll(/\b(?:src|href)\s*=\s*["']([^"']+)["']/gi)].map(m => m[1]);
 let script = false;
 for (const reference of references) {
  const pathname = reference.split(/[?#]/)[0];
  if (!pathname.startsWith('/assets/')) fail('SPA entry must reference only local built assets.');
  safePath(pathname.slice(1));
  if (!paths.has('app/wwwroot' + pathname)) fail('SPA references a missing built asset.');
  if (/\.js$/.test(pathname)) script = true;
 }
 if (!script) fail('Built SPA entry script is missing.');
}
export function createManifest(directory, input) {
 const root = noLinks(directory); const target = path.join(root, manifestName);
 if (fs.existsSync(target)) fail('Refusing to overwrite an existing release manifest.');
 const metadata = { schemaVersion: 1, application: 'WarehouseRent', artifactKind: 'local-demo-candidate', sourceRevision: input.sourceRevision,
  sourceDirty: false, migrations: input.migrations, latestExpectedMigration: input.migrations?.at(-1), tools: input.tools,
  createdAt: new Date().toISOString(), runtimeAcceptanceVerified: false, browserAcceptanceVerified: false, databaseMigrated: false };
 metadataValid(metadata); const files = inventory(root); requiredPayload(root, files);
 const manifest = { ...metadata, payloadSha256: hash(JSON.stringify(files)), files };
 fs.writeFileSync(target, JSON.stringify(manifest, null, 2) + '\n', { flag: 'wx' }); return inspectManifest(root, input.sourceRevision);
}
export function inspectManifest(directory, expectedRevision) {
 const root = noLinks(directory); const target = noLinks(path.join(root, manifestName));
 if (!fs.statSync(target).isFile() || fs.statSync(target).size > 2 * 1024 * 1024) fail('Invalid manifest file.');
 const manifest = JSON.parse(fs.readFileSync(target, 'utf8')); metadataValid(manifest);
 if (expectedRevision !== undefined && (!revisionPattern.test(expectedRevision) || expectedRevision !== manifest.sourceRevision)) fail('Candidate source revision does not match the expected revision.');
 if (!Array.isArray(manifest.files) || !manifest.files.length || manifest.files.length > maxFiles) fail('Invalid file inventory.');
 for (const file of manifest.files) { safePath(file.path); if (!Number.isSafeInteger(file.bytes) || file.bytes < 0 || !/^[0-9a-f]{64}$/.test(file.sha256)) fail('Invalid file digest.'); }
 const files = inventory(root); requiredPayload(root, files);
 if (JSON.stringify(files) !== JSON.stringify(manifest.files) || hash(JSON.stringify(files)) !== manifest.payloadSha256) fail('Payload inventory mismatch: modified, missing or extra file.');
 return { status: 'CONSISTENT_LOCAL_CANDIDATE', sourceRevision: manifest.sourceRevision, latestMigration: manifest.latestExpectedMigration,
  payloadSha256: manifest.payloadSha256, fileCount: files.length, runtimeAcceptanceVerified: false, browserAcceptanceVerified: false, databaseMigrated: false };
}
function main() {
 const [operation, ...rest] = process.argv.slice(2); const args = {};
 for (let i = 0; i < rest.length; i += 2) { if (!/^--[a-z-]+$/.test(rest[i]) || rest[i + 1] === undefined || args[rest[i]]) fail('Invalid manifest arguments.'); args[rest[i]] = rest[i + 1]; }
 let result;
 if (operation === 'create') result = createManifest(args['--directory'], { sourceRevision: args['--revision'], migrations: args['--migrations']?.split(','), tools: { dotnet: args['--dotnet'], node: args['--node'], npm: args['--npm'] } });
 else if (operation === 'inspect') result = inspectManifest(args['--directory'], args['--revision']);
 else fail('Use create or inspect.');
 process.stdout.write(JSON.stringify(result) + '\n');
}
if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
 try { main(); } catch { process.stderr.write('Release inspection failed. Check the candidate identity, payload, configuration and safe paths.\n'); process.exitCode = 1; }
}
