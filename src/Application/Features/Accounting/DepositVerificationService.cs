using System.Globalization;
using System.Text.RegularExpressions;
using SnG.Control.Application.Access;
using SnG.Control.Application.Features.ClientQuotationDecisions;
using SnG.Control.Application.Features.ProjectDocuments;
using SnG.Control.Application.Identity;
using SnG.Control.Application.Scheduling;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
using SnG.Control.Domain.ValueObjects;
namespace SnG.Control.Application.Features.Accounting;

public sealed record VerificationSubmission(Guid RequestKey, IReadOnlyList<Guid> DocumentIds);
public sealed record VerificationReview(Guid Id, string Status, DateTime SubmittedAt, DateTime? DecidedAt,
    DocumentPerson? DecidedBy, string? Note, IReadOnlyList<Guid> DocumentIds);
public sealed record VerificationState(Guid ProjectId, bool Available, bool CanManage, bool CanSubmit, bool CanReview,
    AccountingState Accounting, IReadOnlyList<DocumentResponse> Documents, IReadOnlyList<VerificationReview> Reviews);
public interface IDepositVerificationStore
{
    Task<VerificationState?> ReadAsync(Guid id, ProjectReadScope scope, CancellationToken token);
    Task<Guid> SubmitAsync(Guid id, VerificationSubmission submission, DecisionActor actor, string accountant, CancellationToken token);
    Task DecideAsync(Guid id, Guid reviewId, bool approve, DepositInput? input, string? note, DecisionActor actor, string installer, CancellationToken token);
}
public sealed class DepositVerificationService(IDepositVerificationStore store, ICurrentUser user, IActiveRoleContext active,
    IAccountingAssigneeIdentity accountant, IInstallationAssigneeIdentity installation, TaskTiming timing)
{
    private DecisionActor Actor => new(user.Subject, user.Email, user.Name);
    public Task<VerificationState?> ReadAsync(Guid id, CancellationToken token) => store.ReadAsync(id, new(user.Subject, active.Role), token);
    public Task<Guid> SubmitAsync(Guid id, VerificationSubmission submission, CancellationToken token)
    {
        if (active.Role != SystemRole.CommercialAdvisor) throw new DocumentException(403, "VerificationAccessDenied");
        if (submission.RequestKey == Guid.Empty || submission.DocumentIds.Count is < 1 or > ProjectDocument.MaxCurrentDocuments
            || submission.DocumentIds.Any(d => d == Guid.Empty) || submission.DocumentIds.Distinct().Count() != submission.DocumentIds.Count)
            throw new DocumentException(400, "InvalidVerificationDocuments");
        return store.SubmitAsync(id, submission, Actor, accountant.Subject, token);
    }
    public Task DecideAsync(Guid id, Guid reviewId, bool approve, string? amount, string? receivedAt, string? note, CancellationToken token)
    {
        if (active.Role != SystemRole.Accounting || user.Subject != accountant.Subject) throw new DocumentException(403, "VerificationAccessDenied");
        if (note?.Length > 2000 || (!approve && string.IsNullOrWhiteSpace(note))) throw new DocumentException(400, "VerificationObservationRequired");
        DepositInput? input = null;
        if (approve)
        {
            if (string.IsNullOrWhiteSpace(installation.Subject)) throw new DocumentException(503, "InstallationAssigneeNotConfigured");
            if (amount is null || !Regex.IsMatch(amount, @"^[0-9]{1,18}(?:\.[0-9]{1,28})?$", RegexOptions.CultureInvariant)
                || !decimal.TryParse(amount, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var money)
                || money <= 0 || money > Money.Maximum || Money.Normalize(money) <= 0
                || receivedAt is null || !Regex.IsMatch(receivedAt, @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})?$", RegexOptions.CultureInvariant)
                || !(receivedAt.EndsWith('Z') || Regex.IsMatch(receivedAt, @"[+-]\d{2}:\d{2}$"))
                || !DateTimeOffset.TryParse(receivedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var received) || received.UtcDateTime > timing.UtcNow)
                throw new DocumentException(400, "InvalidDeposit");
            input = new(Money.Normalize(money), received.UtcDateTime, null, note);
        }
        else if (amount is not null || receivedAt is not null) throw new DocumentException(400, "InvalidVerificationDecision");
        return store.DecideAsync(id, reviewId, approve, input, note, Actor, installation.Subject, token);
    }
}
