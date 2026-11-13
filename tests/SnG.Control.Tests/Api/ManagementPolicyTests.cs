using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SnG.Control.Api.Authorization;
using SnG.Control.Application.Identity;

namespace SnG.Control.Tests.Api;

public class ManagementPolicyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    public async Task Startup_RejectsMissingManagementWithOnlyTheRequiredKey(string? subject)
    {
        var builder = Host.CreateApplicationBuilder(); builder.Configuration.Sources.Clear(); builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Authentication:ManagementSubject"] = subject });
        builder.Services.AddManagementAuthorization();
        using var host = builder.Build();
        var error = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
        Assert.Equal("Configure Authentication:ManagementSubject.", error.Message);
        Assert.Equal(new[] { "Configure Authentication:ManagementSubject." }, error.Failures);
    }

    [Theory]
    [InlineData("management-test-subject", "different@example.com", true, true)]
    [InlineData("other-google-subject", "test@example.com", true, false)]
    [InlineData("MANAGEMENT-TEST-SUBJECT", "test@example.com", true, false)]
    [InlineData(" management-test-subject ", "test@example.com", true, false)]
    [InlineData(null, "test@example.com", true, false)]
    [InlineData("management-test-subject", null, false, false)]
    [InlineData("management-test-subject", null, true, true)]
    public async Task Policy_RequiresAuthenticatedExactSubjectRegardlessOfEmail(string? subject, string? email, bool authenticated, bool allowed)
    {
        var services = new ServiceCollection(); services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Authentication:ManagementSubject"] = "management-test-subject" }).Build());
        services.AddManagementAuthorization();
        RoleTestAuthorization.Register(services);
        using var provider = services.BuildServiceProvider();
        Assert.Equal("management-test-subject", provider.GetRequiredService<IManagementIdentity>().Subject);
        var claims = new List<Claim>();
        if (subject is not null) claims.Add(new("sub", subject));
        if (email is not null) claims.Add(new("email", email));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticated ? "test" : null));
        var result = await RoleTestAuthorization.AuthorizeAsync(provider, principal, ManagementRequirement.PolicyName);
        Assert.Equal(allowed, result.Succeeded);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BudgetsOverlap_RetainsOnlyTheConfiguredPolicy(bool sameSubject)
    {
        var services = new ServiceCollection(); services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:ManagementSubject"] = "management-test-subject",
            ["Authentication:BudgetsAssigneeSubject"] = sameSubject ? "management-test-subject" : "budgets-test-subject"
        }).Build());
        services.AddManagementAuthorization(); services.AddBudgetsAssigneeAuthorization();
        RoleTestAuthorization.Register(services);
        using var provider = services.BuildServiceProvider();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "management-test-subject")], "test"));
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        Assert.True((await RoleTestAuthorization.AuthorizeAsync(provider, principal, ManagementRequirement.PolicyName)).Succeeded);
        Assert.Equal(sameSubject, (await RoleTestAuthorization.AuthorizeAsync(provider, principal, BudgetsAssigneeRequirement.PolicyName)).Succeeded);
    }
    [Theory]
    [InlineData("management-test-subject", "google-test-subject", true, false)]
    [InlineData("google-test-subject", "google-test-subject", false, true)]
    [InlineData("management-test-subject", "management-test-subject", true, true)]
    public async Task Policies_UseOnlyTheirOwnConfiguration(string subject, string head, bool managementAllowed, bool headAllowed)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:ManagementSubject"] = "management-test-subject",
            ["Authentication:CommercialHeadSubject"] = head
        }).Build());
        services.AddManagementAuthorization();
        services.AddCommercialHeadAuthorization();
        RoleTestAuthorization.Register(services);
        using var provider = services.BuildServiceProvider();
        Assert.Single(provider.GetServices<IManagementIdentity>());
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", subject)], "test"));
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        Assert.Equal(managementAllowed, (await RoleTestAuthorization.AuthorizeAsync(provider, principal, ManagementRequirement.PolicyName)).Succeeded);
        Assert.Equal(headAllowed, (await RoleTestAuthorization.AuthorizeAsync(provider, principal, CommercialHeadRequirement.PolicyName)).Succeeded);
    }
}
