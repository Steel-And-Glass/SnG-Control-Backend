using Microsoft.AspNetCore.Authorization;
using SnG.Control.Application.Identity;
using SnG.Control.Domain.Enums;

namespace SnG.Control.Api.Authorization;

public sealed class ManagementRequirement : IAuthorizationRequirement
{
    public const string PolicyName = "Management";
}

public sealed class ManagementAuthorizationHandler(IActiveRoleContext active)
    : AuthorizationHandler<ManagementRequirement>
{
    protected override Task HandleRequirementAsync(Microsoft.AspNetCore.Authorization.AuthorizationHandlerContext context, ManagementRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated == true && active.Role == SystemRole.Management)
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
