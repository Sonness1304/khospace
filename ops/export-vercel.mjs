import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { fileURLToPath, pathToFileURL } from 'node:url';

const sha = bytes => crypto.createHash('sha256').update(bytes).digest('hex');
// Exact provider-owned service origins only: no custom domains, ports or URL components.
const validApiOrigin = origin => typeof origin === 'string'
  && /^https:\/\/[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.(?:up\.railway\.app|onrender\.com)\/?$/.test(origin);
const inside = (parent, child) => { const relative = path.relative(parent, child); return relative === '' || !relative.startsWith(`..${path.sep}`) && relative !== '..' && !path.isAbsolute(relative); };
function assertPlainPath(value) {
  for (let cursor = path.resolve(value); ; cursor = path.dirname(cursor)) {
    if (fs.existsSync(cursor) && fs.lstatSync(cursor).isSymbolicLink()) throw Error('Unexpected symlink in export path');
    if (path.dirname(cursor) === cursor) break;
  }
}
function restrictNewPrivateDirectory(directory) {
  if (process.platform !== 'win32') { fs.chmodSync(directory, 0o700); return; }
  // Apply an owner/SYSTEM-only DACL to this newly created empty directory before writing a key.
  const result = spawnSync('pwsh', ['-NoProfile', '-NonInteractive', '-Command', `
    $ErrorActionPreference='Stop'
    $acl=[System.Security.AccessControl.DirectorySecurity]::new()
    $acl.SetAccessRuleProtection($true,$false)
    $sid=[System.Security.Principal.WindowsIdentity]::GetCurrent().User
    $acl.SetOwner($sid)
    foreach($id in @($sid,[System.Security.Principal.SecurityIdentifier]::new('S-1-5-18'))) {
      $rule=[System.Security.AccessControl.FileSystemAccessRule]::new($id,'FullControl','ContainerInherit,ObjectInherit','None','Allow')
      $acl.AddAccessRule($rule)
    }
    Set-Acl -LiteralPath $env:KHOSPACE_PRIVATE_OUTPUT -AclObject $acl
    if(-not (Get-Acl -LiteralPath $env:KHOSPACE_PRIVATE_OUTPUT).AreAccessRulesProtected) { throw 'Private export ACL missing' }
  `], { env: { ...process.env, KHOSPACE_PRIVATE_OUTPUT: directory }, encoding: 'utf8' });
  if (result.status !== 0) throw Error('Could not protect private connected export; no key was written');
}
export function createVercelExport({ dist, output, revision, connection }) {
  dist = path.resolve(dist); output = path.resolve(output);
  assertPlainPath(dist); assertPlainPath(output);
  if (!/^[a-f0-9]{40}$/.test(revision)) throw Error('Invalid source revision');
  if (inside(dist, output) || inside(output, dist)) throw Error('Export must be outside its source directory');
  if (fs.existsSync(output)) throw Error('Export output already exists');
  const files = [];
  function inspect(directory) {
    if (fs.lstatSync(directory).isSymbolicLink()) throw Error('Unexpected symlink in export');
    for (const name of fs.readdirSync(directory)) {
      const absolute = path.join(directory, name), relative = path.relative(dist, absolute).split(path.sep).join('/');
      const info = fs.lstatSync(absolute);
      if (info.isSymbolicLink() || name.startsWith('.')) throw Error('Unexpected export file or link');
      if (info.isDirectory()) { if (!['assets', 'images'].includes(relative)) throw Error(`Unexpected export directory: ${relative}`); inspect(absolute); continue; }
      if (!info.isFile() || !(
        ['index.html', 'khospace-deployment.json'].includes(relative) ||
        /^(?:assets|images)\/[a-zA-Z0-9_./-]+\.(?:js|css|woff2?|svg|png|webp|jpe?g|ico)$/.test(relative)
      )) throw Error(`Unexpected export file: ${relative}`);
      if (info.size > 5 * 1024 * 1024) throw Error('Export asset too large');
      const bytes = fs.readFileSync(absolute);
      files.push({ path: relative, bytes: bytes.length, sha256: sha(bytes), data: bytes });
    }
  }
  inspect(dist);
  if (files.reduce((total, file) => total + file.bytes, 0) > 20 * 1024 * 1024) throw Error('Export too large');
  if (!files.some(file => file.path === 'index.html')) throw Error('Missing frontend index');
  const metadata = JSON.parse(files.find(file => file.path === 'khospace-deployment.json')?.data.toString() ?? '{}');
  const mode = connection ? 'cloud-connected' : 'frontend-only';
  if (metadata.mode !== mode || Object.keys(metadata).some(key => !['mode', 'sourceRevision'].includes(key))) throw Error('Invalid export mode metadata');
  if (metadata.sourceRevision !== revision) throw Error('Export source revision mismatch');
  if (connection && !validApiOrigin(connection.origin))
    throw Error('A fixed public HTTPS Railway or Render API origin is required');
  if (connection && (typeof connection.key !== 'string' || !/^[A-Za-z0-9_-]{32,128}$/.test(connection.key)))
    throw Error('A strong private server-only proxy key is required');
  if (connection && files.some(file => file.data.includes(Buffer.from(connection.key))))
    throw Error('A private gateway key must never appear in public assets');
  const config = { version: 3, routes: [
    { src: '/.*', headers: { 'Cache-Control': 'no-store', 'X-Content-Type-Options': 'nosniff', 'Referrer-Policy': 'no-referrer',
      'X-Frame-Options': 'DENY', 'X-KhoSpace-Deployment': mode,
      'Content-Security-Policy': "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; connect-src 'self'; form-action 'self'; frame-ancestors 'none'; base-uri 'none'" }, continue: true },
    { src: '^/(?:api|health)(?:/.*)?$|^/signin-google(?:/.*)?$|^/payments/return(?:/.*)?$', caseSensitive: false, status: 503,
      headers: { 'Cache-Control': 'no-store', 'Retry-After': '3600' } },
    { src: '^/(?:\\.|backend(?:/|$)|ops(?:/|$)).*$', status: 404, headers: { 'Cache-Control': 'no-store' } },
    { src: '^/khospace-deployment\\.json$', headers: { 'Cache-Control': 'no-store' }, continue: true },
    { handle: 'filesystem' },
    { src: '^/(?:assets|images)/.*$', status: 404 },
    { src: '/.*', methods: ['GET', 'HEAD'], dest: '/index.html', headers: { 'Cache-Control': 'no-store' } },
    { src: '/.*', status: 405, headers: { Allow: 'GET, HEAD', 'Cache-Control': 'no-store' } },
  ] };
  if (connection) {
    // Transform applies only on the server-side external route; it replaces spoofed client keys.
    config.routes[1] = {
      src: '^(/(?:api|health)(?:/.*)?|/signin-google(?:/.*)?|/payments/return(?:/.*)?)$',
      caseSensitive: false, dest: `${connection.origin.replace(/\/$/, '')}$1`,
      headers: { 'Cache-Control': 'no-store', 'CDN-Cache-Control': 'no-store', 'Vercel-CDN-Cache-Control': 'no-store' },
      transforms: [{ type: 'request.headers', op: 'set', target: { key: 'X-KhoSpace-Proxy-Key' }, args: connection.key }],
    };
  }
  // A fresh, isolated output is the only upload root; repository contents are never copied.
  fs.mkdirSync(output, { mode: 0o700 });
  if (connection) restrictNewPrivateDirectory(output);
  const target = path.join(output, '.vercel/output');
  fs.mkdirSync(path.join(target, 'static'), { recursive: true });
  fs.writeFileSync(path.join(target, 'config.json'), JSON.stringify(config, null, 2));
  fs.writeFileSync(path.join(output, 'vercel.json'), JSON.stringify({ framework: null }));
  for (const file of files) {
    const destination = path.join(target, 'static', file.path);
    fs.mkdirSync(path.dirname(destination), { recursive: true });
    fs.writeFileSync(destination, file.data);
    if (sha(fs.readFileSync(destination)) !== file.sha256) throw Error('Export byte verification failed');
  }
  return { mode, sourceRevision: revision, output,
    files: files.map(({ data: _data, ...file }) => file), configSha256: sha(fs.readFileSync(path.join(target, 'config.json'))),
    runtimeAcceptanceVerified: false, backendConnected: false };
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
  function git(...args) {
    const result = spawnSync('git', args, { cwd: root, encoding: 'utf8' });
    if (result.status !== 0) throw Error('Git inspection failed');
    return result.stdout.trim();
  }
  const args = process.argv.slice(2);
  const connected = args.length === 3 && args[2] === '--connected';
  if ((!connected && args.length !== 2) || args[0] !== '--output' || !path.isAbsolute(args[1])) throw Error('Usage: node ops/export-vercel.mjs --output ABSOLUTE_FRESH_DIRECTORY [--connected]');
  const output = path.resolve(args[1]);
  if (inside(root, output) || inside(output, root) || fs.existsSync(output)) throw Error('Fresh external export directory required');
  if (git('status', '--porcelain')) throw Error('Commit reviewed source before export');
  const revision = git('rev-parse', 'HEAD');
  if (Object.keys(process.env).some(key => key.startsWith('VITE_'))) throw Error('Remove VITE environment overrides before export');
  if (fs.readdirSync(path.join(root, 'frontend')).some(name => name.startsWith('.env') && name !== '.env.example')) throw Error('Dotenv files must not be present for export');
  const connection = connected ? { origin: process.env.KHOSPACE_API_ORIGIN, key: process.env.KHOSPACE_PROXY_KEY } : undefined;
  if (connected) {
    if (!validApiOrigin(connection.origin)
      || !/^[A-Za-z0-9_-]{32,128}$/.test(connection.key ?? '')) throw Error('Private cloud origin/key configuration is missing or invalid');
    // Do not replace the truthful public preview until the actual backend schema is ready.
    for (const probe of ['live', 'build', 'ready']) {
      const result = await fetch(`${connection.origin.replace(/\/$/, '')}/health/${probe}`, {
        redirect: 'manual', headers: { 'X-KhoSpace-Proxy-Key': connection.key, 'x-vercel-forwarded-for': '0.0.0.0' },
        signal: AbortSignal.timeout(15000),
      });
      if (!result.ok) throw Error('Cloud backend acceptance unavailable; public deployment was not changed');
      const body = await result.json();
      if (probe === 'build' ? body.sourceRevision !== revision : body.status !== (probe === 'live' ? 'live' : 'ready'))
        throw Error('Cloud backend source/readiness mismatch; public deployment was not changed');
    }
  }
  const env = { ...process.env, VITE_KHOSPACE_MODE: connected ? 'cloud-connected' : 'frontend-only', VITE_KHOSPACE_REVISION: revision };
  // Private gateway values are not needed by Vite and are removed from its child environment.
  delete env.KHOSPACE_API_ORIGIN; delete env.KHOSPACE_PROXY_KEY;
  for (const command of [
    ['node_modules/typescript/bin/tsc', '--noEmit'],
    ['node_modules/vite/bin/vite.js', 'build'],
  ]) {
    const result = spawnSync(process.execPath, command, { cwd: path.join(root, 'frontend'), env, stdio: 'inherit' });
    if (result.status !== 0) throw Error('Frontend-only build failed');
  }
  if (git('rev-parse', 'HEAD') !== revision || git('status', '--porcelain')) throw Error('Source changed during build');
  const receipt = createVercelExport({ dist: path.join(root, 'frontend/dist'), output, revision, connection });
  const ledger = path.join(root, '.qa/vercel'); fs.mkdirSync(ledger, { recursive: true });
  fs.writeFileSync(path.join(ledger, 'export-receipt.json'), JSON.stringify(receipt, null, 2));
  console.log(JSON.stringify(receipt, null, 2));
}
