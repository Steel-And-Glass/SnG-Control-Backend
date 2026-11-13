using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SnG.Control.Application.Features.ProjectDocuments;
using SnG.Control.Application.Files;
using SnG.Control.Application.Scheduling;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
using SnG.Control.Infrastructure.Persistence;
using SnG.Control.Infrastructure.Persistence.Stores;

namespace SnG.Control.Tests.TestDoubles;

// Uses the production mutation/compensation code. Only SQL loading/commit are replaced.
public sealed class DocumentDatabaseDouble(ProjectStoreDouble projects, ProjectTaskStoreDouble tasks)
{
    public List<ProjectDocument> Documents { get; } = [];
    public List<ProjectDocumentHandoff> Handoffs { get; } = [];
    public List<ProjectDocumentHandoffItem> Items { get; } = [];
    public List<CancellationToken> LoadTokens { get; } = [];
    public int Saves { get; private set; }
    public bool FailSave { get; set; }
    public bool FailCommit { get; set; }
    public bool FailRollback { get; set; }
    private readonly SemaphoreSlim gate = new(1, 1);
    public DocumentStore Store(IStreamingFileStorage storage, ProjectDocumentAccess access, TaskTiming timing) => new(this, storage, access, timing);
    public ProjectDocument AddDocument(Guid projectId, DateTime? at = null)
    {
        var document = new ProjectDocument(Guid.NewGuid(), projectId, "synthetic.pdf", null, 3, Guid.NewGuid(),
            at ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), "google-test-subject", null, null);
        Documents.Add(document); return document;
    }
    private ProjectDocumentState? Load(Guid id, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); LoadTokens.Add(token);
        var project = projects.Items.SingleOrDefault(p => p.Id == id);
        return project is null ? null : new(project, Documents.Where(d => d.ProjectId == id).Select(Clone).ToArray(),
            tasks.Items.SingleOrDefault(t => t.ProjectId == id && t.Type == ProjectTaskType.PrepareQuotation),
            Handoffs.SingleOrDefault(h => h.ProjectId == id), Items.Where(i => i.ProjectId == id).ToArray(),
            tasks.Items.SingleOrDefault(t => t.ProjectId == id && t.Type == ProjectTaskType.ReviewQuotation));
    }
    private static T Clone<T>(T entity) where T : class =>
        (T)typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(entity, null)!;

    public sealed class DocumentStore : EfProjectDocumentStore, IDisposable
    {
        private readonly DocumentDatabaseDouble database;
        private readonly SaveContext context;
        internal DocumentStore(DocumentDatabaseDouble database, IStreamingFileStorage storage, ProjectDocumentAccess access, TaskTiming timing)
            : this(database, new SaveContext(database), storage, access, timing) { }
        private DocumentStore(DocumentDatabaseDouble database, SaveContext context, IStreamingFileStorage storage, ProjectDocumentAccess access, TaskTiming timing)
            : base(context, storage, access, timing) { this.database = database; this.context = context; }
        public override Task<ProjectDocumentState?> LoadAsync(Guid id, CancellationToken token) => Task.FromResult(database.Load(id, token));
        protected override Task<ProjectDocumentState?> LoadLockedAsync(Guid id, CancellationToken token) => LoadAsync(id, token);
        protected override async Task<IDbContextTransaction> BeginAsync(CancellationToken token)
        {
            await database.gate.WaitAsync(token);
            context.ChangeTracker.Clear();
            return new Transaction(database, context);
        }
        public void Dispose() => context.Dispose();
    }
    private sealed class SaveContext(DocumentDatabaseDouble database)
        : SnGControlDbContext(new DbContextOptionsBuilder<SnGControlDbContext>().UseNpgsql().Options)
    {
        public override Task<int> SaveChangesAsync(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested(); database.Saves++;
            if (database.FailSave) throw new DbUpdateException("Synthetic document save failure.");
            ChangeTracker.DetectChanges(); return Task.FromResult(1);
        }
    }
    private sealed class Transaction(DocumentDatabaseDouble database, SaveContext context) : IDbContextTransaction
    {
        private bool disposed;
        public Guid TransactionId { get; } = Guid.NewGuid();
        public void Commit()
        {
            if (database.FailCommit) throw new IOException("Synthetic commit failure.");
            foreach (var entry in context.ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified))
            {
                switch (entry.Entity)
                {
                    case ProjectDocument document:
                        database.Documents.RemoveAll(d => d.Id == document.Id); database.Documents.Add(Clone(document)); break;
                    case ProjectDocumentHandoff handoff: database.Handoffs.Add(handoff); break;
                    case ProjectDocumentHandoffItem item: database.Items.Add(item); break;
                    case ProjectTask task: tasksOf(database).Add(task); break;
                    case ProjectNotification notice: noticesOf(database).Add(notice); break;
                }
            }
        }
        private static List<ProjectTask> tasksOf(DocumentDatabaseDouble value) => value.TaskItems;
        private static List<ProjectNotification> noticesOf(DocumentDatabaseDouble value) => value.Notices;
        public Task CommitAsync(CancellationToken token = default) { token.ThrowIfCancellationRequested(); Commit(); return Task.CompletedTask; }
        public void Rollback() { if (database.FailRollback) throw new IOException("Synthetic rollback failure."); }
        public Task RollbackAsync(CancellationToken token = default) { Rollback(); return Task.CompletedTask; }
        public void Dispose() { if (!disposed) { disposed = true; database.gate.Release(); } }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
    private List<ProjectTask> TaskItems => tasks.Items;
    private List<ProjectNotification> Notices => tasks.Notifications;
}
