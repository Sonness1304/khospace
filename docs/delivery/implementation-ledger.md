# Implementation ledger — plan: docs/superpowers/plans/warehouse-rental/plan.md

Current status source: [checkpoint](../project-checkpoint.md). Owner approved policies/UI A/P0 with Atlas coding and independent reviews on October28,2026.

## Preflight: package self-consistency

| Package | Contract/test/file review and ruling |
| --- | --- |
| WP01 | Listed files omit necessary manifests/tests: Atlas may create necessary solution/projects/migrations/tests/config/CI within backend/frontend/ops. No feature expansion |
| WP02 | Last-admin test implies membership admin: include existing-user memberships P0, no invitations P1 |
| WP03 | Availability/unit-lock contract aligns; real SQL tests required |
| WP04 | BR-009 monthly-line rounding authoritative, never daily rounding |
| WP05 | Deposit seam test-only until WP09; no fake-paid production route |
| WP06 | Physical reuse P0; financial close P1; shared CancelRental sequential owner |
| WP07 | Mismatch replacement needs customer authorization; no stock from approval |
| WP08 | Immutable ledger plus locked projection/source uniqueness; missing-row races tested |
| WP09 | Statements after activation; real verified funds replace seam; quote lines reused |
| WP10 | UI A; audit integrated with earlier commands, transaction rollback tested here |
| WP11 | Exact-SHA independent browser/SQL acceptance; no model report as host evidence |
| WP12 | Local demo/setup authorized; external deployment/push excluded |
| WP13–16 | P1 interfaces checked against P0 but implementation deferred |

## Preflight: shared interfaces/files

| Producer → consumers / shared paths | Ruling |
| --- | --- |
| WP01→WP02–10, solution/DbContext/migrations/frontend packages | Sequential Atlas writer may edit shared foundation for the assigned package; no parallel writer |
| WP02→WP03–10, CurrentActor/scope/CSRF | Shared server access policy; validate nested ownership |
| WP03→WP04/05/06, UnitLockService/availability | Every allocation/occupancy writer uses same sorted parent-lock service |
| WP04→WP05/09, quote/monthly lines | Freeze commercial types before holds/billing; no duplicate formulas |
| WP05↔WP06/09, CancelRental/account/funds | Account precedes lease; units→account→receipts→targets lock order; cancelled money retained |
| WP06→WP07/08/09, LeaseUnit/handover | Stable stock grant identity; manager physical clearance P0 reachable |
| WP07→WP08, items/approved requests | Full posting once; replaced original unposted |
| WP08↔WP06/09/10, stock/clearance/audit | Zero goods plus manager clearance; audit same transaction |
| WP09→WP05/10/11, verified deposits/funds | Remove synthetic production seam before integration acceptance |
| WP02–10→WP11/12, contracts/setup | Independent checks exact developer SHA; coordinator sole checkpoint writer |

Ruling: use native existing ATLAS chat explicitly authorized by owner, in separate consumer worktree. Workbench cannot yet select this consumer; use its Ask mode only for bounded supplied-artifact review. No runtime/provider configuration expansion. Cost: explicit root/SHA binding checked every package.

Ruling: SQL Server2019 Developer15.0.2200.2 is reachable with integrated auth. Only create WarehouseRent-prefixed synthetic databases; no Docker installation, server configuration or other database changes.

## Evidence log

Design baseline f997cef:4documentation checks PASS. Approval check initially3PASS/1FAIL because checkpoint still said pending owner; corrected to actual approval and rerun before commit. Append each package's exact base/head, tests, reviews and dispositions here. No product completion claimed.
