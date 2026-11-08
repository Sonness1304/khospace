# Same-unit renewal workflow

A customer administrator requests a new quote for exactly the existing lease units, from the current lease end to the new end. In **Leases & handover**, enter the lease and quote IDs and select **Preview renewal**. Review added rent, unchanged deposit and the observation time. The preview does not reserve capacity.

Enter a reason, check **I accept the added rent with the existing deposit unchanged**, and select **Confirm renewal terms**. Save the displayed renewal acceptance ID. Only a current customer administrator may confirm customer terms; Sales cannot manufacture customer acceptance.

An assigned Sales user reads the lease/acceptance ID using **Read accepted renewal**, enters an application reason and selects **Apply accepted renewal**. The server rechecks the accepted versions, quote expiry/current price and capacity under the shared unit locks. Successful application appends a contract version and monthly commercial snapshots, extends the booking and physical-custody end, and preserves the existing lease-unit IDs, goods and deposit requirement. No new handover is needed for continuously occupied unchanged units.

If a response is uncertain, use **Read renewal command**. A negative readback permits retry with the original command key and body. Current access is still checked by the server. Readback and retry controls preserve unresolved commands while switching workspace views; reloading the page still loses the in-memory command identity, as documented for P0.

An accepted quote does not reserve space. A competing booking or maintenance block may make application fail without changing the existing contract. Expired quotes or changed versions require another quote/confirmation. Same-command replay and another application of the same accepted renewal return its original result without creating another version.

An extension touching an already-issued statement month keeps the issued document unchanged. Finance issues its added commercial version lines through **Issue unbilled rental supplements**; source uniqueness prevents double charging, and uncovered lines block final closure. See the [settlement workflow](settlement-workflow.md). Same-unit extensions remain supported; unit additions/removals are outside this release. Termination/retirement and final financial settlement have their own documented workflows.

API: POST `/api/v1/leases/{id}/renewals` accepts `quoteId`, `expectedQuoteVersion`, `expectedLeaseVersion`, `reason` and a UUID `Idempotency-Key`. POST `/api/v1/leases/{id}/renewals/{acceptanceId}/apply` accepts `reason` and its own command key. GET the same renewal URL reads acceptance/application state. POST `/renewal-preview` remains a non-reserving observation. Migration `RenewalAcceptance` adds durable accepted terms and frozen application results.
