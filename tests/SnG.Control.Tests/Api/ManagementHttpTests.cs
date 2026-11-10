using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SnG.Control.Application.Features.ProjectDocuments;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;

namespace SnG.Control.Tests.Api;

public class ManagementHttpTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private HttpClient Client(string? mode)
    {
        var client = new HttpClient { BaseAddress = fixture.Http.BaseAddress };
        client.DefaultRequestHeaders.Add("X-SnG-Active-Role", "Management");
        if (mode is not null) client.DefaultRequestHeaders.Authorization = new(TestAuthenticationHandler.SchemeName, mode);
        return client;
    }
    private Project Project()
    {
        var client = new Client("Management test client"); fixture.Clients.Items.Add(client);
        var project = new Project(client.Id, "Management test project", "City", ProductLine.Classic, "google-test-subject");
        fixture.Projects.Items.Add(project); return project;
    }
    [Theory]
    [InlineData(null, 401)]
    [InlineData("valid", 403)]
    [InlineData("other", 403)]
    [InlineData("budgets", 403)]
    [InlineData("management", 200)]
    [InlineData("management-other-email", 200)]
    public async Task BothEndpoints_RequireManagementSubject_RegardlessOfSameOrChangedEmail(string? mode, int expected)
    {
        using var client = Client(mode);
        var calls = fixture.Management.ListCalls;
        var portfolioCalls = fixture.Management.PortfolioCalls;
        foreach (var route in new[] { "overview", "tasks", "quotation-portfolio" })
        {
            using var response = await client.GetAsync("/api/management/" + route);
            Assert.Equal(expected, (int)response.StatusCode);
        }
        Assert.Equal(calls + (expected == 200 ? 2 : 0), fixture.Management.ListCalls);
        Assert.Equal(portfolioCalls + (expected == 200 ? 1 : 0), fixture.Management.PortfolioCalls);
    }
    [Theory]
    [InlineData("management", true, "test@example.com")]
    [InlineData("management-other-email", true, "changed@example.com")]
    [InlineData("valid", false, "test@example.com")]
    public async Task Me_KeepsProfileAndReportsExactIdentity(string mode, bool expected, string email)
    {
        using var client = Client(mode);
        var me = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.Equal(new[] { "subject", "email", "name", "isManagement", "roles" }, me.EnumerateObject().Select(p => p.Name));
        Assert.Equal(expected, me.GetProperty("isManagement").GetBoolean());
        Assert.Equal(expected ? "management-test-subject" : "google-test-subject", me.GetProperty("subject").GetString());
        Assert.Equal(email, me.GetProperty("email").GetString()); Assert.Equal("Test User", me.GetProperty("name").GetString());
    }
    [Fact]
    public async Task ManagementJson_HasContextStringEnumsAndNoSubjects_WithoutWrites()
    {
        var project = Project(); project.SetSgCode("SG005");
        var task = new ProjectTask(project.Id, OperationalArea.Budgets, ProjectTaskType.PrepareQuotation, "budgets-test-subject",
            new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc)); fixture.Tasks.Items.Add(task);
        using var client = Client("management");
        var before = JsonSerializer.Serialize(new { fixture.Projects.Items, Tasks = fixture.Tasks.Items, fixture.Tasks.Notifications });
        var rows = await client.GetFromJsonAsync<JsonElement>("/api/management/tasks");
        var row = Assert.Single(rows.EnumerateArray(), t => t.GetProperty("taskId").GetGuid() == task.Id);
        Assert.Equal(18, row.EnumerateObject().Count());
        Assert.Equal(project.Id, row.GetProperty("projectId").GetGuid()); Assert.Equal(project.ClientId, row.GetProperty("clientId").GetGuid());
        Assert.Equal(project.Name, row.GetProperty("projectName").GetString()); Assert.Equal("Management test client", row.GetProperty("clientName").GetString());
        foreach (var (key, value) in new[] { ("sgCode", "SG005"), ("city", "City"), ("productLine", "Classic"),
            ("area", "Budgets"), ("type", "PrepareQuotation"), ("status", "Pending"), ("leadTimeStatus", "NotStarted") })
            Assert.Equal(value, row.GetProperty(key).GetString());
        var overview = await client.GetFromJsonAsync<JsonElement>("/api/management/overview");
        Assert.Equal(new[] { "totalProjects", "tasks", "leadTime", "areas", "attention" }, overview.EnumerateObject().Select(p => p.Name));
        Assert.Equal(fixture.Projects.Items.Count, overview.GetProperty("totalProjects").GetInt32());
        Assert.DoesNotContain("subject", rows.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("subject", overview.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("storageKey", overview.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, JsonSerializer.Serialize(new { fixture.Projects.Items, Tasks = fixture.Tasks.Items, fixture.Tasks.Notifications }));
    }
    [Fact]
    public async Task Documents_AreReadableBeforeAndAfterHandoff_IncludingHistory_WithoutManagementWrites()
    {
        var project = Project(); var root = $"/api/projects/{project.Id}";
        using var manager = Client("management");
        using var batch = ProjectDocumentHttpTests.Batch(("original.pdf", [1, 2, 3], null));
        Assert.Equal(HttpStatusCode.Created, (await fixture.Http.PostAsync(root + "/documents/batch", batch)).StatusCode);
        var original = fixture.Documents.Documents.Single(d => d.ProjectId == project.Id);
        var saves = fixture.Documents.Saves;
        await AssertReads(false);
        using var deniedUpload = ProjectDocumentHttpTests.Batch(("denied.pdf", [8], null));
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsync(root + "/documents/batch", deniedUpload)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.DeleteAsync(root + $"/documents/{original.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsync(root + "/send-to-budgets", null)).StatusCode);
        Assert.Equal(saves, fixture.Documents.Saves);
        Assert.Equal(HttpStatusCode.Created, (await fixture.Http.PostAsync(root + "/send-to-budgets", null)).StatusCode);
        using var replacement = ProjectDocumentHttpTests.Batch(("replacement.pdf", [4, 5], original.Id));
        Assert.Equal(HttpStatusCode.Created, (await fixture.Http.PostAsync(root + "/documents/batch", replacement)).StatusCode);
        saves = fixture.Documents.Saves;
        var before = JsonSerializer.Serialize(new
        {
            fixture.Documents.Documents,
            fixture.Documents.Handoffs,
            fixture.Documents.Items,
            Tasks = fixture.Tasks.Items,
            fixture.Tasks.Notifications,
            Project = project
        });
        await AssertReads(true);
        using var forbidden = ProjectDocumentHttpTests.Batch(("forbidden.pdf", [9], original.Id));
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsync(root + "/documents/batch", forbidden)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.DeleteAsync(root + $"/documents/{original.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsync(root + "/send-to-budgets", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PutAsJsonAsync(root + "/sg-code", new { sgCode = "SG999" })).StatusCode);
        foreach (var suffix in new[] { "/budget-quotation/preview", "/budget-quotation/confirm", "/send-to-commercial", "/commercial-quotation/preview", "/commercial-quotation/confirm" })
            Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsync(root + suffix, null)).StatusCode);
        foreach (var route in new[] { "/api/projects", root, root + "/tasks", root + "/notifications" })
            Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync(route)).StatusCode);
        var personal = await manager.GetFromJsonAsync<JsonElement>("/api/notifications");
        Assert.Empty(personal.EnumerateArray());
        Assert.NotEmpty((await manager.GetFromJsonAsync<JsonElement>(root + "/notifications")).EnumerateArray());
        Assert.Equal(saves, fixture.Documents.Saves);
        Assert.Equal(before, JsonSerializer.Serialize(new
        {
            fixture.Documents.Documents,
            fixture.Documents.Handoffs,
            fixture.Documents.Items,
            Tasks = fixture.Tasks.Items,
            fixture.Tasks.Notifications,
            Project = project
        }));

        async Task AssertReads(bool sent)
        {
            var documents = await manager.GetFromJsonAsync<JsonElement>(root + "/documents");
            foreach (var flag in new[] { "canModify", "canRemove", "canSendToBudgets" }) Assert.False(documents.GetProperty(flag).GetBoolean());
            Assert.Equal(sent ? 2 : 1, documents.GetProperty("documents").GetArrayLength());
            using var file = await manager.GetAsync(root + $"/documents/{original.Id}/file");
            Assert.Equal(HttpStatusCode.OK, file.StatusCode); Assert.Equal(new byte[] { 1, 2, 3 }, await file.Content.ReadAsByteArrayAsync());
            foreach (var initial in new[] { false, true })
            {
                using var response = await manager.GetAsync(root + (initial ? "/documents/initial-handoff.zip" : "/documents/current.zip"));
                if (initial && !sent)
                {
                    Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
                    Assert.Equal("InitialHandoffUnavailable", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
                }
                else
                {
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                    using var zip = new ZipArchive(await response.Content.ReadAsStreamAsync());
                    using var content = Assert.Single(zip.Entries).Open(); using var bytes = new MemoryStream(); await content.CopyToAsync(bytes);
                    Assert.Equal(sent && !initial ? new byte[] { 4, 5 } : new byte[] { 1, 2, 3 }, bytes.ToArray());
                }
            }
        }
    }
    [Theory]
    [InlineData("creator", false)]
    [InlineData("head", false)]
    [InlineData("budgets", false)]
    public void ManagementActiveRole_RemainsReadOnlyDespiteOverlappingIdentity(string identity, bool canModify)
    {
        var project = new Project(Guid.NewGuid(), "Overlap", "City", ProductLine.Classic, "creator");
        var access = new ProjectDocumentAccess(new IdentityDouble(identity), new ActiveRoleDouble(SystemRole.Management), new IdentityDouble("budgets"));
        Assert.True(access.CanRead(new(project, [], null, null, [])));
        Assert.Equal(canModify, access.CanModify(project));
    }
    [Fact]
    public async Task OpenApi_DeclaresManagementOperationsAndBearerSecurity()
    {
        using var client = Client(null);
        var document = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        Assert.Equal("bearer", document.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer").GetProperty("scheme").GetString());
        Assert.True(document.GetProperty("security")[0].TryGetProperty("Bearer", out _));
        foreach (var route in new[] { "/api/management/overview", "/api/management/tasks", "/api/management/quotation-portfolio" })
        {
            var operation = document.GetProperty("paths").GetProperty(route).GetProperty("get");
            foreach (var status in new[] { "200", "401", "403" }) Assert.True(operation.GetProperty("responses").TryGetProperty(status, out _));
        }
        var quotationStatus = document.GetProperty("paths").GetProperty("/api/projects/{id}/budget-quotation/status").GetProperty("get");
        foreach (var status in new[] { "200", "401", "403", "404" }) Assert.True(quotationStatus.GetProperty("responses").TryGetProperty(status, out _));
        Assert.False(quotationStatus.TryGetProperty("requestBody", out _));
        foreach (var route in new[] { "/api/projects/{id}/budget-quotation/view", "/api/projects/{id}/commercial-quotation/view", "/api/projects/{id}/budget-quotation/items/{itemId}/image" })
        {
            var read = document.GetProperty("paths").GetProperty(route).GetProperty("get");
            foreach (var status in new[] { "200", "401", "403", "404" }) Assert.True(read.GetProperty("responses").TryGetProperty(status, out _));
            Assert.False(read.TryGetProperty("requestBody", out _));
        }
        Assert.True(document.GetProperty("paths").GetProperty("/api/projects/{id}/measurements/submissions/{submissionId}/report").TryGetProperty("get", out _));
        Assert.True(document.GetProperty("paths").GetProperty("/api/projects/{id}/measurements/submissions").TryGetProperty("post", out _));
        Assert.Equal(71, document.GetProperty("paths").EnumerateObject().Sum(p => p.Value.EnumerateObject().Count(o => o.Name is "get" or "post" or "put" or "delete")));
    }
}
