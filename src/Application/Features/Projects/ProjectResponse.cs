using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;

namespace SnG.Control.Application.Features.Projects;

public sealed record ProjectResponse(Guid Id, Guid ClientId, string Name, string City,
    ProductLine ProductLine, string? SgCode, DateTime CreatedAt, DateTime UpdatedAt, string? CreatedByName, bool IsCreatedByMe)
{
    internal static ProjectResponse FromEntity(Project entity, string? currentSubject) => new(
        entity.Id, entity.ClientId, entity.Name, entity.City,
        entity.ProductLine, entity.SgCode, entity.CreatedAt, entity.UpdatedAt,
        entity.CreatedByName, entity.CreatedByGoogleSubject is not null && entity.CreatedByGoogleSubject == currentSubject);
}

