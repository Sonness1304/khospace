# WP01 — Atlas implementation and evidence

Worktree: `C:/AI/WarehouseRent-atlas-p0`; branch: `codex/warehouse-p0`; approval baseline: `f916d7ff7ab59d1caeb665b126832baa83493d7b`. Only WP01 is assigned. Coordinator owns checkpoint/design documents. This report is implementation evidence, not owner or independent-review acceptance.

## Test-first checkpoint

The test-host and blank UI/API compile before behavior implementation. Runtime RED is confirmed:

| Command (repository root unless noted) | Exit | Result |
| --- | --- | --- |
| `dotnet test backend/WarehouseRent.sln --logger "trx;LogFileName=red.trx" --results-directory .qa/wp01/red` | 1 | 9 executed: 8 FAILED, 1 PASS. Missing health endpoints return404; missing seed validation does not refuse Production/unrelated database. |
| `npm test -- --reporter=verbose` in `frontend` | 1 | 4 executed, all FAILED: blank operations desk lacks live/checking/unavailable/recovery presentation. |

Local logs: `.qa/wp01/backend-red.txt`, `.qa/wp01/backend-red-exit.json`, `.qa/wp01/red/red.trx`, `.qa/wp01/frontend-red.txt`, `.qa/wp01/frontend-red-exit.json`. They are ignored local artifacts; this summary preserves the guarantees/results in Git.

Initial backend restore failed before tests because a transitive `System.Security.Cryptography.Pkcs 9.0.11` had NU1902/NU1903 advisories. This is **not** counted as runtime RED. Added an explicit patched10.0.14 reference, retained auditing/warnings-as-errors, then obtained the valid RED above. No vulnerability warning was suppressed. Frontend `npm install --ignore-scripts --no-fund` completed with0 vulnerabilities.

## Version provenance

Observed2026-10-28: .NET SDK10.0.403, Node24.19.0, npm11.17.0. Queried official NuGet flat-container version indexes and npm registry package metadata before pinning. Exact direct versions are in project/package files; transitive resolution is locked. TypeScript6.0.3 was selected over latest7.0.2 because typescript-eslint8.72.1 advertises `<6.1.0` compatibility. Node types24.19.2 match the runtime major. No global tool/config installation.

- [NuGet official registry](https://api.nuget.org/v3/index.json): EFCore SqlServer/Design, ASP.NET Mvc.Testing and dotnet-ef10.0.14; Test.Sdk18.11.0; xunit2.9.3; VS runner4.0.1; coverlet10.1.0; Pkcs10.0.14.
- [npm official registry](https://registry.npmjs.org/): React/DOM19.3.1, Vite8.3.5, plugin-react6.1.2, Vitest/coverage5.0.5, jsdom30.1.3, TypeScript6.0.3, test/lint/type packages pinned in `frontend/package.json`.
- [Microsoft EF SQL Server provider](https://learn.microsoft.com/en-us/ef/core/providers/sql-server/), [concurrency](https://learn.microsoft.com/en-us/ef/core/saving/concurrency) and [health checks](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks) guide the foundation; WP01 does not prove allocation/stock concurrency.

## Implemented scope

- SDK pin, classic `backend/WarehouseRent.sln`, API/Infrastructure/test projects, exact NuGet dependencies and committed lock files; local dotnet-ef manifest10.0.14.
- `/health/live` works without SQL; `/health/ready` returns minimal200/503 with a UTC observation time. Readiness verifies connection, applied migrations and the mapped table, with bounded connection/command/probe timeouts. Responses have no-store caching and do not expose connection strings/provider exceptions. No schema/seed runs on normal host startup.
- `AppDbContext`, generated initial SQL Server migration and a foundation provenance table. No customer/facility/booking/stock/payment feature entities were introduced.
- Explicit `--migrate` and `--seed-demo` commands. Seed requires exact Demo environment and a WarehouseRent Demo target before connection/migration; Production is refused. Repeated seed leaves exactly one synthetic marker. These commands are for local synthetic targets, not production schema deployment.
- React/TypeScript direction-A light operations desk: navy navigation, neutral canvas, teal actions, separate API/SQL observations, checking/unavailable/recovery states, timestamp labels, accessible refresh and responsive CSS. No fake counts, leases or live workers. UI GET readback does not execute business commands.
- Frontend unit coverage/lint/typecheck/build, browser QA definitions, local setup/verify scripts and CI definition with an isolated SQL Server service. Browser tests are supplied but **NOT RUN**; CI is defined but **NOT RUN remotely**.
- README setup section only. Existing documentation test originally traversed installed third-party Markdown and failed on `frontend/node_modules/ajv/json-schema.md`; it now excludes generated dependency/build/report folders. All repository-doc link/traceability assertions are retained. Coordinator explicitly accepted this foundation integration ruling.

Changed sources are grouped under `backend/`, `frontend/`, `.config/dotnet-tools.json`, `.github/workflows/ci.yml`, `global.json`, `ops/`, README setup, `tests/documentation.test.mjs` and this report. No checkpoint, design, requirements, Jarvis/Atlas root or provider/auth/config files were edited.

## Additional regression evidence

- Actual SQL RED: `dotnet test backend/WarehouseRent.sln --filter "Category=Sql" --logger "trx;LogFileName=sql-red.trx" --results-directory .qa/wp01/sql-red`, exit1: one executed test FAILED because no migration was applied. Generated `InitialFoundation`, then the same SQL behavior passed. Log: `.qa/wp01/sql-red.txt`.
- Completed-readback RED: `npm test -- --reporter=verbose`, exit1:1 FAILED/4 PASS. A still-armed7s timer changed healthy readback to unavailable. Cleared completion timers; regression passes. Log: `.qa/wp01/health-timeout-red.txt`.
- Independent-source RED: `npm test -- --reporter=dot`, exit1:1 FAILED/13 PASS. API liveness waited for a hanging SQL read. Separated source completion and limited timeout changes to unresolved sources; regression passes. Log: `.qa/wp01/independent-source-red.txt`.
- History preserves test-first checkpoints `b3b9a57627522382d140dcf9bf1398aa19fab9db` and `bb59057` on this branch. Do not squash away the evidence without retaining this report.

## Final local verification

| Command | Exit | Verified result / boundary |
| --- | --- | --- |
| `./ops/setup.ps1` | 0 | Locked restore, local EF tool restore, npm ci, SPA build and solution build. No schema change without explicit switch. Build0 warnings/0 errors. |
| `dotnet test backend/WarehouseRent.sln --collect:"XPlat Code Coverage" --logger "trx;LogFileName=final.trx" --results-directory .qa/wp01/final` | 0 | **16 PASS,0 FAIL,0 SKIP**. ASP.NET TestServer health/static-file tests and real SQL Server migration/seed/schema checks. Native Kestrel/browser acceptance is distinct. |
| `npm run test:coverage` in frontend | 0 | **14 PASS**,0 FAIL. Renderer/source-validation fixtures;97.67% statements,93.18% branches,100% functions/lines reported for configured App/health source scope. Main bootstrap, CSS and browser rendering are outside that coverage scope. |
| `npm --prefix frontend run lint` | 0 | No warnings. |
| `npm --prefix frontend run build` | 0 | TypeScript no-emit check and Vite build succeed; JS225.02kB /70.44kB gzip, CSS6.58kB /2.02kB gzip. No fonts/CDN added. |
| `node --test tests/documentation.test.mjs` | 0 | **4 PASS**,0 FAIL/0 SKIP after dependencies installed. |
| `./ops/verify.ps1` | 0 | Documentation4 PASS, backend15 PASS and frontend14 PASS/lint/build at that intermediate revision. Final backend rerun above adds the static-file test:16 PASS. |
| `dotnet publish backend/WarehouseRent.Api -c Release -o .qa/local-publish` | 0 | Local package includes `wwwroot/index.html`, CSS and JS. Build output only; nothing deployed. |
| `dotnet list backend/WarehouseRent.sln package --vulnerable --include-transitive` | 0 | No known vulnerable packages reported by configured current sources at observation time. |
| `npm --prefix frontend audit --audit-level=moderate` | 0 |0 vulnerabilities reported at observation time. |
| `git diff --check` | 0 | No whitespace errors. |

Backend coverage is approximately87% lines and below80% aggregate branch coverage; the collector does not cover the separate native one-shot command executions. The80% all-axis coverage aspiration is **not fully met**. Do not expand WP01 merely to improve percentages; independent review should assess the documented command/native-browser gaps. No coverage exclusions were added to conceal application code.

Local artifacts: `.qa/wp01/backend-final.txt`, `.qa/wp01/backend-final-exit.json`, `.qa/wp01/final/final.trx` and child `coverage.cobertura.xml`; `.qa/wp01/frontend-coverage.txt`, `frontend/coverage`; `.qa/wp01/setup.txt`, `.qa/wp01/verify.txt`, `.qa/wp01/verify-exit.json`, `.qa/wp01/publish.txt`. Logs are ignored local evidence, not historical runtime claims.

## Real SQL and one-shot command receipts

Observed SQL Server localhost integrated authentication: **15.0.2200.2, Developer Edition64-bit**. No server setting was changed. Test databases use generated `WarehouseRent_Demo_Test_<Guid>` names, created and deleted within the test. The final SQL integration checks an existing empty database is unavailable, applies the migration, seeds twice with one row, reads ready200, then removes the mapped table and verifies readiness503 before deleting that test database.

Explicit commands ran with `ConnectionStrings__WarehouseRent` targeting only new `WarehouseRent_Demo_AtlasP0_20261028`:

| Command / environment | Exit | Result |
| --- | --- | --- |
| `dotnet run --no-build --project backend/WarehouseRent.Api -- --migrate`, Demo | 0 | Initial migration applied. |
| `dotnet run --no-build --project backend/WarehouseRent.Api -- --seed-demo`, Demo | 0 | Synthetic marker written. |
| Same seed command repeated, Demo | 0 | Still one marker, no duplicated data. |
| Same seed command, Production | 2 | Refused before database access; no detailed connection output. |

`sqlcmd -S localhost -E -C -b -l 5 -d WarehouseRent_Demo_AtlasP0_20261028 -Q "SET NOCOUNT ON; SELECT MigrationId, ProductVersion FROM __EFMigrationsHistory; SELECT Id,Description,CreatedAt FROM FoundationMarkers;"`, exit0, read back migration `20261027172359_InitialFoundation`, EF10.0.14 and sole marker `synthetic-demo-v1`. UTC migration/event dates may precede the local October28 date. Command result receipt: `.qa/wp01/database-command-exits.json`.

That Demo database remains available for coordinator review; it contains only the bootstrap marker. No unrelated database was written/dropped. There is no production cleanup/migration operation and no application reset.

## Browser/native launch limitation — UNVERIFIED

Automatic approval review rejected the attempted `exec_command` action that would use PowerShell `Start-Process dotnet WarehouseRent.Api.dll --urls http://127.0.0.1:5080` with `-WindowStyle Hidden` in `.qa/local-publish`, after checking5080 was unused, for an isolated QA host. Stated reason: **"blocked by policy"**; no more specific reason was provided. The action was not executed. No alternative launch was attempted, and no approval bypass was used.

Consequently `npm run test:browser` was **NOT RUN**, no live browser screenshots were captured, and fresh native SPA→Kestrel→SQL acceptance is **UNVERIFIED**. TestServer+SQL verification and a populated publish folder do not substitute for this. Seven responsive viewport definitions, keyboard/readback checks and a labeled failure fixture are committed for later authorized QA.

No owned5080/5173 server process or browser was created. No existing Jarvis/Atlas/Workbench processes were stopped, restarted or reconfigured. A coordinator/operator may run the documented normal setup command when permitted, then execute browser QA; the rejection remains part of this record.

## Handoff and remaining work

Stop after WP01 for coordinator Claude/Gemini exact-SHA review. No merge, push, PR, deploy, worker/subagent dispatch or WP02 feature implementation occurred. Coordinator alone updates checkpoint/design/implementation ledger. The approved roadmap is not a claim that any later work package is complete.

Remaining: native/browser smoke and screenshots, remote CI SQL2022 execution (local verification used SQL2019), independent code review, and eventual WP02 identity/customer scope. The frontend is an honest foundation shell, not a bookable warehouse application. Seeded users and operational fixtures await their own packages.

## Self-evaluation (ecc:agent-self-evaluation)

Evidence-based internal review, not product acceptance: accuracy4/5 (local test/SQL evidence; native/browser unverified); completeness3/5 (WP01 code/build present, live end-to-end run blocked); clarity4/5 (README separates setup from historical design language; coordinator should later reconcile the header); actionability4/5 (exact local commands and test files; QA needs a permitted host launch); conciseness3/5 (detailed audit record intentionally longer than owner summary). Mean3.6/5. Highest-impact follow-ups: permitted browser smoke, independent review, then coordinator document-status reconciliation. The owner should judge the runnable foundation after those checks; no automatic acceptance is inferred.
