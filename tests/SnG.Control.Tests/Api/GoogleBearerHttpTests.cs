using SnG.Control.Api.Authorization;
using System.Net;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using SnG.Control.Api;
using SnG.Control.Api.Authentication;
using SnG.Control.Api.Controllers;
using SnG.Control.Infrastructure.Files;
using SnG.Control.Tests.Fixtures;

namespace SnG.Control.Tests.Api;

public class GoogleBearerHttpTests
{
    [Theory]
    [InlineData("")]
    [InlineData("Bearer not-a-token")]
    [InlineData("Bearer e30.e30.invalid")]
    [InlineData("Basic invalid")]
    public async Task RealBearerHandler_Returns401WithoutNetworkOrErrorDetails(string authorization)
    {
        using var directory = new TemporaryStorage();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(AuthController).Assembly.GetName().Name,
            EnvironmentName = "Testing"
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0"); builder.Logging.ClearProviders();
        builder.Services.AddApiServices();
        builder.Services.Configure<OperationsHeadOptions>(o => o.OperationsHeadSubject = "synthetic-operations");
        builder.Services.Configure<AccountingAssigneeOptions>(options => options.AccountingAssigneeSubject = "accounting-test-subject");
        builder.Services.Configure<ManagementOptions>(options => options.ManagementSubject = "management-test-subject");
        builder.Services.RemoveAll<Microsoft.Extensions.Hosting.IHostedService>();
        builder.Services.Configure<BudgetsAssigneeOptions>(options => options.BudgetsAssigneeSubject = "budgets-test-subject");
        builder.Services.Configure<CommercialHeadOptions>(options => options.CommercialHeadSubject = "google-test-subject");
        builder.Services.Configure<GoogleAuthenticationOptions>(options => options.ClientId = "synthetic-client");
        builder.Services.Configure<FileStorageOptions>(options => options.RootPath = directory.Root);
        // Replace discovery only inside this test. No Google token or outbound request is involved.
        builder.Services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(
                new OpenIdConnectConfiguration { Issuer = "https://accounts.google.com" }));
        await using var app = builder.Build();
        app.UseExceptionHandler(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client = new HttpClient { BaseAddress = new Uri(address) };
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        if (authorization.Length > 0) request.Headers.TryAddWithoutValidation("Authorization", authorization);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var challenge = Assert.Single(response.Headers.WwwAuthenticate);
        Assert.Equal("Bearer", challenge.Scheme); Assert.Null(challenge.Parameter);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }
}
