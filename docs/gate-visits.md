# Gate visits - P2.3

This source package adds gate-lane reservations and observed vehicle arrivals/departures to the existing operations desk. It does not post inventory, approve a stock request, move money or replace a lease.

## Roles and workflow

1. Sign in and choose the facility workspace. A FacilityManager or OperatorAdmin creates a permanent lane code such as NORTH-1. Lane codes use1-40 uppercase ASCII letters, digits, hyphen or underscore and remain unique within a facility.
2. Select the customer workspace and an Active lease. Read the lane list, select an active lane, enter the lease ID, vehicle registration, direction and UTC start/end timestamps. Use ISO values ending in Z, for example2026-11-01T09:00:00Z. The server also accepts an explicit zero offset; it rejects timestamps without a timezone.
3. Supply a reason and choose Schedule gate visit. Keep the returned visit ID. The entire5-240minute window must lie inside the lease's facility-local dates, start in the future and be at most90days away. Adjacent windows are allowed; concurrent overlapping windows on one lane cannot both commit.
4. Clerks, FacilityManagers and OperatorAdmins read the visit and record arrival during its scheduled window. An Active lane/lease and no other vehicle currently checked in are required. A customer's gate visit does not grant permission to post stock.
5. Record departure when the vehicle physically leaves. Departure remains possible after lease termination. An overstaying vehicle blocks the next arrival even after its planned end. Lists always include CheckedIn vehicles outside the selected range; review all pages.
6. Cancel an unused Scheduled visit explicitly, including an expired visit or one whose lease ended. Cancellation frees its planned interval. A checked-in vehicle must depart rather than be cancelled. A lane with any Scheduled or CheckedIn visits cannot be disabled; overdue Scheduled visits need operator follow-up first.

Customer members read, schedule and cancel only their own organization's visits. Assigned clerks/managers can handle visits at their facility. Finance/Sales/unassigned staff do not receive gate permissions automatically. For staff scheduling through the UI, select the customer's organization workspace first. The API derives the customer and facility from the lease.

## Safe retries and listing

Every write uses a UUID command key and reason. Lane status and visit transitions also require the current If-Match version. After a timeout, keep the original command key/body/version and use Read original gate command or Retry original gate command. Editing the draft does not change an unresolved command. Unknown retries remain recoverable across authorization errors. Another actor/workspace cannot retry the original command; return to the original workspace. The open page retains this command in memory; a browser reload is not a durable command-history store. Save the displayed command ID when an outcome is unknown and reconcile it through authorized command readback before creating another reservation.

Lists require a UTC range of at most31days and return50rows per page. Half-open ranges permit adjacency. Observed occupancy is separate from the reservation interval. Gate records retain original lane, lease, facility, organization, vehicle, direction, timestamps and observed-event evidence. Rebooking means cancelling and creating a new visit; history cannot be reassigned or deleted.

## API

All routes below require an authenticated session; writes require CSRF.

| Route | Behavior |
| --- | --- |
| GET /api/v1/gate-lanes?facilityId=...&organizationId=...&page=1 | Scoped lane list; customer organization requires an Active lease at that facility |
| GET /api/v1/gate-lanes/{id}?organizationId=... | Scoped lane detail and version |
| POST /api/v1/gate-lanes | Manager creates facilityId/code/reason |
| POST /api/v1/gate-lanes/{id}/status | Manager changes active/reason with If-Match |
| POST /api/v1/gate-visits | laneId/leaseId/direction/vehicle/startUtc/endUtc/reason |
| GET /api/v1/gate-visits?facilityId=...&organizationId=...&startUtc=...&endUtc=...&page=1 | Scoped bounded reservation list plus physical CheckedIn vehicles |
| GET /api/v1/gate-visits/{id} | Scoped detail/version |
| POST /api/v1/gate-visits/{id}/cancel | Scheduled cancellation with reason and If-Match |
| POST /api/v1/gate-visits/{id}/arrive | Staff observes arrival with reason and If-Match |
| POST /api/v1/gate-visits/{id}/depart | Staff observes departure with reason and If-Match |

## Storage and verification

Migration20261029095457_GateVisitScheduling adds GateLanes/GateVisits, composite facility/lease ownership relationships, rowversions, unique facility/code and a filtered unique currently-CheckedIn lane index. All writers serialize on the lane parent within the command transaction and recheck current active identity/grants. Schedule/arrival also lock the lease against commercial lifecycle changes. Lane status, visit state, audit and command receipt commit together. Populated downgrade refuses data loss.

Recovery reconciliation includes original reservation identity and actual physical state/events. Tests use in-process TestServer and owned GUID SQL databases; the existing owner API/database and P1 archive are unchanged. Browser/accessibility, real gate hardware, scanners, timezone changes during operations and operational throughput are not accepted by source tests. There are no automatic no-show jobs, calendar/business-hours rules, driver personal data, license-plate recognition or barrier-control integration in this slice.

Use the repository's setup/release instructions when the owner chooses to run a fresh package manually. The old P1 ZIP does not include this migration or UI. This implementation did not launch an API/browser or migrate the owner's database.
