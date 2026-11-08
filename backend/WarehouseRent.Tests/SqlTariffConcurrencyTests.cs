using System.Data.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WarehouseRent.Infrastructure.Persistence;

namespace WarehouseRent.Tests;

public sealed partial class SqlTariffTests
{
    [Fact]
    public async Task Rate_change_and_online_payment_on_same_unit_complete_without_lock_cycle()
    {
        var unit = await Unit();
        var grant = await fixture.SeedOccupancyOwner(unit.Id, new DateOnly(2049, 1, 1), new DateOnly(2049, 2, 1));
        Guid account;
        await using (var db = fixture.Db())
            account = await (from g in db.LeaseUnits join l in db.Leases on g.LeaseId equals l.Id
                where g.Id == grant select l.RentalAccountId).SingleAsync();

        var paymentLocks = new PaymentUnitBarrier();
        var rateLocks = new RateUnitBarrier(paymentLocks);
        using var paymentHost = fixture.Host.WithWebHostBuilder(b =>
        {
            b.UseSetting("Payments:PublicOrigin", "https://warehouse.example.test");
            b.UseSetting("Payments:Vnpay:Mode", "Sandbox");
            b.UseSetting("Payments:Vnpay:MerchantId", "TESTMERCHANT");
            b.UseSetting("Payments:Vnpay:Secret", SqlOnlinePaymentTests.Secret);
            b.ConfigureServices(s =>
            {
                s.RemoveAll<AppDbContext>();
                s.AddScoped(_ => new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(fixture.Options())
                    .AddInterceptors(paymentLocks).Options));
            });
        });
        using var rateHost = fixture.Host.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.RemoveAll<AppDbContext>();
            s.AddScoped(_ => new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(fixture.Options())
                .AddInterceptors(rateLocks).Options));
        }));
        using var customer = await LoginHost(paymentHost, "admin.a@example.test");
        using var manager = await LoginHost(rateHost, "operator@example.test");
        paymentLocks.Armed = true;
        var paymentKey = Guid.NewGuid();
        var rateKey = Guid.NewGuid();
        var payment = customer.Send(HttpMethod.Post, "/api/v1/payment-intents", new
        {
            rentalAccountId = account, method = "VnpayCard", amount = "10000", reason = "Synthetic lock concurrency"
        }, key: paymentKey);
        Task<HttpResponseMessage>? rate = null;
        try
        {
            await paymentLocks.UnitHeld.Task.WaitAsync(TimeSpan.FromSeconds(15));
            rate = manager.Send(HttpMethod.Post, $"/api/v1/units/{unit.Id}/rates", Monthly(), unit.Version, rateKey);
            var responses = await Task.WhenAll(payment, rate).WaitAsync(TimeSpan.FromSeconds(45));
            Assert.True(responses.All(x => x.IsSuccessStatusCode),
                $"Responses: {string.Join(", ", responses.Select(x => x.StatusCode))}; SQL: {string.Join("; ", paymentLocks.Failures)}");
            foreach (var response in responses) await Data(response);
            await using var db = fixture.Db();
            Assert.Equal(1, await db.RateVersions.CountAsync(x => x.UnitId == unit.Id));
            Assert.Equal(1, await db.PaymentIntents.CountAsync(x => x.RentalAccountId == account));
            Assert.Equal(2, await db.OperationalCommands.CountAsync(x => x.Id == paymentKey || x.Id == rateKey));
        }
        finally
        {
            paymentLocks.RateRequested.TrySetResult();
            await Task.WhenAll(rate is null ? [payment] : new[] { payment, rate }).WaitAsync(TimeSpan.FromSeconds(45));
        }
    }

    private static bool IsUnitLock(DbCommand command) => command.CommandText.Contains("RentableUnits", StringComparison.Ordinal)
        && (command.CommandText.Contains("UPDLOCK,HOLDLOCK", StringComparison.Ordinal) || command.CommandText.Contains("FOR UPDATE",StringComparison.Ordinal));

    private sealed class PaymentUnitBarrier : DbCommandInterceptor
    {
        public System.Collections.Concurrent.ConcurrentQueue<string> Failures { get; } = new();
        public bool Armed { get; set; }
        public TaskCompletionSource UnitHeld { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource RateRequested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            Failures.Enqueue(eventData.Exception.Message);
            return Task.CompletedTask;
        }
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (Armed && IsUnitLock(command))
            {
                Armed = false;
                // Hold the payment's real SQL unit lock until the competing rate request reaches that same lock.
                UnitHeld.TrySetResult();
                await RateRequested.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            }
            return result;
        }
    }

    private sealed class RateUnitBarrier(PaymentUnitBarrier payment) : DbCommandInterceptor
    {
        public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            payment.Failures.Enqueue(eventData.Exception.Message);
            return Task.CompletedTask;
        }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (IsUnitLock(command)) payment.RateRequested.TrySetResult();
            return ValueTask.FromResult(result);
        }
    }
}
