using SnG.Control.Application.Files;

namespace SnG.Control.Application.Features.Measurements;

public static class MeasurementReportName
{
    public static string ForProject(string projectName) => QuotationDownloadName.Sanitize(
        $"Radicacion de medidas-{projectName.Replace('/', '_').Replace('\\', '_')}.pdf");
}
