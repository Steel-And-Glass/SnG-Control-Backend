using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SnG.Control.Api.Authorization;
using SnG.Control.Application.Identity;
using SnG.Control.Tests.TestDoubles;
namespace SnG.Control.Tests.Api;

internal static class RoleTestAuthorization
{
    public static void Register(IServiceCollection services)
    {
        services.AddActiveRoles();
        services.TryAddSingleton<IAccountingAssigneeIdentity>(new IdentityDouble("unused-accounting"));
        services.AddHttpContextAccessor();
        services.TryAddSingleton<ICommercialHeadIdentity>(new IdentityDouble("unused-head"));
        services.TryAddSingleton<IBudgetsAssigneeIdentity>(new IdentityDouble("unused-budgets"));
        services.TryAddSingleton<IManagementIdentity>(new IdentityDouble("unused-management"));
    }
    public static Task<AuthorizationResult> AuthorizeAsync(IServiceProvider provider, ClaimsPrincipal principal, string policy)
    {
        var http = new DefaultHttpContext { User = principal, RequestServices = provider };
        http.Request.Headers[HttpActiveRoleContext.HeaderName] = policy == "BudgetsAssignee" ? "Budgets" : policy == "AccountingAssignee" ? "Accounting" : policy;
        provider.GetRequiredService<IHttpContextAccessor>().HttpContext = http;
        return provider.GetRequiredService<IAuthorizationService>().AuthorizeAsync(principal, http, policy);
    }
}
