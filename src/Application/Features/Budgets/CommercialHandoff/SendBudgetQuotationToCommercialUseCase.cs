using SnG.Control.Application.Files;
using SnG.Control.Application.Identity;

namespace SnG.Control.Application.Features.Budgets.CommercialHandoff;

public sealed class SendBudgetQuotationToCommercialUseCase(IBudgetQuotationCommercialHandoffStore store,
    IFileStorage storage, IBudgetQuotationWorkbookGenerator generator, ICommercialHeadIdentity commercialHead, ICurrentUser user)
{
    public async Task<CommercialHandoffResult> ExecuteAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var state = await store.LoadAsync(projectId, cancellationToken);
        if (state is null) return new(CommercialHandoffStatus.ProjectNotFound);
        var status = state.Validate();
        if (status != CommercialHandoffStatus.Sent) return new(status);
        if (!string.Equals(BudgetQuotationWorkflow.ReadyTask(state.Project, state.Tasks)?.AssignedToGoogleSubject, user.Subject, StringComparison.Ordinal))
            return new(CommercialHandoffStatus.InvalidState);
        byte[] original;
        try { original = await storage.ReadAsync(state.Version!.OriginalFileStorageKey, cancellationToken); }
        catch (FileNotFoundException) { return new(CommercialHandoffStatus.DocumentUnavailable); }
        catch (DirectoryNotFoundException) { return new(CommercialHandoffStatus.DocumentUnavailable); }
        var generated = await generator.GenerateAsync(original, state.Version!.DocumentSgCode, state.WorkbookItems(), cancellationToken);
        if (generated is null) return new(CommercialHandoffStatus.DocumentMismatch);
        return await store.SendAsync(state, generated, commercialHead.Subject, cancellationToken);
    }
}
