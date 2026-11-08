# Atlas domain review — warehouse rental and customer inventory

Date: 2026-10-27, Asia/Ho_Chi_Minh. Reviewer: Atlas. Scope: research and documentation only; no application, database, runtime or account changes.

## Review boundary and verdict

The confirmed model is one operator, multiple facilities, whole fixed units measured in m², one or more units per rental, and customer-owned goods held in leased space. C#/.NET and React are the selected stack. This is a space-rental and custody application; quantity-based shared storage is a different product model.

**Verdict: sound direction; conditional design recommendation, not implementation acceptance.** Four initial documents were available and read at the revisions below. Detailed state machines, schema/API contracts, test strategy and implementation were not available at this review snapshot. The failure modes below are design requirements to check, not assertions that unimplemented software has defects. No application tests were run and no runtime PASS is claimed.

| Document read | SHA-256 at read |
| --- | --- |
| `README.md` | `56A960F7CAD63A1F913AC0A8655770EBF993BDBF03A1AB444EEA86D5FB1494CE` |
| `GLOSSARY.md` | `21A4F323355D3D49650B18918581DF3038D75117345B4B58D7CFD69A29685889` |
| `docs/requirements/product-brief.md` v0.1 | `665E8C54CE676B3EB81B69848D3C4308E3C4050E4EB02A9FDFD4293D59917E49` |
| `docs/research/comparative-study.md` | `71718040552623990DD21DA1F3009AFE16A92DB85737EB7A030994DF044AE592` |

No applicable `AGENTS.md` was found at `C:/`, `C:/AI/` or the project root/docs at inspection. The user's supplied instructions and one-writer/path assignment apply. Only this review file is owned by Atlas; the coordinator owns the other documents. Later edits to those documents require a separate revision comparison.

## What the initial documents already get right

- The glossary separates quote, hold, booking, lease and physical occupancy. It also separates a stock posting from a mutable balance and a charge statement from a statutory invoice.
- The brief identifies concurrency, repeat-safe billing and organization/facility visibility as outcomes. Business defaults D06–D12 are marked proposals rather than established law or user decisions.
- The comparative study explicitly warns that EF concurrency tokens alone do not prevent two overlapping allocation inserts. It distinguishes conceptual research from code reuse and records missing licenses.
- The release plan separates a portfolio slice from a fuller MVP and defers payments, e-invoicing, shared pallet capacity and multiple operators. Keep those boundaries.

## Public-source cross-check

Three official source publishers were consulted on 2026-10-27. These are design references, not endorsements or proof of this application's correctness. No third-party source code was copied or executed.

| Official source | Source-supported fact | Our design implication / limitation |
| --- | --- | --- |
| Microsoft: [EF Core concurrency](https://learn.microsoft.com/en-us/ef/core/saving/concurrency), [transactions](https://learn.microsoft.com/en-us/ef/core/saving/transactions), [global query filters](https://learn.microsoft.com/en-us/ef/core/querying/filters) | Tokens detect competing updates/deletes; insert conflicts use database/provider mechanisms. Transactions provide atomic writes. Query filters can scope default reads and can also be disabled. | Use database-backed allocation serialization and transaction tests; add write/reference authorization rather than treating a read filter as the entire customer boundary. Exact SQL design remains to be reviewed. |
| Odoo: [customer/vendor-owned stock concept](https://www.odoo.com/documentation/20.0/applications/inventory_and_mrp/inventory/shipping_receiving/daily_operations/owned_stock.html) | The retrieved official search excerpt distinguishes physical stock/location tracking from ownership and valuation. | Keep `CustomerOrganizationId` separate from physical location. This analogy does not imply our operator sells consignment goods. Full-page reads timed out for versions 19 and 20; no detailed policy or executable behavior was verified. |
| Frappe/ERPNext: [Immutable Ledger](https://docs.frappe.io/erpnext/immutable-ledger-in-erpnext) | Original postings and linked reversals preserve the correction trail rather than silently replacing transaction history. | Use traceable quantity/payment adjustments. We are not proposing a full general ledger, FIFO valuation, blockchain or copying ERPNext's entire accounting model. |

The failure modes and acceptance examples that follow are Atlas's proposals for this project. They are not claims that these vendors implement the same rental policies. Tax treatment, custody liability and real contract clauses remain operator/professional validation topics.

## Critical failure modes and minimum design responses

P0 means necessary for the first trustworthy end-to-end slice. P1 means needed before the broader operational MVP. These priorities describe product integrity; they do not introduce changes to Jarvis/Atlas permissions.

| ID | Priority / failure mode | Required behavior and evidence |
| --- | --- | --- |
| AT-01 | P0 — two staff/customers acquire the same unit/date concurrently | Availability search is advisory. Acquisition rechecks overlap and writes allocation in one transaction, serialized against a stable unit/allocation guard or equivalent proven database mechanism. A token on each newly inserted booking does not solve this. Test independent connections against SQL Server, not only an in-memory provider. |
| AT-02 | P0 — a multi-unit booking is partly allocated | Acquire all requested units atomically; deterministic lock order prevents avoidable deadlocks. Any conflict rolls back the entire acquisition. Confirm whether a single booking may span facilities; a one-facility MVP is a proposal, not an assumed user restriction. |
| AT-03 | P0 — quote/hold expiry and finance confirmation race | Quote validity does not reserve space. D07's 15-minute hold and D08's manual finance confirmation need an explicit expiry outcome. Confirmation after expiry must revalidate/acquire capacity; a payment record alone cannot resurrect an allocation or overwrite the next customer. Store server-side expiry and evaluate it even if cleanup jobs are late. |
| AT-04 | P0 — interval boundary or timezone causes double billing/overlap | Specify date-only rentals in the business timezone with a single boundary convention, preferably `[start, endExclusive)`. Adjacent intervals do not overlap. Store UTC instants for events/expiry separately. Do not let a browser's timezone change billable days. Effective move-in/out times need a separate operational rule. |
| AT-05 | P0 — contract end or zero recorded stock is interpreted as an empty unit | Keep scheduled allocation, lease validity, physical handover and clearance separate. An expired lease can still have goods or uncleared occupancy. Zero ledger balance alone is not a signed clearance. Future bookings may be scheduled, but actual handover must surface the unresolved prior occupant and require resolution. |
| AT-06 | P0 — the same SKU/lot belongs to two customers | Every item/movement/balance carries customer ownership independently of warehouse/unit. Key stock dimensions by customer plus item, location and optional lot/status. A document's customer must match all item/location/lease references. An internal transfer cannot silently change owner. |
| AT-07 | P0 — two outbound postings overspend stock or retry posts twice | Validate available quantity and post ledger plus balance/projection atomically; include concurrency control for that stock key. Commit once per idempotency identity. A timeout followed by readback/retry must return the existing posting, not another movement. Posted documents cannot become freely editable drafts. |
| AT-08 | P0 — transfer loses goods between source and destination | A supported internal transfer is a balanced pair of entries in one transaction; customer and quantity/base unit remain consistent. For the smallest P0, defer transfers instead of implementing half of this workflow. Cross-facility transit requires an explicit in-transit state and is a later feature. |
| AT-09 | P0 — receiving or dispatching outside a customer's allowed space | Check actor facility scope, customer, lease/allocation and effective operational state on the server. Expired contracts need an explicit controlled outbound/clearance path; a blanket date check must not trap goods indefinitely. Unapproved inbound overflow/staging must not silently occupy somebody else's unit. |
| AT-10 | P0 — periodic billing duplicates after retry, renewal or amendment | Charges reference the exact lease-line/rate version, unit, service period and charge type. A durable uniqueness/idempotency design plus covered-period validation prevents equivalent or overlapping duplicate charges. Repricing does not alter already issued statements. P0 can use one clearly defined billing model; do not invent monthly/daily conversion rules implicitly. |
| AT-11 | P0 — deposits, payment proof or partial payments falsely settle charges | Track security deposit separately from rental charges and from allocated payment receipts. A screenshot/customer claim is not a confirmed payment. Model partial allocation, overpayment/unallocated money, reversals and refund references explicitly. Never equate booking confirmed, paid, checked in and cleared. Deposit deductions need a documented settlement decision. |
| AT-12 | P0 — customer isolation works on lists but fails on IDs/files/exports | Authorize details, writes, search suggestions, reports, attachments and downloads using authenticated membership and server-side resource scope. Client-supplied customer IDs do not grant membership. EF query filters are a default-read aid, not sufficient evidence of write/reference isolation. Staff cross-customer visibility must follow assigned role/facility scope. |
| AT-13 | P0 — unit area/price changes rewrite history or overstate capacity | Snapshot contracted area/rate/currency and terms. Whole-unit rental charges by agreed area even if goods use less. Distinguish rentable from gross floor area and avoid double-counting parent zones plus child units. Block or version structural changes affecting active/future allocations. |
| AT-14 | P1 — adjustments or early termination erase liabilities and stock history | Use effective-dated lease amendments and linked stock/charge reversals. Keep original references, reason, actor and recorded time. Termination, financial settlement and physical clearance may complete separately. No automatic disposal or owner change of remaining goods. |
| AT-15 | P0/P1 — a stale screen overwrites newer work or reports old counts as live | Expose observed time/version; stale writes require conflict resolution. Derive booked/occupied/available metrics from their own sources with explicit filters. “Available now,” “available for dates,” and “physically cleared” are different questions. Audit identifies actor, organization, facility and source document. |

For AT-01/02, ordinary `ReadCommitted` plus an overlap query is insufficient unless an additional locking/constraint mechanism closes the race. If using a SQL Server unit guard row, every capacity-changing path, including renewal, maintenance blocks and imports, must follow the same protocol and recheck after acquiring locks. Deadlock/conflict retry is bounded and shares the original operation identity.

## Minimum model boundaries

Recommended aggregates, to compare with the coordinator's later schema:

| Concern | Core identities and source of truth |
| --- | --- |
| Space | Facility → optional zone → rentable fixed unit; area, operational availability and version |
| Commercial allocation | Customer organization, quote/rate snapshot, hold/allocation intervals, booking, lease and lease lines/amendments |
| Physical custody | Handover/clearance, customer item, stock document/lines, posted quantity ledger, reconciliable balance projection |
| Billing | Charge statement/lines, payment receipt, payment allocation, separate deposit movements/settlement |
| Access and evidence | User/membership, facility assignments, private attachments, audit records, operation/idempotency identities |

A lease can cover several units, and one customer can hold several leases. Do not infer customer ownership solely through the location's current lease: goods history must remain attributable after a location is re-let. Area and stock quantity use distinct numeric types/units; money and quantities use explicit decimal precision, not floating-point arithmetic.

Keep independent state axes rather than one universal `Completed` flag:

- Booking: draft/requested, held, confirmed, cancelled/expired as specified by the final state machine.
- Lease: scheduled, active, ended/terminated; activation rules are explicit.
- Occupancy: not handed over, occupied, clearance pending, cleared.
- Stock: draft/requested, posted, linked reversal; reported intent is not physical posting.
- Money: statement issued/credited; unpaid/partial/paid derived from allocations; deposit held/settled/refunded separately.

These are proposed distinctions, not a replacement of the coordinator's existing contracts. Final enum names/transitions must be reconciled once those documents exist.

## MVP recommendation and delivery sequence

The initial P0/P1 split is appropriate. Keep a narrow demonstrable vertical slice first, then operational completeness.

1. **Design gate:** settle date boundaries, hold/deposit expiry, allocation serialization, customer scope and the stock-out/clearance path. Add traceable acceptance IDs before product code.
2. **P0 vertical slice:** customer/facility identities; whole-unit date search; explicit rate snapshot; atomic multi-unit hold/confirmation; basic lease/handover; customer items and staff-posted receipt/issue; statement and manually confirmed payment records; customer/operator views. Include read-only traceability, duplicate-request recovery and negative authorization tests from the start.
3. **P1 operational MVP:** renewal/amendment, transfer and stock counts, early termination, deposits/refunds, overdue/aging views, attachments, notifications and backup/restore rehearsal. Add these without rewriting original postings.
4. **P2 only after validation:** live payment integration, tax/e-invoicing, e-signatures, barcode devices, shared capacity, loading optimization, IoT and multiple independent operators.

Do not commit a complete operational MVP to a 15-day estimate before capacity, team availability and acceptance depth are known. The portfolio slice may demonstrate deposit capture without production settlement only if that limitation is visible and P1 settlement remains tracked. A customer portal can initially submit stock requests while staff confirm physical movements; confirm this operating model with the owner.

## Proposed acceptance pack for the eventual implementation

All cases below are **NOT RUN / DESIGN ONLY**. They should map to requirement IDs in the coordinator's traceability document. Infrastructure/concurrency cases need the real selected database provider; unit tests alone cannot prove them.

| Case | Given / when | Required result |
| --- | --- | --- |
| A01 | Two connections acquire unit U over the same interval concurrently | Exactly one allocation commits; losing request receives an explicit conflict; no second confirmed allocation. |
| A02 | Request U1+U2 while another transaction wins U2 | Losing multi-unit request owns neither unit; transaction leaves no partial booking. |
| A03 | U held until T; finance confirms after T while another holder acquired it | Original payment is recorded/needs resolution without allocating occupied capacity; no silent resurrection. |
| A04 | Same customer confirms/retries one operation after a response timeout | Same committed booking/posting identity and result returned; changed payload under that identity is rejected as a conflict. |
| A05 | Rentals `[Nov 1, Nov 3)` and `[Nov 3, Nov 5)`; browser in another timezone | Adjacent scheduling accepted; first interval is two rental days under proposed daily policy. |
| A06 | Lease ends while goods remain, or ledger is zero but clearance absent | Space is not presented as physically cleared; prior customer can follow the controlled goods-release path. |
| A07 | Same SKU code exists for customers A and B | Separate balances and histories; issuing A's goods cannot reduce B's balance. |
| A08 | Balance 10; concurrent issues of 7 and 7 | One succeeds; other conflicts/has insufficient stock. Final quantity 3, never negative. |
| A09 | Posted receipt request is retried, then reversed once | One original and one linked opposite posting; balance reconciles; repeated reversal does not double-correct. |
| A10 | Transfer supported; fail between its two entries | Both entries commit or neither; aggregate customer quantity is conserved. If deferred, no partial transfer action is exposed. |
| A11 | Generate same charges twice; amend a future rate | No duplicate statement lines; issued historical price/period remains unchanged; future charges use the effective snapshot. |
| A12 | Rental charge 1,000,000 VND, payment 400,000, separate deposit 200,000 | Rental outstanding 600,000; deposit tracked separately and not silently allocated. Values are arithmetic fixtures, not prescribed tariff/accounting policy. |
| A13 | Customer A guesses B's item, lease, receipt, export or attachment ID | No B data/content is returned or changed; list totals/search suggestions also preserve scope. |
| A14 | Staff assigned to facility X requests write in facility Y | Denied unless their explicit role/scope permits it; operator-wide reporting remains available to authorized roles. |
| A15 | Someone changes unit area or tariff after confirmation | Historical lease/statement retains agreed values; new allocation uses its own agreed version. |
| A16 | Old browser edits after another staff member changes the record | Explicit conflict/readback; no last-write-wins loss of stock, money or allocation. |
| A17 | Cleanup/notification job is delayed, duplicated or restarted | Hold expiry remains correct by server time; no duplicated billing/postings; notification status does not alter commercial truth. |
| A18 | Restore backup into a separate test environment | Source documents, ledger balances, allocations, payment allocations and private attachment references reconcile. Required for operational MVP acceptance. |

## Decisions to resolve before implementation acceptance

1. Are all selected units in one facility per booking, or can a contract span facilities? Affects transaction scope and staff workflows.
2. What happens when manual finance confirmation takes longer than the 15-minute hold? Define late deposit, refund/credit and reacquisition outcomes; no invented automatic outcome.
3. When do charging, lease activation and physical handover each start/end? Define early arrival, delayed clearance and overstay handling separately.
4. Which services create additional charges, and what is the exact rounding/proration rule? Daily VND is a demo proposal; no tax rate or legally binding invoice claim is assumed.
5. Are lot/serial tracking, mixed-condition goods, staging areas and per-item dimensions needed in the first slice? Floor area does not prove weight/fire/height compliance.
6. Who may authorize outbound goods, adjustments and deposit deductions? Specify real operator roles and evidence rather than adding unnecessary approval layers.
7. What is retained when a customer membership ends? Historical ownership and private evidence must remain auditable while former users lose access.

## Handoff to coordinator

Use AT-01–AT-15 and A01–A18 as a review checklist against the final business rules, data model, API contracts and test matrix. Resolve each critical item by explicit document reference, or record deferral with the corresponding feature kept outside that release. No other project document was changed by Atlas. No workers, app processes, inference, configuration changes, Git operations or cross-chat messages were performed for this review.
