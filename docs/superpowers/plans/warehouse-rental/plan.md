# WarehouseRent Implementation Plan

> **For agentic workers:** Owner approved policies and UI A on October28,2026 and authorized Atlas P0 implementation with independent model reviews. Execute WP01–WP12 task by task; Jarvis coordinates evidence. WP13–WP16 remain P1 and are not authorized for implementation by this approval.

**Goal:** Deliver a testable fixed-unit warehouse rental and customer-owned inventory application.

**Architecture:** Modular ASP.NET Core monolith, React SPA and SQL Server, with shared transactional services for space, stock and funds. A single consumer checkpoint owns progress.

**Tech Stack:** Proposed .NET10/EFCore10, React/TypeScript and SQL Server; exact versions/runtime verified and pinned at WP01.

**Spec:** [Design baseline](../../specs/2026-10-27-warehouse-rental-design.md).

## Global Constraints

- User-confirmed fixed whole m² units; one operator and multiple facilities; one facility per booking is a proposed default.
- Required policy values and formulas are authoritative in [business rules](../../../requirements/business-rules.md); executor reads that source, not an outdated copy in a task.
- Complete docs and receive design acceptance before application scaffolding/dependency installation/code.
- Synthetic data; no payments, e-invoice, account/provider changes or publication without separately scoped authorization.
- One writer per path; preserve concurrent changes; scoped staging and local commits; exact-SHA review.
- PostgreSQL/SQLite/InMemory cannot replace SQL Server concurrency evidence for this design.

## Review Focus

- Two sessions request overlapping intervals or opposite-order unit sets: all-or-none allocation under parent locks.
- Expiry/finance/cancellation races: compare database UTC after locking, retain money and original command identity.
- Overdue physical occupancy: preserve goods and next booking; block handover/new unconditional hold.
- Forged nested customer IDs and revoked download permissions: server ownership checks across every path.
- Partial fulfillment/reversal/amendment/month boundaries: no negative stock, double fulfillment or duplicate charged dates.

## Execution cycle for each package

- [ ] Read current checkpoint, exact Git root/branch/HEAD/dirty scope and referenced requirements/contracts.
- [ ] Write the named meaningful acceptance/regression test first; run focused command and record the expected failure.
- [ ] Implement only the assigned paths/contracts; no command text from model output is executed automatically.
- [ ] Run focused tests, `dotnet test backend/WarehouseRent.sln` where applicable, frontend test/build commands established in WP01, and SQL/browser checks relevant to the package.
- [ ] Inspect diff/secrets, stage explicit paths, local commit; independent review/tests identify that exact SHA.
- [ ] Resolve findings with evidence, update consumer checkpoint and requirement status; owner acceptance remains explicit.

Commands/path layout below are **planned**, not existing executables. WP01 records actual project names/scripts and updates this plan consistently if tooling requires a change.

## WP01 — Foundation and runnable vertical skeleton

**Release:** P0. **Requirements:** FR-043 FR-048.

**Files owned:** backend/WarehouseRent.sln; backend/WarehouseRent.Api/Program.cs; backend/WarehouseRent.Infrastructure/Persistence/AppDbContext.cs; frontend/package.json; frontend/src/main.tsx; .github/workflows/ci.yml; global.json.

**Interfaces:** No prior app. Produce /health/live and /health/ready, configured SQL connectivity, migration/seed commands and SPA API base path.

**Tests:** backend/WarehouseRent.Tests/HealthTests.cs; frontend/src/app.test.tsx.

**Required assertions:** API liveness responds; readiness fails safely without SQL; Demo seed refuses production; fresh run reaches SPA/API/SQL.

**Owner/review:** Jarvis owns foundation; Atlas may implement after environment verification.

- [ ] RED: add named test assertions above and confirm failure for the intended missing behavior.
- [ ] GREEN: implement minimum behavior and verify focused test, then relevant suite.
- [ ] Review exact committed SHA; retain failing/unverified checks explicitly.
- [ ] Update [checkpoint](../../../project-checkpoint.md) with source revision, exact commands/results and next package.

## WP02 — Identity, customer organizations and staff scope

**Release:** P0. **Requirements:** FR-001 FR-002 FR-003.

**Files owned:** backend/WarehouseRent.Api/Features/Identity/*; backend/WarehouseRent.Api/Features/Organizations/*; frontend/src/features/auth/*.

**Interfaces:** Consume AppDbContext and Identity. Produce /auth/login/logout/me/csrf, ICurrentActor scope policy and customer membership context used by every later feature.

**Tests:** backend/WarehouseRent.Tests/IdentityScopeTests.cs; frontend/src/features/auth/auth.test.tsx.

**Required assertions:** Generic failed login; logout revokes; cross-customer detail/write denied; facility scope enforced; last admin cannot remove self.

**Owner/review:** Atlas owns feature; Jarvis reviews permission contract.

- [ ] RED: add named test assertions above and confirm failure for the intended missing behavior.
- [ ] GREEN: implement minimum behavior and verify focused test, then relevant suite.
- [ ] Review exact committed SHA; retain failing/unverified checks explicitly.
- [ ] Update [checkpoint](../../../project-checkpoint.md) with source revision, exact commands/results and next package.

## WP03 — Facilities, units, maintenance and availability

**Release:** P0. **Requirements:** FR-005 FR-006 FR-007 FR-008.

**Files owned:** backend/WarehouseRent.Api/Features/Facilities/*; backend/WarehouseRent.Infrastructure/Persistence/UnitLockService.cs; frontend/src/features/facilities/*.

**Interfaces:** Consume actor scope. Produce GET /availability and fixed unit projections; IUnitLockService.AcquireAsync(sortedUnitIds,transaction,cancellationToken) shared by allocation writers.

**Tests:** backend/WarehouseRent.Tests/AvailabilityTests.cs; backend/WarehouseRent.Tests/SqlUnitLockTests.cs.

**Required assertions:** Duplicate unit code rejected; invalid/excessive dates fail; maintenance conflicts roll back; overdue occupancy excluded for new holds.

**Owner/review:** One owner for shared unit lock and migrations.

- [ ] RED: add named test assertions above and confirm failure for the intended missing behavior.
- [ ] GREEN: implement minimum behavior and verify focused test, then relevant suite.
- [ ] Review exact committed SHA; retain failing/unverified checks explicitly.
- [ ] Update [checkpoint](../../../project-checkpoint.md) with source revision, exact commands/results and next package.

## WP04 — Quotes and deterministic commercial snapshots

**Release:** P0. **Requirements:** FR-010.

**Files owned:** backend/WarehouseRent.Api/Features/Quotes/*; backend/WarehouseRent.Domain/Rentals/PriceCalculator.cs; frontend/src/features/quotes/*.

**Interfaces:** Consume unit/rate versions and actor. Produce POST /quotes plus immutable quote lines: area/rate/date/policy/currency snapshots and separate rentalSubtotal/requiredDeposit.

**Tests:** backend/WarehouseRent.Tests/QuoteTests.cs; frontend/src/features/quotes/quote.test.tsx.

**Required assertions:** Four-day and leap-year calculation; per-line rounding; no capacity from quote; changed rate before hold yields explicit re-quote.

**Owner/review:** Atlas feature owner; Jarvis verifies arithmetic policies.

- [ ] RED: add named test assertions above and confirm failure for the intended missing behavior.
- [ ] GREEN: implement minimum behavior and verify focused test, then relevant suite.
- [ ] Review exact committed SHA; retain failing/unverified checks explicitly.
- [ ] Update [checkpoint](../../../project-checkpoint.md) with source revision, exact commands/results and next package.

## WP05 — Atomic holds, expiry, confirmation and cancellation

**Release:** P0. **Requirements:** FR-011 FR-012 FR-013 FR-014 FR-045.

**Files owned:** backend/WarehouseRent.Api/Features/Bookings/*; backend/WarehouseRent.Infrastructure/Persistence/IdempotencyStore.cs; frontend/src/features/bookings/*.

**Interfaces:** Consume quote snapshots, UnitLockService, database UTC and verified deposit interface. Produce all-or-none holds, stable RentalAccount created with held booking, persisted command receipts and CancelRental command shared with WP06. Use synthetic verified deposit seam until WP09 integrates it; label seam, never fake paid evidence.

**Tests:** backend/WarehouseRent.Tests/SqlBookingConcurrencyTests.cs; backend/WarehouseRent.Tests/IdempotencyTests.cs.

**Required assertions:** Opposite-order unit acquisition yields one winner; cleanup stopped expiry still correct; lost response readback; changed payload key409; cancel/receipt race; no partial allocation.

**Owner/review:** Critical package; Atlas code, Jarvis exact-SHA SQL evidence/review.

- [ ] RED: add named test assertions above and confirm failure for the intended missing behavior.
- [ ] GREEN: implement minimum behavior and verify focused test, then relevant suite.
- [ ] Review exact committed SHA; retain failing/unverified checks explicitly.
- [ ] Update [checkpoint](../../../project-checkpoint.md) with source revision, exact commands/results and next package.

## WP06 — Lease conversion, physical handover and overdue conflicts

**Release:** P0. **Requirements:** FR-015 FR-016 FR-019.

**Files owned:** backend/WarehouseRent.Api/Features/Leases/*; frontend/src/features/leases/*.

**Interfaces:** Consume confirmed booking, RentalAccount and unit locking. Produce one lease attached to the existing account, stable LeaseUnit/Handover identities, manager physical-clearance P0 and /operations/conflicts. CancelRental also cancels Scheduled lease pre-handover. Date end never clears occupancy; financial close remains P1.

**Tests:** backend/WarehouseRent.Tests/LeaseHandoverTests.cs; frontend/src/features/leases/lease.test.tsx.

**Required assertions:** Duplicate conversion same lease/account; partial readiness does not partly activate P0 lease; prior goods/uncleared state blocks handover; zero goods alone insufficient; manager-approved post-term outbound and physical clearance allow reuse; cancelling Scheduled lease releases booking/lease together.

**Owner/review:** Atlas code; Jarvis checks schedule/physical boundary.

- [ ] RED: add named test assertions above and confirm failure for the intended missing behavior.
- [ ] GREEN: implement minimum behavior and verify focused test, then relevant suite.
- [ ] Review exact committed SHA; retain failing/unverified checks explicitly.
- [ ] Update [checkpoint](../../../project-checkpoint.md) with source revision, exact commands/results and next package.

## WP07 — Customer items and controlled stock requests

**Release:** P0. **Requirements:** FR-020 FR-021.

**Files owned:** backend/WarehouseRent.Api/Features/Items/*; backend/WarehouseRent.Api/Features/StockRequests/*; frontend/src/features/stock-requests/*.

**Interfaces:** Consume organization scope and LeaseUnit. Produce customer SKU catalog and Draft/Submitted/Approved request states, one base UOM, complete fulfillment contract P0.

**Tests:** backend/WarehouseRent.Tests/StockRequestTests.cs; frontend/src/features/stock-requests/request.test.tsx.

**Required assertions:** Same SKU across customers distinct; submitted request no balance impact; forged children denied; expired inbound rejected while clearance outbound remains reachable.

Include the P0 discrepancy path: reject an unposted request, authorize a linked replacement at observed quantity, and post the replacement completely once. Do not add partial fulfillment here.

**Owner/review:** Atlas code; shared item/request contracts frozen before WP08.

- [ ] RED: add named test assertions above and confirm failure for the intended missing behavior.
- [ ] GREEN: implement minimum behavior and verify focused test, then relevant suite.
- [ ] Review exact committed SHA; retain failing/unverified checks explicitly.
- [ ] Update [checkpoint](../../../project-checkpoint.md) with source revision, exact commands/results and next package.

## WP08 — Receipt/issue ledger and locked stock balances

**Release:** P0. **Requirements:** FR-022 FR-023 FR-025 FR-028.

**Files owned:** backend/WarehouseRent.Api/Features/StockPosting/*; backend/WarehouseRent.Infrastructure/Persistence/StockLockService.cs; frontend/src/features/stock/*.

**Interfaces:** Consume approved request, staff scope and idempotency. Produce /stock-requests/{id}/post, immutable ledger plus balance projection and scoped query endpoints.

**Tests:** backend/WarehouseRent.Tests/SqlStockPostingTests.cs; backend/WarehouseRent.Tests/LedgerReconciliationTests.cs.

**Required assertions:** Stock10 and concurrent7+7 yields3; multi-line failure rolls all back; duplicate source under different keys once; unchanged other customer; missing-balance-row race covered.

**Owner/review:** Critical package; exact-SHA SQL review, no direct balance editing.

- [ ] RED: add named test assertions above and confirm failure for the intended missing behavior.
- [ ] GREEN: implement minimum behavior and verify focused test, then relevant suite.
- [ ] Review exact committed SHA; retain failing/unverified checks explicitly.
- [ ] Update [checkpoint](../../../project-checkpoint.md) with source revision, exact commands/results and next package.

## WP09 — Rental statements and manual verified funds

**Release:** P0. **Requirements:** FR-030 FR-031 FR-032 FR-035.

**Files owned:** backend/WarehouseRent.Api/Features/Billing/*; backend/WarehouseRent.Api/Features/Payments/*; frontend/src/features/finance/*.

**Interfaces:** Consume immutable lease commercial segments/quote monthly rounded lines and stable RentalAccount. Produce monthly generator only after activation, verified receipts/fund allocation and stable deposit target shared with WP05. Replace synthetic deposit seam through real manual verification; quote total equals unchanged statements' summed segments.

**Tests:** backend/WarehouseRent.Tests/SqlBillingTests.cs; backend/WarehouseRent.Tests/SqlFundsTests.cs.

**Required assertions:** Leap-month/rounding exact; duplicate generation once; partial charge payment correct; deposit separate; over-allocation/cross-customer denied; cancellation/late receipt retained.

**Owner/review:** Atlas code; Jarvis financial arithmetic review, no payment API.

- [ ] RED: add named test assertions above and confirm failure for the intended missing behavior.
- [ ] GREEN: implement minimum behavior and verify focused test, then relevant suite.
- [ ] Review exact committed SHA; retain failing/unverified checks explicitly.
- [ ] Update [checkpoint](../../../project-checkpoint.md) with source revision, exact commands/results and next package.

## WP10 — Integrated operator/customer workspace and transactional audit

**Release:** P0. **Requirements:** FR-037 FR-041 FR-044.

**Files owned:** frontend/src/features/operations/*; frontend/src/app/routes.tsx; backend/WarehouseRent.Api/Features/Audit/*.

**Interfaces:** Consume all P0 modules. Produce role/context navigation, scoped calendar/list, history and error/empty/stale/unknown states. Audit write is part of relevant command transaction.

**Tests:** backend/WarehouseRent.Tests/AuditRollbackTests.cs; frontend/src/features/operations/operations.test.tsx.

**Required assertions:** Audit failure rolls command back; context switch never reuses foreign IDs; calendar distinguishes booked/occupied; duplicate-click UI still backed by command safety.

**Owner/review:** Frontend owner explicit; shared contract edits coordinated.

- [ ] RED: add named test assertions above and confirm failure for the intended missing behavior.
- [ ] GREEN: implement minimum behavior and verify focused test, then relevant suite.
- [ ] Review exact committed SHA; retain failing/unverified checks explicitly.
- [ ] Update [checkpoint](../../../project-checkpoint.md) with source revision, exact commands/results and next package.

## WP11 — Acceptance, resilience and accessible journeys

**Release:** P0. **Requirements:** FR-002 FR-044 FR-045.

**Files owned:** frontend/e2e/rental.spec.ts; frontend/e2e/stock.spec.ts; frontend/e2e/finance.spec.ts; backend/WarehouseRent.Tests/Acceptance/*.

**Interfaces:** Consume implemented P0 at fixed SHA. Produce host evidence for five journeys, SQL concurrency and scope matrix; no new app behavior unless fixing a reproduced failing requirement.

**Tests:** frontend/e2e/*.spec.ts; backend/WarehouseRent.Tests/Acceptance/*.

**Required assertions:** Search/hold, confirmation/handover, receipt/history, issue/conflict, finance/allocations; keyboard/390/768/1440/200% checks; timeout/readback.

**Owner/review:** Tester/read-only reviewer after developer commit; failures back to owner.

- [ ] RED: add named test assertions above and confirm failure for the intended missing behavior.
- [ ] GREEN: implement minimum behavior and verify focused test, then relevant suite.
- [ ] Review exact committed SHA; retain failing/unverified checks explicitly.
- [ ] Update [checkpoint](../../../project-checkpoint.md) with source revision, exact commands/results and next package.

## WP12 — Demo, deployment candidate and portfolio handoff

**Release:** P0. **Requirements:** FR-043 FR-048.

**Files owned:** README.md; docs/demo-script.md; docs/deployment.md; ops/*.

**Interfaces:** Consume accepted test evidence and reproducible seed. Produce clean-clone setup, demo video/script, known gaps and selected deployment instructions. Actual paid/public deployment needs explicit scope.

**Tests:** tests/setup-smoke.ps1; frontend/e2e/demo.spec.ts.

**Required assertions:** Clean install/run; synthetic roles/data only; no secrets; all links work; build/test exact commands and revision; user explains key transaction.

**Owner/review:** Jarvis handoff; owner accepts, no auto-publish.

- [ ] RED: add named test assertions above and confirm failure for the intended missing behavior.
- [ ] GREEN: implement minimum behavior and verify focused test, then relevant suite.
- [ ] Review exact committed SHA; retain failing/unverified checks explicitly.
- [ ] Update [checkpoint](../../../project-checkpoint.md) with source revision, exact commands/results and next package.

## WP13 — Lease amendments, retirement and clearance

**Release:** P1. **Requirements:** FR-009 FR-017 FR-018.

**Files owned:** backend/WarehouseRent.Api/Features/LeaseAmendments/*; frontend/src/features/lease-amendments/*.

**Interfaces:** Consume stable LeaseUnit/Handover/RentalAccount and commercial LeaseVersionLines. Keep occupancy/stock grant IDs through renewal; produce non-overlapping effective charge segments, P1 termination and settlement→closed transitions, unit retirement. Physical-clearance P0 already exists in WP06.

**Tests:** backend/WarehouseRent.Tests/SqlLeaseAmendmentTests.cs.

**Required assertions:** Conflicting renewal keeps original; future bookings survive overstay incident; outstanding goods block closure; unit split/area change cannot rewrite history.

**Owner/review:** Atlas code after P0 acceptance.

- [ ] RED: add named test assertions above and confirm failure for the intended missing behavior.
- [ ] GREEN: implement minimum behavior and verify focused test, then relevant suite.
- [ ] Review exact committed SHA; retain failing/unverified checks explicitly.
- [ ] Update [checkpoint](../../../project-checkpoint.md) with source revision, exact commands/results and next package.

## WP14 — Transfers, partial fulfillment, reversals and stock counts

**Release:** P1. **Requirements:** FR-024 FR-026 FR-027 FR-029.

**Files owned:** backend/WarehouseRent.Api/Features/StockCorrections/*; frontend/src/features/stock-corrections/*.

**Interfaces:** Consume posting/ledger service. Produce fulfillment IDs per partial posting, atomic transfer pair, linked reversal and separate-person count approval.

**Tests:** backend/WarehouseRent.Tests/SqlStockCorrectionTests.cs.

**Required assertions:** Two-leg rollback; no negative reversal; cumulative fulfillment bound; concurrent reverse once; stale count requires review; distinct approver.

**Owner/review:** Migrate P0 unique RequestId into P1 fulfillment uniqueness explicitly.

- [ ] RED: add named test assertions above and confirm failure for the intended missing behavior.
- [ ] GREEN: implement minimum behavior and verify focused test, then relevant suite.
- [ ] Review exact committed SHA; retain failing/unverified checks explicitly.
- [ ] Update [checkpoint](../../../project-checkpoint.md) with source revision, exact commands/results and next package.

## WP15 — Credits, services and deposit settlement

**Release:** P1. **Requirements:** FR-033 FR-034 FR-036.

**Files owned:** backend/WarehouseRent.Api/Features/Settlements/*; frontend/src/features/settlements/*.

**Interfaces:** Consume issued charges/receipts/deposit journal. Produce credits, supplemental charges, authorized deductions/refund records and settlement readiness.

**Tests:** backend/WarehouseRent.Tests/SqlSettlementTests.cs.

**Required assertions:** No issued edit; no over-refund under concurrency; separate deposit liability; cancellation and confirmed receipt converge; closed only after all conditions.

**Owner/review:** Atlas feature owner, Jarvis reviews accounting boundary.

- [ ] RED: add named test assertions above and confirm failure for the intended missing behavior.
- [ ] GREEN: implement minimum behavior and verify focused test, then relevant suite.
- [ ] Review exact committed SHA; retain failing/unverified checks explicitly.
- [ ] Update [checkpoint](../../../project-checkpoint.md) with source revision, exact commands/results and next package.

## WP16 — Invitations, private documents, reporting and recovery

**Release:** P1. **Requirements:** FR-004 FR-038 FR-039 FR-040 FR-042 FR-046 FR-047.

**Files owned:** backend/WarehouseRent.Api/Features/Invitations/*; backend/WarehouseRent.Api/Features/Documents/*; backend/WarehouseRent.Api/Features/Reports/*; backend/WarehouseRent.Infrastructure/Jobs/*; ops/backup-restore.md.

**Interfaces:** Consume identity/domain contracts. Produce single-use invite/reset, private files, outbox, scoped reports/exports and restore rehearsal.

**Tests:** backend/WarehouseRent.Tests/SqlOperationsTests.cs; frontend/e2e/operations.spec.ts.

**Required assertions:** Revoke before download denies; invalid files fail; duplicate notices/statement jobs once; formula-safe CSV; restored allocation/ledger/funds reconcile.

**Owner/review:** May split into independently reviewed tasks at execution; one owner per shared migration.

- [ ] RED: add named test assertions above and confirm failure for the intended missing behavior.
- [ ] GREEN: implement minimum behavior and verify focused test, then relevant suite.
- [ ] Review exact committed SHA; retain failing/unverified checks explicitly.
- [ ] Update [checkpoint](../../../project-checkpoint.md) with source revision, exact commands/results and next package.
