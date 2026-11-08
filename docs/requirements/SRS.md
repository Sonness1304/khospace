# Software Requirements Specification — WarehouseRent

Version 0.2 · October28,2026 · **APPROVED BASELINE FOR P0; IMPLEMENTATION STARTING**. Requirement definitions are not evidence of delivered behavior; progress is in the checkpoint.

## 1. Purpose and document authority

Specify a one-operator warehouse-space leasing and customer-inventory application for an intern-level .NET/React portfolio, with a path to a controlled operational pilot. Confirmed scope and proposed policies are distinguished in [product brief](product-brief.md). This SRS defines required observable behavior. [Business rules](business-rules.md) define authoritative calculations and transitions; [API/design](../design/architecture-api.md) defines proposed implementation contracts.

Requirement words: **shall** is required for the stated release; **should** is advisory; **P0** is the first portfolio slice, **P1** completes the broader MVP, **P2** is deferred. A requirement listed here is not a completed feature. Acceptance cases are proposed until executed.

## 2. Product boundary and operating environment

One operator administers multiple dry-storage facilities. Each facility contains non-overlapping fixed rentable units. Customers rent whole units rather than arbitrary free-form floor area. Inventory is owned by the customer organization, not by the operator. A customer can have several authorized users and several concurrent leases.

Browser React application, ASP.NET API and relational database. Proposed deployment serves SPA/API under one HTTPS origin; synthetic data and a local SQL Server instance first. Exact React/EF/SQL tooling and supported versions are checked before implementation; no packages are installed in this phase.

External interfaces: optional email, payment provider, e-signature, e-invoice and accounting are P2 adapters. MVP records manual receipts; it does not transfer money or issue statutory invoices.

## 3. Actors and access

| Actor | Permitted responsibility | Restriction |
| --- | --- | --- |
| Visitor | View published facilities/units and indicative availability | No customer identities, leases, goods or amounts owed |
| Customer member | View own organization rentals/stock/statements; submit stock requests | No direct posting, staff approval or another customer's objects |
| Customer admin | Member actions plus manage organization invitations and rental requests | No operator roles or self-confirmed payments |
| Sales | Quotes, bookings and proposed lease changes for assigned facilities | Cannot post finance receipts or inventory adjustments |
| Clerk | Execute receipt/issue tasks in assigned facilities | Cannot change commercial terms or customer membership |
| Facility manager | Configure units, handovers, clearance and stock-adjustment approvals in assigned facilities | Cannot grant global admin or silently edit posted records |
| Finance | Record receipts, allocate funds, issue statements/credits and deposit settlement | Cannot manufacture physical clearance or change stock |
| Operator admin | Staff/roles/facility scope and operator configuration | Sensitive overrides still require reason and audit; no unaudited deletion |

Authorization is the intersection of action permission, object customer, facility assignment and document state. Customer organization IDs supplied by the client are never sufficient authorization.

## 4. Functional requirements

### Identity and organization

| ID | Release | Required behavior and acceptance |
| --- | --- | --- |
| FR-001 | P0 | Authenticate local demo users and sign out; valid login establishes a protected session, invalid login returns a generic rejection, sign-out invalidates the session. |
| FR-002 | P0 | Enforce role, facility and organization scope on every read/write/export; a valid user guessing another organization's ID obtains no data or mutation. |
| FR-003 | P0 | Maintain customer organizations and memberships of existing demo users: customer admins add existing users, change customer roles and deactivate memberships in their own organization; last-admin removal/demotion is rejected. Email invitations/reset are P1. |
| FR-004 | P1 | Invite users with single-use expiring tokens and support password reset without account enumeration; expired/reused tokens fail and no raw token is logged. |

### Facilities and availability

| ID | Release | Required behavior and acceptance |
| --- | --- | --- |
| FR-005 | P0 | Manage facilities, zones and fixed units with code, area, publication state and facility scope; duplicate unit codes within a facility are rejected. |
| FR-006 | P0 | Publish only eligible units and display location, area, permitted goods summary and indicative daily price; private customer details never appear. |
| FR-007 | P0 | Search whole units for a valid start/end interval; active holds, confirmed allocations and maintenance blocks remove conflicting units. Unresolved overdue occupancy prevents a new unconditional hold. Invalid or >365-day intervals fail validation. |
| FR-008 | P0 | Manage dated maintenance blocks using the same allocation protection as bookings; conflicts with existing commitments require resolution, never silent displacement. |
| FR-009 | P1 | Retire units and version meaningful area/description changes; allocated units cannot change physical boundaries retroactively. |

### Quote, booking and rental

| ID | Release | Required behavior and acceptance |
| --- | --- | --- |
| FR-010 | P0 | Create a quote with unit/date/rate/area/policy snapshots and an expiry; displayed totals equal server calculations and the quote alone does not reserve space. |
| FR-011 | P0 | Acquire a 15-minute hold for all selected units atomically; when any unit conflicts the whole request fails with no partial allocation. Changed quote rate/area/policy versions require explicit fresh-quote acceptance. |
| FR-012 | P0 | Expire/release holds using authoritative UTC time; an expiry job is not required for a new request to recognize a logically expired hold. |
| FR-013 | P0 | Confirm a held booking once after authorized deposit verification; expired holds are not revived by a late receipt, and duplicate confirmation returns the original result. |
| FR-014 | P0 | Cancel a hold, confirmed booking or converted Scheduled lease before handover through one rental command; booking/lease cancellation and allocation release commit together, while refund-needed funds remain on the stable rental account. |
| FR-015 | P0 | Create one lease from a confirmed booking; duplicate creation cannot produce multiple leases or lose the original price snapshots. |
| FR-016 | P0 | Record handover and activate only eligible leases; occupied/not-cleared units block activation. Manager-approved post-term issues allow goods release, and manager physical clearance after zero goods permits unit reuse. Financial closure remains P1. |
| FR-017 | P1 | Renew or amend a lease through a new version and overlap recheck; the old commitment is retained if any added dates/units conflict. |
| FR-018 | P1 | Terminate and close a lease through clearance and financial settlement checks; remaining goods/physical occupancy prevent final closure. |
| FR-019 | P0 | Display booking/lease timelines with scheduled and physical states separately; an ended term with remaining stock is visibly overdue, not vacant. |

### Customer inventory

| ID | Release | Required behavior and acceptance |
| --- | --- | --- |
| FR-020 | P0 | Maintain customer-specific items with unique organization/SKU and one base quantity unit; the same SKU in two organizations remains distinct. |
| FR-021 | P0 | Submit receipt/issue requests with item quantities and a valid leased unit; the customer request does not change balances. |
| FR-022 | P0 | Post actual receipts by authorized clerks after handover; out-of-scope, inactive or prohibited receipts fail without ledger entries. |
| FR-023 | P0 | Post actual issues using locked available balances; concurrent issues cannot cause negative stock and no customer's goods are consumed for another. |
| FR-024 | P1 | Transfer goods between two eligible units of the same customer atomically; source decrease and destination increase share one document/transaction. |
| FR-025 | P0 | Produce an append-only stock ledger and derived balance per organization/item/unit/lease allocation; each entry links actor, server timestamp and source document. |
| FR-026 | P1 | Reverse a posted document through a linked compensating posting; reversing already-consumed receipts is rejected if it would create negative stock. |
| FR-027 | P1 | Record stock counts and manager-approved adjustments with reason and counted/expected snapshots; the requester cannot approve their own adjustment. |
| FR-028 | P0 | Display item balances and movement history with scoped filters; balance totals reconcile with posted deltas and drafts never contribute. |
| FR-029 | P1 | Handle partial physical receipt/issue through separate posting documents against request lines; cumulative fulfilled quantity cannot exceed the requested quantity without an explicitly approved revised request. |

### Charges, receipts and deposits

| ID | Release | Required behavior and acceptance |
| --- | --- | --- |
| FR-030 | P0 | After activation, generate statements from the same monthly rounded quote segments using scheduled lease dates; repeat generation yields the same statement. Scheduled unactivated leases cannot issue statements. |
| FR-031 | P0 | Record manual payment receipts by finance with reference, amount, method and received date; a customer upload never sets paid status. |
| FR-032 | P0 | Allocate verified receipts to charges or deposit balances; allocation cannot exceed either unallocated receipt value or an outstanding target amount. |
| FR-033 | P1 | Track refundable deposits, approved deductions and refund records separately from rental charges; settlement cannot exceed held funds. |
| FR-034 | P1 | Correct issued statements through credits/supplemental lines with references; editing a historical issued amount in place is rejected. |
| FR-035 | P0 | Show outstanding and overdue statement balances from issued charges minus allocations/credits; unpaid status never deletes goods or auto-terminates a lease. |
| FR-036 | P1 | Record approved service charges with quantity, price and evidence; changing a rate card does not rewrite already issued charges. |

### Operational visibility and administration

| ID | Release | Required behavior and acceptance |
| --- | --- | --- |
| FR-037 | P0 | Show an operator calendar of allocation, maintenance, handover and clearance states with text labels; customer identities are available only to authorized staff. |
| FR-038 | P1 | Report scheduled area-days, physical occupied area and collections separately; each metric displays interval, denominator and source, with no false occupancy percentage. |
| FR-039 | P1 | Provide private document attachments with content/size validation and authorization on download; file names/storage keys cannot cross customer scope. |
| FR-040 | P1 | Deliver in-app notices for pending actions and approaching term end using a deduplicated outbox; retries do not duplicate notices. |
| FR-041 | P0 | Audit sensitive writes, approvals, role changes and reversals with correlation ID and reason; business success and required audit commit atomically. |
| FR-042 | P1 | Export scoped statements/stock history as CSV with formula escaping; exports contain only authorized rows and are auditable. |
| FR-043 | P0 | Provide synthetic demo organizations and staff/customer roles, no real personal data; resetting demo data is explicit and isolated from any operational database. |
| FR-044 | P0 | Supply searchable/paginated lists and accessible forms with loading/empty/error/conflict states; failed mutations preserve valid user input. |
| FR-045 | P0 | Recognize idempotency keys and return resource/status for accepted mutations; a client that loses the response can read back instead of blindly creating a duplicate. |
| FR-046 | P1 | Back up and restore the application database and private attachments consistently; rehearsal evidence includes restored balances, allocations and access checks. |
| FR-047 | P1 | Allow scoped staff administration with audited role assignment and disablement; disabled identities cannot start new sessions and their existing server sessions are revoked. |
| FR-048 | P0 | Publish setup, architecture, known limits, test commands and an end-to-end demo script; a fresh authorized developer can seed and run the documented flow. |

## 5. Non-functional requirements

These are test targets, not measured performance claims.

| ID | Requirement and measurement |
| --- | --- |
| NFR-001 | Security: HTTPS outside localhost; HttpOnly/Secure/SameSite session cookie, CSRF protection on mutations, explicit allowed origin; test cross-site/unauthenticated writes. |
| NFR-002 | Isolation: object/organization/facility permission tests cover list, detail, mutation, export, attachment and background-job paths; default deny. |
| NFR-003 | Integrity: allocation, posting, receipt allocation and settlement remain correct with two independent API/database sessions and concurrent requests. |
| NFR-004 | Performance: at 10k units/100k ledger entries, 20 concurrent demo users, bounded list page 50 (max 100), read p95 <=800ms and command p95 <=1500ms excluding external services; document hardware/query plan and warm/cold state. |
| NFR-005 | Accessibility: aim WCAG 2.2 AA; keyboard operation, labels, visible focus, contrast, error association and 200% zoom; manual checks complement automation. |
| NFR-006 | Responsive: critical journeys usable at 390, 768 and 1440 CSS px, with wide tables scrolling locally and a list alternative to calendar grids. |
| NFR-007 | Recovery: demo target RPO 24h/RTO 4h only after restore rehearsal; no SLA claimed. |
| NFR-008 | Time: rental dates use facility-local DateOnly boundaries; events/hold deadlines use UTC DateTimeOffset and injectable TimeProvider; midnight/expiry boundaries tested. |
| NFR-009 | Money/quantity: decimal arithmetic, VND whole-dong rounding AwayFromZero at specified line boundaries; area decimal(12,2), quantity decimal(18,3); reject overflow/nonpositive quantities. |
| NFR-010 | Observability: correlation IDs, structured nonsecret errors, command duration and job status; redact tokens/cookies/passwords and private uploaded content from logs. |
| NFR-011 | Maintainability: modular monolith, dependency boundaries, migrations, lockfiles and repeatable seed; CI builds both apps and runs scoped integration/browser tests. |
| NFR-012 | Portability: one documented local setup and one documented deployment target; exact SDK/Node/package versions pinned at implementation after live verification. |
| NFR-013 | Resilience: duplicate job execution cannot duplicate statements/postings; transient SQL retry reuses the idempotency key and rechecks the entire transaction. |
| NFR-014 | Uploads P1: allow JPEG/PNG/PDF <=10 MiB, validate content, random storage names, private serving, no executable/HTML/SVG uploads; production malware scanning remains a deployment prerequisite. |
| NFR-015 | Retention: synthetic demo data only for initial release; production retention/erasure/legal hold policy requires owner review and must preserve required financial/ledger references without assuming a statutory duration. |
| NFR-016 | Test truth: fixture/unit/SQL integration/browser/manual evidence labeled separately with source revision, command, exit code and limitations; no production readiness from a green badge alone. |

### NFR release gates

P0 gates: NFR-001/002/003/005/006/008/009/010/011/013/016. NFR-012 requires reproducible local setup in P0; a live hosting target is P1 and separately authorized. NFR-004 full 20-user/10k-unit load target, NFR-007 restore target, NFR-014 private uploads and NFR-015 production retention policy are P1 gates. P0 includes a 5-concurrent-user smoke check without claiming a latency SLA. WCAG target is not reported certified from automated tests.

## 6. Data and interface constraints

Required records: users/memberships/staff scopes; facilities/zones/units; price versions/quotes/quote lines; allocations/bookings/leases/lease versions/handovers; items/requests/postings/ledger/balances; statements/lines/receipts/allocations/deposit transactions; audit/idempotency/outbox records. See [dictionary](../design/data-model.md) for keys and relationships.

Public endpoints disclose only published facility/unit information. Customer endpoints derive organization access from membership. Internal staff endpoints require both role and facility scope. REST errors include stable code, field errors and correlation ID without stack traces.

## 7. Acceptance and change control

P0 exit: all P0 FRs have passing acceptance evidence, allocation/stock/isolation/idempotency negative tests pass on SQL Server, five critical browser journeys pass, setup works from clean clone, known P1/P2 gaps are visible. The owner accepts the result explicitly.

Changes to date semantics, unit granularity, pricing, deposit, posting authority or customer isolation require updates to rules, data/API contracts, tests and traceability together. No current draft requirement is silently weakened to make implementation easier.

## 8. Assumptions and dependencies

No payments are collected by this documentation work. No actual warehouse contract or law has been reviewed. SQL Server runtime and deployment credentials are unverified. The local .NET SDK is observed but package/hosting compatibility must be checked. Workbench can coordinate supplied design text; it is not the consumer's application database or source-of-truth checkpoint.
