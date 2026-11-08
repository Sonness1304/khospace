# KhoSpace: free cloud deployment

This runbook implements the owner-approved zero-hosting-spend deployment: existing Vercel frontend, Render Free .NET API, Azure SQL free-tier database. It is a demo/portfolio configuration, not an always-on production guarantee. Source tests/builds are not proof of real provider readiness.

## Cost boundary

Never buy VPS, upgrade compute, attach a paid disk, provision Render Postgres, enable Azure BillOverUsage, add paid monitoring/security/backup add-ons or attach a payment method to solve a quota failure. Render Free can suspend services/builds at limits and sleeps when idle. An already billing-enabled Render workspace needs an effective no-overage configuration verified before deployment; a usage alert alone is not a spending cap. If no effective zero-spend control can be confirmed, do not provision there. Azure SQL must read back useFreeLimit=true and freeLimitExhaustionBehavior=AutoPause; the free allowance is monthly and bounded, not unlimited.

## Local evidence and export

Use the exact reviewed clean commit. Export with `node ops/export-cloud.mjs --output ABSOLUTE_FRESH_DIRECTORY --provider render`. This exports committed allowlisted build inputs, pins source-revision.txt, excludes generated/local/private files, and includes only the selected Render deployment files. Use the custom Blueprint path `ops/cloud/render/render.yaml`; do not deploy from the repository root containing ignored local data. The Render Dockerfile-specific ignore file is defense in depth; an isolated export remains the upload/build root.

The Blueprint declares Free Docker compute and no database/disk. It references private environment values via sync:false; enter them only in the authenticated provider console/API, never chat/Git. Do not apply a name that collides with an unrelated service. BackendHost and HealthCheckHost are pinned to the exact RENDER_EXTERNAL_HOSTNAME by the startup script. Ordinary startup does not migrate or seed.

## Account and database prerequisites

Owner authenticates Microsoft/Azure and Render privately. Azure requires an enabled eligible subscription; account activation/card verification is not permission to enable paid overage. A portable Azure CLI may be used without installing globally or modifying PATH. No authenticated subscription means no resource creation.

The Azure helper accepts an explicit subscription GUID, resource group, existing dedicated SQL logical server and exact new WarehouseRent_Cloud_* database. It does not create/login to accounts or silently select a subscription. An existing unrelated logical server is not a deployment target. Confirm TLS>=1.2 and cost-safe account eligibility first. New logical-server creation, private bootstrap credentials and tightly scoped SQL firewall rules are coordinated separately after authentication; never open all IPs or reuse the local Demo database.

Offline preview (no cloud request):

```powershell
pwsh -NoProfile -File .\ops\azure-free.ps1 -Action Plan -Subscription SUBSCRIPTION_GUID -ResourceGroup khospace-free -Server KHOSPACE_SERVER -Database WarehouseRent_Cloud_SERIAL
```

After account/server verification, `-Action Inspect` reads existing settings; `-Action Create` can create only a new free-limit database using AutoPause/Local backup redundancy, refusing existing paid targets. Pass `-AzureCli ABSOLUTE_AZ_CMD_PATH` for portable CLI. Missing free flags/refused service requests stop with no paid fallback. On ambiguous create outcome inspect before retry; the helper will never delete, upgrade or modify a paid existing database. A free entitlement failure is a blocker, not permission to substitute a normal serverless database.

## Private Render API settings

- ASPNETCORE_ENVIRONMENT=Production, CloudHosting__Enabled=true
- CloudHosting__DataProtectionStore=SqlServer
- ConnectionStrings__WarehouseRent: full certificate-validating encrypted Azure SQL connection; TrustServerCertificate=False. Use a separate least-privilege runtime SQL principal after explicit schema setup. No permanent SA/admin identity for normal requests.
- CloudHosting__DataProtectionEncryptionKey: random base64 encoding of exactly32bytes, generated once privately. Persist in server secrets/private recovery storage, never rotate/delete casually.
- CloudHosting__SharedProxyKey: random base64url gateway key (32–128 chars), matching the private Vercel export environment.
- CloudHosting__PublicOrigin=https://khospace-eight.vercel.app
- Google client settings only after public callback registration/verification, privately.

The SQL key table is migrated explicitly. Master-key secrets are encrypted with AES256-GCM using a root key kept outside SQL. SQL key rows are append-only. New API hosts must recover the same keys without local disk; invalid/unavailable store or wrong root key fails closed, no ephemeral fallback. Query/connect timeouts remain bounded; cold Azure SQL can make the first startup fail/retry at the provider. A durable-key backup and safe root-key recovery procedure are required before real customer data.

First initialization is explicit and opt-in: configure the exact confirmed CloudHosting__DatabaseName plus private OperatorEmail/OperatorPassword and temporarily KHOSPACE_INITIALIZE_CLOUD_ON_START=1. Start completes --initialize-cloud before normal API and removes bootstrap credentials from its child environment. Once accepted, remove the bootstrap flag/credentials from provider configuration; do not leave repeated migrations enabled. It creates no Demo data and refuses customer promotion/password reset. Unknown initializer outcome requires readback, not blind retry.

## Activate public web only after actual acceptance

Current public frontend-only site is deliberately retained until actual backend build/schema/auth acceptance. A connected export accepts a fixed single-label HTTPS *.onrender.com API origin via private KHOSPACE_API_ORIGIN and KHOSPACE_PROXY_KEY, checks live/build/ready against the current exact commit, then writes a protected private Build Output routing config. Never publish/share that private export or print its config; deploy only prebuilt output to the existing KhoSpace Vercel project. Existing unrelated IELTS projects are out of scope.

Before activation, observe actual provider header transform, trustworthy single platform IP, pinned Host/HTTPS, independent Set-Cookie handling, CSRF/login/logout/role isolation, Google callback, raw signed payment body/binary response hashes, maximum private upload, no-store and restart/key persistence. No real payment charge is part of this deployment. Browser controls previously disabled are not bypassed.

Azure SQL is not a full SQL Server VM: local filesystem BACKUP/RESTORE procedures and server-admin operations are not claimed compatible. The business migration/schema must pass on the real fresh Azure database before acceptance. Retain local recovery guides as local-only. Export/restore rehearsal and provider recovery are separate follow-up acceptance.

Rollback: preserve the previous Vercel deployment and existing local source/package. Source-copy ROLLBACK.sh under .qa is not a production database rollback. Keep cloud SQL keys/data intact; no automatic resource deletion, forced cleanup or destructive downgrade.
