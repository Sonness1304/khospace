# Calendar-month tariffs

P2.4 adds an explicit CalendarMonth tariff alongside existing Daily rates. All amounts are VND; rates are per square metre. This guide describes local source behavior, not live deployment acceptance.

## Register a tariff

1. Sign in as OperatorAdmin or an assigned FacilityManager. Select the facility in the catalog and open Manage selected facility.
2. Edit one unit. Select Rate basis: Daily or Calendar month, enter its positive nominal rate (up to4 decimal places), a reason and the sales-valid start date using Start date.
3. Select Set dated rate. The command uses the selected unit version. Refresh the catalog after success; the returned rowversion is required for another change.
4. Read selected rate history to see immutable rate IDs, basis, nominal prices and half-open sales-valid windows. Pages contain at most50rows.

The UI creates an open-ended rate. A future/same-day overlap is refused; the API can supply an explicit salesValidTo to close a new window before an existing future version. An older preceding version may have its end shortened; economics/identity cannot be overwritten or deleted. Retired units reject new rates.

## Exact price policy

A quote selects the rate valid on its creation day in Asia/Ho_Chi_Minh, not the future service day. Thus a future sales rate does not apply to a quote created today. Dates use [start,end), with the end excluded, and each unit is split at calendar-month boundaries.

- Daily: round(area * dailyRate * segmentDays).
- CalendarMonth: round(area * monthlyRate * segmentDays / actualDaysInThatCalendarMonth).

Rounding is whole VND, midpoint AwayFromZero, once per unit/month segment. Sum those amounts; the security deposit is20% of that sum rounded once. Leap February uses29days; nonleap February28; April30; January31. A complete calendar month costs area * monthlyRate before rounding. This is calendar-month proration, not a30-day bundle or an anniversary month. Mixed daily/monthly units are supported.

Example:10m2 at3100VND/m2/calendar month, January31 to February2,2028: January1/31 =1000VND; February1/29 =1069VND; subtotal2069VND; deposit414VND. Daily prices are never approximated by dividing a monthly price into a four-decimal daily rate.

Quote, contract and statement displays show basis, nominal monthly rate and actual day denominator. A zero dailyRate on a CalendarMonth line means not applicable. Never treat it as a free daily rate. Tiny prorated prices can round0; existing explicit zero-deposit Finance verification still applies.

## Snapshot and renewal behavior

Quote policy P2-CALENDAR-M2-V1 is used when any monthly line exists. P0-DAILY-M2-V1 remains valid for daily-only quotes. Quotes expire after24hours and reserve no capacity. Changing current rate identity requires a new quote before hold/renewal. Once a lease/statement is issued, its source ID, basis, nominal rate, denominator and rounded amount stay frozen. Renewal copies the new quote into a new lease version, keeps existing security deposit and adds no deposit; additional rent remains separately billable under existing Finance rules.

## Lost command responses

An uncertain rate write retains original command UUID, body, If-Match version, actor and facility across draft edits and rejected retries. Read command result first, then Retry original command only when offered. Do not create another tariff while that outcome is unknown. Save the command ID outside the page before reloading; pending UI state is not durable across browser reloads. Authorized staff can reconcile GET /api/v1/commands/{id}?scopeId={facilityId}.

## API and migration

POST /api/v1/units/{id}/rates extends the existing body with billingBasis (Daily default) and monthlyRate. CalendarMonth requires dailyRate:"0" and positive monthlyRate; Daily requires positive dailyRate and no monthlyRate. Preserve Idempotency-Key, If-Match and CSRF. GET on the same path returns current-role manager-only history with page0-based,50rows/page. Public eligible-unit responses disclose billingBasis/monthlyRate; dailyRate remains for compatibility.

Migration20261029104500_CalendarMonthTariffs adds Daily defaults and optional monthly metadata. A populated monthly downgrade refuses with SQL51018. Database setup remains an explicit owner operation; this delivery has not applied migrations to the owner's old API database. Recovery manifests now include rate economics and frozen quote/contract/statement pricing. A manifest taken before this change must not be compared as though its projection were identical.

No API/browser launch, real charge, owner DB migration, hardware activation, deployment or new archive was performed. Browser/accessibility and live merchant/scanner/gate acceptance remain deferred. The P1 ZIP does not include P2.

Concurrent rate/payment writes take unit locks before the shared identity-directory lock. The active user and manager grant are rechecked under that directory lock before any rate mutation. This preserves authorization revocation while preventing a unit/directory lock cycle with payment creation and checkout.
