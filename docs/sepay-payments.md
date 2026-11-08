# SePay QR payments

## Release and activation are separate

This integration extends the existing online-payment workflow. It does not
replace manual receipts, VNPAY or MoMo. It does not open a bank account, approve
a transfer or activate a paid provider plan.

On November 5, 2026 the owner explicitly approved SePay Free and charges for
transactions exceeding its 50-transaction monthly allowance. This supersedes
the earlier strict zero-fee requirement only for that plan and its overages:
it does not authorize a paid-plan upgrade, another service, a bank-account
opening or an agent-initiated transfer.

Use the separate `OverageFeesApproved` attestation for this decision. No
provider-side no-charge cap was verified; never set `ZeroFeeCapVerified` merely
because the owner accepted overage charges. An application QR limit cannot
cap provider fees for other incoming or outgoing bank traffic.

No real recipient details, provider keys or bank passwords belong in this
repository, a browser bundle, a screenshot, a test fixture or a chat message.

## Customer workflow

1. Sign in and select the appropriate rental account.
2. Load the available payment methods. SePay is unavailable unless the API has
   valid private configuration.
3. Create a whole-VND payment request. The server binds its unique reference,
   amount, recipient identity, account and 15-minute expiry.
4. In Live mode only, inspect the bank, account, amount and reference before
   personally authorizing a transfer in a banking app.
5. KhoSpace reads server status for at most two minutes. A return URL, a QR
   image, a screenshot or a claim that money was sent never confirms payment.
6. A verified provider notification can confirm a Live receipt automatically.
   Allocation to a deposit or rent remains a separate financial operation.
7. If the request expires or the network fails, read its status again. Do not
   create a replacement payment or send money again merely because a browser
   has not displayed success.

Sandbox is visibly labelled synthetic. It does not expose a scannable bank QR
and cannot create a real payment receipt or spendable balance.

## Private API configuration

Configuration keys (not credentials):

| Key | Default / requirement |
| --- | --- |
| `Payments:SePay:Enabled` | disabled unless explicitly `true` |
| `Payments:SePay:Mode` | `Sandbox` or `Live` |
| `Payments:SePay:Bank` | approved receiving gateway; Sandbox uses `TESTBANK` |
| `Payments:SePay:AccountNumber` | private approved recipient; Sandbox starts `TEST` |
| `Payments:SePay:Secret` | private HMAC secret, minimum 32 characters |
| `Payments:SePay:AllowLive` | false; explicit owner-controlled Live opt-in |
| `Payments:SePay:ZeroFeeCapVerified` | false; do not assert without actual evidence |
| `Payments:SePay:OverageFeesApproved` | false by default; explicit owner approval of Free-plan overages |

ASP.NET configuration environment variables use double underscores instead of
colons. Live requires Production, `AllowLive=true`, and either verified
`ZeroFeeCapVerified=true` or expressly approved `OverageFeesApproved=true`.
It also requires valid recipient/secret configuration. A configuration value
is an operator attestation, not evidence of free provider service.
Sandbox is restricted to Development, Demo or Testing and synthetic recipients.
Do not paste shell commands containing real secrets into chat or command history.

## Provider webhook

Planned production URL:
`https://khospace-eight.vercel.app/api/payment-notifications/sepay`

Configure JSON, incoming-only events for the approved receiving account, and
HMAC-SHA256 authentication. Use the same privately stored HMAC secret on SePay
and the API. Never choose an unauthenticated webhook.

SePay signs the exact bytes of `timestamp + "." + raw_body`, with headers
`X-SePay-Signature: sha256=HEX` and `X-SePay-Timestamp: UNIX_SECONDS`.
The API enforces one of each header, freshness, bounded payloads, exact bank,
recipient, amount and reference, and a unique durable provider transaction.
Duplicate notifications cannot produce another receipt. Conflicting evidence
is not treated as a second payment.

Late, cancelled, expired or closed-account evidence requires reconciliation.
The integration does not reopen bookings, extend holds, allocate money
automatically or issue bank refunds.

## Migration and deployment order

1. Finish independent source review and isolated relational/frontend tests.
2. Take and validate a dedicated KhoSpace database backup. Protect runtime and
   Data Protection keys separately. Never use the IELTS project or database.
3. Inspect the current migration history and privileges without modifying them.
4. During a controlled maintenance window apply the reviewed PostgreSQL registry:
   frozen `001_initial`, then additive `002_sepay`. Existing 001 history must
   pass checksum and current-schema/privilege fingerprint verification first.
5. Verify the complete two-version history, readiness and existing runtime-role
   permissions. No ad-hoc ALTER or editing 001 is permitted.
6. Deploy the exact reviewed API revision and verify build identity/readiness.
   Deploy the matching frontend only after API acceptance.
7. Keep SePay disabled during deployment. Independently complete provider cost,
   bank-consent, callback, restart/replay and owner-approved real transfer
   acceptance before enabling Live.

Old binaries expect a one-row PostgreSQL history; they cannot be assumed ready
after 002. Source rollback scripts are not cloud/database rollback plans.
Preserve payment evidence; do not downgrade/delete payment history. Recovery
requires a validated compatible backup and a coordinated application revision.

## Isolated checks

Only the agent-owned PostgreSQL fixture at `127.0.0.1:55439` and generated
`warehouse_fixture_*` catalogs are permitted for these checks:

```powershell
$env:WAREHOUSERENT_TEST_PROVIDER = 'PostgreSqlFixture'
dotnet test backend/WarehouseRent.Tests --filter "FullyQualifiedName~SqlSePayPaymentTests|FullyQualifiedName~SePaySettingsTests|FullyQualifiedName~SePayBankTransferProtocolTests|FullyQualifiedName~PostgresMigrationTests"
Set-Location frontend
npm test -- src/features/online-payments
npm run lint
npm run build
```

Do not run SQL-labelled tests without the explicit fixture selection: the
legacy default can select a user-owned SQL Server instance. Test results are
synthetic acceptance, not a real payment or live-provider certification.

## Primary references

- https://sepay.vn/bang-gia.html
- https://developer.sepay.vn/vi/sepay-webhooks/xac-thuc
- https://developer.sepay.vn/vi/sepay-webhooks/tich-hop-webhook
- https://developer.sepay.vn/vi/sepay-webhooks/tao-qr-va-form-thanh-toan
