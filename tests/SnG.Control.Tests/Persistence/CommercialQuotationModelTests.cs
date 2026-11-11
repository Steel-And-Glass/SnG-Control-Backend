using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
using SnG.Control.Domain.ValueObjects;
using SnG.Control.Infrastructure.Persistence;

namespace SnG.Control.Tests.Persistence;

public class CommercialQuotationModelTests
{
    [Theory]
    [InlineData(typeof(CommercialQuotation), "ProjectTotal")]
    [InlineData(typeof(CommercialQuotationItem), "UnitPrice")]
    [InlineData(typeof(CommercialQuotationItem), "TotalPrice")]
    public void Economics_UseMonetaryPrecisionAndScale(Type entityType, string name)
    {
        using var db = Context(); var property = db.Model.FindEntityType(entityType)!.FindProperty(name)!;
        Assert.Equal(typeof(decimal), property.ClrType); Assert.Equal("numeric(20,2)", property.GetColumnType());
        Assert.False(property.IsNullable); Assert.Equal(20, property.GetPrecision()); Assert.Equal(2, property.GetScale());
    }
    [Fact]
    public void Schema_HasTwoSpecificTablesRestrictiveRelationsAndUsefulUniqueIndexes()
    {
        using var db = Context(); Assert.Equal(72, db.Model.GetEntityTypes().Count());
        var quotation = db.Model.FindEntityType(typeof(CommercialQuotation))!;
        var item = db.Model.FindEntityType(typeof(CommercialQuotationItem))!;
        Assert.Equal(11, quotation.GetProperties().Count()); Assert.Equal(18, item.GetProperties().Count());
        Assert.Equal(3, quotation.GetForeignKeys().Count()); Assert.Equal(2, item.GetForeignKeys().Count());
        Assert.All(quotation.GetForeignKeys().Concat(item.GetForeignKeys()), fk => Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior));
        Assert.Contains(quotation.GetIndexes(), i => i.IsUnique && i.GetDatabaseName() == "UX_CommercialQuotations_BudgetQuotationVersionId");
        Assert.Contains(item.GetIndexes(), i => i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual(new[] { "CommercialQuotationId", "Section", "SourceRow" }));
        Assert.Contains(item.GetIndexes(), i => i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual(new[] { "CommercialQuotationId", "CommercialCatalogItemId" }));
        Assert.All(quotation.GetProperties(), p => Assert.Equal(p.Name is "ConfirmedByEmail" or "ConfirmedByName", p.IsNullable));
        Assert.All(item.GetProperties(), p => Assert.False(p.IsNullable));
        Assert.Contains("CommercialQuotations", db.CommercialQuotations.ToQueryString()); Assert.Contains("CommercialQuotationItems", db.CommercialQuotationItems.ToQueryString());
        var model = db.GetService<IDesignTimeModel>().Model;
        Assert.Contains(model.FindEntityType(typeof(CommercialQuotation))!.GetCheckConstraints(), c => c.Sql == "\"ProjectTotal\" >= 0");
        Assert.Contains(model.FindEntityType(typeof(CommercialQuotationItem))!.GetCheckConstraints(), c => c.Sql.Contains("\"UnitPrice\" >= 0"));
    }
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void CommercialQuotation_RequiresActorSubject(string? subject) => Assert.ThrowsAny<ArgumentException>(() =>
        new CommercialQuotation(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "test.xlsx", 0, subject!, "test@example.com", "Name"));
    [Theory]
    [InlineData("project")]
    [InlineData("unit")]
    [InlineData("total")]
    public void NegativeEconomics_AreRejectedByDomain(string field)
    {
        if (field == "project") Assert.Throws<ArgumentOutOfRangeException>(() => new CommercialQuotation(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "test.xlsx", -1, "subject", null, null));
        else Assert.Throws<ArgumentOutOfRangeException>(() => new CommercialQuotationItem(Guid.NewGuid(), Guid.NewGuid(), CommercialItemSection.Main, 15,
            new CatalogItemValues("01", null, null, null, null, null, null, 1, 1, 1, 1), field == "unit" ? -1 : 0, field == "total" ? -1 : 0));
    }
    [Fact]
    public void Quotation_PreservesOpaqueActorSnapshotsSafeKeyAndZero()
    {
        var quotation = new CommercialQuotation(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "../folder/test.xlsx", 0, " Opaque-SUB ", " ", null);
        Assert.Equal(" Opaque-SUB ", quotation.ConfirmedByGoogleSubject); Assert.Null(quotation.ConfirmedByEmail); Assert.Null(quotation.ConfirmedByName);
        Assert.Equal("test.xlsx", quotation.OriginalFileName); Assert.Equal(DateTimeKind.Utc, quotation.ConfirmedAt.Kind);
        Assert.Equal($"projects/{quotation.ProjectId:D}/commercial-quotations/{quotation.Id:D}/commercial.xlsx", quotation.FileStorageKey);
        Assert.Equal(0, quotation.ProjectTotal);
    }
    private static SnGControlDbContext Context() => new(new DbContextOptionsBuilder<SnGControlDbContext>().UseNpgsql().Options);
}
