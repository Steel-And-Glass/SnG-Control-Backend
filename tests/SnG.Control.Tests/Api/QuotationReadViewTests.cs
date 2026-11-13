using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SnG.Control.Api.Authorization;
using SnG.Control.Application.Features.QuotationViews;
using SnG.Control.Application.Features.CommercialQuotations;
using SnG.Control.Application.Files;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
using SnG.Control.Domain.ValueObjects;

namespace SnG.Control.Tests.Api;

public sealed class QuotationReadFixture : ApiFixture
{
    public QuotationReadDouble ReadStore { get; } = new();
    public ImageStorageDouble Images { get; } = new();
    public QuotationReadFixture() => ConfigureServices = services =>
    {
        services.Configure<BudgetsAssigneeOptions>(o => o.BudgetsAssigneeSubject = "google-test-subject");
        services.Configure<ManagementOptions>(o => o.ManagementSubject = "google-test-subject");
        services.RemoveAll<IQuotationReadStore>(); services.AddSingleton<IQuotationReadStore>(ReadStore);
        services.RemoveAll<IStreamingFileStorage>(); services.AddSingleton<IStreamingFileStorage>(Images);
    };
}

public sealed class QuotationReadViewTests(QuotationReadFixture fixture) : IClassFixture<QuotationReadFixture>
{
    private HttpClient Client(string? role, string identity = "valid")
    {
        var client = new HttpClient { BaseAddress = fixture.Http.BaseAddress };
        client.DefaultRequestHeaders.Authorization = new(TestAuthenticationHandler.SchemeName, identity);
        if (role is not null) client.DefaultRequestHeaders.Add(HttpActiveRoleContext.HeaderName, role);
        return client;
    }
    [Fact]
    public async Task OriginalEconomicsAreReadOnlyScopedAndFollowTheReturnedVersionAfterReload()
    {
        var store = fixture.ReadStore;
        var originalVersion = store.VersionId;
        var first = new SnG.Control.Tests.Fixtures.QuotationWorkbook().Item(19)
            .Text("N30", "VALOR TOTAL").Number("O30", 987.654m).Text("A40", "ADICIONALES").Build();
        var second = new SnG.Control.Tests.Fixtures.QuotationWorkbook().Item(19)
            .Number("N20", 9.876m).Text("N30", "VALOR TOTAL").Number("O30", 222.22m).Text("A40", "ADICIONALES").Build();
        store.HasOriginal = true;
        fixture.Images.Originals[store.OriginalKey] = first;
        using var client = Client("Budgets");
        var path = $"/api/projects/{store.Project.Id}/budget-quotation/view";
        try
        {
            for (var reload = 0; reload < 2; reload++)
            {
                using var response = await client.GetAsync(path);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var economics = json.RootElement.GetProperty("economics");
                Assert.Equal("Success", economics.GetProperty("status").GetString());
                Assert.Equal("987.65", economics.GetProperty("projectTotal").GetString());
                Assert.Equal("123456.00", economics.GetProperty("items")[0].GetProperty("unitPrice").GetString());
            }
            store.VersionId = Guid.NewGuid();
            fixture.Images.Originals[store.OriginalKey] = second;
            using var newer = JsonDocument.Parse(await client.GetStringAsync(path));
            Assert.Equal(store.VersionId, newer.RootElement.GetProperty("quotationVersionId").GetGuid());
            Assert.Equal("222.22", newer.RootElement.GetProperty("economics").GetProperty("projectTotal").GetString());
            Assert.Equal("9.88", newer.RootElement.GetProperty("economics").GetProperty("items")[0].GetProperty("unitPrice").GetString());
            Assert.Equal(first, fixture.Images.Originals[$"synthetic/{originalVersion}/original.xlsx"]);
            Assert.Null(await store.GetBudgetOriginalKeyAsync(Guid.NewGuid(), store.VersionId, default));
            Assert.Null(await store.GetBudgetOriginalKeyAsync(store.Project.Id, Guid.NewGuid(), default));
            using var outsider = Client("CommercialAdvisor", "other");
            var opens = fixture.Images.OpenCount;
            Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync(path)).StatusCode);
            Assert.Equal(opens, fixture.Images.OpenCount);
            fixture.Images.Missing = true;
            using var missing = JsonDocument.Parse(await client.GetStringAsync(path));
            Assert.Equal("OriginalUnavailable", missing.RootElement.GetProperty("economics").GetProperty("status").GetString());
        }
        finally
        {
            store.HasOriginal = false; store.VersionId = originalVersion;
            fixture.Images.Originals.Clear(); fixture.Images.Missing = false;
        }
    }

    [Theory]
    [InlineData("CommercialAdvisor", "budget", 200)]
    [InlineData("Budgets", "budget", 200)]
    [InlineData("CommercialHead", "budget", 200)]
    [InlineData("Management", "budget", 200)]
    [InlineData("CommercialAdvisor", "commercial", 200)]
    [InlineData("CommercialHead", "commercial", 200)]
    [InlineData("Management", "commercial", 200)]
    [InlineData("Budgets", "commercial", 403)]
    [InlineData("Accounting", "budget", 403)]
    [InlineData("1", "budget", 403)]
    [InlineData(null, "commercial", 403)]
    public async Task SameSubject_ReadMatrix(string? role, string kind, int status)
    {
        using var client = Client(role);
        var response = await client.GetAsync($"/api/projects/{fixture.ReadStore.Project.Id}/{kind}-quotation/view");
        Assert.Equal(status, (int)response.StatusCode);
        if (status == 200)
        {
            Assert.True(response.Headers.CacheControl?.NoStore);
            using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (kind == "commercial")
            {
                Assert.Equal("COP", payload.RootElement.GetProperty("currencyCode").GetString());
                Assert.Equal(98.12m, payload.RootElement.GetProperty("projectTotal").GetDecimal());
            }
            else Assert.False(payload.RootElement.TryGetProperty("currencyCode", out _));
            var json = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("storageKey", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("base64", json, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(fixture.ReadStore.Project.Id, JsonDocument.Parse(json).RootElement.GetProperty("projectId").GetGuid());
        }
        if (role == "CommercialHead")
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/projects/{fixture.ReadStore.Project.Id}/sg-code", new { sgCode = "SG2" })).StatusCode);
    }
    [Theory]
    [InlineData("budget")]
    [InlineData("commercial")]
    public async Task OutsiderOwnerAndMissingQuotationAreHandled(string kind)
    {
        using var outsider = Client("CommercialAdvisor", "other");
        var root = $"/api/projects/{fixture.ReadStore.Project.Id}/{kind}-quotation/view";
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync(root)).StatusCode);
        using var owner = Client("CommercialAdvisor");
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/projects/{Guid.NewGuid()}/{kind}-quotation/view")).StatusCode);
        fixture.ReadStore.HasQuotation = false;
        try { Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(root)).StatusCode); }
        finally { fixture.ReadStore.HasQuotation = true; }
    }
    [Theory]
    [InlineData("Budgets")]
    [InlineData("CommercialHead")]
    public async Task MembershipWithoutAssignedTaskDoesNotGrantRead(string role)
    {
        using var client = Client(role);
        fixture.ReadStore.HasTasks = false;
        try { Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/projects/{fixture.ReadStore.Project.Id}/budget-quotation/view")).StatusCode); }
        finally { fixture.ReadStore.HasTasks = true; }
    }
    [Theory]
    [InlineData("CommercialAdvisor")]
    [InlineData("Budgets")]
    [InlineData("CommercialHead")]
    [InlineData("Management")]
    public async Task ImageIsScopedToProjectItemAndRole(string role)
    {
        using var client = Client(role);
        var root = $"/api/projects/{fixture.ReadStore.Project.Id}/budget-quotation/items";
        var response = await client.GetAsync($"{root}/{fixture.ReadStore.ItemId}/image");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(new byte[] { 1, 2, 3 }, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{root}/{Guid.NewGuid()}/image")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/projects/{Guid.NewGuid()}/budget-quotation/items/{fixture.ReadStore.ItemId}/image")).StatusCode);
        using var outsider = Client("CommercialAdvisor", "other");
        var opens = fixture.Images.OpenCount;
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync($"{root}/{fixture.ReadStore.ItemId}/image")).StatusCode);
        Assert.Equal(opens, fixture.Images.OpenCount);
    }
    [Fact]
    public async Task MissingImageAndUnsafeContentTypeFailWithoutLeakingPaths()
    {
        using var client = Client("Management");
        var path = $"/api/projects/{fixture.ReadStore.Project.Id}/budget-quotation/items/{fixture.ReadStore.ItemId}/image";
        fixture.Images.Missing = true;
        try { Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path)).StatusCode); }
        finally { fixture.Images.Missing = false; }
        fixture.ReadStore.ImageType = "image/svg+xml";
        var opens = fixture.Images.OpenCount;
        try { Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path)).StatusCode); }
        finally { fixture.ReadStore.ImageType = "image/png"; }
        Assert.Equal(opens, fixture.Images.OpenCount);
    }
}

public sealed class QuotationReadDouble : IQuotationReadStore
{
    public Project Project { get; } = new(Guid.NewGuid(), "Synthetic", "City", ProductLine.Classic, "google-test-subject");
    public Guid ItemId { get; } = Guid.NewGuid();
    public Guid VersionId { get; set; } = Guid.NewGuid();
    public bool HasOriginal { get; set; }
    public string OriginalKey => $"synthetic/{VersionId}/original.xlsx";
    public Task<string?> GetBudgetOriginalKeyAsync(Guid id, Guid versionId, CancellationToken token)
        => Task.FromResult(HasOriginal && id == Project.Id && versionId == VersionId ? OriginalKey : null);
    public bool HasQuotation { get; set; } = true;
    public bool HasTasks { get; set; } = true;
    public string ImageType { get; set; } = "image/png";
    private static readonly CatalogItemValues Values = new("V1", "Room", "System", "Glass", "Finish", "Lock", "Notes", 2, 3, 6, 2);
    public Task<QuotationReadAccess?> GetAccessAsync(Guid id, CancellationToken token) => Task.FromResult(id != Project.Id ? null : new QuotationReadAccess(Project,
        HasTasks ? [new(Project.Id, OperationalArea.Budgets, ProjectTaskType.PrepareQuotation, "google-test-subject"),
            new(Project.Id, OperationalArea.Commercial, ProjectTaskType.ReviewQuotation, "google-test-subject")] : []));
    public Task<ConfirmedBudgetQuotationView?> GetBudgetAsync(Guid id, CancellationToken token) => Task.FromResult(!HasQuotation ? null : new ConfirmedBudgetQuotationView(id, VersionId, 1,
        new(2026, 10, 20, 15, 0, 0, DateTimeKind.Utc), new("synthetic.xlsx", "Client", "Project", "City", "ClientLine", "Production", "Author", "SG9001"),
        [new(ItemId, CommercialItemSection.Main, 20, Values, Values, CatalogItemImageStatus.Stored, true, [])]));
    public Task<ConfirmedCommercialQuotationView?> GetCommercialAsync(Guid id, CancellationToken token) => Task.FromResult(!HasQuotation ? null : new ConfirmedCommercialQuotationView(id, Guid.NewGuid(),
        new(2026, 10, 21, 15, 0, 0, DateTimeKind.Utc), 98.12m, [new(CommercialItemSection.Main, 20, ItemId, Values, 1.23m, 42.57m, [], CatalogItemImageStatus.Stored, true)]));
    public Task<QuotationImageFile?> GetBudgetImageAsync(Guid id, Guid item, CancellationToken token) => Task.FromResult(id == Project.Id && item == ItemId ? new QuotationImageFile("synthetic/image.png", ImageType) : null);
}
public sealed class ImageStorageDouble : IStreamingFileStorage
{
    public Dictionary<string, byte[]> Originals { get; } = [];
    public int OpenCount { get; private set; }
    public bool Missing { get; set; }
    public Task<Stream> OpenReadAsync(string key, CancellationToken token)
    {
        OpenCount++;
        if (Missing) throw new FileNotFoundException();
        if (Originals.TryGetValue(key, out var bytes)) return Task.FromResult<Stream>(new MemoryStream(bytes, false));
        Assert.Equal("synthetic/image.png", key);
        return Task.FromResult<Stream>(new MemoryStream([1, 2, 3]));
    }
    public Task SaveFromStreamAsync(string key, Stream content, CancellationToken token) => throw new NotSupportedException();
    public Task SaveAsync(string key, ReadOnlyMemory<byte> content, CancellationToken token) => throw new NotSupportedException();
    public Task<byte[]> ReadAsync(string key, CancellationToken token) => throw new NotSupportedException();
    public Task DeleteAsync(string key, CancellationToken token) => throw new NotSupportedException();
}

