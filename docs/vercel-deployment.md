# KhoSpace on Vercel

## What this delivery publishes

The new web export is **frontend-only**, not a fully deployed warehouse service. It provides the
Vietnamese KhoSpace design, navigation, explanations and local space estimator. A persistent
notice explains the missing public backend. Account/password/Google forms are not mounted in
this build. No fake successful login, booking, inventory or payment response is provided.

The usual local .NET-hosted build is unchanged: no deployment mode is set by default, and existing
authentication and business panels retain their integrated behavior.

## Reproducible export

Commit and review the exact source first. Keep the worktree clean and remove private dotenv
files or any VITE environment overrides from this build environment. Never put secrets in
VITE variables. The exporter fails if these overrides/files are present.

```powershell
Set-Location 'C:\AI\WarehouseRent-atlas-p0'
node .\ops\export-vercel.mjs --output C:\AI\KhoSpace-vercel-UNIQUE
```

Choose a fresh absolute directory outside the repository; existing output is never overwritten.
The script typechecks and builds with only the dedicated frontend-only mode/revision variables,
validates the output and copies only allowlisted frontend assets into `.vercel/output/static`.
Build Output APIv3 configuration reserves all API/health/Google callback/payment-return routes
with503/no-store before the filesystem and SPA fallback. SPA fallback accepts only GET/HEAD.
There is no external proxy, CORS relaxation, local tunnel or API/SQL upload.

Only deploy that isolated directory, never the application checkout or `.qa`:

```powershell
# Confirm the already-authenticated account; no secrets are printed.
npm exec --yes --package=vercel@65.9.0 -- vercel whoami --non-interactive
# Link the isolated output to a new, explicitly named KhoSpace project.
npm exec --yes --package=vercel@65.9.0 -- vercel link --cwd C:\AI\KhoSpace-vercel-UNIQUE --project khospace --yes --non-interactive
npm exec --yes --package=vercel@65.9.0 -- vercel deploy --cwd C:\AI\KhoSpace-vercel-UNIQUE --prebuilt --prod --yes --non-interactive
```

Review the target account/scope before linking. Existing IELTS or other projects must not be
modified. No paid upgrade is authorized. If account access or quota is unavailable, stop that
publication and keep the inspected export; do not switch account or create replacement retries
after an unknown deployment outcome. Read back the existing deployment first.

## Acceptance after publication

Use the actual returned URL. Confirm deployment Ready, homepage/JS/CSS200 and metadata
`/khospace-deployment.json` containing exact sourceRevision and modefrontend-only. Verify reserved
`/health/ready`, `/api/v1/auth/me`, `/signin-google` and payment callbacks return503, not indexHTML200.
Verify both GET and non-GET requests fail closed without issuing a real business command.
Record cache/security headers, deep-link fallback and missing-asset404. HTTP checks are not
interactive visual/accessibility or Google-session acceptance.

## Before full-stack activation

An Internet-accessible production API and SQL database are still needed. Do not expose the owner
Demo DB or seed accounts. Decide hosting with the owner before procuring services/paid plans.
Vercel currently documents an OCI Container runtime as well as frontend hosting; account/runtime
eligibility for the .NET application and durable external services must be verified, not assumed.

Use a fixed same-origin proxy contract if API hosting is separate; reserve the full `/api` path,
health and `/signin-google`, including payment notification/return paths. Validate trusted
forwarded proxy scheme/host/IP, AllowedHosts, no-store, exact public Google OAuth callback,
host-only cookies, multiple Set-Cookie headers, CSRF and original mutation identity/version.
Configure durable shared DataProtection keys, private production secrets, backup/restore and
database migration separately. Keep payment providers disabled until sandbox and merchant
activation are deliberately accepted. Do not loosen authentication/cookies just to show a green
status. Never claim full-stack ready from a frontend deployment.

## Primary sources

- [Vercel Vite integration](https://vercel.com/docs/frameworks/frontend/vite)
- [Build Output API configuration](https://vercel.com/docs/build-output-api/configuration)
- [Prebuilt deployment CLI](https://vercel.com/docs/cli/deploy)
- [Vercel function runtimes](https://vercel.com/docs/functions/runtimes)
- [External rewrites](https://vercel.com/docs/routing/rewrites)
