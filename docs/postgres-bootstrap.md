# PostgreSQL migration, bootstrap and Render Free handoff

## Scope and target lock

- Application: `C:/AI/WarehouseRent-atlas-p0`, branch `codex/warehouse-p0`.
- Dedicated Supabase project: `khospace`, ref `jhemdameyrcxqxsffugw`, Singapore, Sonness1304's Org. Creation was quoted at USD0/month and read back healthy. Recheck the account plan before deploying; no paid branch, IPv4 add-on, disk or upgrade is authorized.
- Protected IELTS ref: `uwjglyucmyrjxzzryhth`. Never connect, migrate, restore, read credentials from or reuse that project.
- SQL Server remains the default. PostgreSQL is an explicit cloud-only selection; invalid settings never fall back.

## Migration contract

`PostgresMigrations` uses a separately versioned **frozen SQL catalog**, not EF's historical SQL Server migration assembly. Version `001_initial` is an embedded resource under `Persistence/PostgresMigrations`. Its canonical-LF SHA256 and a schema/ACL/policy/sequence fingerprint are recorded in private `khospace."MigrationHistory"`. This is application migration history, not Supabase CLI migration history.

This bounded initial catalog was chosen over regenerating DDL on every startup: a model change must receive a separately reviewed migration, never silently alter an already deployed baseline. Never edit an applied catalog in place, delete its history or use `EnsureCreated` as an upgrade path. There is deliberately no destructive production downgrade. Restore a separately verified backup into a NEW approved project for recovery; the source rollback supplied with QA does not roll back a database.

Initialization obtains a transaction-owned advisory lock, refuses unversioned application objects, applies all initial DDL and its history atomically, and verifies checksums/fingerprint on repeat. A failed statement rolls back the entire application schema. A cancelled/unknown outcome is read back before retry. Drift, unknown history, disabled token triggers/RLS, unexpected policies/ACLs or changed sequence parameters prevent readiness/reinitialization; none is silently repaired. Explicit owner role provisioning verifies the old fingerprint under the same migration mutex before updating known grants/policies and recording the new fingerprint atomically. Runtime may not bless drift. Incoming role memberships are refused except trusted postgres/supabase_admin or superuser creator administration; outgoing inherited roles and Data API roles with direct/transitive runtime membership are refused. Normal API startup does not migrate or seed.

The schema includes8-byte concurrency tokens, translated constraints/indexes/collations, encrypted append-only DataProtection keys and RLS. Existing business immutability stays in `AppDbContext.SaveChanges` guards and parent-row locks; historical business triggers did not exist. RLS is defense in depth: a private server role may access app rows, while application authorization enforces tenant boundaries. This is NOT a Supabase-auth user-scoped policy and must not be granted to browser/Data API roles.

## Separate database principals and private configuration

Runtime uses `khospace_api` (LOGIN, no superuser/createDB/createRole/replication/BYPASSRLS/memberships/schema creation). The role gets table DML, but only SELECT on migration history and SELECT/INSERT on encrypted keys; no TRUNCATE, key mutation, sequence reset or DDL. Provisioning creates it NOLOGIN first, then activates ONLY a freshly created role with the supplied private runtime password (20–128 characters) in the same transaction, using parameterized transaction-local settings and server-side identifier/literal quoting. This makes a fresh initialization one-shot. Existing roles are never reactivated or have their password reset automatically; repair an existing NOLOGIN/misconfigured role privately as an administrator. No demo identity is installed remotely.

Migration owner is a separate `postgres` connection, supplied only for explicit initialization and removed before the long-running API process. Both connections must bind to this project with TLS `VerifyFull`; direct host or the project's session pooler on5432 is accepted, not transaction pooler6543. Use the actual Singapore pooler hostname from this project's Connect dialog, not a guessed hostname. Client pool<=5 and bounded query timeouts remain enforced. An SSL certificate validation failure is not a reason to disable verification or buy an IPv4 add-on. The shared pooler uses the Supabase Root 2021 CA. Download the certificate through the project Database Settings SSL Configuration link and verify its SHA256 `700723581420dd1ac98fd7e9ac529f0ef210eadcaf87fc868a3ad7d114c2f3b7` (root thumbprint `A4518A0933AF6949482CCA3014C007C369DF9F6F`). The reviewed Render image copies this PUBLIC certificate to `/app/certificates/supabase-prod-ca-2021.crt` and validates its digest during build. Set `Root Certificate=/app/certificates/supabase-prod-ca-2021.crt` in the private Render runtime connection; use the verified local certificate path for local initialization. Keep `SSL Mode=VerifyFull`. No global trust-store installation or TLS acceptance callback is used by the application.

Required private settings (enter in the provider's protected environment UI, never chat/source/frontend/logs):

```text
ASPNETCORE_ENVIRONMENT=Production
Database__Provider=PostgreSQL
Database__SupabaseProjectRef=jhemdameyrcxqxsffugw
Database__ProtectedProjectRef=uwjglyucmyrjxzzryhth
Database__ExpectedHost=HOST
CloudHosting__Enabled=true
CloudHosting__DataProtectionStore=PostgreSQL
CloudHosting__PublicOrigin=https://khospace-eight.vercel.app
CloudHosting__SharedProxyKey=TOKEN
CloudHosting__DataProtectionEncryptionKey=BASE64_32_BYTE_PRIVATE_ROOT_KEY
ConnectionStrings__WarehouseRent=PRIVATE_RUNTIME_CONNECTION
```

For one explicit initialization only, additionally supply:

```text
ConnectionStrings__WarehouseRentMigration=PRIVATE_MIGRATION_OWNER_CONNECTION
CloudHosting__OperatorEmail=PRIVATE_OPERATOR_EMAIL
CloudHosting__OperatorPassword=PRIVATE_PASSWORD_20_TO_128_CHARACTERS
KHOSPACE_INITIALIZE_CLOUD_ON_START=1
```

Use `dotnet WarehouseRent.Api.dll --initialize-cloud` on a private trusted host, or the explicit Render entrypoint flag on the first boot. The command validates both bindings and operator inputs before DDL, applies the catalog/role grants, checks runtime privilege restrictions, then creates an operator only in an empty identity catalog. Repeating identical credentials is idempotent; a matching customer is never promoted, and existing passwords are not reset. Invalid configuration returns the sanitized initialization error and no public server starts.

After successful initialization, remove migration-owner/operator secrets from the provider configuration and set the flag back to0; the shell also unsets them in the API child environment. Keep the encryption root key stable in the provider's secret store across restarts and protect an offline recovery copy. Losing it loses the ability to decrypt persisted auth keys. Do not store it on Render's ephemeral filesystem.

## Connect Render to Codex (OAuth, no pasted API key)

The installed CLI help and official Render MCP documentation were checked. Run in PowerShell:

```powershell
codex mcp add render --url https://mcp.render.com/mcp --oauth-client-id codex
```

Complete authorization in Chrome using the owner's `luongson8648` profile. If the server is already configured but not authenticated:

```powershell
codex mcp login render
```

Reload Codex if the tools have not appeared, then ask it to list Render workspaces/services and select the correct workspace. Browser sign-in alone is not CLI/MCP authentication. Alternatively enable the official Render integration in Codex and finish its authorization. Do not create duplicate configurations or replace an existing server definition without checking it first. Never paste a token or OAuth callback into this chat.

Render reference: https://render.com/docs/mcp-server . Local `codex mcp add --help` and `codex mcp login --help` were also inspected. No account login or global Codex configuration was changed by this handoff.

## Free deployment gates

Use only the reviewed committed Render context exported by `ops/export-cloud.mjs --output ABSOLUTE_FRESH_DIRECTORY --provider render`. It includes the exact frozen migration resource and an exact source-revision marker, no `.env`, owner credentials, frontend artifacts or QA data. The Blueprint selects a **Free Docker Web Service** in Singapore, not Render Postgres, a worker, disk or paid plan. Its environment overrides the image's historical SQL Server default. The secret root/proxy/database values must be provided privately.

Before creating a service inspect the selected workspace and actual service quote. Free compute does not by itself prevent charges for extra build/bandwidth usage. For a zero budget, verify no automatic supplementary billing/payment fallback can occur and appropriate limits are active; stop rather than upgrade if this cannot be confirmed. Free sleeps on idle and has ephemeral disk; Render's Free PostgreSQL expires, so persistent app data remains in the dedicated Supabase project. Do not promise always-on performance or unlimited usage.

Official limits: https://render.com/docs/free . No paid option, account switch, secret extraction, public localhost tunnel or bypass of disabled browser controls is authorized.

Only connect Vercel after the real API reports the exact build/provider/catalog checksum, readiness200 under the trusted proxy boundary, and actual auth/callback/cookie/body/restart tests succeed. Register the public Google callback separately from localhost. A successful local test, service creation or `/health/live`200 does not establish full-stack acceptance. Keep the frontend-only notice and503 routes until these gates pass.

## Tests and recovery boundaries

Migrations/bootstrap tests create only generated catalogs on the agent-owned loopback PostgreSQL17.11 cluster. They exercise atomic DDL failure, repeat/concurrent init, drift, key/RLS/token tampering, least-privilege refusal, no customer promotion, readiness and encrypted auth-cookie reuse across independent hosts. `WAREHOUSERENT_TEST_PROVIDER=PostgreSqlFixture` runs selected shared HTTP business tests on this migrated fixture, never a configurable remote PG connection. Normal tests continue using isolated SQL Server fixtures.

This does not replace full payment-provider certification, every business/concurrency scenario on PostgreSQL, cloud backup/restore, browser QA, Google OAuth or actual provider acceptance. See the coordinator checkpoint and `.qa/pg-bootstrap` receipts for exact runs rather than treating this document as evidence that deployment occurred.
