# WP02 — identity, customer organizations and staff scope

Approval base: `3f636e993be180a4af6aaa0b0cf470f5aac428b1`; isolated worktree `C:/AI/WarehouseRent-atlas-p0`, branch `codex/warehouse-p0`. Coordinator checkpoint at `C:/AI/WarehouseRent/docs/project-checkpoint.md` was read; WP01 source accepted for downstream implementation, native/browser remains UNVERIFIED. This report is not owner acceptance.

Scope: WP02 only; Identity local users, revocable protected cookie, CSRF, current organization/facility scope, existing-user memberships, last-admin concurrency and transactional security audit. No WP03 features, invitations/password reset, server/provider/account changes, app launch, worker or external publication.

Version/source check2026-10-28: official [NuGet Identity EF registry](https://api.nuget.org/v3-flatcontainer/microsoft.aspnetcore.identity.entityframeworkcore/index.json) contains10.0.14, matching existing EF/ASP.NET10.0.14. References: [Microsoft Identity](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity?view=aspnetcore-10.0), [protected cookie and validation events](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/cookie?view=aspnetcore-10.0), [ASP.NET antiforgery](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0). No global tooling changes.

## Ownership and completion evidence

Atlas committed RED `dbf54c897fbc75fe0cff6a1b1fbbe2f9bfcf4614`. Its resumed turn failed explicitly with workspace-out-of-credits. After inactive readback Jarvis became the sole writer, preserved Atlas's code and completed WP02 inline. This filename preserves continuity; this is not an Atlas completion claim.

Implemented Identity10.0.14, HttpOnly/SameSiteStrict protected cookies, SQL-backed revocable sessions, generic credential failures and Identity password lockout. CSRF required before API mutations. Current database memberships/facility grants authorize access; no client/cookie role trust. Membership administration uses organization parent locks, rowversion preconditions, actor/org-scoped command receipts, transactional audit and last-active-admin protection.

UI A adds login/logout and explicit organization/facility context, access readback and unavailable/unknown handling. No credentials/tokens in localStorage. Lost mutation replies cause one access readback, never automatic mutation replay. Health Refresh preserves focus using aria-disabled and a handler guard; cards stack below768px. Operational modules remain pending.

- Inherited backend: `dotnet test backend/WarehouseRent.sln --no-restore --logger 'console;verbosity=minimal'`, exit0,27PASS.
- Refresh RED: frontend exit1 because control natively disabled during refresh; aria-disabled+guard fixes it and prevents duplicate requests.
- Auth UI RED with empty component: `npm --prefix frontend test -- src/features/auth/SessionPanel.test.tsx`, exit1,3FAIL for missing login/context/unavailable behavior; now GREEN, plus lost-logout-response regression.
- Health regression: temporarily omitted inherited health bypass, `dotnet test backend/WarehouseRent.sln --no-restore --filter FullyQualifiedName~SessionHealthTests`, exit1, live500 instead of200. Restored original fix; copied authenticated cookie against unreachable SQL now yields live200/ready503. No server configuration changed.
- Relogin RED: `dotnet test backend/WarehouseRent.sln --no-restore --filter FullyQualifiedName~Signing_in_as_another_user`, exit1, copied prior cookie incorrectly returned200. Revoke the prior authenticated user's session rather than matching the new user; now GREEN.
- `./ops/verify.ps1`, exit0: docs4/backend29/frontend19PASS, lint/typecheck/buildPASS. Real SQL covers logout/cookie replay, current scope/revocation, forged nested IDs, version preconditions, idempotency, concurrent admin removal and transaction rollback.
- Expanded coverage to frontend feature source: `npm --prefix frontend run test:coverage`, exit0; reported86.36% statements/81.88% branches/84.37% functions/92.47% lines. Bootstrap/CSS/browser layout excluded from unit coverage.

Guarded Demo users share PUBLIC synthetic password `WarehouseRent_Demo!42`: admin.a/admin2.a/member.a/admin.b/clerk/operator/unassigned at `example.test`. Never use for real accounts. Normal startup never migrates/reseeds. Tests delete only their generated synthetic databases; WP01 retained demo DB was not silently updated.

Native/browser and remote CI NOT RUN. Prior policy refusal of native API QA launch remains; no alternate launch attempted. Real HTTP above means ASP.NET TestServer with cookies/SQL, not browser acceptance. Exact-SHA model reviews tracked separately by coordinator. No push/merge/deploy or provider/account/server changes.
