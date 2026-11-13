using SnG.Control.Application.Access;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using SnG.Control.Application.Features.Budgets.CommercialHandoff;
using SnG.Control.Application.Features.CommercialQuotations;
using SnG.Control.Application.Files;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
using SnG.Control.Infrastructure.Files;
using SnG.Control.Infrastructure.Persistence;
using SnG.Control.Infrastructure.Persistence.Stores;
using SnG.Control.Infrastructure.Quotations;
using SnG.Control.Tests.TestDoubles;

namespace SnG.Control.Tests.Fixtures;

public sealed class CommercialQuotationScenario : ICommercialQuotationStore, IFileStorage, IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public TemporaryStorage Directory { get; } = new();
    public LocalFileStorage Storage { get; }
    public CommercialQuotationState? State { get; set; }
    public List<CommercialQuotationItem> Items { get; } = [];
    public int SaveCalls { get; private set; }
    public int DeleteCalls { get; private set; }
    public int ConfirmCalls { get; private set; }
    public byte[] Bytes { get; private set; } = [];
    public static XlsxCommercialQuotationParser Parser() => new(new XlsxBudgetQuotationParser(Options.Create(new BudgetQuotationParserOptions())), Options.Create(new BudgetQuotationParserOptions()));
    public static QuotationWorkbook Workbook(int totalRow = 30) => new QuotationWorkbook().Item().Text($"N{totalRow}", " VALOR TOTAL ").Number($"O{totalRow}", 123.4567m)
        .Item(totalRow + 10, "02", additional: true).Text($"N{totalRow + 20}", "VALOR TOTAL").Number($"O{totalRow + 20}", 999m);
    public CommercialQuotationScenario() => Storage = new(Options.Create(new FileStorageOptions { RootPath = Directory.Root }));
    public async Task InitializeAsync()
    {
        var handoff = await CommercialHandoffScenario.CreateAsync(Workbook().Build());
        await new SendBudgetQuotationToCommercialUseCase(handoff, handoff, CommercialHandoffScenario.Generator(), new IdentityDouble(), new IdentityDouble("budgets-test-subject")).ExecuteAsync(handoff.State!.Project.Id, default);
        var old = handoff.State!;
        typeof(Project).GetProperty(nameof(Project.CreatedByGoogleSubject))!.SetValue(old.Project, "other-google-subject");
        State = new(old.Project, old.Tasks, old.Version, old.Catalog, old.Items, old.Extractions, null);
        foreach (var file in handoff.Files) await Storage.SaveAsync(file.Key, file.Value, default);
        Bytes = Workbook().Number("N15", 7.125m).Number("O15", 99.9876m).Text("D15", "Changed glass").Build();
    }
    public PreviewCommercialQuotationUseCase Preview() => new(this, Parser(), this, new IdentityDouble());
    public ConfirmCommercialQuotationUseCase Confirm(IdentityDouble? user = null) => new(Preview(), this, new IdentityDouble(), user ?? new IdentityDouble());
    public Task<CommercialQuotationState?> LoadAsync(Guid id, CancellationToken token) => Task.FromResult(State?.Project.Id == id ? State : null);
    public Task<CommercialQuotationFileState?> GetFileAsync(Guid id, ProjectReadScope scope, CancellationToken token) => Task.FromResult(State?.Project.Id == id && scope.CanRead(State.Project, State.Tasks) ? new CommercialQuotationFileState(State.Project, State.Quotation) : null);
    public async Task<CommercialQuotationConfirmationResult> ConfirmAsync(CommercialQuotationState expected, string headSubject, CommercialQuotation quotation,
        IReadOnlyList<CommercialQuotationItem> items, byte[] bytes, CommercialQuotationPreviewResponse preview, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            ConfirmCalls++;
            var status = State!.Validate(headSubject, true);
            if (status != CommercialQuotationStatus.Success) return new(status);
            using var db = new SaveContext(); using var transaction = new TransactionDouble();
            var result = await new EfCommercialQuotationStore(db, this, TestTiming.Value, new NotificationWriterDouble(db)).PersistAsync(State, quotation, items, bytes, preview, transaction, token);
            if (result.Status == CommercialQuotationStatus.Success)
            {
                var present = db.ChangeTracker.Entries<ProjectTask>().Single(e => e.Entity.Type == ProjectTaskType.PresentQuotationToClient).Entity;
                State = State with { Quotation = quotation, Tasks = State.Tasks.Append(present).ToArray() }; Items.AddRange(items);
            }
            return result;
        }
        finally { gate.Release(); }
    }
    public Task<byte[]> ReadAsync(string key, CancellationToken token) => Storage.ReadAsync(key, token);
    public Task SaveAsync(string key, ReadOnlyMemory<byte> bytes, CancellationToken token) { SaveCalls++; return Storage.SaveAsync(key, bytes, token); }
    public Task DeleteAsync(string key, CancellationToken token) { DeleteCalls++; return Storage.DeleteAsync(key, token); }
    public void Dispose() { Directory.Dispose(); gate.Dispose(); }

    public sealed class SaveContext() : SnGControlDbContext(new DbContextOptionsBuilder<SnGControlDbContext>().UseNpgsql().Options)
    {
        public Exception? Failure { get; init; }
        public int Calls { get; private set; }
        public override Task<int> SaveChangesAsync(CancellationToken token = default)
        { Calls++; if (Failure is not null) throw Failure; var count = ChangeTracker.Entries().Count(); ChangeTracker.AcceptAllChanges(); return Task.FromResult(count); }
    }
    public sealed class TransactionDouble : IDbContextTransaction
    {
        public Guid TransactionId { get; } = Guid.NewGuid();
        public bool FailCommit { get; init; }
        public bool FailRollback { get; init; }
        public bool Committed { get; private set; }
        public bool RolledBack { get; private set; }
        public void Commit() { if (FailCommit) throw new IOException("Synthetic commit failure"); Committed = true; }
        public Task CommitAsync(CancellationToken token = default) { Commit(); return Task.CompletedTask; }
        public void Rollback() { if (FailRollback) throw new IOException("Synthetic rollback failure"); RolledBack = true; }
        public Task RollbackAsync(CancellationToken token = default) { Rollback(); return Task.CompletedTask; }
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
