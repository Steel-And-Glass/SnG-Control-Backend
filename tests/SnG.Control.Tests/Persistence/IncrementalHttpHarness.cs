using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using SkiaSharp;
using SnG.Control.Api;
using SnG.Control.Api.Controllers;
using SnG.Control.Application.Files;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
using SnG.Control.Domain.ValueObjects;
using SnG.Control.Infrastructure.Persistence;
using SnG.Control.Tests.Fixtures;
using SnG.Control.Tests.TestDoubles;
namespace SnG.Control.Tests.Persistence;

internal sealed class IncrementalHttpHarness : IAsyncDisposable
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private readonly TemporaryStorage storage = new(); private WebApplication app = null!; private string connection = "";
    public ControlledTimeProvider Clock { get; } = new(new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc));
    public Guid ProjectId { get; private set; }
    public HttpClient Http { get; private set; } = null!;
    public SnGControlDbContext Db() => new(new DbContextOptionsBuilder<SnGControlDbContext>().UseNpgsql(connection).Options);
    public void EnableExtraction()=>app.Services.GetRequiredService<IOptions<SnG.Control.Application.Features.Operations.DocumentExtractionOptions>>().Value.Enabled=true;
    public IStreamingFileStorage Files => app.Services.GetRequiredService<IStreamingFileStorage>();
    // Test-only replacement of the effective startup membership. Production IOptions is not hot-reloaded.
    public void SetAdvisorMembership(params string[] subjects) => app.Services.GetRequiredService<IOptions<SnG.Control.Api.Authorization.CommercialAdvisorOptions>>().Value.CommercialAdvisorSubjects = subjects;
    public void SetFilingLimits(long fileBytes,long projectBytes)
    { var limits=app.Services.GetRequiredService<IOptions<SnG.Control.Application.Features.Engineering.OpeningFilingLimits>>().Value;limits.MaxFileBytes=fileBytes;limits.MaxProjectStorageBytes=projectBytes; }
    public static async Task<IncrementalHttpHarness> Start() { var h = new IncrementalHttpHarness(); await h.Initialize(); return h; }
    private async Task Initialize()
    {
        var config = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SNG_VERIFICATION_TEST_DATABASE")); Assert.Equal("127.0.0.1", config.Host); Assert.Equal("verification_test", config.Database);
        var schema = "e2e_" + Guid.NewGuid().ToString("N"); await using (var c = new NpgsqlConnection(config.ConnectionString)) { await c.OpenAsync(); await using var q = new NpgsqlCommand($"CREATE SCHEMA {schema}", c); await q.ExecuteNonQueryAsync(); }
        config.SearchPath = schema; connection = config.ConnectionString;
        await using (var db = Db()) await db.Database.MigrateAsync();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ApplicationName = typeof(MeasurementsController).Assembly.GetName().Name, EnvironmentName = "Testing" });
        builder.Configuration.Sources.Clear(); builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:Google:ClientId"] = "synthetic-client",
            ["Authentication:CommercialAdvisorSubjects:0"] = "advisor",
            ["Authentication:CommercialAdvisorSubjects:1"] = "other-advisor",
            ["Authentication:InstallationAssigneeSubject"] = "installer",
            ["Authentication:EngineeringAssigneeSubject"] = "engineer",
            ["Authentication:EngineeringWorkerSubjects:0"] = "worker",
            ["Authentication:ManagementSubject"] = "management",
            ["Authentication:OperationsHeadSubject"] = "operations",
            ["Authentication:CommercialHeadSubject"] = "head",
            ["Authentication:BudgetsAssigneeSubject"] = "budgets",
            ["Authentication:AccountingAssigneeSubject"] = "accountant",
            ["Measurements:IncrementalNewProjectsFromUtc"] = "2020-01-01T00:00:00Z",
            ["FileStorage:RootPath"] = storage.Root
        });
        builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("SNG_E2E_BFF_URL") is null ? "http://127.0.0.1:0" : "http://127.0.0.1:55486"); builder.Logging.ClearProviders();
        builder.Services.AddDbContext<SnGControlDbContext>(o => o.UseNpgsql(connection)); builder.Services.AddApiServices(); builder.Services.RemoveAll<IHostedService>(); builder.Services.RemoveAll<TimeProvider>(); builder.Services.AddSingleton<TimeProvider>(Clock);
        builder.Services.AddAuthentication(o => { o.DefaultAuthenticateScheme = "SyntheticE2E"; o.DefaultChallengeScheme = "SyntheticE2E"; }).AddScheme<AuthenticationSchemeOptions, IncrementalSyntheticAuthentication>("SyntheticE2E", _ => { });
        app = builder.Build(); app.UseExceptionHandler(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers(); await app.StartAsync();
        var backend = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        Http = new() { BaseAddress = new Uri(Environment.GetEnvironmentVariable("SNG_E2E_BFF_URL") ?? backend + "/api/"), Timeout = TimeSpan.FromSeconds(60) };
        await Seed();
    }
    private async Task Seed()
    {
        await using var db = Db(); var now = Clock.Now; var client = new Client("Entrega 5 synthetic"); var p = new Project(client.Id, "Ten synthetic structures", "Test city", ProductLine.Classic, "advisor"); ProjectId = p.Id; db.AddRange(client, p); await db.SaveChangesAsync();
        var v = new BudgetQuotationVersion(p.Id, 1, "synthetic.xlsx", "Client", "Project", "City", "Line", "Line", "Author", "SG9005"); var catalog = new CommercialCatalog(v.Id); var quote = new CommercialQuotation(p.Id, v.Id, catalog.Id, "synthetic.xlsx", 100, "head", null, null);
        var task = new ProjectTask(p.Id, OperationalArea.Commercial, ProjectTaskType.PresentQuotationToClient, "advisor", now.AddDays(-1)); task.Start(now.AddDays(-1), now.AddDays(1)); task.Complete(now); var decision = new ClientQuotationDecision(p.Id, quote.Id, task.Id, ClientQuotationDecisionType.Approved, null, now, "advisor", null, null);
        var account = new ProjectTask(p.Id, OperationalArea.Accounting, ProjectTaskType.RegisterInitialDeposit, "accountant", now); account.StartUntracked(now); account.Complete(now); var handoff = new AccountingHandoff(p.Id, decision.Id, quote.Id, 1, account.Id, now, "advisor", null, null);
        db.AddRange(v, catalog, quote, task, decision, account, handoff, new InitialDeposit(handoff, 50, now, now, null, null, "accountant", null, null), new ProjectTask(p.Id, OperationalArea.Installation, ProjectTaskType.TakeMeasurements, "installer", now));
        for (var i = 1; i <= 10; i++) { var values = new CatalogItemValues($"V{i}", $"Location {i}", "Synthetic system", null, null, null, null, 1.2m, 2, 2.4m, 1); var source = new CommercialCatalogItem(catalog.Id, CommercialItemSection.Main, i, values); var key = $"projects/{p.Id:D}/budget-quotations/{v.Id:D}/images/{source.Id:D}.png"; db.AddRange(source, new CommercialCatalogItemExtraction(source.Id, values, CatalogItemImageStatus.Stored, key, "image/png"), new CommercialQuotationItem(quote.Id, source.Id, CommercialItemSection.Main, i, values, 10, 10)); using var bitmap = new SKBitmap(48, 48); bitmap.Erase(new SKColor((byte)(i * 20), 80, 120)); using var image = SKImage.FromBitmap(bitmap); using var encoded = image.Encode(SKEncodedImageFormat.Png, 100); await Files.SaveAsync(key, encoded.ToArray(), default); }
        await db.SaveChangesAsync();
    }
    public string Path(string suffix) => $"projects/{ProjectId}/{suffix}";
    public async Task<HttpResponseMessage> Send(string actor, string role, HttpMethod method, string path, object? body = null) { var request = new HttpRequestMessage(method, path); if (actor.Length > 0) request.Headers.Authorization = new("Bearer", "synthetic-" + actor); if (role.Length > 0) request.Headers.Add("X-SnG-Active-Role", role); if (body != null) request.Content = body is HttpContent content ? content : JsonContent.Create(body, options: Json); return await Http.SendAsync(request); }
    public async Task<T> Get<T>(string actor, string role, string path) { using var response = await Send(actor, role, HttpMethod.Get, path); await Status(response, 200); return (await response.Content.ReadFromJsonAsync<T>(Json))!; }
    public static async Task Status(HttpResponseMessage response, int expected) { var body = response.IsSuccessStatusCode ? "" : await response.Content.ReadAsStringAsync(); Assert.True((int)response.StatusCode == expected, $"Expected {expected}, got {(int)response.StatusCode}: {body}"); }
    public async ValueTask DisposeAsync() { Http.Dispose(); await app.DisposeAsync(); storage.Dispose(); }
}
// Compiled exclusively into the test assembly. No Google calls or production authentication bypass.
public sealed class IncrementalSyntheticAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync() { var value = Request.Headers.Authorization.ToString(); if (value.Length == 0) return Task.FromResult(AuthenticateResult.NoResult()); var actor = value.StartsWith("Bearer synthetic-", StringComparison.Ordinal) ? value[17..] : ""; if (!new[] { "advisor", "other-advisor", "installer", "engineer", "worker", "management", "head", "outsider", "operations" }.Contains(actor)) return Task.FromResult(AuthenticateResult.Fail("Unknown synthetic identity")); var p = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", actor), new Claim("email", actor + "@example.test"), new Claim("name", actor), new Claim("email_verified", "true") }, Scheme.Name)); return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(p, Scheme.Name))); }
}
