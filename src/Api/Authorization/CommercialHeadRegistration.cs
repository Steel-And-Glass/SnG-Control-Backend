using Microsoft.AspNetCore.Authorization;
using SnG.Control.Application.Identity;

namespace SnG.Control.Api.Authorization;

public static class CommercialHeadRegistration
{
    public static IServiceCollection AddCommercialHeadAuthorization(this IServiceCollection services)
    {
        services.AddOptions<CommercialHeadOptions>().BindConfiguration(CommercialHeadOptions.SectionName)
            .Validate(options => !string.IsNullOrWhiteSpace(options.CommercialHeadSubject),
                "Configure Authentication:CommercialHeadSubject.").ValidateOnStart();
        services.AddSingleton<ICommercialHeadIdentity, ConfiguredCommercialHeadIdentity>();
        services.AddScoped<IAuthorizationHandler, CommercialHeadAuthorizationHandler>();
        services.AddAuthorization(options => options.AddPolicy(CommercialHeadRequirement.PolicyName,
            policy => policy.RequireAuthenticatedUser().AddRequirements(new CommercialHeadRequirement())));
        return services;
    }
}
