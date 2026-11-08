using Microsoft.EntityFrameworkCore;
using WarehouseRent.Infrastructure.Facilities;
using WarehouseRent.Infrastructure.Finance;
using WarehouseRent.Infrastructure.Rentals;

namespace WarehouseRent.Infrastructure.Persistence;

public sealed partial class AppDbContext
{
    private const string TariffRateCheck = "([BillingBasis]='Daily' AND [DailyRate]>0 AND [MonthlyRate] IS NULL) OR ([BillingBasis]='CalendarMonth' AND [DailyRate]=0 AND [MonthlyRate] IS NOT NULL AND [MonthlyRate]>0)";
    private const string TariffLineCheck = "([BillingBasis]='Daily' AND [DailyRate]>0 AND [MonthlyRate] IS NULL AND [CalendarDays] IS NULL) OR ([BillingBasis]='CalendarMonth' AND [DailyRate]=0 AND [MonthlyRate] IS NOT NULL AND [MonthlyRate]>0 AND [CalendarDays] IS NOT NULL AND [CalendarDays] BETWEEN 28 AND 31)";
    private static void ConfigureTariffs(ModelBuilder model)
    {
        model.Entity<RateVersion>(e => { e.Property(x=>x.BillingBasis).HasMaxLength(16).HasDefaultValue("Daily"); e.Property(x=>x.MonthlyRate).HasPrecision(18,4); e.ToTable(t=>t.HasCheckConstraint("CK_Rate_Basis",TariffRateCheck)); });
        model.Entity<QuoteLine>(e => { e.Property(x=>x.BillingBasis).HasMaxLength(16).HasDefaultValue("Daily"); e.Property(x=>x.MonthlyRate).HasPrecision(18,4); e.ToTable(t=>t.HasCheckConstraint("CK_QuoteLine_Basis",TariffLineCheck)); });
        model.Entity<LeaseVersionLine>(e => { e.Property(x=>x.BillingBasis).HasMaxLength(16).HasDefaultValue("Daily"); e.Property(x=>x.MonthlyRate).HasPrecision(18,4); e.ToTable(t=>t.HasCheckConstraint("CK_LeaseVersionLine_Basis",TariffLineCheck)); });
        model.Entity<ChargeStatementLine>(e => { e.Property(x=>x.BillingBasis).HasMaxLength(16).HasDefaultValue("Daily"); e.Property(x=>x.MonthlyRate).HasPrecision(18,4); e.ToTable(t=>t.HasCheckConstraint("CK_ChargeLine_Basis",TariffLineCheck)); });
    }
    private void ProtectTariffs()
    {
        foreach(var entry in ChangeTracker.Entries())
        {
            if(entry.Entity is RentalQuote or QuoteLine or LeaseVersion or LeaseVersionLine && entry.State is EntityState.Modified or EntityState.Deleted)
                throw new InvalidOperationException("Frozen price snapshots are immutable.");
            if(entry.Entity is not RateVersion || entry.State is not (EntityState.Modified or EntityState.Deleted)) continue;
            if(entry.State == EntityState.Deleted || entry.Properties.Any(p=>p.IsModified && p.Metadata.Name != nameof(RateVersion.SalesValidTo)))
                throw new InvalidOperationException("Rate economic identity is immutable. Register a new rate version.");
            var rate=(RateVersion)entry.Entity; var prior=entry.OriginalValues.GetValue<DateOnly?>(nameof(RateVersion.SalesValidTo));
            if(rate.SalesValidTo == null || rate.SalesValidTo <= rate.SalesValidFrom || prior.HasValue && rate.SalesValidTo > prior)
                throw new InvalidOperationException("Rate validity can only be shortened after its start.");
        }
    }
}
