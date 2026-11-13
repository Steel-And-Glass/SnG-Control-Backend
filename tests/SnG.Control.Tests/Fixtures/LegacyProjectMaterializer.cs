using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
using SnG.Control.Infrastructure.Persistence;

namespace SnG.Control.Tests.Fixtures;

public static class LegacyProjectMaterializer
{
    public static Project Create(SnGControlDbContext context)
    {
        var entity = context.Model.FindEntityType(typeof(Project))!;
        var values = new object?[entity.GetProperties().Count()];
        var row = new Dictionary<string, object?>
        {
            [nameof(Project.Id)] = Guid.NewGuid(),
            [nameof(Project.ClientId)] = Guid.NewGuid(),
            [nameof(Project.Name)] = "Historical project",
            [nameof(Project.City)] = "City",
            [nameof(Project.ProductLine)] = ProductLine.Classic,
            [nameof(Project.CreatedAt)] = DateTime.UtcNow,
            [nameof(Project.UpdatedAt)] = DateTime.UtcNow
        };
        foreach (var property in entity.GetProperties()) values[property.GetIndex()] = row.GetValueOrDefault(property.Name);
        return (Project)context.GetService<IStructuralTypeMaterializerSource>().GetMaterializer(entity)(
            new MaterializationContext(new ValueBuffer(values), context));
    }
}
