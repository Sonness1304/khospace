# Owner review guide

Status: **approved October28,2026; direction A; P0 implementation authorized**. Owner explicitly approved the proposed defaults below and requested Atlas coding with independent reviews. This page preserves the decision checklist; delivered status remains in the checkpoint.

WarehouseRent is a proposed .NET/React portfolio project for one company renting fixed warehouse units across multiple facilities. Customers book whole units by date and manage their own goods. This package covers P0 and P1; the first 15 working days target P0 only.

## Decisions already provided

One operator, multiple facilities; fixed whole units measured in m²; multiple units per rental; C#/.NET and React; complete documentation before implementation. Atlas/Jarvis may assist, including Claude, Gemini3.8 and DeepSeek High when the actual route exists. Repository documents are English.

## Proposed defaults to approve or change

| Topic | Proposal and practical consequence |
| --- | --- |
| Rental dates | Local Asia/Ho_Chi_Minh dates, end excluded, 1–365 days; one facility per booking |
| Pricing | VND per m² per day; whole selected units billed; monthly segment lines rounded individually; no tax assumption |
| Reservation | Quote24h without space reservation; hold15min; payment evidence alone does not extend it. Manual finance verification and sales confirmation must finish before expiry. A real operator may need a different hold policy |
| Deposit | 20% refundable security separate from rental charges. Late/cancelled funds remain in finance review; no automatic money transfer |
| Cancellation | Before handover, cancel booking and Scheduled lease together and release unoccupied space. Refund records and full financial closure are P1 |
| Delayed handover | P0 bills scheduled dates after activation even if physical handover is late. No automatic credits. This policy needs explicit business acceptance or redesign before code |
| Custody | Customer requests, staff posts observed goods; a mismatch requires rejecting the unposted request and authorizing a replacement. Partial fulfillment P1 |
| Overstay | No incoming handover until physical clearance; manager-approved outbound remains possible. No automatic goods disposal or conditional hold P0 |
| Visual direction | Recommend light operations desk for staff plus a small public/customer booking wizard; alternative catalog-first direction remains documented |
| Schedule | P0: 15 working days at4–6h/day after approval, 60–90h. P1 adds60–100h. Availability has not been confirmed; this is an estimate, not a calendar promise |

## Read and inspect

Start with the [SRS](SRS.md), [business rules](business-rules.md), [UX/wireframes](../design/ux-design.md), [roadmap](../delivery/roadmap.md) and [review dispositions](../reviews/review-disposition.md). The [README](../../README.md) indexes the complete package.

Approval should identify the accepted policies, visual direction and any requested changes. Once accepted, record the decision in the consumer checkpoint, verify Workbench selects this project root and the actual toolchain/SQL provider, then begin WP01. Design approval does not authorize public deployment, live payments or sending secrets to a model.

All booking, stock, finance, browser, security and performance tests are still NOT RUN because the application does not exist. Documentation checks validate document consistency only.
