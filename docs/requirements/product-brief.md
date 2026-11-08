# Product brief and decision register

Version 0.2, October28,2026. Status: **owner approved proposed demo policies and UI A; P0 implementation authorized**. Historical proposed-default labels in the register describe their original source; owner approval now applies to D05–D14 for the demo, without establishing real-world legal or market validity.

## Problem and users

A small warehouse operator needs one view of rentable space, upcoming rentals, customer-owned goods and amounts owed. Spreadsheets can confuse a quote with a booking, a booked unit with physically cleared space, and a deposit with rental income. The application should make these distinctions visible and enforce them on writes.

Primary users: prospects, customer admins/members, sales staff, facility managers, warehouse clerks, finance staff and operator administrators. The portfolio audience also needs a reproducible demonstration, credible tests and a clear explanation of design tradeoffs.

## Outcomes

- A customer can find eligible units and request a rental with a transparent price.
- Concurrent requests cannot allocate the same unit/date twice.
- Staff can receive and release the correct customer's goods with traceable quantities.
- Charge generation and payment recording can be repeated safely without duplication.
- Staff and customers see only data permitted by their organization/facility scope.

## Approaches considered

| Approach | Benefit | Cost / decision |
| --- | --- | --- |
| Configure a large ERP/WMS | Broad inventory features already modeled | Custom rental/tenant rules and a different stack; not selected as implementation base |
| Build booking only | Fast demo, smaller surface | Does not satisfy customer inventory management |
| Build a modular ASP.NET/React application | Fits chosen stack; explicit rental/inventory boundaries | More design/testing work; coordinator recommendation |

The user selected fixed m² units over shared pallet capacity. The modular application architecture is a **coordinator proposal**, not yet owner-approved.

## Scope by release

**Portfolio slice P0:** identities/roles, facilities/units, date search, price snapshot, atomic hold/confirmation, basic lease activation, customer items, receipt/issue ledger, charge statement and manual payment record, responsive customer/operator screens, CI and demo data.

**MVP P1:** renewal/amendment, controlled early termination, unit transfers, stock count adjustments, deposit settlement, aging reports, private attachments, operational notifications, backup/restore rehearsal and richer administration.

**Later P2:** live payment gateway, e-signature/e-invoice, accounting exports, barcode workflows, gate scheduling, configurable monthly tariffs, pallet/shared capacity, maps, IoT, multilingual UI and additional operator companies.

The full requirements cover P0/P1 and identify P2 explicitly. A complete SRS does not imply every release is a 15-day build.

## Decision register

| ID | Decision / proposed default | Authority |
| --- | --- | --- |
| D01 | One operator with multiple facilities | User confirmed |
| D02 | Fixed whole units measured in m²; one booking may contain multiple units | User confirmed |
| D03 | Complete documentation/design before product code | User confirmed |
| D04 | C#/.NET and React | User selected in preceding portfolio discussion |
| D05 | Working name WarehouseRent; separate root `C:/AI/WarehouseRent` | Coordinator proposal; rename is easy |
| D06 | VND, one base unit per item, daily rates, date-only rentals in Asia/Ho_Chi_Minh | Proposed demo policy |
| D07 | 1–365 day rentals; 15-minute hold; quote valid 24 hours | Proposed demo policy |
| D08 | Deposit 20% of rental subtotal; manual finance confirmation before booking confirmation | Proposed demo policy |
| D09 | Dry storage only; no dangerous/perishable goods; no automatic loading-plan validation | Proposed scope |
| D10 | Customer requests; authorized warehouse staff posts actual stock movements | Proposed custody workflow |
| D11 | English repository documents and baseline UI; Vietnamese conversation with owner | User requested English repository presentation |
| D12 | No automatic lease extension or customer goods disposal on overdue dates | Proposed safe operating rule |
| D13 | Statements after activation, based on agreed scheduled dates even if actual handover is delayed; no automatic P0 credit | Proposed demo policy; requires explicit owner review |
| D14 | No evidence-upload hold extension; late funds retained for finance review within a proposed two-business-day target | Proposed demo policy; manual verification may make a 15-minute hold impractical for real operations |

## Open business questions before production

Actual price/deposit/cancellation policies, tax treatment, custody liability, insurance, prohibited goods, permitted customer access, weights/fire/load limits, data retention and operating hours require validation with a real operator. The demo policies are concrete enough for implementation review but are not statements of Vietnamese law or industry practice.

## Completion definition for this phase

Research assessed; requirements identified and prioritized; rules, state machines, data/API/UX designs, tests and schedule cross-reference the same IDs; Atlas/reviewer findings resolved or clearly deferred; owner can inspect the package. Product implementation begins only after design acceptance.
