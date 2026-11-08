# Backup and recovery (WP16D)

## Local synthetic rehearsal

Run from the application checkout with an already authorized local SQL Server instance and .NET toolchain:

```powershell
./ops/recovery-rehearsal.ps1
```

The xUnit fixture creates a GUID WarehouseRent_Demo_Test_* source and a separate WarehouseRent_Demo_Test_Restore_* target. It never accepts an owner target or uses RESTORE WITH REPLACE. WAREHOUSERENT_TEST_SQL selects the test SQL instance/credentials; InitialCatalog is always replaced by a generated test name. Do not place connection secrets in tracked files/logs. The test uses in-process TestServer, not Kestrel/Vite or a browser launch.

It seeds a confirmed allocation/lease and uncleared physical custody, seven units of customer stock, a verified receipt and separate deposit/charge allocations, a credit releasing a deduction, manual deposit/unapplied refunds and private PDF content. It revokes one membership before backup. Under a consistent reconciliation read, it validates ledger/balances, effective funds and private hashes and captures a deterministic manifest of IDs/journals/allocations/access/notices. SQL COPY_ONLY + CHECKSUM creates a unique backup in the SQL instance's configured backup directory; RESTORE VERIFYONLY precedes restore to new GUID data/log files in the configured directories. The manifest includes UnitOccupancies; changing a restored custody boundary must change its hash, and reverting it restores equality. Restored manifest equality, quantity7, held25/unapplied25 and current authorized/revoked/foreign document downloads must all pass.

Private attachment bytes live in SQL, so the database backup contains file content and metadata in the same snapshot. No separate filesystem attachment manifest can race a SQL backup in this implementation. A future external blob store requires a new consistency protocol.

Both generated databases are removed by their fixtures. The test attempts to delete only its exact GUID backup. If the caller cannot delete a SQL-service-owned backup, it retains that synthetic artifact and records backupRetained=true in .qa/recovery/*.json; no ACL change, privileged delete helper or server reconfiguration is attempted. The test console/result is authoritative; the receipt alone is not a passing test. Earlier failed-run backups can remain. An authorized operator may remove those exact GUID .bak files during normal test-artifact maintenance; never mass-delete a backup directory.

## Before a real operational recovery

Production recovery is outside this local authorization. Agree an incident owner and approved source/isolated target, stop or fence writers/jobs, verify backup provenance/checksums and retention, and preserve the original. Back up and protect the ASP.NET Data Protection key ring separately (cookie/encrypted credential delivery material), secrets and configuration through the approved secret store. This database rehearsal does not prove cross-host key recovery, real email transport, cryptographic key rotation, production storage encryption or disaster recovery timing.

Restore only into an approved isolated target, verify schema/migrations and document bytes, run reconciliation and scoped access checks, inspect outstanding jobs and payment/refund journals, and rotate/revoke sessions and single-use delivery credentials according to the recovery incident policy before allowing users. Re-enable jobs only after confirming deduplication/readback. Promote traffic only after an explicit operational approval and smoke checks. Preserve source/target evidence and observe the rollback plan; do not overwrite or force-delete the original database.

The proposed RPO24h/RTO4h remains a target, not an achieved SLA. A same-instance small synthetic restore demonstrates functional reconciliation only. Measure backup age and an end-to-end timed cross-host rehearsal with key recovery before claiming either target. Owner API/database, deployment and browser acceptance remain unchanged.
