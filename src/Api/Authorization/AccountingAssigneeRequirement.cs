using Microsoft.AspNetCore.Authorization;
using SnG.Control.Application.Identity;
using SnG.Control.Domain.Enums;

namespace SnG.Control.Api.Authorization;

public sealed class AccountingAssigneeRequirement : IAuthorizationRequirement
{
    public const string PolicyName = "AccountingAssignee";
}

public sealed class AccountingAssigneeAuthorizationHandler(IActiveRoleContext active, IAccountingAssigneeIdentity accountant)
    : AuthorizationHandler<AccountingAssigneeRequirement>
{
    protected override Task HandleRequirementAsync(Microsoft.AspNetCore.Authorization.AuthorizationHandlerContext context, AccountingAssigneeRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated == true && active.Role == SystemRole.Accounting
            && string.Equals(context.User.FindFirst("sub")?.Value, accountant.Subject, StringComparison.Ordinal))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
