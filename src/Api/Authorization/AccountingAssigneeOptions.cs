using Microsoft.Extensions.Options;
using SnG.Control.Application.Identity;

namespace SnG.Control.Api.Authorization;

public sealed class AccountingAssigneeOptions
{
    public string AccountingAssigneeSubject { get; set; } = "";
}
public sealed class ConfiguredAccountingAssigneeIdentity(IOptions<AccountingAssigneeOptions> options) : IAccountingAssigneeIdentity
{
    public string Subject => options.Value.AccountingAssigneeSubject;
}
