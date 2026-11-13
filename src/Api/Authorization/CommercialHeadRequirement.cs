using Microsoft.AspNetCore.Authorization;
using SnG.Control.Application.Identity;
using SnG.Control.Domain.Enums;

namespace SnG.Control.Api.Authorization;

public sealed class CommercialHeadRequirement : IAuthorizationRequirement
{
    public const string PolicyName = "CommercialHead";
}

public sealed class CommercialHeadAuthorizationHandler(IActiveRoleContext active)
    : AuthorizationHandler<CommercialHeadRequirement>
{
    protected override Task HandleRequirementAsync(Microsoft.AspNetCore.Authorization.AuthorizationHandlerContext context, CommercialHeadRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated == true && active.Role == SystemRole.CommercialHead)
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
