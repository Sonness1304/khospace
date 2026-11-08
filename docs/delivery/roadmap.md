# Delivery roadmap, estimates and risks

Status: proposed estimates, not a promise or scheduled automation. Scope is [product brief](../requirements/product-brief.md). Actual throughput depends on available hours, SQL Server setup and review findings.

## Phase gates

| Gate | Output | Completion evidence |
| --- | --- | --- |
| G0 Research | Source/license comparison and confirmed business model | Sources read, limitations stated |
| G1 Requirements | SRS/rules/use cases/glossary | Contradictions resolved; owner reviews proposed policies |
| G2 Design | Data/API/UX/test/traceability | Builder/reviewer findings disposition; visual direction selected |
| G3 Implementation approval | Frozen baseline and executable work packages | Explicit user acceptance; no automatic start from a draft plan |
| G4 Portfolio slice | Working P0 end-to-end flow | SQL Server/HTTP/browser/CI evidence, setup and demo |
| G5 Operational MVP | P1 settlement/corrections/restore | Negative tests, restore rehearsal and user acceptance |

## 15 working-day portfolio slice after G3

Assumes approximately 4–6 focused hours per day plus agent assistance: 60–90 human hours. This is a challenging planning envelope, not evidence of completion. Days1–15 start **after design approval**, not automatically on a calendar date.

| Day | Work package | Daily deliverable |
| --- | --- | --- |
| 1 | WP01 | Verified toolchain, API/SPA/SQL connection, migrations and CI skeleton |
| 2 | WP02 | Login, organization/facility scope and negative auth tests |
| 3 | WP03 | Facilities/whole units/maintenance and searchable catalog |
| 4 | WP04 | Price snapshots, day calculations and quote UI/API |
| 5–6 | WP05 | Atomic holds/expiry/confirmation and concurrent SQL tests |
| 7 | WP06 | Lease conversion, handover and physical conflict display |
| 8 | WP07 | Customer items and stock requests with server ownership checks |
| 9–10 | WP08 | Receipt/issue posting ledger; no-negative/dedup SQL tests |
| 11 | WP09 | Monthly charge slices, verified manual receipts and deposit separation |
| 12 | WP10 | Integrated operator/customer screens and operational calendar |
| 13 | WP11 | Critical browser flows, responsive/error/keyboard checks |
| 14 | WP12 | Clean-clone run, deployment candidate or clearly labeled local demo, English README/video |
| 15 | WP12 | Adversarial regression, review fixes, acceptance demo and CV evidence |

If human availability is 3–4h/day, plan roughly 4–6 weeks for the same slice rather than pretending every task fits. The full P1 operational MVP is estimated at a further 60–100 human hours including regression and rehearsal; total rough range **120–190 hours**, to be recalibrated after WP05 and WP08. Agent tasks may overlap documentation/review but cannot eliminate integration and owner decisions.

## P1 follow-on sequence

1. WP13 lease amendments/renewals and clearance/closure.
2. WP14 transfers, partial fulfillment, reversals/counts with independent approval.
3. WP15 statement credits, service charges and deposit settlement.
4. WP16 private attachments, invitations, notices, reports and restore rehearsal.

These are separate deliverables; every package has tests, exact revision review, local commit and evidence. No live bank, email, e-invoice or deployment account is implied.

## Critical path and ownership

Identity scope → unit catalog → quote → allocation locking → lease handover → stock posting → charges/integrated demo. UI scaffolding can progress against approved API contracts, but allocation/stock/funds contracts have one owner at a time. Two writers never modify shared migrations/contracts concurrently.

Atlas is a possible implementation owner after approval, not mandatory for every task. Jarvis coordinates requirements, sources, integration and acceptance evidence. Claude reviews architecture/requirements; Gemini 3.8 can challenge UX/edge cases and suggest documentation queries. All model findings require source/behavior verification; model agreement is not owner acceptance.

## Risk register

| Risk | Impact | Mitigation / trigger |
| --- | --- | --- |
| Full scope mistaken for 15-day scope | Unfinished project | Freeze P0, defer P1 features visibly; reassess after WP05 |
| 15-minute hold too short for manual deposit | Poor operational fit | Demo policy only; owner can choose longer reservation/deposit workflow before implementation |
| SQL Server unavailable locally | Concurrency evidence missing | Resolve at WP01, use authorized SQL instance; do not relabel in-memory tests as equivalent |
| Overstay vs next rental | Physical double occupancy | Explicit conflict queue and blocked handover; operator resolution required |
| Monolithic “Completed” state | Lost money/goods trace | Independent schedule/physical/stock/financial states |
| Frontend permission-only checks | Customer data leak | API/object/reference/job/export negative tests |
| Overengineering architecture | Schedule loss | Modular monolith, no broker/microservices until justified |
| AI-generated code not understood | Weak interview demonstration | User explains each critical transaction and fixes one issue independently |
| Missing operational/legal policies | Unsuitable real deployment | Mark demo defaults; operator/professional review before real usage |
| Hosting free-tier changes | Demo unavailable | Verify terms near deployment; local reproducible demo/video fallback, no invented uptime |

## Portfolio evidence

Show one race test for booking, one concurrent stock issue test, one cross-customer denial, one idempotent retry, one charge boundary calculation and five browser journeys. Record personal contribution and upstream/model assistance honestly. No fabricated users, performance numbers, uptime, commits or project adoption.
