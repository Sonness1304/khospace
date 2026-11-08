# Local P1 demo candidate and acceptance

This process builds and inspects a synthetic local candidate. It does not publish a site, launch an API/browser or migrate an owner database. P0 browser/accessibility acceptance is still deferred. Source tests and successful packaging do not certify production readiness.

## Build a fresh candidate

Use the pinned .NET SDK/Node/npm and the application checkout, not the coordinator documentation checkout. Preserve/commit source changes first; packaging refuses a dirty source. From PowerShell7:

```powershell
./ops/package-demo.ps1
```

The script checks pinned tools and plain source/output paths, including generated dist/node_modules/bin/obj roots before build tools can clear or write them, restores locked dependencies, builds the SPA and publishes Release with the captured source SHA. Every run creates a new `.qa/releases/<sha-prefix>-<guid>/app`; `.qa/local-publish` and old candidates are never overwritten. Failures leave output for diagnosis. Do not use incomplete output lacking a valid manifest. Build logs/manifest do not contain database connection settings. Safe base logging/loopback appsettings only are included; local config/key/database files are refused. These guards are not a general secret scanner: review source and configuration before publication.

The manifest inventories every payload file, full source SHA, required migration IDs and pinned tool versions. It has no signing key or external provenance service. A person who can replace both payload and manifest can create a matching inventory; verify the source SHA against your trusted Git checkout/receipt. Do not execute scripts from an untrusted downloaded package.

## Inspect without starting anything

Use the candidate path printed by the package script; the following values are placeholders:

```powershell
./ops/inspect-demo.ps1 -Directory 'C:/AI/WarehouseRent-atlas-p0/.qa/releases/<candidate>' -ExpectedRevision '<full-source-SHA>'
```

Inspection is read-only. Missing/changed/extra files, unsafe paths/junctions, absent SPA entry assets, unsupported config or an unexpected source revision fail the whole check. It does not interpret a DLL or run migrations. Candidate paths must remain locally owned and unchanged during build/inspection; static path and hash checks do not claim to defeat an adversarial concurrent filesystem attacker.

## When runtime acceptance is separately resumed

The current task stops at local packaging. The historical API launch rejection remains recorded; this script is not an alternative launcher. The old owner-started API/database has not been stopped, upgraded or migrated. Use existing README commands only after runtime scope/host permission is actually available; do not overwrite an in-use folder, force-kill a process, reuse an old database as a fresh target or change machine permissions to make a check pass.

For an authorized manual run, use only a newly selected synthetic WarehouseRent_Demo_* target and the exact inspected candidate. Run the packaged DLL directly; source-project dotnet run builds different bytes. Example PowerShell7 commands below contain a candidate placeholder; replace it with the inspected path:

```powershell
$ErrorActionPreference = 'Stop'
Set-Location 'C:/AI/WarehouseRent-atlas-p0/.qa/releases/<candidate>/app'
$env:ASPNETCORE_ENVIRONMENT = 'Demo'
$demoDatabase = 'WarehouseRent_Demo_P1_' + [Guid]::NewGuid().ToString('N')
$env:ConnectionStrings__WarehouseRent = "Server=localhost;Database=$demoDatabase;Integrated Security=True;Encrypt=True;TrustServerCertificate=True"
dotnet .\WarehouseRent.Api.dll --seed-demo
if ($LASTEXITCODE -ne 0) { throw 'Synthetic schema/seed failed. Do not start this candidate.' }
dotnet .\WarehouseRent.Api.dll --urls http://127.0.0.1:5081
```

Seed performs the explicit one-shot migration and inserts synthetic data. Keep the chosen database name if resuming it later; rerunning this block creates a fresh name. Port5081 keeps the historical owner process on5080 separate; if5081 is occupied, choose a free loopback port instead of stopping another process. In another terminal read http://127.0.0.1:5081/health/build and /health/ready, then open the same root URL manually in the owner's Chrome profile. Normal startup never migrates. Do not put a connection string or secret in the bundle/manifest. Observe these independent checks:

| Readback | What it proves |
| --- | --- |
| Offline inspect with expected SHA | Current candidate bytes match their inventory and expected source label |
| GET /health/build | API assembly revision and latest expected migration; compare both with manifest |
| GET /health/live | The selected process can answer HTTP |
| GET /health/ready | SQL is reachable and this API sees no pending migrations and its foundation table |
| Role/scoped workflow acceptance | Actual user flow on the inspected running revision; source/component tests alone are insufficient |

A ready old process is still the wrong revision. A correct build label with readiness503 is not a usable demo. Do not report success by checking liveness alone. Build metadata exposes no connection string, SQL server/database name or filesystem path. In a developer run the assembly SHA is the last source commit, not proof the checkout was clean; the packaging script's clean-source check is separate.

## Demo and operational limits

The [demo walkthrough](demo-script.md) covers P0/P1 roles, renewal, inventory corrections, settlement, private documents, reports and notices. Raw invitation/reset tokens have no public extraction endpoint and external mail transport is not configured; that UI records queued instructions without claiming delivered email. Do not extract real account secrets to stage a demo. Private uploads require production malware scanning. In-app jobs are explicit local batches; no scheduler is started.

Use [backup/restore rehearsal](../ops/backup-restore.md) for synthetic restoration evidence. Same-instance restore does not establish cross-host Data Protection key recovery or RPO/RTO. Production TLS, retention, scale, incident response, legal/accounting policy, hosting/deployment and browser/a11y acceptance remain separate. No production or GitHub publication is performed by these commands.
