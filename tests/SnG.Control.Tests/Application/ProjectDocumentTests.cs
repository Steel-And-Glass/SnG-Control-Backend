using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SnG.Control.Api.Controllers;
using SnG.Control.Application.Features.ProjectDocuments;
using SnG.Control.Application.Files;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
using SnG.Control.Infrastructure.Files;
using SnG.Control.Tests.Fixtures;
using SnG.Control.Tests.TestDoubles;

namespace SnG.Control.Tests.Application;

public class ProjectDocumentTests
{
    [Theory]
    [InlineData(100_000_000, 1, null)]
    [InlineData(100_000_001, 1, "FileTooLarge")]
    [InlineData(100_000_000, 3, null)]
    [InlineData(75_000_001, 4, "BatchTooLarge")]
    public async Task Limits_CountActualStreamingBytes_AndRollbackWholeBatch(long length, int files, string? error)
    {
        var projects = new ProjectStoreDouble(); var tasks = new ProjectTaskStoreDouble();
        var project = new Project(Guid.NewGuid(), "Limits", "City", ProductLine.Classic, "creator"); projects.Items.Add(project);
        var database = new DocumentDatabaseDouble(projects, tasks); var storage = new CountingStorage();
        using var store = database.Store(storage, Access(), TestTiming.Value);
        var operation = store.UploadAsync(project.Id, Uploads(Enumerable.Range(0, files).Select(i => new DocumentUpload($"file-{i}", "same.bin", null, null, new GeneratedStream(length))).ToArray()), default);
        if (error is null)
        {
            Assert.Equal(files, (await operation).Documents.Count); Assert.Equal(files * length, database.Documents.Sum(d => d.SizeBytes));
        }
        else
        {
            var failure = await Assert.ThrowsAsync<DocumentException>(() => operation); Assert.Equal(413, failure.Status); Assert.Equal(error, failure.Code);
            Assert.Empty(database.Documents); Assert.Empty(storage.Keys); Assert.Empty(tasks.Notifications);
        }
        Assert.True(storage.MaxRead <= 81920);
    }
    [Theory]
    [InlineData("save", false)]
    [InlineData("storage", false)]
    [InlineData("commit", false)]
    [InlineData("commit", true)]
    [InlineData("cancel", false)]
    public async Task FailedBatch_CompensatesOrPreservesUncertainCommit(string failure, bool uncertain)
    {
        var projects = new ProjectStoreDouble(); var tasks = new ProjectTaskStoreDouble();
        var project = new Project(Guid.NewGuid(), "Failure", "City", ProductLine.Classic, "creator"); projects.Items.Add(project);
        var database = new DocumentDatabaseDouble(projects, tasks) { FailSave = failure == "save", FailCommit = failure == "commit", FailRollback = uncertain };
        var original = database.AddDocument(project.Id); var before = JsonSerializer.Serialize(original);
        var storage = new CountingStorage { FailAfterWrite = failure == "storage", Cancel = failure == "cancel" };
        storage.Keys.Add("unrelated");
        using var store = database.Store(storage, Access(), TestTiming.Value);
        await Assert.ThrowsAnyAsync<Exception>(() => store.UploadAsync(project.Id, Uploads(new DocumentUpload("file-0", "replacement", null, original.Id, new GeneratedStream(3))), default));
        Assert.Equal(before, JsonSerializer.Serialize(Assert.Single(database.Documents)));
        Assert.Empty(tasks.Notifications); Assert.Empty(database.Handoffs);
        Assert.Contains("unrelated", storage.Keys); Assert.Equal(uncertain ? 2 : 1, storage.Keys.Count);
        Assert.All(storage.DeleteTokens, t => Assert.False(t.IsCancellationRequested));
    }
    [Theory]
    [InlineData(3, 0, false)]
    [InlineData(0, 2, false)]
    [InlineData(2, 1, false)]
    [InlineData(2, 1, true)]
    public async Task Updates_GenerateOneEventPerBatch_WithoutMutatingTask(int additions, int replacements, bool completed)
    {
        var projects = new ProjectStoreDouble(); var tasks = new ProjectTaskStoreDouble();
        var project = new Project(Guid.NewGuid(), "Updates", "City", ProductLine.Classic, "creator"); projects.Items.Add(project);
        var task = new ProjectTask(project.Id, OperationalArea.Budgets, ProjectTaskType.PrepareQuotation, "budgets");
        task.Start(new(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc), new(2026, 10, 20, 22, 0, 0, DateTimeKind.Utc));
        if (completed) task.Complete(new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc));
        tasks.Items.Add(task); var before = JsonSerializer.Serialize(task);
        var database = new DocumentDatabaseDouble(projects, tasks); var storage = new CountingStorage();
        using var store = database.Store(storage, Access(), TestTiming.Value);
        for (var batch = 0; batch < 2; batch++)
        {
            var uploads = new List<DocumentUpload>();
            for (var i = 0; i < replacements; i++) uploads.Add(new($"replace-{i}", "same.bin", null, database.AddDocument(project.Id).Id, new GeneratedStream(3)));
            for (var i = 0; i < additions; i++) uploads.Add(new($"add-{i}", "same.bin", null, null, new GeneratedStream(3)));
            await store.UploadAsync(project.Id, Uploads(uploads.ToArray()), default);
        }
        Assert.Equal(completed ? 0 : 2, tasks.Notifications.Count); Assert.Equal(completed ? 0 : 2, tasks.Notifications.Select(n => n.EventKey).Distinct().Count());
        Assert.All(tasks.Notifications, n => { Assert.Equal(NotificationKind.ProjectDocumentsUpdated, n.Kind); Assert.Equal(NotificationState.Informational, n.State); Assert.Equal("budgets", n.RecipientGoogleSubject); Assert.DoesNotContain("budgets", n.EventKey); Assert.Equal("Documentación del proyecto actualizada", n.Title); });
        Assert.Equal(before, JsonSerializer.Serialize(task)); Assert.Single(tasks.Items); Assert.Empty(database.Handoffs);
    }
    [Fact]
    public async Task CurrentLimitAndReplacements_AreSerializedAndHistoricalRowsDoNotCount()
    {
        var projects = new ProjectStoreDouble(); var tasks = new ProjectTaskStoreDouble();
        var project = new Project(Guid.NewGuid(), "Limit", "City", ProductLine.Classic, "creator"); projects.Items.Add(project);
        var database = new DocumentDatabaseDouble(projects, tasks); var storage = new CountingStorage();
        for (var i = 0; i < 14; i++) database.AddDocument(project.Id);
        using var first = database.Store(storage, Access(), TestTiming.Value); using var second = database.Store(storage, Access(), TestTiming.Value);
        async Task<bool> Add(DocumentDatabaseDouble.DocumentStore store)
        {
            try { await store.UploadAsync(project.Id, Uploads(new DocumentUpload("file", "same", null, null, new GeneratedStream(1))), default); return true; }
            catch (DocumentException e) { Assert.Equal("DocumentLimitExceeded", e.Code); return false; }
        }
        Assert.Single(await Task.WhenAll(Add(first), Add(second)), success => success);
        var old = database.Documents[0];
        var replacement = await first.UploadAsync(project.Id, Uploads(new DocumentUpload("file", "same", null, old.Id, new GeneratedStream(1))), default);
        Assert.Equal(15, database.Documents.Count(d => d.IsCurrent)); Assert.Equal(16, database.Documents.Count);
        var historical = database.Documents.Single(d => d.Id == old.Id);
        Assert.False(historical.IsCurrent); Assert.Equal(replacement.Documents.Single().Id, historical.ReplacedByDocumentId);
        var invalid = await Assert.ThrowsAsync<DocumentException>(() => second.UploadAsync(project.Id, Uploads(new DocumentUpload("file", "same", null, old.Id, new GeneratedStream(1))), default));
        Assert.Equal("InvalidReplacement", invalid.Code);
        var foreign = database.AddDocument(Guid.NewGuid());
        Assert.Equal("InvalidReplacement", (await Assert.ThrowsAsync<DocumentException>(() => second.UploadAsync(project.Id, Uploads(new DocumentUpload("file", "same", null, foreign.Id, new GeneratedStream(1))), default))).Code);
    }
    [Theory]
    [InlineData("budgets")]
    [InlineData("outsider")]
    public async Task UploadAuthorization_RunsBeforeReadingBody(string actor)
    {
        using var directory = new TemporaryStorage();
        var projects = new ProjectStoreDouble(); var tasks = new ProjectTaskStoreDouble();
        var project = new Project(Guid.NewGuid(), "Private", "City", ProductLine.Classic, "creator"); projects.Items.Add(project);
        var database = new DocumentDatabaseDouble(projects, tasks);
        var storage = new LocalFileStorage(Options.Create(new FileStorageOptions { RootPath = directory.Root }));
        var access = Access(actor); using var store = database.Store(storage, access, TestTiming.Value);
        var service = new ProjectDocumentService(store, access, storage, new ProjectDocumentArchive(storage));
        var body = new GeneratedStream(300_000_000);
        var http = new DefaultHttpContext(); http.Request.Body = body; http.Request.ContentType = "multipart/form-data; boundary=test";
        var controller = new ProjectDocumentsController(service) { ControllerContext = new ControllerContext { HttpContext = http } };
        Assert.Equal(403, (await Assert.ThrowsAsync<DocumentException>(() => controller.Upload(project.Id, default))).Status);
        Assert.Equal(0, body.BytesRead); Assert.Empty(database.Documents);
    }
    [Theory]
    [InlineData("../x")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("C:x")]
    [InlineData("bad\0name")]
    [InlineData("bad\r\nname")]
    [InlineData("")]
    [InlineData("..")]
    public void UnsafeNames_AreRejected(string name) => Assert.Throws<ArgumentException>(() => ProjectDocument.ValidateFileName(name));
    [Fact]
    public void UnicodeName_IsMetadataAndNewKindCannotUseOldEventKey()
    {
        var doc = new ProjectDocument(Guid.NewGuid(), Guid.NewGuid(), "Planos Bogotá 日本.pdf", "text/html\r\nunsafe", 1, Guid.NewGuid(), DateTime.UtcNow, "creator", null, null);
        Assert.Null(doc.ContentType); Assert.DoesNotContain(doc.OriginalFileName, doc.StorageKey);
        var task = new ProjectTask(doc.ProjectId, OperationalArea.Budgets, ProjectTaskType.PrepareQuotation, "budgets");
        Assert.Throws<ArgumentException>(() => ProjectNotification.Key(task, NotificationKind.ProjectDocumentsUpdated));
        var id = Guid.NewGuid(); var at = DateTime.UtcNow;
        Assert.Equal(ProjectNotification.DocumentsUpdated(task, "budgets", id, 1, 0, at).EventKey, ProjectNotification.DocumentsUpdated(task, "budgets", id, 1, 0, at).EventKey);
    }
    internal static ProjectDocumentAccess Access(string actor = "creator") => new(new IdentityDouble(actor), new ActiveRoleDouble(actor == "head" ? SystemRole.CommercialHead : actor == "budgets" ? SystemRole.Budgets : actor == "management" ? SystemRole.Management : SystemRole.CommercialAdvisor), new IdentityDouble("budgets"));
    internal static async IAsyncEnumerable<DocumentUpload> Uploads(params DocumentUpload[] uploads)
    {
        foreach (var upload in uploads) { yield return upload; }
        await Task.CompletedTask;
    }
    private sealed class GeneratedStream(long length) : Stream
    {
        public long BytesRead { get; private set; }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested(); var count = (int)Math.Min(buffer.Length, length - BytesRead);
            buffer.Span[..count].Clear(); BytesRead += count; return ValueTask.FromResult(count);
        }
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
    private sealed class CountingStorage : IStreamingFileStorage
    {
        public HashSet<string> Keys { get; } = []; public List<CancellationToken> DeleteTokens { get; } = [];
        public bool FailAfterWrite { get; set; }
        public bool Cancel { get; set; }
        public int MaxRead { get; private set; }
        public async Task SaveFromStreamAsync(string key, Stream content, CancellationToken token)
        {
            var buffer = new byte[81920]; int count;
            while ((count = await content.ReadAsync(buffer, token)) > 0) MaxRead = Math.Max(MaxRead, count);
            Keys.Add(key);
            if (FailAfterWrite) throw new IOException("Synthetic storage failure after writing.");
            if (Cancel) throw new OperationCanceledException();
        }
        public Task DeleteAsync(string key, CancellationToken token) { DeleteTokens.Add(token); Keys.Remove(key); return Task.CompletedTask; }
        public Task<Stream> OpenReadAsync(string key, CancellationToken token) => throw new NotSupportedException();
        public Task<byte[]> ReadAsync(string key, CancellationToken token) => throw new NotSupportedException();
        public Task SaveAsync(string key, ReadOnlyMemory<byte> bytes, CancellationToken token) => throw new NotSupportedException();
    }
}
