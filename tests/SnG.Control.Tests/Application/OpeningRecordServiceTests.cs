using SnG.Control.Application.Access;
using SnG.Control.Application.Features.ClientQuotationDecisions;
using SnG.Control.Application.Features.Engineering;
using SnG.Control.Application.Features.ProjectDocuments;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
using SnG.Control.Tests.TestDoubles;

namespace SnG.Control.Tests.Application;

public sealed class OpeningRecordServiceTests
{
    private static (OpeningRecordService Service, RecordingOpeningStore Store) Create(SystemRole? role, string subject = "google-test-subject")
    {
        var store = new RecordingOpeningStore();
        return (new OpeningRecordService(store, new IdentityDouble(subject, "u@example.com", "Usuario"), new ActiveRoleDouble(role)), store);
    }

    private static async IAsyncEnumerable<DocumentUpload> NoFiles() { await Task.CompletedTask; yield break; }

    private static async Task<DocumentException> Fails(Func<Task> action) => await Assert.ThrowsAsync<DocumentException>(action);

    [Theory]
    [InlineData("Present", SystemRole.CommercialAdvisor)]
    [InlineData("ConfirmStructure", SystemRole.CommercialAdvisor)]
    [InlineData("RequestChanges", SystemRole.CommercialAdvisor)]
    [InlineData("Accept", SystemRole.CommercialAdvisor)]
    [InlineData("DeclareAffected", SystemRole.Engineering)]
    [InlineData("ReviewCheck", SystemRole.Engineering)]
    [InlineData("CloseReview", SystemRole.Engineering)]
    public async Task EachActionBelongsToExactlyOneArea(string action, SystemRole owner)
    {
        var (allowed, store) = Create(owner);
        var project = Guid.NewGuid(); var round = Guid.NewGuid();
        await allowed.CommandAsync(project, round, new(Guid.NewGuid(), 3, action), default);
        var call = Assert.Single(store.Commands);
        Assert.Equal((project, round, action, owner, "google-test-subject", "Usuario"), (call.Project, call.Round, call.Command.Action, call.Role, call.Actor.Subject, call.Actor.Name));

        var other = owner == SystemRole.Engineering ? SystemRole.CommercialAdvisor : SystemRole.Engineering;
        foreach (var role in new SystemRole?[] { other, SystemRole.Management, SystemRole.Accounting, SystemRole.Installation, SystemRole.CommercialHead, null })
        {
            var (denied, deniedStore) = Create(role);
            var error = await Fails(() => denied.CommandAsync(project, round, new(Guid.NewGuid(), 0, action), default));
            Assert.Equal((403, "OpeningAccessDenied"), (error.Status, error.Code));
            Assert.Empty(deniedStore.Commands);
        }
    }

    public static TheoryData<OpeningCommand> InvalidCommands() => new()
    {
        new(Guid.Empty, 0, "Present"),
        new(Guid.NewGuid(), -1, "Present"),
        new(Guid.NewGuid(), 0, "present"),
        new(Guid.NewGuid(), 0, ""),
        new(Guid.NewGuid(), 0, "RequestChanges", Observations: new string('x', 4001)),
        new(Guid.NewGuid(), 0, "RequestChanges", Changes: [null!]),
        new(Guid.NewGuid(), 0, "RequestChanges", Changes: [new(Guid.Empty, "Cambio")]),
        new(Guid.NewGuid(), 0, "RequestChanges", Changes: [new(Guid.NewGuid(), new string('x', 4001))]),
        new(Guid.NewGuid(), 0, "RequestChanges", Changes: Enumerable.Range(0, 10001).Select(_ => new OpeningChange(Guid.NewGuid(), "c")).ToArray()),
        new(Guid.NewGuid(), 0, "DeclareAffected", AffectedStructureIds: Enumerable.Range(0, 10001).Select(_ => Guid.NewGuid()).ToArray()),
    };

    [Theory]
    [MemberData(nameof(InvalidCommands))]
    public async Task InvalidCommandsFailBeforePersistence(OpeningCommand command)
    {
        var (service, store) = Create(SystemRole.CommercialAdvisor);
        var error = await Fails(() => service.CommandAsync(Guid.NewGuid(), Guid.NewGuid(), command, default));
        Assert.Equal((400, "InvalidOpeningCommand"), (error.Status, error.Code));
        Assert.Empty(store.Commands);
    }

    [Fact]
    public async Task BoundaryLengthsAreAccepted()
    {
        var (service, store) = Create(SystemRole.CommercialAdvisor);
        await service.CommandAsync(Guid.NewGuid(), Guid.NewGuid(), new(Guid.NewGuid(), 0, "RequestChanges",
            Changes: [new(Guid.NewGuid(), new string('x', 4000))], Observations: new string('y', 4000)), default);
        Assert.Single(store.Commands);
    }

    [Theory]
    [InlineData(OpeningDocumentKind.Revised, SystemRole.Engineering)]
    [InlineData(OpeningDocumentKind.Signed, SystemRole.CommercialAdvisor)]
    public async Task RevisedActaIsEngineeringAndSignedActaIsAdvisor(OpeningDocumentKind kind, SystemRole owner)
    {
        var (allowed, store) = Create(owner);
        var key = Guid.NewGuid();
        await allowed.UploadAsync(Guid.NewGuid(), Guid.NewGuid(), key, 2, kind, NoFiles(), default);
        var call = Assert.Single(store.Uploads);
        Assert.Equal((key, 2L, kind, owner), (call.Key, call.Revision, call.Kind, call.Role));

        var other = owner == SystemRole.Engineering ? SystemRole.CommercialAdvisor : SystemRole.Engineering;
        foreach (var role in new SystemRole?[] { other, SystemRole.Management, null })
        {
            var (denied, deniedStore) = Create(role);
            Assert.Equal(403, (await Fails(() => denied.UploadAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0, kind, NoFiles(), default))).Status);
            Assert.Empty(deniedStore.Uploads);
        }
    }

    [Fact]
    public async Task UploadRejectsInvalidKeyRevisionAndKindBeforePersistence()
    {
        var (service, store) = Create(SystemRole.Engineering);
        Assert.Equal(400, (await Fails(() => service.UploadAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, 0, OpeningDocumentKind.Revised, NoFiles(), default))).Status);
        Assert.Equal(400, (await Fails(() => service.UploadAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), -1, OpeningDocumentKind.Revised, NoFiles(), default))).Status);
        Assert.Equal(400, (await Fails(() => service.UploadAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0, (OpeningDocumentKind)7, NoFiles(), default))).Status);
        Assert.Empty(store.Uploads);
    }

    [Fact]
    public async Task HistoricalInitializationIsEngineeringOnlyWithBoundedReason()
    {
        var (service, store) = Create(SystemRole.Engineering);
        var checklist = Guid.NewGuid();
        await service.InitializeHistoricalAsync(Guid.NewGuid(), checklist, new string('r', 4000), default);
        Assert.Equal(checklist, Assert.Single(store.Historical).Checklist);

        foreach (var (id, reason) in new[] { (Guid.Empty, "Motivo"), (Guid.NewGuid(), " "), (Guid.NewGuid(), new string('r', 4001)) })
            Assert.Equal(400, (await Fails(() => service.InitializeHistoricalAsync(Guid.NewGuid(), id, reason, default))).Status);
        foreach (var role in new SystemRole?[] { SystemRole.CommercialAdvisor, SystemRole.Management, null })
            Assert.Equal(403, (await Fails(() => Create(role).Service.InitializeHistoricalAsync(Guid.NewGuid(), checklist, "Motivo", default))).Status);
        Assert.Single(store.Historical);
    }

    [Theory]
    [InlineData(SystemRole.Management)]
    [InlineData(SystemRole.Accounting)]
    [InlineData(null)]
    public async Task ReadsAndDownloadsPassTheActiveScopeWithoutWriteChecks(SystemRole? role)
    {
        var (service, store) = Create(role, "reader");
        await service.ReadAsync(Guid.NewGuid(), default);
        await service.DownloadAsync(Guid.NewGuid(), Guid.NewGuid(), default);
        Assert.Equal([new ProjectReadScope("reader", role), new ProjectReadScope("reader", role)], store.Scopes);
    }

    private sealed class RecordingOpeningStore : IOpeningRecordStore
    {
        public List<(Guid Project, Guid Round, OpeningCommand Command, DecisionActor Actor, SystemRole Role)> Commands { get; } = [];
        public List<(Guid Key, long Revision, OpeningDocumentKind Kind, SystemRole Role)> Uploads { get; } = [];
        public List<(Guid Project, Guid Checklist, string Reason)> Historical { get; } = [];
        public List<ProjectReadScope> Scopes { get; } = [];

        public Task<OpeningState?> ReadAsync(Guid projectId, ProjectReadScope scope, CancellationToken token) { Scopes.Add(scope); return Task.FromResult<OpeningState?>(null); }
        public Task CommandAsync(Guid projectId, Guid roundId, OpeningCommand command, DecisionActor actor, SystemRole role, CancellationToken token)
        { Commands.Add((projectId, roundId, command, actor, role)); return Task.CompletedTask; }
        public Task UploadAsync(Guid projectId, Guid roundId, Guid key, long revision, OpeningDocumentKind kind, IAsyncEnumerable<DocumentUpload> files, DecisionActor actor, SystemRole role, CancellationToken token)
        { Uploads.Add((key, revision, kind, role)); return Task.CompletedTask; }
        public Task<DocumentDownload> DownloadAsync(Guid projectId, Guid documentId, ProjectReadScope scope, CancellationToken token)
        { Scopes.Add(scope); return Task.FromResult<DocumentDownload>(null!); }
        public Task InitializeHistoricalAsync(Guid projectId, Guid checklistId, string reason, DecisionActor actor, CancellationToken token)
        { Historical.Add((projectId, checklistId, reason)); return Task.CompletedTask; }
    }
}
