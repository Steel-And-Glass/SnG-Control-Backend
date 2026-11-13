using SnG.Control.Application.Scheduling;
using SnG.Control.Application.Features.Budgets.PreviewQuotation;
using SnG.Control.Application.Features.ProjectTasks;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
using SnG.Control.Domain.ValueObjects;

namespace SnG.Control.Application.Features.Budgets.ConfirmQuotation;

public sealed class ConfirmBudgetQuotationUseCase(PreviewBudgetQuotationUseCase preview, IBudgetQuotationConfirmationStore store, TaskTiming timing)
{
    public async Task<ConfirmBudgetQuotationResult> ExecuteAsync(Guid projectId, byte[] original, string fileName,
        ConfirmBudgetQuotationRequest confirmation, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(original, writable: false);
        var parsed = await preview.ExecuteAsync(projectId, stream, fileName, cancellationToken);
        if (parsed is not PreviewBudgetQuotationResult.Success success)
            return parsed switch
            {
                PreviewBudgetQuotationResult.ProjectNotFound => new(QuotationConfirmationStatus.ProjectNotFound),
                PreviewBudgetQuotationResult.InvalidWorkflowState => new(QuotationConfirmationStatus.InvalidWorkflowState),
                PreviewBudgetQuotationResult.SgCodeMismatch => new(QuotationConfirmationStatus.SgCodeMismatch),
                PreviewBudgetQuotationResult.InvalidWorkbook invalid => new(QuotationConfirmationStatus.InvalidWorkbook, FileError: invalid.Error),
                _ => throw new InvalidOperationException("Unexpected preview result.")
            };
        var extracted = success.Response;
        if (confirmation.Items is null || confirmation.Items.Count != extracted.Items.Count)
            return new(QuotationConfirmationStatus.InvalidConfirmation);
        var confirmed = new Dictionary<(CommercialItemSection, int), CatalogItemValues>();
        try
        {
            var rows = new HashSet<int>();
            foreach (var item in confirmation.Items)
            {
                if (item is null || !Enum.IsDefined(item.Section) || item.SourceRow <= 0 || !rows.Add(item.SourceRow))
                    return new(QuotationConfirmationStatus.InvalidConfirmation);
                confirmed.Add((item.Section, item.SourceRow), new CatalogItemValues(item.ItemNumber, item.Location, item.System,
                    item.Glass, item.AluminumFinish, item.LockType, item.Observations, item.Width, item.Height, item.SquareMeters, item.Quantity));
            }
        }
        catch (ArgumentException) { return new(QuotationConfirmationStatus.InvalidConfirmation); }
        if (extracted.Items.Any(item => !confirmed.ContainsKey((item.Section, item.SourceRow))))
            return new(QuotationConfirmationStatus.InvalidConfirmation);

        var document = extracted.Document;
        var version = new BudgetQuotationVersion(projectId, extracted.QuotationRound, document.FileName, document.ClientName, document.ProjectName,
            document.Location, document.ClientLine, document.ProductionLine, document.PreparedBy, document.SgCode);
        var catalog = new CommercialCatalog(version.Id);
        var items = new List<CommercialCatalogItem>();
        var snapshots = new List<CommercialCatalogItemExtraction>();
        var files = new List<QuotationFile> { new(version.OriginalFileStorageKey, original) };
        var responses = new List<ConfirmedCatalogItemResponse>();
        foreach (var source in extracted.Items.OrderBy(item => item.SourceRow))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var values = confirmed[(source.Section, source.SourceRow)];
            var item = new CommercialCatalogItem(catalog.Id, source.Section, source.SourceRow, values);
            var originalValues = new CatalogItemValues(source.ItemNumber, source.Location, source.System, source.Glass,
                source.AluminumFinish, source.LockType, source.Observations, source.Width, source.Height, source.SquareMeters, source.Quantity);
            var status = source.Image is not null ? CatalogItemImageStatus.Stored
                : extracted.Warnings.Any(warning => warning.SourceRow == source.SourceRow && warning.Code == BudgetQuotationWarningCode.AmbiguousImage)
                    ? CatalogItemImageStatus.Ambiguous : CatalogItemImageStatus.Missing;
            string? key = null;
            if (source.Image is not null)
            {
                var extension = source.Image.ContentType.ToLowerInvariant() switch
                { "image/png" => ".png", "image/jpeg" => ".jpg", "image/gif" => ".gif", "image/bmp" => ".bmp", "image/tiff" => ".tiff", _ => ".bin" };
                key = $"projects/{projectId:D}/budget-quotations/{version.Id:D}/images/{item.Id:D}{extension}";
                files.Add(new QuotationFile(key, Convert.FromBase64String(source.Image.Base64)));
            }
            items.Add(item);
            snapshots.Add(new CommercialCatalogItemExtraction(item.Id, originalValues, status, key, source.Image?.ContentType));
            responses.Add(new ConfirmedCatalogItemResponse(item.Id, item.Section, item.SourceRow, originalValues, values,
                new CatalogItemImageResponse(status, source.Image?.ContentType)));
        }
        var saved = await store.ConfirmAsync(new BudgetQuotationConfirmation(version, catalog, items, snapshots, files), cancellationToken);
        if (saved.Status != QuotationConfirmationStatus.Created) return new(saved.Status);
        return new(QuotationConfirmationStatus.Created, new ConfirmedBudgetQuotationResponse(version.Id, version.VersionNumber, catalog.Id, projectId,
            version.ConfirmedAt, document, items.Count, items.Sum(item => (long)item.Quantity), responses, ProjectTaskResponse.FromEntity(saved.Task!, timing), extracted.Economics));
    }
}
