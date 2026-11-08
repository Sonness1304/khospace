# WP08 — Posted custody ledger and locked balances

October28,2026. Base3844deea30c6b9f1a62ee9ce17c2636d272a5706, sole writer Jarvis. FR-022/023/025/028.

Assigned clerks post every approved request line once at exactly observed authorized quantities. Any mismatch requires explicit customer replacement, never silent adjustment/partial posting. The source request has a unique StockPosting; different-key repeated complete posting returns that original posting. Sorted parent units→booking/account→lease→request→ordered balance keys hold in one transaction. UPDLOCK/HOLDLOCK over the composite balance PK protects missing rows as well as existing rows. Positive receipt or negative issue ledger, locked balance projection, Fulfilled state, transactional audit and command receipt commit together. Negative/precision-overflow balances fail; current custody, receipt eligibility and post-term manager authorization are rechecked at actual posting. A prior in-term approval does not become post-term authorization automatically. Ledger/posting entities reject update/delete through DbContext; no direct balance-edit API or stock correction P1 is exposed.

Real IInventoryClearance replaces the unavailable production reader. Zero balances plus no Draft/Submitted/Approved requests are observed under the same persistent parent locks as all request/posting writers, then manager inspection clears occupancy separately from finance. New pending request/clearance race cannot both succeed. The old WP06 absent-module fixture now explicitly injects UnavailableInventoryClearance when testing that condition, rather than pretending the installed module is missing.

UI requires explicit per-line observed quantity and physical count confirmation for clerk posting, preserves original unknown command identities/readback/versioning, and displays scoped paginated balances and source-linked movement history. Read pages are identified as independent observations while operations continue. Full shared workspace navigation WP10 remains outstanding.

Verification:

- `dotnet test backend/WarehouseRent.sln --no-restore --filter FullyQualifiedName~SqlStockPostingTests` RED exit1/3FAIL404; GREEN and extended final exit0/6PASS.
- Real SQL covers10stock/concurrent7+7→onewinner/balance3 with ledger reconciliation; absent balance receipt5+8→13 and duplicate source/newkey no second posting; complete observation mismatch409; pending/nonzero block clearance then zero/no-pending plus inspection permit it; multi-line insufficient issue rolls back projection/posting/audit/command and keeps Approved; append-only update rejected; create-request/clearance race one winner; late inbound rejected at posting and previously approved outbound requires new manager approval. Customer cannot post and foreign customer cannot read balances.
- Additional SQL staff-command recovery assertion verifies organization scope200/facility scope404; it supports rejection of Gemini's WP07 scope suggestion.
- UI posting RED missingbutton then4requestUIPASS, stock query missingmodule RED then2UIPASS (scoped hiding, source links, error/independent pagination).
- First full verification failed only stale WP06 absent-module fixture (expected409/actual200 because real inventory was correctly installed). TEST-DEFECT resolved with explicit unavailable test DI, no production check weakened.
- Final `./ops/verify.ps1`: exit0 docs4/backend74/frontend67PASS, no skips; lint/typecheck/buildPASS. Aggregate frontend statements88.54/branches83.20/functions89.95/lines95.68, unchanged80% thresholds.

Native exact-SHA review pending commit. Funds verification remains unavailable untilWP09. Only generated fresh synthetic databases migrated. No browser/Kestrel/remote CI after earlier policy rejection; no workaround, publication or server/account configuration changes.
