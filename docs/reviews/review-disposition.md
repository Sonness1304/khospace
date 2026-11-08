# Review disposition and evidence

Current status: coordinator dispositions recorded; owner design review pending. This is not implementation acceptance.

## Atlas native-chat initial domain review

Existing chat **ATLAS**, thread `01a16ba0-2698-7132-b9e2-1dc15e755d96`, was explicitly delegated a bounded read-only review. Result: [atlas-domain-review.md](atlas-domain-review.md), hash-scoped to the four early documents listed there. It did not review the entire later design package.

| Findings | Disposition | Evidence in current design |
| --- | --- | --- |
| AT-01/02 allocation race/partial multi-unit booking | Accepted | BR-003/004; parent UnitLockService, WP03/WP05, TC-04 |
| AT-03/04 expiry/time boundaries | Accepted | BR-002/005/011; database time after lock; TC-04/18 |
| AT-05 overdue occupancy | Accepted | BR-007/016, FR-016/019, UC-03/09, TC-06 |
| AT-06/09 ownership/custody | Accepted | BR-017/018/025, composite ownership references, TC-07/08/16 |
| AT-07 repeat stock/negative balance | Accepted | BR-020/021/026, WP08, TC-08 |
| AT-08 transfer atomicity | Deferred to P1, not weakened | FR-024, WP14, TC-09; no P0 transfer action |
| AT-10 billing periods/amendments | Accepted | BR-009/013/014; TC-10 boundary/coverage cases |
| AT-11 deposit/payment separation | Accepted | BR-010/011/012/015/016; TC-05/11 |
| AT-12 customer leakage | Accepted | FR-002/039/042, BR-025; TC-01/13/16 |
| AT-13 unit history/capacity | Accepted | BR-001/006/008, snapshots and retirement P1 |
| AT-14 corrections/termination | Accepted for P1 | FR-017/018/026/027/033/034, WP13–15 |
| AT-15 stale screen/reports | Accepted | If-Match/412, FR-037/038/044/045, TC-12/14/15 |

Atlas's questions were resolved as proposed defaults, not reclassified as user decisions: one facility per booking, late money unapplied/refund review, scheduled daily billing separate from physical handover, no lot/serial/staging in P0, staff-posted actuals and retained historical ownership after membership removal.

## Workbench Atlas Ask review

Session `885534b6-be4e-465b-ba6a-59115ea2ded6`, runtime `ses_e81cbf82dffeUWJtUTXqJP6NKo`, request `1c916d60-c954-4d06-8d76-339b081bf55b`. Agent Atlas, project atlas, mode Ask. Requested catalog entry GPT-6.1 Sol through alias codex; exact upstream identity/account pin remains UNVERIFIED. Runtime reported input586/output4259/reasoning7111/cacheRead0 at readback; local usage is not billing.

Response visibly supplied eight prioritized model critiques; no tools/file access claimed. Accepted: stable parent locks across every mutation; database UTC after lock; no hold release gap; overdue blocks new unconditional holds/actual handover; customer organization differs from operator; canceled/expired bookings retain late funds; source-based posting dedup; partial fulfillment P1; explicit month boundaries; quote drift requires re-quote.

One suggested fixture used daily rounding. **Not adopted**: BR-009 defines statement-line rounding. TC-18's explicit 3182VND example pins our chosen policy. This is a policy difference, not a claim the model's arithmetic is universally wrong.

## Other requested models

Live 9router catalog at inspection exposed Gemini3.8 Flash High and ClaudeSonnet4.6. No DeepSeek High route appeared; Workbench displayed a DeepSeek Flash Free entry. User explicitly requested DeepSeek High; retain it as an optional review route when actually available, rather than substituting Flash/Free under that name. No DeepSeek inference or provider/account/catalog change was made.

### Claude Sonnet 4.6, supplied-summary review

Workbench session `2f9cd10a-ddf6-4450-8b93-d521ed1b9e33`, runtime `ses_e81bae691ffe1HZk2s0eyQf9sV`, request `a2c6f4cf-1012-404a-9a70-cfc3f6ece080`. Requested/reported route `ag/claude-sonnet-4-6`, Jarvis/Ask, READY_FOR_REVIEW. Runtime usage: input14157/output2957/reasoning0/cacheRead0/cacheWrite0; these are local reported counters, not a charge or upstream identity proof. The model read a supplied summary before the fresh-builder fixes, not all current files.

| Finding | Coordinator disposition |
| --- | --- |
| Money exists before lease | Accepted risk; stable RentalAccount anchors funds before lease. Do not adopt destructive cancellation reversal: refund-needed does not mean money refunded. BR-010/012, data model, TC-05/11/18 |
| Cancel vs finance race | Accepted; shared commercial lock hierarchy, state recheck, persistent finance review queue/filter and both race schedules required. Booking state and account finance state stay separate |
| Late funds/two clocks/SLA | Accepted; independent 24h quote/15min hold, explicit fresh hold checks, proposed finance review within two business days, no automatic revival |
| P0→P1 posting migration | Accepted; data model upgrade rehearsal and WP14, retain Type/ReversalOfId and original ledger IDs |
| Monthly segment identity | Accepted risk; use each unit's immutable LeaseVersionLine×covered period, not the suggested facility/version-only tuple which can conflate multiple units. BR-009/013, TC-10/18 |
| Expired outbound authority | Accepted; manager-approved post-term issue plus physical clearance P0. Shared posting invariant remains; authorization branch differs. No conditional hold P0 |

### Gemini 3.8 Flash High, supplied-summary UX review

Session `df5335c9-7832-47ce-9ffd-ccbf6f87fda3`, runtime `ses_e81b9a074ffeodk8Ix2KrEqIDK`, request `8e1921e7-404e-44e0-9d04-0c3c171595ed`. Requested/reported route `ag/gemini-3.8-flash-high`, Jarvis/Ask, READY_FOR_REVIEW. Runtime usage input10781/output3272/reasoning3446/cacheRead0/cacheWrite0; no independent billing/account/upstream verification. No file/web tools were used by this Ask review.

| Finding | Coordinator disposition |
| --- | --- |
| Billing dates vs actual handover | Accept explicit display and blocked handover. Automatic delayed-handover credits deferred P1; scheduled-date billing remains a prominent owner decision |
| P0 actual arrival mismatch | Accept reject/re-authorize replacement flow without partial posting. UX UI-07, stock state rules, TC-07/08 and WP07 |
| Quote/hold/deposit disclosure | Accept clear labels and rounding disclosure. Reject suggested automatic four-hour extension: evidence alone cannot reserve capacity under chosen demo policy |
| Cancellation during verification | Accept visible refund-needed state and immutable funds. Reject keeping units locked until refund; unoccupied capacity release and funds settlement are separate |
| Calendar accessibility/mobile | Accept agenda below768px and list at every size; semantic table P0 is allowed. Verified W3C APG grid source independently; grid role requires implemented focus/keyboard behavior |
| Cross-tab organization context | Accept form-bound organization/context version, preserved draft, explicit reconfirmation and server ownership validation; TC-01/15/16 |

Gemini suggested Temporal and Stripe documentation as possible leads. They were not used to establish the chosen rental dates, VND rounding or bank hold policy. The W3C source is cited in UX; reviewer-proposed accounting/legal claims were not adopted as facts.

## Fresh builder check

A fresh read-only builder reviewed the complete linked package and identified seven material ambiguities. Coordinator resolved them as explicit **proposed design decisions**, not owner-approved policies:

| Finding | Resolution |
| --- | --- |
| Deposit before/without lease | Stable RentalAccount created with held booking; deposit targets account; lease attaches, funds never migrate; account survives cancellation/expiry |
| P0 clearance pathway missing | Manager-approved post-term issue and physical-clearance P0 in WP06–08; financial closure/termination P1 |
| Cancel after lease conversion | One CancelRental updates booking+Scheduled lease+allocations atomically; P0 statements issue only after activation; activated termination P1 |
| Quote/statement rounding mismatch | Quote unit×calendar-month segments use the same per-line rounding as statements; TC-18 examples pin cross-month totals |
| Rate validity meaning | Rate sales-validity is evaluated on quote local creation date; one rate applies entire term; drift before hold requires re-quote |
| LeaseUnit identity across versions | Stable physical grant per lease/unit; immutable commercial LeaseVersionLines change without moving occupancy/stock identities |
| P0 memberships/NFR gates vague | Existing-user membership add/change/deactivate P0, invitation P1; SRS explicitly assigns NFR gates and local vs live deployment |

This check does not execute application code. The absence of an implementation remains explicit.

## Review limits

Models analyze supplied artifacts/text; they do not grant authority, run host tests or establish legal compliance. All application scenarios remain NOT RUN. Owner acceptance of demo policies and visual direction is pending.
