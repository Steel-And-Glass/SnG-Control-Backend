using Microsoft.AspNetCore.Authorization;
using SnG.Control.Application.Identity;

namespace SnG.Control.Api.Authorization;

public static class AccountingAssigneeRegistration
{
    public static IServiceCollection AddAccountingAssigneeAuthorization(this IServiceCollection services)
    {
        services.AddOptions<AccountingAssigneeOptions>().BindConfiguration("Authentication")
            .Validate(options => !string.IsNullOrWhiteSpace(options.AccountingAssigneeSubject),
                "Configure Authentication:AccountingAssigneeSubject.").ValidateOnStart();
        services.AddSingleton<IAccountingAssigneeIdentity, ConfiguredAccountingAssigneeIdentity>();
        services.AddScoped<IAuthorizationHandler, AccountingAssigneeAuthorizationHandler>();
        services.AddAuthorization(options => options.AddPolicy(AccountingAssigneeRequirement.PolicyName,
            policy => policy.RequireAuthenticatedUser().AddRequirements(new AccountingAssigneeRequirement())));
        return services;
    }
}
