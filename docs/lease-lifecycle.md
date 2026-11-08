# Termination, clearance and permanent unit retirement

These WP13 workflows use manager authorization, fresh resource versions, CSRF and durable command receipts. They retain all historical leases, grants, allocations, quotes, stock and financial documents. Browser acceptance is deferred; automated SQL/TestServer and component tests are separate evidence.

## Terminate an active lease

1. Sign in as the facility manager. Read the lease in **Leases & handover**.
2. In **End rental use**, enter a termination reason and explicitly confirm immediate termination.
3. Submit **Terminate lease**. Read back `ClearancePending` and termination metadata.
4. Cancel outstanding inbound requests. No new receipts or inbound postings are allowed after termination.
5. For outbound requests, the manager must approve release after termination. The clerk can then post the complete observed quantity. Approval given before termination does not authorize release afterward.
6. Once stock is zero and no Draft, Submitted or Approved request remains, inspect the warehouse and record physical clearance. The lease moves to `SettlementPending`.

Termination takes effect immediately at database time. Only this booking's remaining schedule allocations are truncated/released at the local date. Physical occupancy stays uncleared and blocks unconditional reuse until inspected clearance. Other customers' future bookings remain untouched. Commercial dates, issued statements, original contract versions and the existing deposit are preserved for settlement review. Continue through the [WP15 settlement workflow](settlement-workflow.md) for refunds, credit notes, deductions and final financial closure. Termination alone does not move money or mark a lease `Closed`.

Before handover, use the existing booking/lease cancellation workflow instead. Termination rejects Scheduled, Cancelled, SettlementPending and Closed leases. Duplicate submission with the original command key replays the saved result; a different stale command requires a fresh version.

## Permanently retire an empty unit

1. Open **Manage selected facility** as its manager and edit one unit.
2. Enter a reason, confirm permanent retirement, then select **Retire selected unit**.
3. Read the catalog again. It shows the retirement timestamp/reason and disables reuse actions.

The server refuses retirement while any live hold, future/current confirmed allocation, maintenance block, uncleared occupancy, nonzero stock or pending stock request remains. Expired holds and ended allocations remain as history without blocking retirement. Retirement unpublishes the unit and permanently prevents republishing/editing, new rates, maintenance, quotes, holds and renewals. No reactivation or deletion endpoint is provided.

## API contract

| Endpoint | Body | Required version | Receipt scope |
| --- | --- | --- | --- |
| `POST /api/v1/leases/{id}/terminate` | `{"reason":"…","terminationConfirmed":true}` | Lease `If-Match` | Customer organization |
| `POST /api/v1/units/{id}/retire` | `{"reason":"…","retirementConfirmed":true}` | Unit `If-Match` | Facility |

Both require a UUID `Idempotency-Key` and `X-CSRF-Token`. Reasons contain 1–500 characters. Foreign resources return 404; known resources with insufficient role return 403. Missing versions return 428, stale versions 412, invalid confirmation/input 422, and operational conflicts 409. `GET /leases/{id}` returns `terminatedAt`, `terminatedBy`, `terminationReason`; the manager catalog returns `retiredAt`, `retirementReason`.

For an unknown response, use the existing saved-command readback and original retry. Retain the original key, body and version even after editing the form or receiving an authentication failure on retry. Do not submit a second command while the first remains uncertain.

Migration: `20261028161040_LeaseTerminationUnitRetirement` adds nullable lifecycle metadata. Normal application startup does not migrate databases. Local tests apply migrations only to their own GUID-generated synthetic databases. Existing owner databases and running packages are not changed by this delivery.
