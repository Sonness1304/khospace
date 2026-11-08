using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using WarehouseRent.Infrastructure.Finance;
using WarehouseRent.Infrastructure.Persistence;

namespace WarehouseRent.Tests;

public sealed class SePayModelTests
{
    [Fact]
    public void Postgres_bank_transfer_translation_has_exact_reviewed_reference_recipient_and_legacy_guards()
    {
        using var db = new AppDbContext(PostgresPreviewConfiguration.ForSyntheticFixture(new(
            "Host=127.0.0.1;Database=warehouse_rent_synthetic;Username=fixture;Password=fixture;SSL Mode=Disable",
            "PostgresSchemaPreviewTests", true)));
        var entity = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(PaymentIntent))!;
        var check = entity.GetCheckConstraints().Single(x => x.ModelName == "CK_PaymentIntent_BankTransfer").Sql;
        Assert.Equal("(\"Provider\"='SePay' AND \"Method\"='SePayQr' AND \"BankTransferReference\" IS NOT NULL AND \"BankTransferReference\" ~ '^SEVQR[A-Z0-9]{10}$' AND \"ExpiresAt\" IS NOT NULL AND \"ExpiresAt\">\"CreatedAt\" AND \"MerchantId\" ~ '^[0-9A-F]{64}$' AND \"CheckoutUrl\" IS NULL) OR (\"Provider\"<>'SePay' AND \"BankTransferReference\" IS NULL AND \"ExpiresAt\" IS NULL)", check);
        var index = entity.GetIndexes().Single(x => x.Properties.Count == 1 && x.Properties[0].Name == "BankTransferReference");
        Assert.True(index.IsUnique);
        Assert.Equal("\"BankTransferReference\" IS NOT NULL", index.GetFilter());
        Assert.Equal("C", entity.FindProperty("BankTransferReference")!.GetCollation());
    }
}
