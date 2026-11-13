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

public class BudgetsAssigneePolicyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    public async Task Startup_RejectsMissingBudgetsWithOnlyTheRequiredKey(string? subject)
    {
        var builder = Host.CreateApplicationBuilder(); builder.Configuration.Sources.Clear(); builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Authentication:BudgetsAssigneeSubject"] = subject });
        builder.Services.AddBudgetsAssigneeAuthorization();
        using var host = builder.Build();
        var error = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
        Assert.Equal("Configure Authentication:BudgetsAssigneeSubject.", error.Message);
        Assert.Equal(new[] { "Configure Authentication:BudgetsAssigneeSubject." }, error.Failures);
    }

    [Theory]
    [InlineData("budgets-test-subject", "different@example.com", true, true)]
    [InlineData("other-google-subject", "test@example.com", true, false)]
    [InlineData("BUDGETS-TEST-SUBJECT", "test@example.com", true, false)]
    [InlineData(" budgets-test-subject ", "test@example.com", true, false)]
    [InlineData(null, "test@example.com", true, false)]
    [InlineData("budgets-test-subject", null, false, false)]
    [InlineData("budgets-test-subject", null, true, true)]
    public async Task Policy_RequiresAuthenticatedExactSubjectRegardlessOfEmail(string? subject, string? email, bool authenticated, bool allowed)
    {
        var services = new ServiceCollection(); services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Authentication:BudgetsAssigneeSubject"] = "budgets-test-subject" }).Build());
        services.AddBudgetsAssigneeAuthorization();
        RoleTestAuthorization.Register(services);
        using var provider = services.BuildServiceProvider();
        Assert.Equal("budgets-test-subject", provider.GetRequiredService<IBudgetsAssigneeIdentity>().Subject);
        var claims = new List<Claim>();
        if (subject is not null) claims.Add(new("sub", subject));
        if (email is not null) claims.Add(new("email", email));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticated ? "test" : null));
        var result = await RoleTestAuthorization.AuthorizeAsync(provider, principal, BudgetsAssigneeRequirement.PolicyName);
        Assert.Equal(allowed, result.Succeeded);
    }
    [Theory]
    [InlineData("budgets-test-subject", "google-test-subject", true, false)]
    [InlineData("google-test-subject", "google-test-subject", false, true)]
    [InlineData("budgets-test-subject", "budgets-test-subject", true, true)]
    public async Task Policies_UseOnlyTheirOwnConfiguration(string subject, string head, bool budgetsAllowed, bool headAllowed)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:BudgetsAssigneeSubject"] = "budgets-test-subject",
            ["Authentication:CommercialHeadSubject"] = head
        }).Build());
        services.AddBudgetsAssigneeAuthorization();
        services.AddCommercialHeadAuthorization();
        RoleTestAuthorization.Register(services);
        using var provider = services.BuildServiceProvider();
        Assert.Single(provider.GetServices<IBudgetsAssigneeIdentity>());
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", subject)], "test"));
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        Assert.Equal(budgetsAllowed, (await RoleTestAuthorization.AuthorizeAsync(provider, principal, BudgetsAssigneeRequirement.PolicyName)).Succeeded);
        Assert.Equal(headAllowed, (await RoleTestAuthorization.AuthorizeAsync(provider, principal, CommercialHeadRequirement.PolicyName)).Succeeded);
    }
}
