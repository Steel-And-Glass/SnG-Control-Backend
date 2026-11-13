using SnG.Control.Application.Files;
using SnG.Control.Application.Identity;

namespace SnG.Control.Application.Features.CommercialQuotations;

public sealed class PreviewCommercialQuotationUseCase(ICommercialQuotationStore store, ICommercialQuotationParser parser,
    IFileStorage storage, ICommercialHeadIdentity head)
{
    public async Task<CommercialQuotationPreviewResult> ExecuteAsync(Guid projectId, byte[] bytes, string fileName, CancellationToken token)
        => (await PrepareAsync(projectId, bytes, fileName, false, token)).Result;

    internal async Task<(CommercialQuotationState? State, CommercialQuotationPreviewResult Result)> PrepareAsync(
        Guid projectId, byte[] bytes, string fileName, bool confirming, CancellationToken token)
    {
        var state = await store.LoadAsync(projectId, token);
        if (state is null) return (null, new(CommercialQuotationStatus.ProjectNotFound));
        var status = state.Validate(head.Subject, confirming);
        if (status != CommercialQuotationStatus.Success) return (state, new(status));
        try { await storage.ReadAsync(state.Version!.ConfirmedFileStorageKey!, token); }
        catch (FileNotFoundException) { return (state, new(CommercialQuotationStatus.DocumentUnavailable)); }
        catch (DirectoryNotFoundException) { return (state, new(CommercialQuotationStatus.DocumentUnavailable)); }
        var parsed = await parser.ParseAsync(bytes, fileName, token);
        if (parsed.Status != CommercialQuotationStatus.Success) return (state, new(parsed.Status, FileError: parsed.FileError));
        var document = parsed.Quotation!;
        if (!string.Equals(document.SgCode, state.Version!.DocumentSgCode, StringComparison.Ordinal)) return (state, new(CommercialQuotationStatus.SgCodeMismatch));
        var expected = state.Items.ToDictionary(i => new CommercialItemKey(i.Section, i.SourceRow));
        var actual = document.Items.Select(i => i.Key).ToHashSet();
        var missing = expected.Keys.Except(actual).ToArray(); var extra = actual.Except(expected.Keys).ToArray();
        if (missing.Length > 0 || extra.Length > 0 || actual.Count != document.Items.Count)
            return (state, new(CommercialQuotationStatus.StructuralMismatch, MissingItems: missing, UnexpectedItems: extra));
        var items = document.Items.OrderBy(i => i.Key.SourceRow).Select(i => new CommercialQuotationPreviewItem(i.Key.Section, i.Key.SourceRow,
            expected[i.Key].Id, i.Values, i.UnitPrice, i.TotalPrice, CommercialTechnicalDifferences.Compare(CommercialTechnicalDifferences.Values(expected[i.Key]), i.Values))).ToArray();
        return (state, new(CommercialQuotationStatus.Success, new(projectId, document.ProjectTotal, items, state.Version.VersionNumber)));
    }
}
