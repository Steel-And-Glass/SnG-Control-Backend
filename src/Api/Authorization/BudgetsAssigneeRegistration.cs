using Microsoft.AspNetCore.Authorization;
using SnG.Control.Application.Identity;

namespace SnG.Control.Api.Authorization;

public static class BudgetsAssigneeRegistration
{
    public static IServiceCollection AddBudgetsAssigneeAuthorization(this IServiceCollection services)
    {
        services.AddOptions<BudgetsAssigneeOptions>().BindConfiguration("Authentication")
            .Validate(options => !string.IsNullOrWhiteSpace(options.BudgetsAssigneeSubject),
                "Configure Authentication:BudgetsAssigneeSubject.").ValidateOnStart();
        services.AddSingleton<IBudgetsAssigneeIdentity, ConfiguredBudgetsAssigneeIdentity>();
        services.AddScoped<IAuthorizationHandler, BudgetsAssigneeAuthorizationHandler>();
        services.AddAuthorization(options => options.AddPolicy(BudgetsAssigneeRequirement.PolicyName,
            policy => policy.RequireAuthenticatedUser().AddRequirements(new BudgetsAssigneeRequirement())));
        return services;
    }
}
