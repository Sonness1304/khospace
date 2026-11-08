# WP15: charge corrections and final settlement

These workflows are in **Finance → Corrections & final settlement**. They record accounting decisions and evidence for synthetic local data. They never call a bank, transfer money or produce a statutory tax invoice. Real-browser acceptance remains deferred.

## Correct issued charges

1. Sign in with Finance scope for the facility, or as the operator administrator.
2. Read the statement ID. Check its original issued amount, corrected net amount and current outstanding balance.
3. Enter a reason and evidence reference. Select Credit, Supplement or Service.
4. For Credit/Supplement, enter a positive whole-VND amount. For Service, enter positive quantity (up to three decimals) and unit price (up to four decimals). The server snapshots both and rounds their product once to whole VND, AwayFromZero.
5. Explicitly approve the correction and issue it. Read the account/statement and settlement history again.

The issued statement and its original lines are immutable. Credit cannot exceed its current net charge. If a credit reduces a paid charge, linked allocation-release entries return excess receipt payments to unapplied funds, or excess deposit deductions to held security. Nothing is silently refunded. The historical allocation remains intact.

For renewal dates added to a month already issued, select **Issue unbilled rental supplements** on that month's statement. It copies only uncovered commercial version lines, with one unique supplement per source. Repeating it cannot charge a line twice. Other months still use **Issue rental statement** in the existing finance panel. Renewal continues to retain the existing required deposit.

## Settle security and unapplied money

Read the rental account's settlement readiness. The server shows corrected rent, effective rent payments, outstanding charges, held refundable security, unapplied verified funds and blocking conditions.

After physical clearance, or pre-handover cancellation/expiry:

- **Deduction:** record a documented approved use of held security against one outstanding statement in the same account. It cannot exceed either held deposit or target outstanding. This is explicit; deposit is never automatically treated as rental income.
- **RefundDeposit:** after completing a manual refund outside the application, record its amount, unique transaction reference, evidence and actual performed-at UTC date. The amount cannot exceed held security.
- **RefundUnapplied:** select one verified receipt in this account and document a manual refund of its available unapplied funds. Refunded funds cannot subsequently be allocated.

Confirm the documented deduction or completed manual refund before saving. Refund and deduction entries are append-only. A customer-wide unique reference prevents accidentally recording the same external transaction twice. The original idempotency key replays the saved result; a different key with an already used reference conflicts.

Credits can return a previous deduction to held security. Review both outstanding charges and security again before final closure. Historical deposit received, deductions/refunds and effective security are distinct values; the deposit receipt journal is never rewritten.

## Resolve receipts awaiting a decision

In **Rental finance**, read a Recorded receipt and independently check its evidence. Verify actual received funds, or explicitly **Reject recorded receipt** when evidence shows it is not a valid payment. Rejection preserves the recorded evidence and audit reason. Verified and rejected receipt evidence cannot be edited or changed to another state.

Late verification after cancellation/expiry retains the stable rental account and routes money to unapplied funds/refund review. It never restores capacity. Every receipt must have a decision before account closure.

## Close the rental account and lease

1. Read current settlement readiness after all stock and financial decisions.
2. Resolve every reported blocker; the button remains disabled until the server snapshot permits closure.
3. Enter the closure reason, confirm permanent closure, then **Close settled rental account**.

The server rereads all conditions under shared unit, booking/account and lease locks. A fresh account version is required. Closure requires:

- An activated lease physically cleared into SettlementPending, or an expired/cancelled booking with no activated custody.
- Zero goods, no pending stock instructions or stock counts, and no uncleared physical occupancy.
- All agreed commercial rental lines covered by original statement lines or unique rental supplements. A credit is an explicit correction; it does not excuse missing billing coverage.
- Zero outstanding charges, held security and verified unapplied funds; no Recorded receipt awaiting verification/rejection.

Closure records actor, database time and reason, sets the account Closed and an activated lease Closed, and releases any remaining schedule allocations for this booking only. A cancelled scheduled lease remains historical Cancelled. Subsequent receipt, allocation, charge correction, settlement, renewal and cancellation commands cannot reopen a Closed account. Read-only history and original-command replay remain available. Reopening or correcting a closed account requires a future explicit policy outside this release.

## API and command recovery

| Endpoint under `/api/v1` | Input / identity |
| --- | --- |
| `POST /statements/{id}/adjustments` | `kind`, whole-VND `amount`, or Service `quantity`/`unitPrice`; `evidenceReference`, `reason`, `approved:true` |
| `POST /statements/{id}/supplement-rent` | Evidence, reason, approved; source-version uniqueness |
| `POST /rental-accounts/{id}/settlement-entries` | `kind`, `amount`, optional matching statement/receipt ID, unique `reference`, evidence, `performedAt`, reason, `confirmed:true` |
| `GET /rental-accounts/{id}/settlement` | Account-scoped consistent readiness, totals, version and blockers |
| `GET /rental-accounts/{id}/settlement-history?page=1` | Independent 50-row pages for settlement and adjustment history |
| `POST /rental-accounts/{id}/close` | Reason, `closureConfirmed:true`, account `If-Match` |
| `POST /receipts/{id}/reject` | Reason and receipt `If-Match` |

Finance authorization, fresh CSRF and a UUID `Idempotency-Key` apply to every mutation. Customer members may read their own results. Known resources with insufficient role return403, foreign resources404; invalid evidence/amount/confirmation422, missing/stale versions428/412 and financial/state conflicts409. Whole-VND values are strings, bounded to decimal(18,0).

If a response is lost, retain the original command key, body and version. **Read settlement command** retrieves its saved result in the original organization's scope. Retry only the original command after a negative readback. An authentication failure after an uncertain retry does not establish failure. Draft edits and context changes never create a new identity for the unresolved action. Success messages stay bound to the originating UI context, including an operator without a selected facility. Pending commands are in component memory; a full page reload is not durable recovery storage.

Migration `20261028171009_FinancialSettlementJournals` adds append-only adjustments, settlement entries, allocation releases, account closure metadata and rejected-receipt support. Its down migration refuses to discard financial history or closure decisions. Existing owner databases and running packages are not migrated or replaced by this source delivery. Use a matching application package and schema during a separately authorized runtime update.
