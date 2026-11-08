# PostgreSQL runtime dialect: current acceptance

This slice continues the owner-approved Supabase Free migration, after explicit organization selection. It is NOT a production provider switch. Program, CloudHosting, PostgreSQL migrations/guard parity and public website activation are unchanged until complete acceptance.

## Implemented contracts

- `DatabaseDialect.Rows<T>` translates only the reviewed exact row-lock query formats. Identifiers are hardcoded allowlisted tables/columns in private khospace schema, parameters remain parameter objects, and unknown shapes are refused. SQL Server receives the original FormattableString unchanged.
- All23API +2Infrastructure row-lock sites use this helper. Seven API +one UnitLock DB-clock reads use server UTC; PostgreSQL clock_timestamp is not frozen at transaction start.
- Four API command/identity/job mutexes use transaction-owned advisory locks on PostgreSQL and existing sp_getapplock on SQL Server. Identity timeout/error policy is retained. Resource lengths/timeouts are bounded; lock_timeout is restored on success and rollback restores failed transactions. There are no automatic write retries.
- Stock balance lock acquires a deterministic per-composite-key advisory lock before lookup, including an absent row, rather than replacing SQL key-range locks with an insufficient FOR UPDATE.
- PostgreSQL commands needing Serializable select it before any query, never upgrade after the receipt read. SQLSTATE40001/40P01 (including known EF wrappers) returns409COMMAND_RETRY_REQUIRED with readback/same-key guidance, not transparent retry. Serializable snapshots may abort after mutex waits; this is explicit, not a freshness guarantee.
- Separate PostgreSQL DataProtection repository persists bounded encrypted XML and validates metadata before the framework sees it. Keys use the same private AES256-GCM root-key mechanism. Append-only PostgreSQL key-table DDL is provided for later reviewed migrations. This repository is tested but not registered in production CloudHosting yet.

## Real local test fixture

A new, dedicated PostgreSQL17.11 binary distribution was installed outside Git under C:/AI/KhoSpace-postgres-runtime. It runs ONLY on loopback127.0.0.1:55439, with synthetic trust authentication; it is not publicly exposed and contains no user data. No Windows service/account/global PATH was created. The test fixture creates and drops only generated warehouse_fixture_GUID catalogs on this agent-owned cluster. Never point these tests at Supabase, IELTS or a user database.

Runtime tests use real Npgsql TCP connections and multiple transactions to measure rowlock/UTC, competing mutex timeout plus prior lock_timeout restoration and invalid input refusal, and absent-stock-key wait→commit→read. A minimal StockBalances table intentionally isolates mutex behavior; it does not prove the full warehouse FK/stock/financial guard port. Key tests use the full preview model plus explicit key DDL and prove encrypted-key reuse across independent providers, wrong-root refusal, plaintext/malformed refusal, missing schema failure and update/delete rejection.

For a subsequent local QA session, verify the existing binary path/owneddata/port before starting the cluster:

```powershell
& 'C:\AI\KhoSpace-postgres-runtime\node_modules\@embedded-postgres\windows-x64\native\bin\pg_ctl.exe' -D 'C:\AI\KhoSpace-postgres-runtime\cluster-synthetic' -l 'C:\AI\KhoSpace-postgres-runtime\fixture-server.log' -o '-h 127.0.0.1 -p 55439' -w start
```

Do not start a second server or kill a process using an occupied port. The cluster is synthetic only; keep the original SQL Server fixture suite unchanged. A stopped cluster must be explicitly restarted for the PostgreSQL integration tests; missing prerequisites are failures, not silently skipped acceptance.

## Still required

Full PostgreSQL business regression coverage for the existing SaveChanges immutability protections and parent-row overlap locks, provider migrations, complete bootstrap/readiness/build/recovery wiring, least-privilege roles and live Supabase/Render/Google/proxy acceptance are NOT complete. Source review confirmed that the original migrations contain only the DataProtectionKeys append-only trigger, not historical business triggers to port. Preview-only production connection guards remain. The new helper does not authorize using incomplete DDL remotely or removing the frontend-only notice.

The owner selected Sonness1304's Org and authorized only exact0USD creation. A fresh native inventory and Free plan readback preceded a monthly amount0 quote and creation of the NEW isolated khospace project jhemdameyrcxqxsffugw in ap-southeast-1. Native readback reports ACTIVE_HEALTHY/PostgreSQL17.11; an SQL health query and empty public/khospace table inventory succeeded on this project only. No application DDL or credentials were applied, and no IELTS database queries or changes occurred. Do not create a duplicate or reuse IELTS.

Additional real PostgreSQL acceptance proves a Serializable write conflict rolls back both the business write and receipt, yields409 without automatic retry/callback, permits an explicit same-key retry and replays without another write. HTTP fixture tests prove operator bootstrap idempotence/no customer promotion, real password/cookie login, CSRF denial, foreign-tenant denial and opposing two-unit holds with a single winner. These tests override only the synthetic host's scoped context, not production registration. They do not establish all business workflows or actual cloud/cookie/Google acceptance.
