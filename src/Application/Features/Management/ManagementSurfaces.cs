using System.Globalization;
using SnG.Control.Domain.Enums;
namespace SnG.Control.Application.Features.Management;

public sealed record SurfaceFilter(Guid? ProjectId = null, Guid? ClientId = null, string? City = null,
    ProductLine? ProductLine = null, OperationalArea? Area = null, ProjectTaskStatus? Status = null,
    int Skip = 0, bool Ascending = false);
public sealed record SurfaceCoverage(string? Value, string Unit, long Eligible, long Excluded,
    string Source, string Definition, IReadOnlyDictionary<string, long> Exclusions);
public sealed record SurfaceProject(Guid ProjectId, Guid ClientId, string ProjectName, string ClientName,
    string? SgCode, string City, string ProductLine, string? QuotedSquareMeters,
    long QuotedItems, string? ReferenceSquareMeters, long MeasuredStructures, long PendingStructures);
public sealed record ManagementSurfacesResponse(DateTime GeneratedAt, int TotalProjects, int Skip, int PageSize,
    bool HasMore, SurfaceCoverage Quoted, SurfaceCoverage Measured, SurfaceCoverage Filed,
    IReadOnlyList<SurfaceProject> Projects);
public interface IManagementSurfaceStore
{
    Task<ManagementSurfacesResponse> ReadAsync(SurfaceFilter filter, CancellationToken token);
}
public static class SurfaceDefinition
{
    public const string Reference = "Superficie rectangular de referencia: ancho real (m) × alto real (m), una vez por estructura física activa. No es superficie neta de vidrio, material ni fabricación; no es comparable automáticamente con la cotización.";
    public static decimal? ReferenceArea(decimal? width, decimal? height, bool retired) => !retired && width>0 && height>0 ? checked(width.Value*height.Value) : null;
    // Column L is already the document-authoritative TOTAL per item, including quantity.
    public const string Quoted = "Suma de SquareMeters documentales de los ítems de la cotización comercial vigente. Incluye la cantidad; no se multiplica de nuevo.";
    public static string Exact(decimal value) => value.ToString("0.############################", CultureInfo.InvariantCulture);
    public static SurfaceCoverage Quotation(decimal? sum, long eligible, long excluded) => new(
        eligible == 0 ? null : Exact(sum ?? 0), "m2", eligible, excluded, "CommercialQuotationItem.SquareMeters", Quoted,
        new Dictionary<string,long> { ["InvalidDocumentArea"] = excluded });
    public static SurfaceCoverage Unavailable(long count, string source, string reason) => new(null,"m2",0,count,source,
        "No disponible: falta definir qué versión de evidencia congelada representa la superficie radicada. No se sustituyen snapshots históricos por medidas actuales ni se resta esta superficie de la cotizada.",
        new Dictionary<string,long> { [reason] = count });
}
