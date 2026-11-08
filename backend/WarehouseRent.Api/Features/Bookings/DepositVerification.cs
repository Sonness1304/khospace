using WarehouseRent.Infrastructure.Rentals;
namespace WarehouseRent.Api.Features.Bookings;

public sealed record DepositEvidence(bool Verified, decimal Amount, bool HasFunds = false);
// WP05 seam: only tests replace this. WP09 registers the real transactional funds reader.
public interface IDepositVerification
{
    Task<DepositEvidence> ReadAsync(RentalAccount account, CancellationToken ct);
}
public sealed class UnavailableDepositVerification : IDepositVerification
{
    public Task<DepositEvidence> ReadAsync(RentalAccount account, CancellationToken ct) => Task.FromResult(new DepositEvidence(false, 0));
}
// WP06 replaces this with Scheduled lease/occupancy validation and atomic lease cancellation.
public interface ICancelScheduledLease
{
    Task CancelAsync(Guid bookingId, CancellationToken ct);
}
public sealed class NoLeaseModule : ICancelScheduledLease
{
    public Task CancelAsync(Guid bookingId, CancellationToken ct) => Task.CompletedTask;
}
