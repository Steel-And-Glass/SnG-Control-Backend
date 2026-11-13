using Microsoft.AspNetCore.Authorization;
using SnG.Control.Application.Identity;
using SnG.Control.Domain.Enums;

namespace SnG.Control.Api.Authorization;

public sealed class BudgetsAssigneeRequirement : IAuthorizationRequirement
{
    public const string PolicyName = "BudgetsAssignee";
}

public sealed class BudgetsAssigneeAuthorizationHandler(IActiveRoleContext active)
    : AuthorizationHandler<BudgetsAssigneeRequirement>
{
    protected override Task HandleRequirementAsync(Microsoft.AspNetCore.Authorization.AuthorizationHandlerContext context, BudgetsAssigneeRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated == true && active.Role == SystemRole.Budgets)
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
