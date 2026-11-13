using Microsoft.Extensions.Logging;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SnG.Control.Api.Authentication;

namespace SnG.Control.Tests.Api;

public class GoogleAuthenticationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Startup_RejectsMissingClientIdWithoutPrintingValues(string? clientId)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders(); // Startup validation must not depend on Windows Event Log permissions.
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Authentication:Google:ClientId"] = clientId });
        builder.Services.AddGoogleAuthentication();
        using var host = builder.Build();
        var error = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
        Assert.Equal(new[] { "Configure Authentication:Google:ClientId." }, error.Failures);
    }

    [Fact]
    public void Registration_UsesGoogleDiscoveryAndAllMandatoryValidations()
    {
        using var services = Services();
        var options = services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);
        Assert.Equal("https://accounts.google.com", options.Authority);
        Assert.Equal("https://accounts.google.com/.well-known/openid-configuration", options.MetadataAddress);
        Assert.Equal("synthetic-client", options.Audience); Assert.True(options.RequireHttpsMetadata);
        Assert.False(options.MapInboundClaims); Assert.False(options.SaveToken); Assert.False(options.IncludeErrorDetails);
        var validation = options.TokenValidationParameters;
        Assert.True(validation.ValidateIssuer); Assert.True(validation.ValidateAudience); Assert.True(validation.ValidateLifetime);
        Assert.True(validation.ValidateIssuerSigningKey); Assert.True(validation.RequireExpirationTime); Assert.True(validation.RequireSignedTokens);
        Assert.Equal(new[] { "https://accounts.google.com", "accounts.google.com" }, validation.ValidIssuers);
        Assert.Equal("synthetic-client", validation.ValidAudience);
        Assert.False(validation.IgnoreTrailingSlashWhenValidatingAudience);
        Assert.Equal(new[] { SecurityAlgorithms.RsaSha256 }, validation.ValidAlgorithms);
        Assert.Equal(TimeSpan.FromSeconds(60), validation.ClockSkew);
        Assert.False(validation.IncludeTokenOnFailedValidation); Assert.False(validation.LogTokenId);
        Assert.NotNull(options.ConfigurationManager);
        Assert.Equal(JwtBearerDefaults.AuthenticationScheme, services.GetRequiredService<IOptions<AuthenticationOptions>>().Value.DefaultScheme);
    }

    [Fact]
    public async Task FallbackPolicy_RequiresOnlyAuthentication()
    {
        using var services = Services();
        var authorization = services.GetRequiredService<IAuthorizationService>();
        var policy = services.GetRequiredService<IOptions<AuthorizationOptions>>().Value.FallbackPolicy!;
        Assert.False((await authorization.AuthorizeAsync(new ClaimsPrincipal(new ClaimsIdentity()), null, policy)).Succeeded);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "synthetic")], "test"));
        Assert.True((await authorization.AuthorizeAsync(principal, null, policy)).Succeeded);
        Assert.Single(policy.Requirements);
    }

    [Fact]
    public async Task TokenValidated_PreservesOriginalClaimsIncludingEmailVerified()
    {
        using var services = Services();
        var options = services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "synthetic"), new Claim("email_verified", "true", ClaimValueTypes.Boolean)], "test"));
        var context = new TokenValidatedContext(new DefaultHttpContext(), new AuthenticationScheme("Bearer", null, typeof(JwtBearerHandler)), options) { Principal = principal };
        await options.Events.TokenValidated(context);
        Assert.Null(context.Result); Assert.Same(principal, context.Principal);
        Assert.Equal("true", context.Principal.FindFirst("email_verified")!.Value);
        Assert.Null(context.Principal.FindFirst(ClaimTypes.NameIdentifier));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void CurrentUser_RejectsUnauthenticatedOrMissingSubjectWithoutFallback(bool authenticated, bool hasSub)
    {
        var claims = new List<Claim> { new("email", "test@example.com"), new(ClaimTypes.NameIdentifier, "mapped-identity") };
        if (hasSub) claims.Add(new("sub", "synthetic"));
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticated ? "test" : null)) } };
        var user = new HttpContextCurrentUser(accessor);
        Assert.Throws<InvalidOperationException>(() => user.Subject);
    }

    private static ServiceProvider Services()
    {
        var services = new ServiceCollection(); services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Authentication:Google:ClientId"] = "synthetic-client" }).Build());
        services.AddGoogleAuthentication();
        return services.BuildServiceProvider();
    }
}
