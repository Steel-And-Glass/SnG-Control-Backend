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

public class CommercialHeadPolicyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    public async Task Startup_RejectsMissingHeadWithOnlyTheRequiredKey(string? subject)
    {
        var builder = Host.CreateApplicationBuilder(); builder.Configuration.Sources.Clear(); builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Authentication:CommercialHeadSubject"] = subject });
        builder.Services.AddCommercialHeadAuthorization();
        using var host = builder.Build();
        var error = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
        Assert.Equal("Configure Authentication:CommercialHeadSubject.", error.Message);
        Assert.Equal(new[] { "Configure Authentication:CommercialHeadSubject." }, error.Failures);
    }

    [Theory]
    [InlineData("google-test-subject", "different@example.com", true, true)]
    [InlineData("other-google-subject", "test@example.com", true, false)]
    [InlineData("GOOGLE-TEST-SUBJECT", "test@example.com", true, false)]
    [InlineData(" google-test-subject ", "test@example.com", true, false)]
    [InlineData(null, "test@example.com", true, false)]
    [InlineData("google-test-subject", null, false, false)]
    [InlineData("google-test-subject", null, true, true)]
    public async Task Policy_RequiresAuthenticatedExactSubjectRegardlessOfEmail(string? subject, string? email, bool authenticated, bool allowed)
    {
        var services = new ServiceCollection(); services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Authentication:CommercialHeadSubject"] = "google-test-subject" }).Build());
        services.AddCommercialHeadAuthorization();
        RoleTestAuthorization.Register(services);
        using var provider = services.BuildServiceProvider();
        Assert.Equal("google-test-subject", provider.GetRequiredService<ICommercialHeadIdentity>().Subject);
        var claims = new List<Claim>();
        if (subject is not null) claims.Add(new("sub", subject));
        if (email is not null) claims.Add(new("email", email));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticated ? "test" : null));
        var result = await RoleTestAuthorization.AuthorizeAsync(provider, principal, CommercialHeadRequirement.PolicyName);
        Assert.Equal(allowed, result.Succeeded);
    }
}
