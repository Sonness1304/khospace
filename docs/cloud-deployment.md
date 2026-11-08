# KhoSpace cloud deployment

## Current state

Cloud deployment is not activated merely by building these files. The public Vercel preview stays frontend-only until the actual API revision and SQL schema pass acceptance. Never upload the repository root, local Demo database, `.qa`, user credentials or local OAuth configuration.

## Prerequisites

- Authenticated Railway account with explicitly approved capacity/cost. SQL Server Linux requires x64, at least 2 GB RAM and 6 GB disk; Express licensing is free, hosting is not. Free/trial entitlements may be insufficient. Do not upgrade plans automatically.
- Fresh private SQL service and persistent volume at `/var/opt/mssql`; API persistent volume at `/data`.
- Existing Vercel project `khospace` in scope `sonness-ielts`; leave unrelated projects unchanged.
- Google Web OAuth client configured privately for `https://khospace-eight.vercel.app/signin-google` before advertising Google login.

## Immutable build context

Commit reviewed source first, then run `node ops/export-cloud.mjs --output ABSOLUTE_FRESH_DIRECTORY`. The exporter reads committed blobs, excludes tests/generated/local data and pins `source-revision.txt`. Its receipt is integrity evidence, not Docker/cloud execution evidence.

API build: context is the export root, Dockerfile `ops/cloud/Dockerfile`. SQL build: context is exported `ops/cloud/sql`, Dockerfile `Dockerfile`. Railway config for API: `ops/cloud/railway.json`. Mount volumes at runtime; entrypoints refuse ephemeral storage and drop root privileges before starting SQL/API. Docker execution must be tested on the actual provider; local Docker is not assumed available.

## Private configuration

SQL: explicitly accept the license (`ACCEPT_EULA=Y`), use `MSSQL_PID=Express`, and generate a strong private `MSSQL_SA_PASSWORD`. Never publish a database TCP endpoint. Provision a separate least-privilege runtime database login after schema creation; SA must not be the permanent API identity.

API settings:
- `ASPNETCORE_ENVIRONMENT=Production`
- `CloudHosting__Enabled=true`
- `CloudHosting__PublicOrigin=https://khospace-eight.vercel.app`
- `CloudHosting__BackendHost`: exact generated Railway public hostname
- `CloudHosting__HealthCheckHost`: exact provider health hostname if required (no wildcard)
- `CloudHosting__SharedProxyKey`: random base64url secret, at least 32 characters
- `CloudHosting__DataProtectionPath=/data/keys`
- `ConnectionStrings__WarehouseRent`: encrypted private SQL connection; explicit database `WarehouseRent_Cloud_SERIAL`. TrustServerCertificate is permitted only for a private `.railway.internal` host, without explicit port.
- `CloudHosting__DatabaseName`: same exact database
- Bootstrap-only `CloudHosting__OperatorEmail` and `CloudHosting__OperatorPassword` (20–128 characters)

Set `KHOSPACE_INITIALIZE_CLOUD_ON_START=1` only for an explicitly approved first initialization/migration. It runs `--initialize-cloud` before the server and refuses existing customer promotion/password replacement. Remove the flag and bootstrap credentials after acceptance. Ordinary startup never migrates or seeds Demo data. Schema migration and operator initialization are separate transactions: a failed operator bootstrap may leave the schema migrated; read back before retrying. Persist and back up DataProtection keys privately along with database recovery procedures.

## Vercel activation gate

Use private process environment `KHOSPACE_API_ORIGIN=https://FIXED-HOST.up.railway.app` and `KHOSPACE_PROXY_KEY` matching the API key. Run `node ops/export-vercel.mjs --output ABSOLUTE_FRESH_DIRECTORY --connected`. It requires a clean source tree and exact live/build/ready responses; no fallback to localhost. The connected export contains a private routing secret in `.vercel/output/config.json`, protected before writing. Never commit/share that directory or print its configuration. Public assets and receipts exclude the key.

Publish only the prebuilt output to the existing KhoSpace project after the following real-provider checks. Export success alone does not prove end-to-end connectivity:
1. Direct API access is 403 without the proxy key; liveness alone is public.
2. Vercel forwards one trustworthy `X-Vercel-Forwarded-For` address; missing/malformed data is refused, no fallback to spoofed headers.
3. Correct public Host/HTTPS, independent Set-Cookie values, Secure session/CSRF cookies, login/logout, role isolation and real Google callback.
4. Raw request body and binary downloads retain hashes; test maximum 10 MiB private upload (~14 MiB JSON), signed payment notification integrity, no CDN caching. No real charge without separate merchant activation.
5. API readiness/build revision, fresh cloud data and persistent sessions survive a provider restart. Test backup/restore separately.

Use external rewrites, not a universal Node Function proxy (its payload cap would regress uploads). Provider body limits and header preservation remain unverified until measured.

## Recovery and limits

Keep the previous immutable Vercel deployment available. Reverting the frontend does not reverse SQL migrations or restore production data. The source-copy rollback under `.qa/khospace-transaction` is only a local source probe, not a cloud rollback. Preserve SQL/key volumes; never delete them as deployment cleanup.

If Railway is unauthorized, connect the Railway integration or run `railway login` privately in your own terminal. Do not paste tokens/passwords into chat. Missing authentication or an unapproved paid entitlement blocks provisioning, not local builds/tests.
