# Use cases and exception flows

All scenarios use synthetic organizations and money. Rules in [business-rules.md](business-rules.md) override illustrative prose. Every command receives an idempotency key and is authorized at execution time.

## UC-01 Search and quote — FR-005–FR-010

Actor: visitor/customer admin. Preconditions: published facility and units.
1. Select facility, start and exclusive end date, minimum desired total area.
2. Display eligible whole units with area and indicative price; explain that requested area may require a larger whole unit.
3. Customer selects explicit unit IDs and requests a server quote.
4. Server checks current publication, rates, date policy and eligibility; stores immutable quote lines and expiry.
Postcondition: quote exists, no capacity allocated.
Alternatives: zero results → suggest dates/area; rate missing → unavailable for quoting; stale search → re-evaluate; invalid dates → preserve form and field errors. Search response is never a promise of availability.

## UC-02 Hold and confirm — FR-011–FR-015

Actors: customer admin, finance, sales. Preconditions: live quote and authorized customer.
1. Customer requests a hold for all quote lines.
2. Transaction locks unit IDs in order; checks overlap and valid quote/time; allocates all or none.
3. UI shows authoritative expiry countdown and deposit amount separately from rental charges.
4. Finance records/verifies actual deposit receipt; sales requests confirmation before expiry.
5. Same allocation locks verify live hold, required funds and state; booking becomes Confirmed; hold allocation becomes a confirmed commitment without a release gap.
6. One lease is created from the booking, preserving snapshots.
Exceptions: conflict → 409 with safe conflicting unit codes; deadline reached → 409 HOLD_EXPIRED; late money → unapplied receipt/refund review; lost response → read command/resource status; repeated confirm → original booking; one unit fails → no partial booking.

## UC-03 Handover and use — FR-016, FR-019

Actor: manager. Preconditions: confirmed lease, current rental start reached, no clearance conflict.
1. Inspect unit, record condition/reference and handover time.
2. Server verifies expected active allocation and absence of other physical occupancy.
3. Mark unit handed over and lease Active in one transaction.
Exceptions: early arrival → not yet eligible; prior goods present → HANDOVER_BLOCKED; only some units ready → no partial activation in P0; no staff override bypasses the occupancy invariant. P0 manager approves post-term outbound requests, then records physical clearance once stock and pending requests are zero and the inspection passes. Full financial closure remains P1.

## UC-04 Receive goods — FR-020–FR-022, FR-025

Actors: customer member, clerk. Preconditions: Active lease and handed-over unit.
1. Customer submits item quantities for a permitted unit; request is Submitted without stock impact.
2. Assigned clerk checks the physical delivery and approves for posting.
3. Post one complete document in P0, locking request and balance keys; append deltas/update projection/audit atomically.
4. Customer sees the resulting balance and history.
Exceptions: wrong organization child ID → 404; lease expired → no new receipt; wrong UOM/quantity → 422; duplicate post/new key → original posting or duplicate-source conflict, never doubled quantity; network failure → query request/posting state. P1 adds partial actuals as separate postings.

## UC-05 Release goods — FR-021, FR-023, FR-025, FR-028

Actors: customer member, clerk. Preconditions: positive stock owned by that customer.
1. Customer submits issue request; clerk verifies release authority.
2. Posting transaction locks request/balance; validates current availability and lease or authorized clearance mode.
3. Append negative ledger entries and update balances.
Exceptions: insufficient quantity → whole posting rejected; concurrent depletion → one commit and one conflict; ended term → manager-authorized clearance can issue existing goods, not receive new goods; release to external carrier is recorded, not a carrier API integration.

## UC-06 Renew/amend — FR-017

Actors: customer admin/sales. Requote added periods/units, show old/new commercial lines and differences. In one transaction, lock old and new unit sets, validate no conflicts and create next lease version. Failure leaves old version/allocations intact. Issued statements stay unchanged; future charge periods reference their applicable version. An amendment cannot retroactively remove units holding goods.

## UC-07 Transfer/count/correct — FR-024, FR-026–FR-029

Transfers preserve customer/item identity and post both legs atomically. Reversals reference unreversed quantities and cannot make source balances negative. Counts store expected/count snapshots; a different manager approves the delta. An intervening stock posting invalidates the count version and requires a recount/review. All rejected commands leave ledger and projection unchanged.

## UC-08 Charges and collections — FR-030–FR-036

Finance generates a monthly statement from lease-version intersections and snapshots. Repeating generation returns existing business keys. Finance verifies a manual receipt, then allocates it to a charge or deposit target. A customer screenshot alone does not verify money. Partial payment derives PartPaid; reaching zero derives Paid. Corrections use credits, and refund records describe external manual actions rather than initiating them.

## UC-09 Cancel/clear/close — FR-014, FR-018, FR-033

Before handover: CancelRental atomically cancels the booking plus an existing Scheduled lease and releases schedule allocations, preserving receipt/refund-needed records on RentalAccount. No P0 statement can be issued before activation. After handover: explicit P1 termination decision, no automated goods movement. P0 supports outbound clearance and physical inspection/clearance; P1 adds settlement/deposit refund and final closure. An overdue customer cannot erase the incoming customer's confirmed booking; staff must resolve the conflict with a documented rebooking/amendment, never fabricate availability.

## UC-10 Staff/customer access — FR-002–FR-004, FR-039, FR-042, FR-047

Customer admin invites only into their organization. Operator admin assigns explicit facility scopes to staff. Every detail/download/export rechecks membership and role. Disabling membership invalidates access immediately on the next request, including previously issued attachment URLs via authorized download. Last-admin removal is refused. Cross-customer IDs are treated as inaccessible; audit excludes secret contents.

## UC-11 Recovery and operation — FR-040, FR-041, FR-045, FR-046

After a timeout the client reads the saved command result; jobs reclaim durable work with unique keys and bounded retries. A restore rehearsal verifies allocation history, ledger/projection reconciliation and access controls against synthetic snapshots. Restoring a backup does not imply already-executed external email/payment actions can be repeated; P2 adapters need reconciliation.
