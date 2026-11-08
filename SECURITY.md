# Security Policy

KhoSpace is a portfolio application under development. It has not been independently
certified for production banking, accounting, legal compliance, or security.

## Reporting a vulnerability

Report suspected vulnerabilities privately to the owner using an available private
GitHub channel. If private vulnerability reporting is enabled, use that feature.
Do not publish exploit details or sensitive production data in an issue.

Include the affected revision, component, minimal synthetic reproduction, impact,
and possible mitigation. Redact credentials, cookies, personally identifiable data,
bank details, and provider payloads.

## Testing boundaries

- Use an authorized local synthetic environment.
- Do not probe another tenant or production account.
- Do not send money or forge a production webhook to demonstrate a vulnerability.
- Do not weaken signatures, ownership checks, or CSRF to make a test pass.

## Credential handling

Keep secrets outside source and frontend bundles. Private stores, production
environment settings, and backups are not included. Historical private commits
are intentionally not imported into this clean publication.

A pattern scan is a publication check, not a complete security audit. If a real
credential is exposed, privately notify the owner and coordinate rotation and
impact assessment. Removing a file alone does not revoke a credential.
