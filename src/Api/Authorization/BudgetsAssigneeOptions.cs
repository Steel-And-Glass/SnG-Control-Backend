using Microsoft.Extensions.Options;
using SnG.Control.Application.Identity;

namespace SnG.Control.Api.Authorization;

public sealed class BudgetsAssigneeOptions
{
    public string BudgetsAssigneeSubject { get; set; } = "";
}
public sealed class ConfiguredBudgetsAssigneeIdentity(IOptions<BudgetsAssigneeOptions> options) : IBudgetsAssigneeIdentity
{
    public string Subject => options.Value.BudgetsAssigneeSubject;
}
