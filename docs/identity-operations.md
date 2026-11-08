# Identity operations (WP16A)

Local development slice, English UI. No production deployment or live email delivery.

## Customer invitations

CustomerAdmin uses Settings > Invitations & staff access, supplies the customer organization, Member/CustomerAdmin role, recipient email and reason. The server rechecks active issuer membership on creation, listing, revocation and acceptance. An existing identity must use the existing versioned membership workflow; invitations cannot replace another user's password. Recipient emails must also satisfy the configured ASP.NET Identity username character policy; unsupported addresses are rejected before a token is queued. New invitations expire after 48 hours. Revocation removes the private delivery envelope. Acceptance creates the user and membership once under the identity directory transaction lock. Permission withdrawal, expiry, revocation, token reuse and a foreign customer scope fail safely.

## Recovery

Request recovery always returns the same 202 body for a syntactically accepted JSON request, regardless of whether the email identifies an eligible account. Duplicate eligible addresses are treated as unavailable. A 60-second per-address cooldown and a 60-request/minute per-peer-IP endpoint limit bound repeated requests. No forwarded-IP header is trusted by this application. Deployment must configure and assess proxy/rate-limit policy separately. A replacement reset invalidates the previous token. Reset lifetime is 30 minutes. Password policy uses the configured ASP.NET Identity validators (12 characters, upper/lowercase, number and symbol). Successful reset changes the password and security stamp, clears password lockout, consumes the token and revokes all existing server sessions in one transaction.

## Secret delivery boundary

Tokens are 32 cryptographically random bytes, hex-encoded. Only a SHA-256 digest is used for redemption. The raw token is encrypted using ASP.NET Data Protection with the WarehouseRent.CredentialDelivery.v1 purpose; the private SQL delivery table has no read/download endpoint. Local SQL tests unprotect synthetic envelopes through the test host service to exercise redemption. No email provider or dispatcher is configured, and the UI clearly reports PendingTransport. Invitation/recovery is therefore source-tested, not an end-to-end delivered-email claim. Do not put raw tokens into logs, URLs, command journals, support messages or browser storage.

Production delivery, Data Protection key-ring persistence/protection/backup, token-envelope cleanup/retention and operational abuse controls require a separately reviewed transport/recovery deployment. Expired envelopes remain private in this local build; consumed/revoked invitation envelopes and superseded reset envelopes are removed. Do not extract a real user's token as a support workaround.

## Staff administration

Only an active OperatorAdmin can read the paginated directory or assign/revoke Clerk, Sales, Finance or FacilityManager to an existing active identity for a catalog facility. Commands require a UUID idempotency key, If-Match directory version and reason. Replays return the same saved result; changed payloads fail. Staff grants are read from SQL on each request. Global disablement is permanent through this slice: it revokes sessions, invalidates outstanding credentials and changes the security stamp. Self/OperatorAdmin disablement and disabling a last active CustomerAdmin are rejected. There is no implicit OperatorAdmin grant, re-enable endpoint or account deletion. Audits link to the command identity; saved command results record target user/facility/role/granted state.

Login, membership mutations, token redemptions and staff writes share the transaction-owned identity application lock. This deliberately serializes local-demo identity changes, including permission withdrawal versus invitation acceptance. Database UTC is injected through TimeProvider for credentials. Directory mutation uses SQL rowversion. Do not claim production throughput from these functional tests.

## Verification

See the coordinator checkpoint/review receipt for exact commits and current SQL/UI counts. Browser, external email delivery and owner database migration remain unverified/deferred. The identity migration refuses destructive downgrade once credential/command history exists.
