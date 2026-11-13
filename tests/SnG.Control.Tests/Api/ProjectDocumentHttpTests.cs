using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
using SnG.Control.Tests.TestDoubles;

namespace SnG.Control.Tests.Api;

public class ProjectDocumentHttpTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private Project Project(string owner = "google-test-subject")
    {
        var project = new Project(Guid.NewGuid(), "Documents", "City", ProductLine.Classic, owner);
        fixture.Projects.Items.Add(project); return project;
    }
    private HttpClient Client(string? mode)
    {
        var client = new HttpClient { BaseAddress = fixture.Http.BaseAddress };
        client.DefaultRequestHeaders.Add("X-SnG-Active-Role", mode == "valid" ? "CommercialHead" : mode == "budgets" ? "Budgets" : "CommercialAdvisor");
        if (mode is not null) client.DefaultRequestHeaders.Authorization = new(TestAuthenticationHandler.SchemeName, mode);
        return client;
    }
    [Theory]
    [InlineData("GET", "")]
    [InlineData("POST", "/batch")]
    [InlineData("GET", "/current.zip")]
    [InlineData("GET", "/initial-handoff.zip")]
    [InlineData("GET", "/00000000-0000-0000-0000-000000000001/file")]
    [InlineData("DELETE", "/00000000-0000-0000-0000-000000000001")]
    public async Task AllDocumentRoutes_RequireAuthentication(string method, string suffix)
    {
        using var client = Client(null);
        using var request = new HttpRequestMessage(new HttpMethod(method), $"/api/projects/{Guid.NewGuid()}/documents{suffix}");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
    [Fact]
    public async Task TruncatedMultipart_Is400AndLeavesNoVisibleBatch()
    {
        var project = Project();
        using var form = Batch(("first.bin", [1, 2, 3], null), ("second.bin", [4, 5], null));
        var bytes = await form.ReadAsByteArrayAsync();
        using var truncated = new ByteArrayContent(bytes[..^20]);
        truncated.Headers.ContentType = form.Headers.ContentType;
        using var response = await fixture.Http.PostAsync($"/api/projects/{project.Id}/documents/batch", truncated);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(fixture.Documents.Documents, d => d.ProjectId == project.Id);
    }
    internal static MultipartFormDataContent Batch(params (string Name, byte[] Bytes, Guid? Replaces)[] files)
    {
        var form = new MultipartFormDataContent();
        form.Add(JsonContent.Create(new { files = files.Select((f, i) => new { partName = $"file-{i}", replacesDocumentId = f.Replaces }) }), "manifest");
        for (var i = 0; i < files.Length; i++) form.Add(new ByteArrayContent(files[i].Bytes), $"file-{i}", files[i].Name);
        return form;
    }
    [Theory]
    [InlineData(null, 401)]
    [InlineData("other", 201)]
    [InlineData("valid", 403)]
    [InlineData("budgets", 403)]
    [InlineData("outsider", 403)]
    public async Task UploadAndSend_RequireAdvisorCreator(string? mode, int status)
    {
        var project = Project("other-google-subject"); using var client = Client(mode);
        using var batch = Batch(("planos.pdf", [1, 2, 3], null));
        using var response = await client.PostAsync($"/api/projects/{project.Id}/documents/batch", batch);
        Assert.Equal(status, (int)response.StatusCode);
        using var sent = await client.PostAsync($"/api/projects/{project.Id}/send-to-budgets", null);
        Assert.Equal(mode == "valid" ? 403 : status, (int)sent.StatusCode);
        Assert.Equal(status == 201 && mode != "valid" ? 1 : 0, fixture.Documents.Handoffs.Count(h => h.ProjectId == project.Id));
    }
    [Fact]
    public async Task EmptyProject_CannotBeSent_AndNoEffects()
    {
        var project = Project();
        using var response = await fixture.Http.PostAsync($"/api/projects/{project.Id}/send-to-budgets", null);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("DocumentsRequired", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.DoesNotContain(fixture.Tasks.Items, t => t.ProjectId == project.Id);
        Assert.DoesNotContain(fixture.Tasks.Notifications, n => n.ProjectId == project.Id);
        Assert.DoesNotContain(fixture.Documents.Handoffs, h => h.ProjectId == project.Id);
    }
    [Fact]
    public async Task FullDocumentFlow_PreservesSnapshotAndBytes_AndNotifiesOncePerBatch()
    {
        var project = Project("other-google-subject"); var root = $"/api/projects/{project.Id}";
        using var budgets = Client("budgets"); using var creator = Client("other");
        Assert.Equal(HttpStatusCode.Forbidden, (await budgets.GetAsync(root + "/documents")).StatusCode);
        using var firstBatch = Batch(("planos.pdf", [1, 2], null), ("planos.pdf", [3, 4], null));
        using var uploaded = await creator.PostAsync(root + "/documents/batch", firstBatch);
        Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);
        var original = fixture.Documents.Documents.Where(d => d.ProjectId == project.Id).ToArray();
        Assert.Equal(2, original.Length); Assert.All(original, d => Assert.DoesNotContain("planos", d.StorageKey));
        using var sent = await creator.PostAsync(root + "/send-to-budgets", null);
        Assert.Equal(HttpStatusCode.Created, sent.StatusCode);
        var task = fixture.Tasks.Items.Single(t => t.ProjectId == project.Id);
        Assert.Equal(ProjectTaskStatus.Pending, task.Status); Assert.Null(task.StartedAt); Assert.Null(task.DeadlineAt); Assert.Null(task.WasOverdue);
        var before = JsonSerializer.Serialize(task);
        var handoffIds = fixture.Documents.Items.Where(i => i.ProjectId == project.Id).Select(i => i.ProjectDocumentId).Order().ToArray();
        Assert.Equal(original.Select(d => d.Id).Order(), handoffIds);
        using var update = Batch(("planos.pdf", [9], original[0].Id), ("archivo.html", [7, 8], null));
        Assert.Equal(HttpStatusCode.Created, (await creator.PostAsync(root + "/documents/batch", update)).StatusCode);
        Assert.Equal(before, JsonSerializer.Serialize(task));
        var notice = Assert.Single(fixture.Tasks.Notifications, n => n.ProjectId == project.Id && n.Kind == NotificationKind.ProjectDocumentsUpdated);
        Assert.Equal("budgets-test-subject", notice.RecipientGoogleSubject); Assert.Equal(NotificationState.Informational, notice.State);
        Assert.Contains("agregó 1 documento y reemplazó 1 documento", notice.Message);
        Assert.Equal(handoffIds, fixture.Documents.Items.Where(i => i.ProjectId == project.Id).Select(i => i.ProjectDocumentId).Order());
        var list = await budgets.GetFromJsonAsync<JsonElement>(root + "/documents");
        Assert.False(list.GetProperty("canModify").GetBoolean()); Assert.False(list.GetProperty("canRemove").GetBoolean());
        Assert.Equal(4, list.GetProperty("documents").GetArrayLength());
        Assert.DoesNotContain("storageKey", list.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("googleSubject", list.GetRawText(), StringComparison.OrdinalIgnoreCase);
        using var historical = await budgets.GetAsync(root + $"/documents/{original[0].Id}/file");
        Assert.Equal(new byte[] { 1, 2 }, await historical.Content.ReadAsByteArrayAsync());
        Assert.Equal("attachment", historical.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("application/octet-stream", historical.Content.Headers.ContentType!.MediaType);
        foreach (var (route, count) in new[] { ("current.zip", 3), ("initial-handoff.zip", 2) })
        {
            using var download = await budgets.GetAsync(root + "/documents/" + route);
            Assert.Equal(HttpStatusCode.OK, download.StatusCode);
            using var zip = new ZipArchive(await download.Content.ReadAsStreamAsync());
            Assert.Equal(count, zip.Entries.Count); Assert.Equal(count, zip.Entries.Select(e => e.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.All(zip.Entries, e => { Assert.DoesNotContain("/", e.FullName); Assert.DoesNotContain("\\", e.FullName); });
            var contents = new List<string>();
            foreach (var entry in zip.Entries) { using var stream = entry.Open(); using var bytes = new MemoryStream(); await stream.CopyToAsync(bytes); contents.Add(Convert.ToHexString(bytes.ToArray())); }
            Assert.Equal(route == "current.zip" ? new[] { "0304", "0708", "09" } : new[] { "0102", "0304" }, contents.Order().ToArray());
        }
        Assert.Equal(HttpStatusCode.Conflict, (await creator.DeleteAsync(root + $"/documents/{original[1].Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await budgets.DeleteAsync(root + $"/documents/{original[1].Id}")).StatusCode);
        using var forbiddenBatch = Batch(("x", [1], null));
        Assert.Equal(HttpStatusCode.Forbidden, (await budgets.PostAsync(root + "/documents/batch", forbiddenBatch)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await creator.PostAsync(root + "/send-to-budgets", null)).StatusCode);
    }
    [Theory]
    [InlineData("missing-manifest")]
    [InlineData("missing-file")]
    [InlineData("extra-file")]
    [InlineData("duplicate-part")]
    [InlineData("duplicate-replacement")]
    [InlineData("zero")]
    [InlineData("bad-json")]
    [InlineData("large-manifest")]
    [InlineData("empty-file")]
    public async Task InvalidBatch_Is400AndLeavesNoDocuments(string kind)
    {
        var project = Project(); using var form = new MultipartFormDataContent();
        var replacement = Guid.NewGuid();
        var files = kind == "zero" ? Array.Empty<object>() : kind == "duplicate-part"
            ? [new { partName = "file-0" }, new { partName = "file-0" }]
            : kind == "duplicate-replacement" ? [new { partName = "file-0", replacesDocumentId = replacement }, new { partName = "file-1", replacesDocumentId = replacement }]
            : new object[] { new { partName = "file-0" } };
        if (kind != "missing-manifest") form.Add(new StringContent(kind == "bad-json" ? "{" : kind == "large-manifest" ? new string(' ', 17000) : JsonSerializer.Serialize(new { files }), Encoding.UTF8, "application/json"), "manifest");
        if (kind != "missing-file") form.Add(new ByteArrayContent(kind == "empty-file" ? [] : [1]), "file-0", "same.pdf");
        if (kind == "extra-file") form.Add(new ByteArrayContent([2]), "extra", "extra");
        using var response = await fixture.Http.PostAsync($"/api/projects/{project.Id}/documents/batch", form);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(fixture.Documents.Documents, d => d.ProjectId == project.Id);
        Assert.DoesNotContain(fixture.Tasks.Notifications, n => n.ProjectId == project.Id);
    }
    [Fact]
    public async Task RemovalAndLegacyAndCrossProjectAccess_AreExplicit()
    {
        var project = Project(); var root = $"/api/projects/{project.Id}/documents";
        using var batch = Batch(("dato.svg", [1], null));
        Assert.Equal(HttpStatusCode.Created, (await fixture.Http.PostAsync(root + "/batch", batch)).StatusCode);
        var document = fixture.Documents.Documents.Single(d => d.ProjectId == project.Id);
        var other = Project();
        Assert.Equal(HttpStatusCode.NotFound, (await fixture.Http.GetAsync($"/api/projects/{other.Id}/documents/{document.Id}/file")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await fixture.Http.DeleteAsync(root + $"/{document.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await fixture.Http.GetAsync(root + $"/{document.Id}/file")).StatusCode);
        Assert.Empty((await fixture.Http.GetFromJsonAsync<JsonElement>(root)).GetProperty("documents").EnumerateArray());
        fixture.Tasks.Items.Add(new(project.Id, OperationalArea.Budgets, ProjectTaskType.PrepareQuotation, "budgets-test-subject"));
        using var budgets = Client("budgets");
        Assert.Equal(HttpStatusCode.OK, (await budgets.GetAsync(root)).StatusCode);
        using var missing = await budgets.GetAsync(root + "/initial-handoff.zip");
        Assert.Equal(HttpStatusCode.Conflict, missing.StatusCode);
        Assert.Equal("InitialHandoffUnavailable", (await missing.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        using var update = Batch(("nuevo.bin", [2], null));
        Assert.Equal(HttpStatusCode.Created, (await fixture.Http.PostAsync(root + "/batch", update)).StatusCode);
        Assert.Null((await fixture.Http.GetFromJsonAsync<JsonElement>(root)).GetProperty("initialHandoff").Deserialize<object>());
        Assert.Equal("budgets-test-subject", fixture.Tasks.Notifications.Single(n => n.ProjectId == project.Id).RecipientGoogleSubject);
    }
}
