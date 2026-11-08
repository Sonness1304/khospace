# Contributing to KhoSpace

Thank you for exploring the project. This is a public portfolio source publication;
access does not grant permission to change the hosted application, deploy
infrastructure, move money, or access customer/bank data.

## Development workflow

1. Read the README, relevant requirement, and affected workflow guide.
2. Create a focused branch, for example `codex/booking-validation`.
3. Use synthetic data and dedicated local databases only.
4. Add a regression test before changing observable behavior.
5. Preserve authorization, CSRF, tenant isolation, financial integrity, and history.
6. Run relevant tests and frontend lint/build.
7. Submit a focused pull request with commands/results and acceptance limitations.

The customer-facing language is Vietnamese. Keep code identifiers, technical
documentation, commit descriptions, and pull-request explanations in English.
Do not casually translate protocol values or database enum representations.

## Sensitive changes

Authentication, payment matching, stock corrections, migrations, and finance
require careful review. Test retries, concurrency, authorization failure, and
transaction rollback, not only the happy path.

Never edit a released PostgreSQL migration in place. Preserve immutable financial
journals and contract versions. A source rollback does not establish that a
production database downgrade is safe.

## Secrets and test evidence

Do not commit private environment settings, provider credentials, database
archives, OAuth tokens, cookies, bank statements, production QR payloads, or
local DPAPI stores. Label public synthetic demo credentials clearly.

Include redacted evidence only. Do not claim a browser, device, real provider,
or independent review passed unless it actually ran against the stated revision.

## Automation

The verification workflow is manual-only. Check Actions allowances before running
it. A push does not deploy the application. Production changes, paid services,
provider activation, and real transfers require separate owner approval.
