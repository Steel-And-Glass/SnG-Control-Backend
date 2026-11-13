using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using SnG.Control.Api.Controllers;
using SnG.Control.Application.Identity;
using SnG.Control.Domain.Enums;

namespace SnG.Control.Api.Authorization;

public sealed class CommercialAdvisorOptions
{
    public string[] CommercialAdvisorSubjects { get; set; } = [];
}

public sealed class ConfiguredRoleResolver(IOptions<CommercialAdvisorOptions> advisors,
    IBudgetsAssigneeIdentity budgets, ICommercialHeadIdentity head, IManagementIdentity management, IAccountingAssigneeIdentity accounting, IInstallationAssigneeIdentity? installation = null, IEngineeringAssigneeIdentity? engineering = null, IOperationsHeadIdentity? operations = null) : IRoleResolver
{
    public IReadOnlyList<SystemRole> Resolve(string subject)
    {
        if (string.IsNullOrWhiteSpace(subject)) return [];
        var roles = new List<SystemRole>();
        if (advisors.Value.CommercialAdvisorSubjects.Contains(subject, StringComparer.Ordinal)) roles.Add(SystemRole.CommercialAdvisor);
        if (string.Equals(subject, budgets.Subject, StringComparison.Ordinal)) roles.Add(SystemRole.Budgets);
        if (string.Equals(subject, head.Subject, StringComparison.Ordinal)) roles.Add(SystemRole.CommercialHead);
        if (string.Equals(subject, management.Subject, StringComparison.Ordinal)) roles.Add(SystemRole.Management);
        if (string.Equals(subject, accounting.Subject, StringComparison.Ordinal)) roles.Add(SystemRole.Accounting);
        if (!string.IsNullOrWhiteSpace(installation?.Subject) && string.Equals(subject, installation.Subject, StringComparison.Ordinal)) roles.Add(SystemRole.Installation);
        if (engineering?.Subjects.Contains(subject, StringComparer.Ordinal) == true) roles.Add(SystemRole.Engineering);
        if (!string.IsNullOrWhiteSpace(operations?.Subject) && string.Equals(subject, operations.Subject, StringComparison.Ordinal)) roles.Add(SystemRole.OperationsHead);
        return roles;
    }
}

public sealed class HttpActiveRoleContext(IHttpContextAccessor accessor, IRoleResolver roles) : IActiveRoleContext
{
    public const string HeaderName = "X-SnG-Active-Role";
    public SystemRole? Role
    {
        get
        {
            var http = accessor.HttpContext;
            var subject = http?.User.FindFirst("sub")?.Value;
            if (http?.User.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(subject)) return null;
            var header = http.Request.Headers[HeaderName];
            if (header.Count != 1 || !Enum.GetNames<SystemRole>().Contains(header[0], StringComparer.Ordinal)) return null;
            var role = Enum.Parse<SystemRole>(header[0]!);
            return roles.Resolve(subject).Contains(role) ? role : null;
        }
    }
}

// All operational controllers require an owned active role, including read-only requests.
// Identity discovery must work before the user has selected an area.
public sealed class ActiveRoleFilter(IActiveRoleContext active) : IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (context.ActionDescriptor is ControllerActionDescriptor action && action.ControllerTypeInfo.AsType() == typeof(AuthController)) return;
        if (active.Role is null) context.Result = new ForbidResult();
    }
}

public static class ActiveRoleRegistration
{
    public const string CommercialAdvisorPolicy = "CommercialAdvisor";
    public static IServiceCollection AddActiveRoles(this IServiceCollection services)
    {
        services.AddOptions<CommercialAdvisorOptions>().BindConfiguration("Authentication")
            .Validate(options => options.CommercialAdvisorSubjects is not null
                && options.CommercialAdvisorSubjects.All(subject => !string.IsNullOrWhiteSpace(subject) && subject == subject.Trim())
                && options.CommercialAdvisorSubjects.Distinct(StringComparer.Ordinal).Count() == options.CommercialAdvisorSubjects.Length,
                "Configure Authentication:CommercialAdvisorSubjects with distinct non-empty Subjects.").ValidateOnStart();
        services.AddSingleton<IRoleResolver, ConfiguredRoleResolver>();
        services.AddScoped<IActiveRoleContext, HttpActiveRoleContext>();
        services.AddAuthorization(options => options.AddPolicy(CommercialAdvisorPolicy, policy => policy.RequireAuthenticatedUser()
            .RequireAssertion(context => context.Resource is HttpContext http
                && http.RequestServices.GetRequiredService<IActiveRoleContext>().Role == SystemRole.CommercialAdvisor)));
        return services;
    }
}
