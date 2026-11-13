using System.Globalization;
using System.Text.RegularExpressions;
using SnG.Control.Application.Access;
using SnG.Control.Application.Features.ClientQuotationDecisions;
using SnG.Control.Application.Features.ProjectTasks;
using SnG.Control.Application.Identity;
using SnG.Control.Application.Scheduling;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
using SnG.Control.Domain.ValueObjects;
namespace SnG.Control.Application.Features.Accounting;

public enum AccountingStatus { Success, Forbidden, ProjectNotFound, ClientDecisionRequired, DecisionNotApproved, InvalidWorkflowState, AlreadySentToAccounting, AccountingHandoffRequired, AlreadyRecorded, InvalidDeposit }
public sealed record AccountingHandoffState(bool Available, int? QuotationRound, Guid? CommercialQuotationId, DateTime? SentAt);
public sealed record InitialDepositState(bool Available, string? Amount,
    string CurrencyCode, DateTime? ReceivedAt, DateTime? RecordedAt, string? Reference, string? Note);
public sealed record AccountingState(Guid ProjectId, AccountingHandoffState AccountingHandoff, InitialDepositState Deposit)
{
    public static AccountingState From(Guid id, AccountingHandoff? h, InitialDeposit? d) => new(id,
        new(h is not null, h?.QuotationRound, h?.CommercialQuotationId, h?.SentAt),
        new(d is not null, d?.Amount.ToString("0.00", CultureInfo.InvariantCulture), Money.CurrencyCode, d?.ReceivedAt, d?.RecordedAt, d?.Reference, d?.Note));
}
public sealed record AccountingWriteResponse(AccountingState State, ProjectTaskResponse Task);
public sealed record AccountingResult(AccountingStatus Status, AccountingWriteResponse? Response = null);
public sealed record DepositInput(decimal Amount, DateTime ReceivedAt, string? Reference, string? Note);
public interface IAccountingStore
{
    Task<AccountingState?> ReadAsync(Guid id, ProjectReadScope scope, CancellationToken token);
    Task<AccountingResult> SendAsync(Guid id, DecisionActor actor, string accountantSubject, CancellationToken token);
    Task<AccountingResult> RecordAsync(Guid id, DepositInput input, DecisionActor actor, CancellationToken token);
}
public sealed class AccountingService(IAccountingStore store, ICurrentUser user, IActiveRoleContext active, IAccountingAssigneeIdentity accountant, TaskTiming timing)
{
    public Task<AccountingState?> ReadAsync(Guid id, CancellationToken token) => store.ReadAsync(id, new(user.Subject, active.Role), token);
    private DecisionActor Actor => new(user.Subject, user.Email, user.Name);
    public Task<AccountingResult> SendAsync(Guid id, CancellationToken token) => active.Role != SystemRole.CommercialAdvisor
        ? Task.FromResult(new AccountingResult(AccountingStatus.Forbidden)) : store.SendAsync(id, Actor, accountant.Subject, token);
    public Task<AccountingResult> RecordAsync(Guid id, string amount, string receivedAt, string? reference, string? note, CancellationToken token)
    {
        if (active.Role != SystemRole.Accounting || user.Subject != accountant.Subject) return Task.FromResult(new AccountingResult(AccountingStatus.Forbidden));
        if (!Regex.IsMatch(amount, @"^[0-9]{1,18}(?:\.[0-9]{1,28})?$", RegexOptions.CultureInvariant)
            || !decimal.TryParse(amount, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var money) || money <= 0 || money > Money.Maximum || Money.Normalize(money) <= 0
            || !Regex.IsMatch(receivedAt, @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant)
            || !DateTimeOffset.TryParse(receivedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var received)
            || received.UtcDateTime > timing.UtcNow || reference?.Length > 200 || note?.Length > 2000)
            return Task.FromResult(new AccountingResult(AccountingStatus.InvalidDeposit));
        return store.RecordAsync(id, new(Money.Normalize(money), received.UtcDateTime, reference, note), Actor, token);
    }
}
