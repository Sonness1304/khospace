# Engineering Portfolio

## The problem

A warehouse operator leases fixed units to multiple businesses. Customers need to
reserve space and track their goods; staff need contracts, stock movements, and
receivables. Reservations, leases, balances, receipts, and deposit allocations are
related but not interchangeable records.

## Stack and responsibilities

The application uses C# and ASP.NET Core for server workflows, EF Core for
relational persistence, and React/TypeScript for the customer/staff interface.
SQL Server supports the synthetic demo; a PostgreSQL path supports hosting.

Work includes requirements, workflow design, APIs, UI, relational tests, explicit
migrations, deployment exports, and scoped cloud configuration. This is a learning
project, not evidence of professional employment or certified operational experience.

## Decisions worth discussing

### Fixed units instead of arbitrary floor polygons

Fixed-area units make overlap prevention, pricing, and availability understandable
and testable. The trade-off is less flexibility than full spatial warehouse planning.

### Server-side state transitions

Screens request commands; the API checks state, membership, permissions, and
invariants. UI visibility is not the security boundary.

### Immutable financial and contract history

Renewals issue new versions. Corrections and reversals preserve history rather
than silently rewriting business activity.

### A provider event is not automatically a business outcome

Evidence must be authenticated and matched before accepting a receipt. Retries
need durable deduplication. Deposit/rent allocation is separate; conflicting or
expired evidence requires reconciliation.

### Database tests matter

Concurrency and transaction behavior are tested against relational fixtures.
In-memory substitutes cannot establish the same locking and rollback guarantees.

### Source, configuration, and acceptance are separate

A build does not prove the intended revision is deployed. Liveness, readiness,
build identity, provider settings, and customer acceptance have separate evidence.

## Suggested interview walkthrough

1. Explain the operator/customer model and reservation overlap invariant.
2. Trace a React command through authorization and persistence.
3. Show a regression test for a rejected or concurrent operation.
4. Explain how stock reversal differs from deleting a movement.
5. Walk through payment retries and why a redirect cannot mark Paid.
6. Describe an observed failure, correction, and remaining acceptance limit.

Use revision-specific results. Do not present synthetic tests as customer usage
or claim real-money acceptance before observing it.

## Next improvements

- Complete owner-performed real-provider payment acceptance.
- Expand mobile, keyboard, and accessibility coverage.
- Improve deployment portability without weakening ownership safeguards.
- Rehearse compatible data recovery and document operational limits.
- Refine customer-facing error recovery and observability.
