# Public customer authentication

## Implemented contract

Public registration is `POST /api/v1/auth/register` with JSON `{ "name": "Workspace name", "email": "person@example.test", "password": "..." }`. Obtain a fresh `/api/v1/auth/csrf` token and cookie first, and send the token as `X-CSRF-Token`. The frontend confirms the password locally; the server remains authoritative for validation.

Workspace names are trimmed, nonempty, at most 200 characters and contain no control characters. Emails are trimmed, validated, limited to 256 characters and compared using Identity normalization. Passwords are limited to 1024 characters and use the existing Identity policy: at least 12 characters, uppercase, lowercase, a digit and a non-alphanumeric character. Identity username restrictions also apply to email addresses. The identity-directory transaction lock serializes public registration with existing membership, invitation and account writers; `RequireUniqueEmail` is enabled.

A successful request atomically creates one active Identity user, one new isolated `CustomerOrganization`, one `CustomerAdmin` membership, a `CustomerRegistered` audit, and the existing audited revocable eight-hour login session. The user has no operator or staff grants. Local registration does not claim verified email or SMTP delivery. The response is `{ "authenticated": true }`; refresh CSRF and read `/api/v1/auth/me` to obtain current scopes. Registration and password-login replacements revoke the current session. Workspace selection belongs to the frontend; existing multiple memberships continue to require an explicit selection.

Validation returns 400 with `INVALID_REGISTRATION` or `PASSWORD_POLICY`. A normalized email/username collision returns 409 `REGISTRATION_UNAVAILABLE`, with no partial user/workspace. Database failures produce the existing generic problem response and transaction rollback. Do not replay a registration automatically after a lost response: read the session first. Registration is limited to 10 attempts per remote IP per 10 minutes, without a queue; rate rejection is 429. Limits are per process and are not a distributed deployment quota. Password login still uses the existing lockout policy and accepts a trimmed username/email.

`GET /api/v1/auth/me` retains its existing identity/scope contract and adds the optional facility `name` to facility entries. A left join preserves existing grants even when their catalog facility is absent.

## Google flow and safe redirects

`GET /api/v1/auth/providers` returns exactly `{ "google": true }` or `{ "google": false }`. This is configuration availability only, not proof of valid credentials or provider connectivity. Neither the ID nor secret is returned.

`GET /api/v1/auth/google` starts the maintained `Microsoft.AspNetCore.Authentication.Google` 10.0.14 handler. The handler owns `/signin-google`; it validates protected state, correlation cookies and a PKCE S256 exchange. It requests authentication/profile/email scopes, uses a secure HttpOnly external cookie for at most five minutes, and does not persist provider access or refresh tokens. The fixed local completion is `/api/v1/auth/google/complete`; a supplied `returnUrl` cannot change it. Successful completion redirects to `/#account`.

The maintained handler uses Google v3 userinfo (`/oauth2/v3/userinfo`). Its response must contain a nonempty bounded `sub`, a valid `email` and a JSON boolean `email_verified: true`. Legacy v2 `id` / `verified_email` fields alone are rejected; strings such as `"true"` are not verification. The local provider fixture asserts the v3 endpoint and matches this response contract. Existing identities are looked up by the `Google` provider and subject in `AspNetUserLogins`, not by their current email. Disabled or locked users cannot sign in. A new external identity creates a separate customer workspace atomically. An email collision with any existing account is never silently linked. There is no self-service account-linking endpoint in this slice; the user can sign in with the existing password or use the existing account recovery process.

A random external-ticket identifier is consumed under the identity-directory lock using `GoogleTicketConsumed` security audits. This makes a captured external cookie fail on replay, including across app instances sharing the database/key ring. Session creation, ticket consumption and new-customer provisioning share one SQL transaction. The validated prior application session is carried in protected OAuth state so that it can also be revoked when a Strict application cookie is absent on the cross-site callback. Completion deletes the external cookie.

Errors return fixed same-origin locations `/?authError=CODE#account`:

- `google-unavailable`: one or both server settings are absent/blank; password authentication remains available.
- `google-cancelled`: the user declined provider consent.
- `google-unverified`: missing/invalid subject, missing/invalid email, or email not verified by Google.
- `google-link-required`: an existing account already uses the normalized email/username.
- `account-disabled`: an existing provider identity is inactive or locked.
- `google-failed`: invalid state/correlation, failed provider exchange, missing/expired/replayed external cookie, or failed provisioning.

Raw provider errors, exception messages and tokens are excluded from browser redirects. Google start/completion requests share a per-process limit of 30 requests per remote IP per minute. The external middleware callback is protected by state/correlation and the provider code exchange; it is not a JSON/CSRF-token endpoint.

## Owner-only real Google setup

Real consent/account login is **UNVERIFIED**. Tests use synthetic credentials, a GUID-owned SQL fixture and a fake provider HTTP handler. No real client credentials, Google account, cloud resources or owner database were used.

The owner must privately configure a Google OAuth **Web application** client and its consent audience/test users. Add the actual server origin followed by the exact path `/signin-google` as an **Authorized redirect URI**. For an owner-selected local HTTPS origin `https://localhost:7443`, the exact example is `https://localhost:7443/signin-google`. Replace the origin with the API's actual listening origin; scheme, hostname and port must match exactly. Do not enter `/api/v1/auth/google/complete` in Google Console. Local HTTPS is needed for the secure external/correlation cookies. Normal HTTP demo/password operation remains available without Google configuration.

Provide these values only through private **server process** configuration:

- `Authentication:Google:ClientId` / environment `Authentication__Google__ClientId`.
- `Authentication:Google:ClientSecret` / environment `Authentication__Google__ClientSecret`.

Do not put values into frontend `VITE_*` settings, browser storage, source, packages, chat, screenshots or published configuration files. Both values are evaluated at startup; restart the API through the owner-approved launcher after configuring them. A true providers boolean does not validate them. Use the configured HTTPS application and test consent manually as the owner, then verify a single customer workspace and logout. If behind a proxy, the owner must separately configure trusted forwarding and the external HTTPS origin; do not trust arbitrary forwarded headers.

The guide records the configuration procedure only. It does not create a Google project/client or change the current owner's runtime, launcher, database or secret store.

## Local verification

New SQL/TestServer coverage includes customer isolation and revocable session, input/password/CSRF rejection, normalized duplicate and concurrent registration, prior-session replacement, per-IP registration limit, partial/unconfigured Google settings, complete rollback on provisioning failure, real handler PKCE/state/correlation, provider cancellation/error, verified/unverified/missing-subject profiles, local-account collision, active/locked external identities, returning identity, ticket replay, code callback replay and replacement with the Strict cookie absent. Fake provider requests never leave the test process. Package restore uses the repository lock files; there is no new schema/migration.

Commands:

```text
dotnet restore backend/WarehouseRent.sln --locked-mode
dotnet test backend/WarehouseRent.sln --no-restore --filter "FullyQualifiedName~SqlGoogleAuthenticationTests|FullyQualifiedName~SqlPublicCustomerAuthenticationTests" --verbosity minimal
dotnet test backend/WarehouseRent.sln --no-restore --verbosity minimal
```

Source verification in the assigned backend workspace: locked restore exited 0; focused auth tests passed 15/15; the complete backend suite passed 320/320 (exit 0, approximately 1 minute 30 seconds). The coordinator checkpoint owns the committed final revision/results and independent review. Passing synthetic tests does not establish real Google login, browser consent, production deployment or SMTP delivery.

Official references checked October 29, 2026:

- [Microsoft ASP.NET Core Google external login setup](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/social/google-logins?view=aspnetcore-10.0): OAuth Web application client, callback and server secret configuration.
- [Google OAuth 2.0 web server applications](https://developers.google.com/identity/protocols/oauth2/web-server): authorization libraries, redirect URI matching and private server credentials.
- [Microsoft GoogleOptions for ASP.NET Core 10](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.authentication.google.googleoptions?view=aspnetcore-10.0): PKCE, external sign-in scheme, token persistence and remote authentication options.


## Local Google activation (Windows)

A separate Google Cloud project `WarehouseRent Local` (`warehouserent-local`) was created on October 29, 2026 in the owner's selected Chrome profile. Branding and the Web client were created on October 30, 2026 with the owner-approved support/contact email and owner test user. Real login acceptance remains separate from Cloud configuration. Keep other project clients separate.

The Web application OAuth client is named `WarehouseRent Local Web`, with the exact authorized redirect URI `https://localhost:7443/signin-google`. Use only basic sign-in identity/profile/email permissions. Configure the consent audience/test users as appropriate for the owner's intended test accounts. Do not publish broadly or enable billing as part of this local setup. The server handles the redirect, so a JavaScript origin is not needed for this server flow.

After obtaining that client's ID and secret privately, run these commands in an owner's PowerShell 7 terminal:

```powershell
Set-Location 'C:\AI\WarehouseRent-atlas-p0'
pwsh -NoProfile -File .\ops\configure-google.ps1
dotnet dev-certs https --trust
pwsh -NoProfile -File .\ops\start-local-demo.ps1 -Https
```

The configuration prompt masks the secret. Both values are encrypted using Windows CurrentUser DPAPI at `%LOCALAPPDATA%/WarehouseRent/google-oauth.json`, outside the repository and OneDrive. Do not paste either value into chat, source files, screenshots or command-line arguments. Only that Windows user can decrypt the file. Use `configure-google.ps1 -Replace` for an intentional local replacement.

The launcher inspects every packaged payload hash against `.qa/runtime/current-p2-candidate.json`, rejects occupied ports and explicit ambient Kestrel endpoints, checks the trusted localhost certificate, temporarily injects Google configuration into the child API environment, and restores it after exit. It runs the existing Demo database without migration/seed; live payments stay disabled. Open `https://localhost:7443/#account` using the owner's Chrome profile. Never bypass a browser certificate interstitial: complete the certificate trust setup first.

`-InspectOnly` verifies the selected package without starting a process. `-Https` selects the secure origin; without it the launcher uses HTTP5080 and does not load the private Google file. An HTTP Google challenge returns the fixed `google-https-required` recovery message without setting a provider cookie or contacting Google. Local email/password auth remains available. A trusted certificate check is not proof the API has started or SQL is ready.

The host previously rejected agent API process launch. This guide is a manual foreground launch, not an alternative agent process mechanism. Real Google callback, account creation and session readback remain UNVERIFIED until completed with the actual client and the owner's consent. Synthetic helper checks: `pwsh -NoProfile -File tests/google-local.test.ps1`; the standard `ops/verify.ps1` includes them.
