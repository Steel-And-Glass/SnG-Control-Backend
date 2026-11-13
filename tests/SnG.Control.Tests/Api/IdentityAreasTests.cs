using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SnG.Control.Api.Authorization;
using SnG.Control.Application.Identity;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
using SnG.Control.Tests.TestDoubles;

namespace SnG.Control.Tests.Api;

public sealed class MultiRoleFixture : ApiFixture
{
    public MultiRoleFixture() => ConfigureServices = services =>
    {
        services.Configure<BudgetsAssigneeOptions>(options => options.BudgetsAssigneeSubject = "google-test-subject");
        services.Configure<ManagementOptions>(options => options.ManagementSubject = "google-test-subject");
    };
}

public class IdentityAreasTests(MultiRoleFixture fixture) : IClassFixture<MultiRoleFixture>
{
    private HttpClient Client(string? role, string mode = "valid")
    {
        var client = new HttpClient { BaseAddress = fixture.Http.BaseAddress };
        client.DefaultRequestHeaders.Authorization = new(TestAuthenticationHandler.SchemeName, mode);
        if (role is not null) client.DefaultRequestHeaders.TryAddWithoutValidation(HttpActiveRoleContext.HeaderName, role);
        return client;
    }

    [Fact]
    public async Task DiscoveryWithoutRole_ReturnsAllNamedRolesAndDerivedManagement()
    {
        using var client = Client(null);
        var me = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.Equal(new[] { "CommercialAdvisor", "Budgets", "CommercialHead", "Management" }, me.GetProperty("roles").EnumerateArray().Select(role => role.GetString()));
        Assert.True(me.GetProperty("isManagement").GetBoolean());
        using var outsider = Client(null, "outsider");
        var noRoles = await outsider.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.Empty(noRoles.GetProperty("roles").EnumerateArray());
        Assert.False(noRoles.GetProperty("isManagement").GetBoolean());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("SuperAdmin")]
    [InlineData("1")]
    [InlineData("commercialadvisor")]
    [InlineData("Budgets,Management")]
    [InlineData("Accounting")]
    public async Task InvalidOrUnavailableRole_IsForbiddenBeforeOperationalAccess(string? role)
    {
        using var client = Client(role);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/projects")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task UnownedRole_AndMultipleHeaderValues_AreRejected()
    {
        using var advisor = Client("Budgets", "other");
        Assert.Equal(HttpStatusCode.Forbidden, (await advisor.GetAsync("/api/projects")).StatusCode);
        using var multi = Client("Budgets");
        multi.DefaultRequestHeaders.TryAddWithoutValidation(HttpActiveRoleContext.HeaderName, "Management");
        Assert.Equal(HttpStatusCode.Forbidden, (await multi.GetAsync("/api/projects")).StatusCode);
    }

    [Theory]
    [InlineData("CommercialAdvisor")]
    [InlineData("Budgets")]
    [InlineData("CommercialHead")]
    [InlineData("Management")]
    public async Task SameSubject_OnlyActiveAreaCanUseItsPolicy(string role)
    {
        using var client = Client(role);
        var clientResponse = await client.PostAsJsonAsync("/api/clients", new { name = "Synthetic area client" });
        Assert.Equal(role == "CommercialAdvisor" ? HttpStatusCode.Created : HttpStatusCode.Forbidden, clientResponse.StatusCode);
        var existingClient = new Client("Synthetic"); fixture.Clients.Items.Add(existingClient);
        var created = await client.PostAsJsonAsync("/api/projects", new { clientId = existingClient.Id, name = "Synthetic", city = "City", productLine = "Classic" });
        Assert.Equal(role == "CommercialAdvisor" ? HttpStatusCode.Created : HttpStatusCode.Forbidden, created.StatusCode);
        Assert.Equal(role == "Management" ? HttpStatusCode.OK : HttpStatusCode.Forbidden, (await client.GetAsync("/api/management/overview")).StatusCode);
        Assert.Equal(role == "Budgets" ? HttpStatusCode.NotFound : HttpStatusCode.Forbidden,
            (await client.PutAsJsonAsync($"/api/projects/{Guid.NewGuid()}/sg-code", new { sgCode = "SG1" })).StatusCode);
        Assert.Equal(role == "CommercialHead" ? HttpStatusCode.NotFound : HttpStatusCode.Forbidden,
            (await client.GetAsync($"/api/projects/{Guid.NewGuid()}/budget-quotation/commercial-file")).StatusCode);
        Assert.Equal(role == "CommercialHead" ? HttpStatusCode.BadRequest : HttpStatusCode.Forbidden,
            (await client.PostAsync($"/api/projects/{Guid.NewGuid()}/commercial-quotation/preview", new StringContent("invalid"))).StatusCode);
    }

    [Fact]
    public async Task MultiRoleDocuments_KeepOwnershipAndAssignmentAcrossAreaSwitches()
    {
        var project = new Project(Guid.NewGuid(), "Synthetic", "City", ProductLine.Classic, "google-test-subject");
        fixture.Projects.Items.Add(project); fixture.Documents.AddDocument(project.Id);
        var root = $"/api/projects/{project.Id}";
        using (var budgets = Client("Budgets")) Assert.Equal(HttpStatusCode.Forbidden, (await budgets.GetAsync(root + "/documents")).StatusCode);
        using (var advisor = Client("CommercialAdvisor")) Assert.Equal(HttpStatusCode.Created, (await advisor.PostAsync(root + "/send-to-budgets", null)).StatusCode);
        var task = Assert.Single(fixture.Tasks.Items, task => task.ProjectId == project.Id);
        fixture.Tasks.Items.Add(new ProjectTask(project.Id, OperationalArea.Commercial, ProjectTaskType.ReviewQuotation, "google-test-subject"));
        var before = JsonSerializer.Serialize(new { project, task });
        foreach (var role in new[] { "CommercialAdvisor", "Budgets", "CommercialHead", "Management" })
        {
            using var client = Client(role);
            var documents = await client.GetFromJsonAsync<JsonElement>(root + "/documents");
            Assert.Equal(role is "CommercialAdvisor", documents.GetProperty("canModify").GetBoolean());
            if (role != "CommercialAdvisor")
            {
                using var form = ProjectDocumentHttpTests.Batch(("synthetic.txt", [1], null));
                Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync(root + "/documents/batch", form)).StatusCode);
            }
            if (role != "CommercialAdvisor") Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync(root + "/send-to-budgets", null)).StatusCode);
        }
        Assert.Equal(before, JsonSerializer.Serialize(new { project, task }));
        Assert.Equal("google-test-subject", task.AssignedToGoogleSubject);
        using var other = Client("CommercialAdvisor", "other");
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync(root + "/documents")).StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("different-assignee")]
    public async Task BudgetsRoleWithoutTaskAssignment_CannotStartTask(string? assignee)
    {
        var project = new Project(Guid.NewGuid(), "Synthetic", "City", ProductLine.Classic, "google-test-subject");
        fixture.Projects.Items.Add(project);
        var task = new ProjectTask(project.Id, OperationalArea.Budgets, ProjectTaskType.PrepareQuotation, assignee);
        fixture.Tasks.Items.Add(task);
        using var budgets = Client("Budgets");
        Assert.Equal(HttpStatusCode.Conflict, (await budgets.PutAsJsonAsync($"/api/projects/{project.Id}/sg-code", new { sgCode = "SG123" })).StatusCode);
        Assert.Null(project.SgCode); Assert.Null(task.StartedAt); Assert.Null(task.DeadlineAt);
    }

    [Theory]
    [InlineData("advisor", SystemRole.CommercialAdvisor)]
    [InlineData("budgets", SystemRole.Budgets)]
    [InlineData("accounting", SystemRole.Accounting)]
    [InlineData("head", SystemRole.CommercialHead)]
    [InlineData("management", SystemRole.Management)]
    public void Resolver_UsesExactSubjectAndOnlyConfiguredMembership(string subject, SystemRole expected)
    {
        var resolver = new ConfiguredRoleResolver(Options.Create(new CommercialAdvisorOptions { CommercialAdvisorSubjects = ["advisor"] }),
            new IdentityDouble("budgets"), new IdentityDouble("head"), new IdentityDouble("management"), new IdentityDouble("accounting"));
        Assert.Equal(expected, Assert.Single(resolver.Resolve(subject)));
        Assert.Empty(resolver.Resolve(subject.ToUpperInvariant()));
        Assert.Empty(resolver.Resolve(" " + subject));
        Assert.Empty(resolver.Resolve("unknown"));
    }

    [Theory]
    [InlineData(" Budgets")]
    [InlineData("Budgets ")]
    [InlineData("Budgets, Management")]
    [InlineData("2")]
    public void Parser_DoesNotNormalizeHeader(string value)
    {
        var http = new DefaultHttpContext { User = new(new ClaimsIdentity([new Claim("sub", "budgets")], "test")) };
        http.Request.Headers[HttpActiveRoleContext.HeaderName] = value;
        var resolver = new ConfiguredRoleResolver(Options.Create(new CommercialAdvisorOptions()), new IdentityDouble("budgets"), new IdentityDouble("head"), new IdentityDouble("manager"), new IdentityDouble("accounting"));
        Assert.Null(new HttpActiveRoleContext(new HttpContextAccessor { HttpContext = http }, resolver).Role);
    }
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" advisor ")]
    [InlineData("duplicate")]
    public void AdvisorConfiguration_RejectsInvalidEntriesWithoutRevealingValues(string value)
    {
        var services = new ServiceCollection();
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        services.AddActiveRoles();
        services.Configure<CommercialAdvisorOptions>(options => options.CommercialAdvisorSubjects = value == "duplicate" ? [value, value] : [value]);
        using var provider = services.BuildServiceProvider();
        var error = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<CommercialAdvisorOptions>>().Value);
        Assert.Equal("Configure Authentication:CommercialAdvisorSubjects with distinct non-empty Subjects.", error.Message);
    }

}
