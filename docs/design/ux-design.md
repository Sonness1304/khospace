# UX specification and wireframes

Approved design specification: owner selected direction A on October28,2026. No application UI existed at approval. Applies Jarvis UI Design's Operate guidance and frontend-design principles; the industrial subject determines the visuals rather than reusing Jarvis Aurora branding.

## 1. User surfaces and navigation

**Public:** facilities → facility detail → dates/whole-unit availability → price quote → sign in/booking.
**Customer:** overview, my rentals, stock, stock requests, charges/deposit, organization members.
**Operator:** occupancy calendar, booking desk, leases/handovers, warehouse operations, finance, reports, settings/audit. Role-based navigation is convenience; backend still enforces access.

Persistent context: facility selector for scoped staff, customer organization selector for members, authenticated user and environment banner. Changing context clears incompatible filters and reloads data; it never silently applies a write to another customer.

Bind each open form to its explicit organization ID and context version. If another tab changes the active organization, preserve the old draft but block submission until its organization is explicitly reconfirmed. Never rewrite draft child IDs into the new organization. Backend validates current membership and all referenced ownership regardless of the selector.

## 2. Two visual approaches

| Direction | Visual treatment | Tradeoff |
| --- | --- | --- |
| A — Operations desk (recommended) | Light neutral canvas, navy structure, teal actions, compact tables, unit/date matrix and focused detail drawer | Clear operational density; requires careful mobile table alternatives |
| B — Customer-first property catalog | Larger facility photography, generous unit cards and a guided rental wizard | Easier discovery; less efficient for many staff tasks and may hide operational conflicts |

Approved: A for the staff application, with a restrained catalog/wizard for public/customer booking. Same vocabulary and tokens throughout. B is retained as a considered alternative only.

## 3. Token proposal

Canvas `#F4F7FA`, surface `#FFFFFF`, ink `#172B4D`, muted `#52657A`, navy navigation `#15324F`, primary teal `#006B68`, border `#CBD5E1`, warning `#8A4B08`, danger `#B42318`. Verify actual component contrast rather than assuming palette compliance.

System sans-serif: Segoe UI/Inter fallback; body 15–16px, table14px, page heading24px. Tabular numerals for quantity/money. Spacing4/8/12/16/24/32; radius6px controls/10px panels; visible focus; no decorative motion. Critical touch targets aim44px. Status uses text+icon+color, never color alone.

## 4. Screen inventory

| Screen | Primary task | Required states |
| --- | --- | --- |
| UI-01 Facility catalog/detail | Understand location, area and storage conditions | loading, empty, unpublished, error |
| UI-02 Availability/quote | Select whole units and dates with visible totals | invalid dates, no match, stale result, tariff unavailable |
| UI-03 Booking review/hold | Understand deadline, rental cost and separate deposit | live countdown, expired, confirmation pending, conflict, unknown response |
| UI-04 Customer rentals/detail | See dates, status, units, handover and actions | scheduled/active/overdue/clearance/settlement |
| UI-05 Operator calendar | Allocate and find occupancy conflicts | booked, held, maintenance, physically occupied, no permission |
| UI-06 Stock list/history | Identify own item/unit balance and movements | empty, loading, filters, reconciliation warning |
| UI-07 Receipt/issue desk | Verify actual goods and post once | validation, insufficient stock, stale version, posting/unknown/posted |
| UI-08 Lease/handover desk | Check readiness and record handover/clearance | blocked by occupancy, goods remaining, future start |
| UI-09 Finance desk | View charges, receipts, allocations and deposits | part-paid, overdue, unapplied, refund-needed, rejected receipt |
| UI-10 Organization/staff admin | Manage memberships and scopes | last-admin protection, disabled member, token expiry P1 |
| UI-11 Reports/audit P1 | Understand metrics and trace actions | explicit period/source, no data, export pending/failure |

## 5. Critical wireframes

### UI-02 Whole-unit search and quote

```text
WarehouseRent                      [Organization A] [Account]
Find storage
[Facility: Thu Duc v] [Start: 01 Nov] [End (excluded): 05 Nov]
[Desired minimum area: 30 m²]                    [Find units]

Available for these dates • checked 14:32 • not reserved yet
[ ] A-01 | 20 m² | dry | 100,000 VND/day | details
[ ] A-02 | 15 m² | dry |  75,000 VND/day | details

Selected: 35 m² • 4 days
Rental estimate: 700,000 VND | refundable deposit: 140,000 VND
Whole units are rented. End date is not a charged day.
                                            [Get exact quote]
```

Never display arbitrary requested area as the billed area. Quoting and holding are distinct buttons/actions. At mobile sizes use stacked unit rows with selection and a sticky summary without covering focused fields.

### UI-03 Hold and deposit

```text
Booking B-104 • Held               Expires at 14:47 (server time)
A-01 + A-02 | 01–05 Nov (4 days) | 35 m²
Rental charges      700,000 VND
Security deposit    140,000 VND (separate refundable balance)
Deposit verification: Awaiting finance

[Cancel hold] [Read booking status]
If your hold expires, payment evidence does not reserve the units.
```

Confirmation is a staff action after verification. Expired state stops the timer, disables confirmation and offers a fresh quote, preserving the receipt/unapplied-funds explanation. Unknown response says “Checking whether your request was saved,” not “Booking failed.”

### UI-05 Operator calendar

```text
[Facility v] [01 Nov ← week →] [Unit code] [State filters]
Unit/area        Mon 1    Tue 2    Wed 3    Thu 4    Fri 5
A-01 20 m²      [Booked A---------] [Booked B------------]
                [Occupied A! ---- physical clearance pending]
A-02 15 m²      [Hold 8m]          [Maintenance-----------]

Conflicts requiring action: A-01 prior occupant not cleared
[Open booking] [View handover] [View conflict history]
```

Physical and scheduled rows are separate. A future confirmed booking must not vanish when a prior occupant overstays. Drag-to-reallocate is deferred; use explicit date/unit form and confirmation preview.

Below 768px default to a unit agenda: unit code, contractual customer/dates, physical occupant/clearance and explicit permitted actions. Keep a list view available at every size. P0 may use a semantic table with ordinary links/buttons; use an ARIA grid only if implementing its complete focus and arrow-key behavior, following the [W3C APG grid pattern](https://www.w3.org/WAI/ARIA/apg/patterns/grid/). A grid role alone does not provide keyboard support.

### UI-07 Stock posting

```text
Issue request SR-042 • Approved • Customer A • Lease L-012
Unit A-01 | Authorized by customer member | Snapshot v7
Item        Base UOM    Requested    Available now    Actual issue
BOX-RED     box             7             10              [7.000]
[Reason / notes]
This records physical goods leaving storage.
[Back] [Review posting]

Review: 7 boxes out; resulting balance 3 • [Confirm issue]
```

After a stock conflict, refresh the balance and require a new review; retain entered actuals but never automatically post them. Posted view links ledger entries and disables editing; correction links appear only in P1 for permitted roles.

P0 mismatch escape: when 100 requested boxes arrive as 85, clerk rejects the unposted request with a discrepancy reason. Customer creates/approves a replacement for the observed 85, with a predecessor reference in audit notes; clerk posts the replacement once. Neither request changes stock until its valid posting. Do not silently mark 100 received, edit a fulfilled document, or implement partial fulfillment through this shortcut. No physical acceptance is represented before the replacement is authorized.

### UI-09 Finance detail

```text
Lease L-012 | Customer A | Nov statement
Issued charges       700,000
Allocated payments   300,000
Outstanding          400,000

Deposit held         140,000 (not included in payments above)
Unapplied receipts         0
[Record receipt] [Allocate verified funds] [View audit]
```

Keep deposit and rental sections visibly distinct. Manual receipt flow asks reference/method/date/evidence and then authorized verification; no “Paid” switch.

## 6. Interaction and content contracts

Buttons use explicit verbs: Get quote, Hold selected units, Confirm booking, Record handover, Review issue, Confirm posting. Consequences appear before irreversible record posting. Validation identifies fields and moves focus to the first error; success links the new resource.

Forms: date labels explicitly state exclusive end, currency/unit displayed beside inputs, localized number display backed by canonical decimal strings. Time countdown derived from server offset and revalidated on commands. No client countdown controls backend expiry.

Booking review must state that uploading payment evidence does not extend a hold; finance must verify and sales confirm before expiry in this demo. Late funds remain visible for refund review. Separate scheduled billing dates from actual handover timestamps. A delayed handover shows a blocked status/reason and contact-manager action; do not promise an automatic credit because P0 uses the scheduled-date billing proposal. These commercially sensitive defaults require explicit owner review.

Lists preserve filters in URLs; server pagination; keyboard reachable actions; headings and table captions; loading skeleton does not show invented amounts. Calendar has accessible list alternative. At200% zoom, navigation/composer/form remain reachable and tables scroll in their own region.

## 7. Design review checklist

Five journeys: search→quote→hold, staff confirmation→handover, receipt→stock history, issue→conflict recovery, charge→receipt allocation. Check each on desktop/mobile, keyboard, empty/error/loading/unknown/stale/denied states. No visual acceptance is claimed from these wireframes. Prototype and real browser QA begin only after the owner selects a direction and approves the design baseline.
