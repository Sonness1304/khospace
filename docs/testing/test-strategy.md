# Test strategy and acceptance catalog

All cases below are **DESIGN ONLY / NOT RUN** for the application. Documentation tests are separate. Use exact commit/commands/exit codes and real provider evidence when implemented.

## Test layers

- Unit: date intersections, rounding, state guards, policy calculations and authorization decisions.
- HTTP integration: ASP.NET WebApplicationFactory with real auth policy/configured test identities, request validation and error contracts.
- SQL Server integration: two independent connections/process requests for allocation, stock, funds, job and idempotency races. InMemory/SQLite cannot prove SQL Server lock behavior.
- Frontend component: form states, API errors, decimals/date display, role context and conflict recovery.
- Browser: five customer/operator journeys, keyboard/mobile/zoom; browser fixtures clearly labeled synthetic.
- Manual: physical workflow assumptions, visual usability, restore and portfolio explanation. Human approval is not replaced by model review.

## Acceptance cases

| Case | Requirements covered | Fixture/action | Expected result |
| --- | --- | --- | --- |
| TC-01 | FR-001–FR-004, FR-047 | Invalid login, sign-out, role/facility/org matrix, last-admin removal, invitation replay | Generic failures, revoked sessions and default-deny scope; no privilege escalation |
| TC-02 | FR-005–FR-009 | Published/retired units, bad dates, missing rate, maintenance conflict | Correct catalog projection, whole units, invalid ranges rejected and no displaced allocation |
| TC-03 | FR-010 | 20m²×5000/day for four days; change rate after quote/hold | 400000 rental +80000 deposit displayed separately; agreed snapshot and expiry preserved |
| TC-04 | FR-011–FR-013 | Two simultaneous opposite-order multi-unit holds; expiry/confirm race | One all-unit winner, zero partial losers; now>=expiry cannot confirm |
| TC-05 | FR-014–FR-015 | Cancel vs receipt verification; duplicate lease conversion | Allocation released once; money retained/unapplied/refund-needed; one lease |
| TC-06 | FR-016–FR-019 | Adjacent bookings with overdue occupant; zero stock but no clearance; conflicting renewal | Calendar history preserved, handover blocked, old lease unchanged on failed renewal, no automatic disposal |
| TC-07 | FR-020–FR-022 | Same SKU in A/B; forged item/unit references; draft/submitted request; expired inbound | Isolated items, no draft stock, authorized clerk posting only, expired inbound refused |
| TC-08 | FR-023, FR-025, FR-028 | Stock10, concurrent issues7+7; repeat same source with new request key | Exactly one issue commits, balance3, one effective posting and ledger/projection reconciliation |
| TC-09 | FR-024, FR-026–FR-027, FR-029 | Transfer crash, reversal after consumption, two count approvals, partial fulfillment overflow | Paired/all-or-none transfer, negative reversal rejected, independent approval, cumulative limits respected |
| TC-10 | FR-030, FR-034, FR-036 | Month/leap boundaries, double generation, overlapping amendment segments, issued-price correction | No missing/double days or duplicate obligation; immutable original with linked credit/supplement |
| TC-11 | FR-031–FR-033, FR-035 | Partial rental payment, separate deposit, cross-customer receipt, simultaneous full allocations/refunds | No over-allocation/refund, correct outstanding, deposit not revenue/payment, denied ownership mismatch |
| TC-12 | FR-037–FR-038 | Scheduled vs physically occupied vs maintenance; chosen reporting interval | Explicit labels, separate axes and documented denominators; no invented totals |
| TC-13 | FR-039–FR-042 | Download/export after membership revoke, malformed upload, duplicated outbox, transaction rollback | Scope rechecked, invalid content refused, deduplicated notice, required audit rolls back with business command |
| TC-14 | FR-043–FR-046, FR-048 | Clean clone/seed, lost command response, changed payload key, isolated restore | Synthetic-only seed; readback returns original; conflicting payload rejected; restore reconciliation and docs reproducible |
| TC-15 | FR-044 | Critical forms at390/768/1440 and200% zoom; keyboard; error/empty/unknown states | Controls reachable/labeled, no root clipping, input preserved, no blind mutation retry |
| TC-16 | NFR-001–NFR-003, NFR-010, NFR-014–NFR-015 | Cross-site write, guess foreign IDs, malicious upload and secrets in error path | CSRF/auth enforced, scope preserved, private storage, redacted logs; production-policy gaps remain explicit |
| TC-17 | NFR-004, NFR-006, NFR-011–NFR-013, NFR-016 | Fixed load fixture and baseline, CI builds, restart/double jobs | Measured latency recorded with environment; durable uniqueness and repeatable build; evidence types distinct |
| TC-18 | NFR-005, NFR-007–NFR-009 | Manual accessibility, backup/restore, timezone/leap/rounding/precision boundaries | Targets checked explicitly; RPO/RTO reported only after rehearsal; exact decimal/date outcomes |

## Mandatory adversarial examples

1. `[2026-11-01,2026-11-03)` and `[2026-11-03,2026-11-05)` are schedulable; midnight in another browser timezone does not alter two billable days.
2. Hold expires at10:15:00; confirmation waits for lock until10:15:00: reject, even if cleanup is stopped.
3. February leap year `[2028-02-28,2028-03-02)` has3days. At100m²×1000/day: February200000, March100000. Deposit60000 separate.
4. Rounding policy is **per statement line**, not per day: 10.5m²×101/day×2days =2121VND for February; one March day=1060.5→1061VND; total3182VND. A review proposing daily rounding is not silently adopted.
5. CustomerA has10 boxes; CustomerB has same SKU and20. Concurrent A issues7+7 yield A3/B20; no cross-organization fallback.
6. Receipt+10, issue6, reverse receipt10: refuse because projected balance would become-6. No history rewrite.
7. Generate a calendar month after an amendment splitting it on day15: price/version segments cover each lease date once, never once per version plus once per whole lease.
8. User revokes a membership between export creation and download: content delivery rechecks access.
9. Cancellation and finance verification race: final booking stays cancelled, verified money survives as unapplied/refund-needed and cannot recreate capacity.
10. Zero ledger balance but no manager clearance: incoming tenant handover still refused.
11. 10.5m²×101/day, Jan31–Feb2: quote contains two monthly lines1061+1061=2122VND; statements total2122 and deposit424VND. Whole-term rounding to2121 is forbidden.
12. Hold creates RentalAccount, finance allocates deposit before lease exists; cancel/expire keeps the account and refund-needed funds, while conversion attaches exactly one lease without moving/duplicating funds.
13. P0 Scheduled lease converted from confirmed booking: CancelRental cancels both states and releases units. Statement generation before activation fails. P0 post-term goods release requires manager approval, and physical-clearance after goods removal permits reuse without falsely completing P1 settlement.
14. P0 request100/observed85: reject original with reason, authorize replacement85, then post once; original contributes zero stock. This is covered by TC-07/08 and WP07/08.
15. Organization changes in another tab: old draft stays intact and submission is blocked until context is reconfirmed; forged child IDs still fail server authorization (TC-01/15/16, WP02/10/11).

## Data fixture

Two facilities, four units each, two customer organizations with matching SKU strings, separate customer admins/members, sales/clerk/manager per facility, finance and operator admin. At least one maintenance block, expired hold, future booking, active/overdue lease, partial payment and separate deposit. All names/references synthetic. Clock injectable for domain tests; authoritative database time for locked live allocation checks.

## Evidence and release gates

Each requirement mapping in [traceability](traceability.md) names a case and work package. Passing a broad case means all associated assertions for that release passed, not merely one happy path. P0 must pass all P0 mappings plus relevant security/concurrency checks. P1 adds its scenarios, attachment/private access, reversals/settlement and restore.

Receipt fields: repository/root, exact SHA, dirty scope, command argv, tool version, start/end UTC, structured exit code, test counts, fixture/provider, artifact hashes, unresolved failures. UI/manual and performance results remain separate. A reviewer may challenge evidence but cannot fabricate a host test receipt.
