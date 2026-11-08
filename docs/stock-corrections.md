# WP14: controlled inventory changes

Use **Inventory → Transfers, reversals & counts** for corrections. Existing customer requests remain the authorization for ordinary receipts and issues; corrections never edit a posted ledger row. Data is synthetic and local. Browser acceptance is deferred.

## Transfer between leased units

1. Sign in as the facility clerk, select the facility and read current inventory.
2. Enter the customer organization, source lease-unit grant, item and reason. Use grant IDs, not physical unit IDs.
3. Enter the destination grant and quantity in the item's base unit, with at most three decimal places.
4. Check both physical legs, then **Post transfer**. The saved posting document ID identifies both the negative source and positive destination entry.

Both grants must be handed over, within their rental term, nonterminated, and allocated to the same organization at the same facility. Hidden units can retain existing custody; retired units cannot be used. Transfers cannot create negative balances or exceed quantity precision. They move existing goods, so an inactive item's existing stock may be transferred. Cross-facility transport is excluded.

The client transfer UUID is a business identity. Reusing it with the same payload returns the same document; changing its payload is a conflict. A different, deliberately submitted transfer UUID represents a different observed movement.

## Reverse an erroneous posting

1. As a facility manager, copy the source posting ID from movement history and **Read posting document**.
2. Inspect all entries and enter a reason. Confirm **a full compensating reversal**, then reverse.
3. Read the saved reversal document and refresh inventory.

A reversal appends the exact opposite deltas and links the original immutable document. A Receipt, Issue or Transfer can be reversed once, including an original P0 full posting. Consumed receipts cannot be reversed if the result would be negative. A transfer reversal reverses both legs atomically. Grants must still have physical custody. A positive reversal delta requires a current, nonterminated term; reversal cannot reintroduce goods after physical clearance or termination.

Reversal does not reopen a fulfilled customer instruction or reduce historical gross fulfillment. Any replacement receipt/issue needs a new authorized request. Reverse-of-reversal and direct reversal of count adjustments are refused; record a fresh independently approved count for a count correction.

## Count and independently approve an adjustment

1. As a clerk or manager, enter the expected stock shown by the server, actual counted stock, organization/grant/item and a reason. **Record count observation** does not change inventory.
2. A different facility manager finds its ID through **List count observations** or reads the supplied observation ID.
3. Review the recorder, expected and counted quantities. Enter the approval reason and **Approve count adjustment**.
4. Approval rereads quantity and balance rowversion under locks. Any intervening movement invalidates the observation, even when stock later returns to the original quantity. Cancel a stale observation and recount.

The recorder cannot approve their own count, including an operator administrator. Pending counts block physical clearance and permanent retirement. The recorder or a facility manager can cancel an unapproved count. Zero-difference approval records a completed observation and adjustment document without a zero ledger entry. Post-term counts may reconcile actual goods still in custody; a count is not a new delivery and cannot recreate cleared custody.

## Partial receipt and issue

Read an Approved request in **Stock requests**. Its lines show requested, gross fulfilled and remaining quantities. Enter only positive quantities actually observed; leave unobserved lines blank, check the physical confirmation and **Post partial stock movement**. Each partial posting has a distinct fulfillment UUID and immutable posting ID. Cumulative fulfillment cannot exceed any approved request line.

The request remains Approved until every line is complete. Customer cancellation stops its remaining quantity while preserving posted history. A partially posted request cannot be rejected or sent through the legacy complete-post route. An excess quantity needs a new/revised customer-approved instruction; the server never silently increases the authorized amount. Both partial and complete postings recheck WP13 termination, custody and manager outbound-release authorization.

## API and recovery

| Endpoint | Required role | Concurrency / identity |
| --- | --- | --- |
| `POST /stock/transfers` | Clerk | `transferId`, same payload for replay |
| `POST /stock/postings/{id}/reverse` | FacilityManager | Original posting unique reversal, `reversalConfirmed: true` |
| `POST /stock/counts` | Clerk or FacilityManager | Expected quantity; server captures balance rowversion |
| `POST /stock/counts/{id}/approve` | Different FacilityManager | Observation `If-Match`, current expected balance and rowversion |
| `POST /stock/counts/{id}/cancel` | Recorder with current staff scope, or FacilityManager | Observation `If-Match` |
| `POST /stock-requests/{id}/fulfillments` | Clerk | `fulfillmentId`, request `If-Match`, positive observed subset |
| `GET /stock/postings/{id}` | Customer member or scoped stock staff | Source, reversal link and all entries |
| `GET /stock/counts/{id}` | Customer member or scoped stock staff | Immutable observation, approval actor and posting link |
| `GET /stock/counts?organizationId=…&facilityId=…&page=1` | Customer member or scoped stock staff | Bounded 50-row pages |

Paths use `/api/v1`. Every mutation also requires fresh CSRF, a reason and a UUID `Idempotency-Key`. Foreign resources return 404; known resources with insufficient role return 403. Validation returns 422; missing/stale versions return 428/412; stock/state/snapshot conflicts return 409. Stock quantities are decimal strings, never floating-point financial values.

After an unknown response, read the original command result by organization scope and retain its original key, body and version. Retry only that original command after readback reports no saved result. Authentication failure after an uncertain retry does not prove the original operation failed. Switching staff context must not display private results or permit retry in another scope.

## Upgrade and delivery boundaries

Migration `20261028163914_StockCorrectionsPartialFulfillment` preserves existing posting/ledger IDs and deltas, backfills original full fulfillment IDs and cumulative quantities, adds balance rowversions and correction sources, and replaces P0 request uniqueness with explicit full-post/fulfillment uniqueness. Posted records remain append-only. Full P0 API requests remain compatible through the new application package. Run a package matched to its schema; do not point the old binary at an upgraded database.

A populated synthetic P0-schema rehearsal upgrades, checks unchanged history/balances and reverses an original document. Down migration works before P1 records are created; once P1 postings or count observations exist it refuses destructive downgrade. Production rollback requires a deliberate data-preserving migration, not dropping correction history.

No existing owner database, running API or browser process is changed by this source delivery. Automated SQL/TestServer/component checks do not establish browser accessibility acceptance. Financial settlement and final lease closure use the separate [WP15 settlement workflow](settlement-workflow.md).
