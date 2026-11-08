using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using WarehouseRent.Api.Features.Recovery;
using WarehouseRent.Api.Features.Settlements;
using WarehouseRent.Infrastructure.Documents;
using WarehouseRent.Infrastructure.Finance;
using WarehouseRent.Infrastructure.Identity;
using WarehouseRent.Infrastructure.Persistence;
using WarehouseRent.Infrastructure.Stock;
using WarehouseRent.Infrastructure.Gates;
using WarehouseRent.Infrastructure.Facilities;
using WarehouseRent.Infrastructure.Rentals;

namespace WarehouseRent.Tests;
[Trait("Category", "Sql")]
public sealed class SqlRecoveryTests : IClassFixture<IdentitySqlFixture>
{
    private readonly IdentitySqlFixture fixture;
    private readonly Xunit.Abstractions.ITestOutputHelper output;
    public SqlRecoveryTests(IdentitySqlFixture fixture, Xunit.Abstractions.ITestOutputHelper output) { this.fixture = fixture; this.output = output; }
    [Fact]
    public async Task Copy_only_backup_restores_allocation_stock_funds_private_content_and_revoked_access()
    {
        var grant = await fixture.SeedOccupancyOwner(SqlFacilitiesTests.UnitA, new DateOnly(2039, 1, 1), new DateOnly(2039, 2, 1));
        Guid leaseId, accountId, docId; var barcodeId = Guid.NewGuid(); var paymentId = Guid.NewGuid(); var laneId = Guid.NewGuid(); var gateId = Guid.NewGuid(); var tariffId = Guid.NewGuid(); var tariffQuoteId = Guid.NewGuid(); string expected;
        await using (var db = fixture.Db())
        {
            var leaseUnit = await db.LeaseUnits.SingleAsync(x => x.Id == grant); var lease = await db.Leases.SingleAsync(x => x.Id == leaseUnit.LeaseId);
            leaseId = lease.Id; accountId = lease.RentalAccountId;
            db.GateLanes.Add(new GateLane { Id = laneId, FacilityId = lease.FacilityId, Code = "RESTORE-LANE", CreatedBy = "synthetic-test-fixture", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
            db.GateVisits.Add(new GateVisit { Id = gateId, LaneId = laneId, LeaseId = lease.Id, OrganizationId = lease.OrganizationId, FacilityId = lease.FacilityId, Direction = "Inbound", Vehicle = "RESTORE-123", StartUtc = new DateTimeOffset(2039,1,1,2,0,0,TimeSpan.Zero), EndUtc = new DateTimeOffset(2039,1,1,2,30,0,TimeSpan.Zero), State = "CheckedIn", ArrivedAt = new DateTimeOffset(2039,1,1,2,0,0,TimeSpan.Zero), ArrivedBy = "clerk@example.test", CreatedBy = "synthetic-test-fixture", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
            db.PaymentIntents.Add(new PaymentIntent { Id = paymentId, RentalAccountId = accountId, OrganizationId = lease.OrganizationId, FacilityId = lease.FacilityId, Provider = "Vnpay", Mode = "Sandbox", Method = "VnpayQr", MerchantId = "SYNTHETIC", Amount = 10000, CreatedBy = "admin.a@example.test", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow, State = "Ready" });
            db.UnitOccupancies.Add(new WarehouseRent.Infrastructure.Facilities.UnitOccupancy { Id = Guid.NewGuid(), UnitId = leaseUnit.UnitId, LeaseUnitId = grant,
                OrganizationId = lease.OrganizationId, ScheduledEnd = lease.EndDate, HandedOverAt = DateTimeOffset.UtcNow, ClearedAt = null });
            var item = new CustomerItem { Id = Guid.NewGuid(), OrganizationId = lease.OrganizationId, Sku = "RECOVERY", Description = "Synthetic restored stock", BaseUom = "box" };
            var request = new StockRequest { Id = Guid.NewGuid(), OrganizationId = lease.OrganizationId, FacilityId = lease.FacilityId, LeaseId = lease.Id, LeaseUnitId = grant,
                Type = "Receipt", State = "Fulfilled", CreatedBy = "clerk@example.test", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
            var line = new StockRequestLine { Id = Guid.NewGuid(), OrganizationId = lease.OrganizationId, StockRequestId = request.Id, ItemId = item.Id, Quantity = 7, FulfilledQuantity = 7 };
            var posting = new StockPosting { Id = Guid.NewGuid(), StockRequestId = request.Id, OrganizationId = lease.OrganizationId, FacilityId = lease.FacilityId, LeaseUnitId = grant,
                Type = "Receipt", PostedBy = "clerk@example.test", PostedAt = DateTimeOffset.UtcNow };
            db.Items.Add(item); db.ItemBarcodes.Add(new ItemBarcode { Id = barcodeId, OrganizationId = item.OrganizationId, ItemId = item.Id, Kind = "Internal", CanonicalValue = "RECOVERY-BOX", Active = true, CreatedBy = "admin.a@example.test", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
            db.StockRequests.Add(request); db.StockRequestLines.Add(line); db.StockPostings.Add(posting);
            db.StockLedger.Add(new StockLedgerEntry { Id = Guid.NewGuid(), StockPostingId = posting.Id, RequestLineId = line.Id, OrganizationId = lease.OrganizationId,
                ItemId = item.Id, LeaseUnitId = grant, Delta = 7, PostedAt = posting.PostedAt, PostedBy = posting.PostedBy });
            db.StockBalances.Add(new StockBalance { OrganizationId = lease.OrganizationId, ItemId = item.Id, LeaseUnitId = grant, Quantity = 7 });
            var receipt = new PaymentReceipt { Id = Guid.NewGuid(), RentalAccountId = accountId, OrganizationId = lease.OrganizationId, FacilityId = lease.FacilityId,
                Method = "Cash", Reference = "SYNTHETIC-RECOVERY", EvidenceReference = "Fixture only", Amount = 100, State = "Verified", ReceivedAt = DateTimeOffset.UtcNow, VerifiedAt = DateTimeOffset.UtcNow, VerifiedBy = "finance@example.test", UpdatedAt = DateTimeOffset.UtcNow };
            var allocation = new FundAllocation { Id = Guid.NewGuid(), RentalAccountId = accountId, PaymentReceiptId = receipt.Id, OrganizationId = lease.OrganizationId, TargetType = "Deposit", Amount = 30, ActorId = "finance@example.test", At = DateTimeOffset.UtcNow };
            db.PaymentReceipts.Add(receipt); db.FundAllocations.Add(allocation); db.DepositTransactions.Add(new DepositTransaction { Id = Guid.NewGuid(), RentalAccountId = accountId, FundAllocationId = allocation.Id, Amount = 30, At = allocation.At });
            var statement = new ChargeStatement { Id = Guid.NewGuid(), LeaseId = lease.Id, RentalAccountId = accountId, OrganizationId = lease.OrganizationId, FacilityId = lease.FacilityId, PeriodStart = new DateOnly(2039, 1, 1), DueDate = new DateOnly(2039, 1, 5), IssuedAt = DateTimeOffset.UtcNow, Total = 100 };
            var chargeAllocation = new FundAllocation { Id = Guid.NewGuid(), RentalAccountId = accountId, PaymentReceiptId = receipt.Id, OrganizationId = lease.OrganizationId, TargetType = "Charge", ChargeStatementId = statement.Id, Amount = 40, ActorId = "finance@example.test", At = allocation.At };
            var deduction = new SettlementEntry { Id = Guid.NewGuid(), RentalAccountId = accountId, OrganizationId = lease.OrganizationId, ChargeStatementId = statement.Id, Kind = "Deduction", Amount = 10, Reference = "RESTORE-DEDUCT", EvidenceReference = "Synthetic", Reason = "Fixture", ActorId = "finance@example.test", At = allocation.At, PerformedAt = allocation.At };
            var credit = new StatementAdjustment { Id = Guid.NewGuid(), RentalAccountId = accountId, ChargeStatementId = statement.Id, Kind = "Credit", Amount = 60, EvidenceReference = "Synthetic", Reason = "Fixture", ActorId = "finance@example.test", At = allocation.At };
            var tariff = new RateVersion {Id=tariffId,UnitId=leaseUnit.UnitId,SalesValidFrom=new DateOnly(2038,1,1),BillingBasis="CalendarMonth",DailyRate=0,MonthlyRate=3100};
            db.RateVersions.Add(tariff);
            var tariffQuote = new RentalQuote {Id=tariffQuoteId,OrganizationId=lease.OrganizationId,FacilityId=lease.FacilityId,CreatedBy="synthetic-test-fixture",CreatedAt=DateTimeOffset.UtcNow,ExpiresAt=DateTimeOffset.UtcNow.AddHours(24),StartDate=new DateOnly(2039,1,1),EndDate=new DateOnly(2039,1,2),RentalSubtotal=100,RequiredDeposit=20,PolicyVersion="P2-CALENDAR-M2-V1"};db.Quotes.Add(tariffQuote);
            var tariffLine=new QuoteLine {Id=Guid.NewGuid(),QuoteId=tariffQuoteId,UnitId=leaseUnit.UnitId,UnitCode="RESTORE-PRICE",UnitVersion=[],RateVersionId=tariffId,StartDate=tariffQuote.StartDate,EndDate=tariffQuote.EndDate,Days=1,AreaM2=1,DailyRate=0,BillingBasis="CalendarMonth",MonthlyRate=3100,CalendarDays=31,Amount=100};db.QuoteLines.Add(tariffLine);
            var tariffVersion=new LeaseVersion {Id=Guid.NewGuid(),LeaseId=leaseId,VersionNo=1,PolicyVersion=tariffQuote.PolicyVersion};db.LeaseVersions.Add(tariffVersion);
            var leasePrice=new LeaseVersionLine {Id=Guid.NewGuid(),LeaseVersionId=tariffVersion.Id,LeaseUnitId=grant,SourceQuoteLineId=tariffLine.Id,StartDate=tariffLine.StartDate,EndDate=tariffLine.EndDate,Days=1,AreaM2=1,DailyRate=0,BillingBasis="CalendarMonth",MonthlyRate=3100,CalendarDays=31,Amount=100};db.LeaseVersionLines.Add(leasePrice);
            db.ChargeStatementLines.Add(new ChargeStatementLine {Id=Guid.NewGuid(),ChargeStatementId=statement.Id,SourceLeaseVersionLineId=leasePrice.Id,StartDate=tariffLine.StartDate,EndDate=tariffLine.EndDate,Days=1,AreaM2=1,DailyRate=0,BillingBasis="CalendarMonth",MonthlyRate=3100,CalendarDays=31,Amount=100});
            db.ChargeStatements.Add(statement); db.FundAllocations.Add(chargeAllocation); db.SettlementEntries.Add(deduction); db.StatementAdjustments.Add(credit);
            db.AllocationReleases.Add(new AllocationRelease { Id = Guid.NewGuid(), RentalAccountId = accountId, StatementAdjustmentId = credit.Id, SettlementEntryId = deduction.Id, Amount = 10, At = allocation.At });
            foreach (var kind in new[] { "RefundDeposit", "RefundUnapplied" }) db.SettlementEntries.Add(new SettlementEntry { Id = Guid.NewGuid(), RentalAccountId = accountId, OrganizationId = lease.OrganizationId, Kind = kind, Amount = 5,
                PaymentReceiptId = kind == "RefundUnapplied" ? receipt.Id : null, Reference = "RESTORE-" + kind, EvidenceReference = "Synthetic", Reason = "Fixture", ActorId = "finance@example.test", At = allocation.At, PerformedAt = allocation.At });
            var content = System.Text.Encoding.ASCII.GetBytes("%PDF-1.4 obj<</Type/Catalog>>endobj %%EOF");
            var doc = new PrivateDocument { Id = Guid.NewGuid(), LeaseId = lease.Id, OrganizationId = lease.OrganizationId, FacilityId = lease.FacilityId,
                StorageKey = Guid.NewGuid().ToString("N") + ".pdf", FileName = "recovery.pdf", ContentType = "application/pdf", Sha256 = Convert.ToHexString(SHA256.HashData(content)), Content = content, UploadedBy = "admin.a@example.test", UploadedAt = DateTimeOffset.UtcNow };
            docId = doc.Id; db.PrivateDocuments.Add(doc);
            (await db.Memberships.SingleAsync(x => x.UserId == "member.a@example.test" && x.OrganizationId == lease.OrganizationId)).Active = false;
            // This bulk, direct fixture preparation is not an API command. Keep its bounded setup budget
            // separate from runtime queries and restore/reconciliation acceptance (which remain unchanged).
            var runtimeTimeout=db.Database.GetCommandTimeout();
            db.Database.SetCommandTimeout(30);
            try { await db.SaveChangesAsync(); }
            finally { db.Database.SetCommandTimeout(runtimeTimeout); }
            Assert.Equal(3,db.Database.GetCommandTimeout());
            expected = await RecoverySnapshot.Capture(db, CancellationToken.None);
        }
        var original = new SqlConnectionStringBuilder(fixture.Connection); var targetName = "WarehouseRent_Demo_Test_Restore_" + Guid.NewGuid().ToString("N");
        var masterConnection = new SqlConnectionStringBuilder(fixture.Connection) { InitialCatalog = "master", Pooling = false }.ConnectionString;
        string? backupFile = null; var retained = false; var restored = new AppDbContext(SqlConfiguration.Options(new SqlConnectionStringBuilder(fixture.Connection) { InitialCatalog = targetName }.ConnectionString));
        try
        {
            await using var master = new SqlConnection(masterConnection); await master.OpenAsync();
            async Task<string> Scalar(string sql)
            { await using var command = new SqlCommand(sql, master); return (string)(await command.ExecuteScalarAsync())!; }
            var backupPath = await Scalar("SELECT CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(260))");
            var dataPath = await Scalar("SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(260))");
            var logPath = await Scalar("SELECT CAST(SERVERPROPERTY('InstanceDefaultLogPath') AS nvarchar(260))");
            backupFile = Path.Combine(backupPath, targetName + ".bak");
            string logicalData, logicalLog;
            await using (var names = new SqlCommand("SELECT name,type FROM sys.master_files WHERE database_id=DB_ID(@database)", master))
            { names.Parameters.AddWithValue("@database", original.InitialCatalog); await using var reader = await names.ExecuteReaderAsync(); var files = new Dictionary<int, string>(); while (await reader.ReadAsync()) files.Add(reader.GetByte(1), reader.GetString(0)); logicalData = files[0]; logicalLog = files[1]; }
            // Names originate only from this fixture's GUID databases. No REPLACE/forced-drop is used.
            await using (var backup = new SqlCommand($"BACKUP DATABASE [{original.InitialCatalog}] TO DISK=@backup WITH COPY_ONLY, CHECKSUM;", master) { CommandTimeout = 120 })
            { backup.Parameters.AddWithValue("@backup", backupFile); await backup.ExecuteNonQueryAsync(); }
            await using (var verify = new SqlCommand("RESTORE VERIFYONLY FROM DISK=@backup WITH CHECKSUM;", master) { CommandTimeout = 120 })
            { verify.Parameters.AddWithValue("@backup", backupFile); await verify.ExecuteNonQueryAsync(); }
            await using (var restore = new SqlCommand($"RESTORE DATABASE [{targetName}] FROM DISK=@backup WITH MOVE @logicalData TO @data, MOVE @logicalLog TO @log, CHECKSUM;", master) { CommandTimeout = 120 })
            {
                restore.Parameters.AddWithValue("@backup", backupFile); restore.Parameters.AddWithValue("@logicalData", logicalData); restore.Parameters.AddWithValue("@logicalLog", logicalLog);
                restore.Parameters.AddWithValue("@data", Path.Combine(dataPath, targetName + ".mdf")); restore.Parameters.AddWithValue("@log", Path.Combine(logPath, targetName + "_log.ldf")); await restore.ExecuteNonQueryAsync();
            }
            Assert.Equal(expected, await RecoverySnapshot.Capture(restored, CancellationToken.None));
            var restoredLane = await restored.GateLanes.SingleAsync(x => x.Id == laneId);
            Assert.Equal("CalendarMonth",(await restored.RateVersions.AsNoTracking().SingleAsync(x=>x.Id==tariffId)).BillingBasis);
            Assert.Equal(31,(await restored.QuoteLines.AsNoTracking().SingleAsync(x=>x.QuoteId==tariffQuoteId)).CalendarDays);
            // Owned restored DB fault injection verifies frozen price evidence is part of the manifest.
            await restored.Database.ExecuteSqlInterpolatedAsync($"UPDATE RateVersions SET MonthlyRate=3200 WHERE Id={tariffId}");
            Assert.NotEqual(expected,await RecoverySnapshot.Capture(restored,CancellationToken.None));
            await restored.Database.ExecuteSqlInterpolatedAsync($"UPDATE RateVersions SET MonthlyRate=3100 WHERE Id={tariffId}");
            Assert.Equal(expected,await RecoverySnapshot.Capture(restored,CancellationToken.None));
            var restoredGate = await restored.GateVisits.SingleAsync(x => x.Id == gateId); Assert.Equal("CheckedIn", restoredGate.State); Assert.Equal(leaseId, restoredGate.LeaseId);
            restoredLane.Active = false; await restored.SaveChangesAsync(); Assert.NotEqual(expected, await RecoverySnapshot.Capture(restored, CancellationToken.None));
            restoredLane.Active = true; await restored.SaveChangesAsync(); Assert.Equal(expected, await RecoverySnapshot.Capture(restored, CancellationToken.None));
            var restoredPayment = await restored.PaymentIntents.SingleAsync(x => x.Id == paymentId); Assert.Equal(accountId, restoredPayment.RentalAccountId); Assert.Equal("Sandbox", restoredPayment.Mode); Assert.Null(restoredPayment.PaymentReceiptId);
            restoredPayment.State = "Unknown"; await restored.SaveChangesAsync(); Assert.NotEqual(expected, await RecoverySnapshot.Capture(restored, CancellationToken.None));
            restoredPayment.State = "Ready"; await restored.SaveChangesAsync(); Assert.Equal(expected, await RecoverySnapshot.Capture(restored, CancellationToken.None));
            var restoredBarcode = await restored.ItemBarcodes.SingleAsync(x => x.Id == barcodeId);
            Assert.Equal("RECOVERY-BOX", restoredBarcode.CanonicalValue); Assert.True(restoredBarcode.Active);
            restoredBarcode.Active = false; await restored.SaveChangesAsync();
            Assert.NotEqual(expected, await RecoverySnapshot.Capture(restored, CancellationToken.None));
            restoredBarcode.Active = true; await restored.SaveChangesAsync();
            Assert.Equal(expected, await RecoverySnapshot.Capture(restored, CancellationToken.None));
            Assert.Equal(7, await restored.StockBalances.SumAsync(x => x.Quantity));
            Assert.Equal(25, await SettlementBalances.HeldAsync(restored, accountId, CancellationToken.None));
            Assert.Equal(25, await SettlementBalances.AccountUnappliedAsync(restored, accountId, CancellationToken.None));
            Assert.True(await restored.Leases.AnyAsync(x => x.Id == leaseId));
            var custody = await restored.UnitOccupancies.SingleAsync(x => x.LeaseUnitId == grant);
            Assert.Equal(DemoIdentitySeed.OrganizationA, custody.OrganizationId); Assert.Equal(SqlFacilitiesTests.UnitA, custody.UnitId); Assert.Null(custody.ClearedAt);
            var originalEnd = custody.ScheduledEnd; custody.ScheduledEnd = originalEnd.AddDays(1); await restored.SaveChangesAsync();
            Assert.NotEqual(expected, await RecoverySnapshot.Capture(restored, CancellationToken.None));
            custody.ScheduledEnd = originalEnd; await restored.SaveChangesAsync();
            Assert.Equal(expected, await RecoverySnapshot.Capture(restored, CancellationToken.None));
            restoredGate.State = "Completed"; restoredGate.DepartedAt = restoredGate.ArrivedAt!.Value.AddMinutes(40); restoredGate.DepartedBy = "clerk@example.test"; await restored.SaveChangesAsync();
            Assert.NotEqual(expected, await RecoverySnapshot.Capture(restored, CancellationToken.None));
            using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(b => { b.UseEnvironment("Testing"); b.UseSetting("ConnectionStrings:WarehouseRent", restored.Database.GetConnectionString()); });
            async Task<BrowserSession> SignIn(string name)
            {
                var browser = new BrowserSession(host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = false })); await browser.RefreshCsrf();
                Assert.Equal(HttpStatusCode.OK, (await browser.Send(HttpMethod.Post, "/api/v1/auth/login", new { userName = name, password = DemoIdentitySeed.Password })).StatusCode); await browser.RefreshCsrf(); return browser;
            }
            using var customer = await SignIn("admin.a@example.test"); using var revoked = await SignIn("member.a@example.test"); using var foreign = await SignIn("admin.b@example.test");
            var barcodeLookup = $"/api/v1/leases/{leaseId}/barcode-lookup?kind=Internal&value=RECOVERY-BOX";
            Assert.Equal(HttpStatusCode.OK, (await customer.Send(HttpMethod.Get, barcodeLookup)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await revoked.Send(HttpMethod.Get, barcodeLookup)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await foreign.Send(HttpMethod.Get, barcodeLookup)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await customer.Send(HttpMethod.Get, $"/api/v1/documents/{docId}/download")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await revoked.Send(HttpMethod.Get, $"/api/v1/documents/{docId}/download")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await foreign.Send(HttpMethod.Get, $"/api/v1/documents/{docId}/download")).StatusCode);
            var export = await customer.Send(HttpMethod.Get, $"/api/v1/exports/statements?organizationId={DemoIdentitySeed.OrganizationA}&facilityId={DemoIdentitySeed.FacilityA}&startDate=2039-01-01&endDate=2039-02-01");
            Assert.Equal(HttpStatusCode.OK, export.StatusCode); Assert.Contains(",100,40,40,0", await export.Content.ReadAsStringAsync());
            var stockExport = await customer.Send(HttpMethod.Get, $"/api/v1/exports/stock?organizationId={DemoIdentitySeed.OrganizationA}&facilityId={DemoIdentitySeed.FacilityA}&startDate={DateTimeOffset.UtcNow.AddDays(-1):yyyy-MM-dd}&endDate={DateTimeOffset.UtcNow.AddDays(2):yyyy-MM-dd}");
            Assert.Equal(HttpStatusCode.OK, stockExport.StatusCode); Assert.Contains("Synthetic restored stock", await stockExport.Content.ReadAsStringAsync());
        }
        finally
        {
            await restored.Database.EnsureDeletedAsync(); await restored.DisposeAsync();
            if (backupFile is not null && Path.GetFileName(backupFile) == targetName + ".bak")
                {
                    try { File.Delete(backupFile); }
                    catch (UnauthorizedAccessException) { retained = true; output.WriteLine("RETAINED synthetic GUID backup (SQL service-owned directory): " + backupFile); }
                } // No ACL changes or privileged delete workaround; never touch another backup.
            var evidenceDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../.qa/recovery"));
            Directory.CreateDirectory(evidenceDirectory);
            await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, targetName + ".json"), System.Text.Json.JsonSerializer.Serialize(new {
                sourceDatabase = original.InitialCatalog, restoredDatabase = targetName, manifestHash = expected, backupFile, backupRetained = retained,
                checkedAt = DateTimeOffset.UtcNow, evidence = "See exact test exit/result; receipt alone is not acceptance" }));
        }
    }
}
