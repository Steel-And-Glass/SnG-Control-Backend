using Microsoft.AspNetCore.Authorization;
using SnG.Control.Application.Identity;

namespace SnG.Control.Api.Authorization;

public static class ManagementRegistration
{
    public static IServiceCollection AddManagementAuthorization(this IServiceCollection services)
    {
        services.AddOptions<ManagementOptions>().BindConfiguration("Authentication")
            .Validate(options => !string.IsNullOrWhiteSpace(options.ManagementSubject),
                "Configure Authentication:ManagementSubject.").ValidateOnStart();
        services.AddSingleton<IManagementIdentity, ConfiguredManagementIdentity>();
        services.AddScoped<IAuthorizationHandler, ManagementAuthorizationHandler>();
        services.AddAuthorization(options => options.AddPolicy(ManagementRequirement.PolicyName,
            policy => policy.RequireAuthenticatedUser().AddRequirements(new ManagementRequirement())));
        return services;
    }
}
