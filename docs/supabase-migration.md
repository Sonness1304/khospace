# Supabase PostgreSQL migration — current boundaries

The owner selected a zero-hosting-spend approach using the same database service as IELTS, not Azure. Read-only source and authenticated native-provider inventory confirm IELTS uses Supabase PostgreSQL/Auth with Vercel. WarehouseRent keeps its existing .NET/React business logic and must use a NEW dedicated project/schema; never copy IELTS credentials/data or apply WarehouseRent DDL to the IELTS project.

## This slice

- Pinned Npgsql EF10 provider added side-by-side; SQL Server remains the production/local default. PostgreSQL is OFFLINE PREVIEW ONLY, not an enabled runtime.
- Bound Supabase project + protected IELTS project reference; direct or session pooler5432 only, TLS VerifyFull, bounded clientpool/timeouts, private khospace schema. No arbitrary host/trust-cert/public schema injection.
- Preview model translates known checks/partial indexes/collations and preserves byte[8] Version/ETag snapshots with proposed sequence/trigger tokens.
- Offline connection interceptor and empty migrations assembly prevent accidentally running SQL Server history against PostgreSQL. GenerateScript is a reviewable preview, NOT a production migration and must not be applied remotely.
- Supabase free-project planning guard requires explicit organization selection, a current Free plan and an exact0USD monthlyquote. Existing project names are refused pending readback; no paid fallback, upgrade, delete or reuse of IELTS.

## Required remaining work before cloud activation

All runtime raw SQL/mutex/rowlocks/DBclock/SQLexception paths, absent-stock-balance serialization, receipt isolation behavior, integrity/history/overlap triggers, encrypted PostgreSQL keyrepository, explicitbootstrap/migrations/readiness/buildidentity and recovery must be ported/tested. Generated schema alone does not reproduce triggers from SQL migrations. No PostgreSQL production flag is exposed yet.

Keep SQLServer tests/history intact. Actual PostgreSQL engine fixtures need concurrency and command parity; PGlite schema/token tests do not establish Npgsqlnetwork, SupabaseTLS/pooler, auth or end-to-end acceptance. Do not disable guards or switch connection strings to make a deployment appear complete.

## Provider scope

The native Supabase connection works even though Supabase CLI has no access token. Use native tools, not credential extraction. At the initial inventory the organization Sonness1304's Org is Free and contains one active Ielts-Learning project. Creation still requires the owner's explicit organization selection per provider-tool contract, then a fresh quoted0USDcost and readback of the new project. No Azure subscription is required for this route. Never use a Pro organization, paid branch/add-on or modify current project billing.

Render Free .NET API remains a candidate after full PostgreSQL parity. If Render is still unconnected, authenticate it privately; never paste tokens into chat. Vercel current frontend-only site stays unchanged until exact full-stack readiness/session/Google/proxy/key-restart acceptance.
