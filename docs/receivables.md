# Receivables reconciliation and accounting CSV

P2.5 adds a current operational receivables view. Use it to explain outstanding issued statements and export their reconciliation components. It is not a certified general ledger, tax invoice or historical as-of report.

## Access and workflow

1. Sign in as OperatorAdmin or staff with a current Finance grant for the selected facility.
2. Select the customer organization and facility. In Reports & in-app notices, open Receivables reconciliation.
3. Choose Due date from and Due date until (exclusive). The range filters statement due dates, not service months, issue timestamps or receipt dates. At most366days are allowed. Move the start earlier to include older debt.
4. Read receivables. Whole-range totals and five aging buckets appear above the50-row statement page. IDs identify the original statement, lease and rental account. Previous/Next reads a fresh current snapshot.
5. Export receivables CSV exports every matched row in that range, including settled statements, with an audit. It is a fresh authorized snapshot and may differ from the displayed observation after another financial transaction.

Customer admins, members, FacilityManagers, Sales and Clerks cannot use this staff finance report unless separately granted Finance/operator access. Other existing customer statement/stock exports retain their own permissions. Revoked grants are checked on each request. Changing workspace, dates or grants discards old results and pending responses.

## Financial meaning

For each statement:

- Effective charge = original issued total + supplements/services/rent supplements - credits.
- Effective paid = statement fund allocations + deposit deductions against that statement - linked allocation releases.
- Outstanding = effective charge - effective paid.

A credit may release prior receipt allocation or deposit deduction. Both release sources reduce effective paid; they do not create a cash refund. Refunds/unapplied money and held deposits remain separate financial records. Merely recording a receipt or creating a hosted payment intent never pays a statement. A verified receipt counts only after allocation to that statement.

All amounts are exact whole-VND decimal strings in JSON and CSV. The UI does not convert them to JavaScript numbers, avoiding precision loss beyond9007199254740991. CSV includes original, additions, credits, allocations, deductions, releases, effective charge/paid and outstanding. When importing into a spreadsheet, choose Text for monetary columns to preserve every digit; automatic numeric conversion by spreadsheet software may round long integers. Negative charge, paid or outstanding fails409 ACCOUNTING_RECONCILIATION_REQUIRED rather than hiding inconsistency by clamping0.

The aging date is SQL UTC converted to the facility timezone. Due today and future due dates are Current. Past-due unpaid amounts fall into1-30,31-60,61-90 or Over90days. Zero outstanding is Settled. Buckets sum to the range outstanding; totals are not just the visible page. No interest, penalty, grace period or revenue recognition is inferred.

## API, limits and failure handling

GET /api/v1/reports/receivables?organizationId=...&facilityId=...&startDate=YYYY-MM-DD&endDate=YYYY-MM-DD&page=0

GET /api/v1/exports/receivables?organizationId=...&facilityId=...&startDate=YYYY-MM-DD&endDate=YYYY-MM-DD

The JSON schema is P2-RECEIVABLES-V1, page0-based,50rows/page, sorted due date then statement ID, maximum5000matched statements. Count includes settled rows. Matching5001rows refuses422 rather than returning incomplete totals. Narrow the interval and export separate ranges. Missing Finance/scope returns404. Invalid bound/page returns422. Current permissions, all financial components and export audit are read/committed in one Serializable SQL transaction. Each page/export is a separate observation, not a frozen report session.

CSV is UTF8 with BOM, attachment filename receivables.csv, no-store/nosniff. Text is quoted/formula-escaped using the existing SafeCsv rules. ReceivablesExported audit records organization/facility/range/count before the response; audit failure returns an error without a file or successful committed export audit. Retrying an export creates a new audit; it does not alter financial records.

There is no schema migration or new accounting state. Existing SQL recovery already covers the source financial journals. This source delivery does not update the owner's running API/database or the preserved P1 package. Browser/accessibility, provider certification/live payments, hardware and production acceptance remain deferred.
