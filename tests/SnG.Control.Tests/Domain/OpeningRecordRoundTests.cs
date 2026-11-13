using SnG.Control.Domain.Entities;

namespace SnG.Control.Tests.Domain;

public sealed class OpeningRecordRoundTests
{
    private static readonly DateTime T0 = new(2026, 11, 2, 13, 0, 0, DateTimeKind.Utc);
    private static readonly string Hash = new('a', 64);

    private static EngineeringChecklist CompletedSource(DateTime? at = null)
    {
        var checklist = new EngineeringChecklist(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), T0.AddHours(-2));
        checklist.SetDocument(Guid.NewGuid());
        checklist.Complete(at ?? T0.AddHours(-1), "engineer", "{}");
        return checklist;
    }

    private static OpeningRecordRound NewRound(EngineeringChecklist? source = null)
        => new(source ?? CompletedSource(), 1, Guid.NewGuid(), Guid.NewGuid(), T0);

    private static OpeningRecordRound Presented()
    {
        var round = NewRound();
        round.Present(T0.AddMinutes(10), "advisor");
        return round;
    }

    private static OpeningRecordRound ChangesRequested()
    {
        var round = Presented();
        round.RequestChanges(T0.AddMinutes(20), "advisor", Guid.NewGuid(), "Ajustar vanos");
        return round;
    }

    private static string Code(Action action) => Assert.Throws<InvalidOperationException>(action).Message;

    [Fact]
    public void ConstructorCopiesCompletedSourceAndStartsReadyAtRevisionZero()
    {
        var source = CompletedSource();
        var task = Guid.NewGuid(); var document = Guid.NewGuid();
        var round = new OpeningRecordRound(source, 2, task, document, T0);

        Assert.Equal((source.ProjectId, source.Id, source.SubmissionId), (round.ProjectId, round.ChecklistId, round.SubmissionId));
        Assert.Equal((2, task, document, T0), (round.Number, round.PresentationTaskId, round.PresentedDocumentId, round.ReceivedAt));
        Assert.Equal(OpeningRoundStatus.Ready, round.Status);
        Assert.Equal(0, round.Revision);
        Assert.Null(round.PresentedAt); Assert.Null(round.RespondedAt); Assert.Null(round.FinalSnapshotJson);
    }

    [Fact]
    public void ConstructorRejectsIncompleteSourceInvalidNumberEmptyIdsAndNonUtc()
    {
        var open = new EngineeringChecklist(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), T0.AddHours(-1));
        var done = CompletedSource();
        Assert.Throws<ArgumentException>(() => new OpeningRecordRound(open, 1, Guid.NewGuid(), Guid.NewGuid(), T0));
        Assert.Throws<ArgumentException>(() => new OpeningRecordRound(done, 0, Guid.NewGuid(), Guid.NewGuid(), T0));
        Assert.Throws<ArgumentException>(() => new OpeningRecordRound(done, 1, Guid.Empty, Guid.NewGuid(), T0));
        Assert.Throws<ArgumentException>(() => new OpeningRecordRound(done, 1, Guid.NewGuid(), Guid.Empty, T0));
        Assert.Throws<ArgumentException>(() => new OpeningRecordRound(done, 1, Guid.NewGuid(), Guid.NewGuid(), DateTime.SpecifyKind(T0, DateTimeKind.Local)));
    }

    [Fact]
    public void PresentRecordsActorOnceAndRejectsTimeTravelAndRepeats()
    {
        var early = NewRound();
        Assert.Equal("OpeningRoundTimeConflict", Code(() => early.Present(T0.AddTicks(-1), "advisor")));
        Assert.Equal(OpeningRoundStatus.Ready, early.Status);
        Assert.Equal(0, early.Revision);
        Assert.Throws<ArgumentException>(() => early.Present(T0.AddMinutes(1), " "));
        Assert.Throws<ArgumentException>(() => early.Present(DateTime.SpecifyKind(T0.AddMinutes(1), DateTimeKind.Unspecified), "advisor"));

        var round = NewRound();
        round.Present(T0, "advisor");
        Assert.Equal((OpeningRoundStatus.Presented, T0, "advisor", 1L), (round.Status, round.PresentedAt, round.PresentedBySubject, round.Revision));
        Assert.Equal("OpeningRoundStateConflict", Code(() => round.Present(T0.AddMinutes(1), "advisor")));
        Assert.Equal(1, round.Revision);
    }

    [Fact]
    public void AcceptRequiresSignedActaForPresentedRoundAndIsTerminal()
    {
        var round = Presented();
        Assert.Equal("OpeningSignedActaRequired", Code(() => round.Accept(T0.AddMinutes(30), "advisor", Guid.NewGuid(), "{}")));
        Assert.Equal(OpeningRoundStatus.Presented, round.Status);

        var signed = Guid.NewGuid(); var filing = Guid.NewGuid();
        round.SetDocument(signed, OpeningDocumentKind.Signed);
        Assert.Equal("OpeningSignedActaRequired", Code(() => round.Accept(T0.AddMinutes(30), "advisor", Guid.Empty, "{}")));
        Assert.Throws<ArgumentException>(() => round.Accept(T0.AddMinutes(30), "advisor", filing, " "));
        Assert.Equal("OpeningRoundTimeConflict", Code(() => round.Accept(T0.AddMinutes(9), "advisor", filing, "{}")));
        var revision = round.Revision;

        round.Accept(T0.AddMinutes(30), "advisor", filing, "{\"final\":true}");
        Assert.Equal(OpeningRoundStatus.Accepted, round.Status);
        Assert.Equal((signed, filing, "{\"final\":true}"), (round.SignedDocumentId, round.FilingTaskId, round.FinalSnapshotJson));
        Assert.Equal((T0.AddMinutes(30), "advisor"), (round.RespondedAt, round.RespondedBySubject));
        Assert.Equal(revision + 1, round.Revision);

        Assert.Equal("OpeningRoundStateConflict", Code(() => round.RequestChanges(T0.AddMinutes(40), "advisor", Guid.NewGuid(), null)));
        Assert.Equal("OpeningRoundStateConflict", Code(() => round.SetDocument(Guid.NewGuid(), OpeningDocumentKind.Signed)));
        Assert.Equal(signed, round.SignedDocumentId);
    }

    [Fact]
    public void DocumentsAreAcceptedOnlyInTheirOwnPhase()
    {
        var ready = NewRound();
        Assert.Equal("OpeningRoundStateConflict", Code(() => ready.SetDocument(Guid.NewGuid(), OpeningDocumentKind.Signed)));
        Assert.Equal("OpeningRoundStateConflict", Code(() => ready.SetDocument(Guid.NewGuid(), OpeningDocumentKind.Revised)));

        var presented = Presented();
        Assert.Equal("OpeningRoundStateConflict", Code(() => presented.SetDocument(Guid.NewGuid(), OpeningDocumentKind.Revised)));
        Assert.Throws<ArgumentException>(() => presented.SetDocument(Guid.Empty, OpeningDocumentKind.Signed));
        Assert.Null(presented.RevisedDocumentId);

        var changes = ChangesRequested();
        Assert.Equal("OpeningRoundStateConflict", Code(() => changes.SetDocument(Guid.NewGuid(), OpeningDocumentKind.Signed)));
        Assert.Null(changes.SignedDocumentId);
    }

    [Fact]
    public void RequestChangesThenEngineeringReviewClosesAsRevisedWithRevisedActa()
    {
        var round = Presented();
        Assert.Throws<ArgumentException>(() => round.RequestChanges(T0.AddMinutes(20), "advisor", Guid.Empty, null));
        Assert.Equal("OpeningRoundTimeConflict", Code(() => round.RequestChanges(T0.AddMinutes(5), "advisor", Guid.NewGuid(), null)));
        Assert.Equal(OpeningRoundStatus.Presented, round.Status);

        var review = Guid.NewGuid();
        round.RequestChanges(T0.AddMinutes(20), "advisor", review, "Ajustar vanos");
        Assert.Equal((OpeningRoundStatus.ChangesRequested, review, "Ajustar vanos"), (round.Status, round.ReviewTaskId, round.GeneralObservations));

        round.DeclareAffected();
        Assert.True(round.AffectedDeclared);
        Assert.Equal("OpeningRevisedActaRequired", Code(() => round.CompleteReview(T0.AddMinutes(40), "engineer", "{}")));

        var revised = Guid.NewGuid();
        round.SetDocument(revised, OpeningDocumentKind.Revised);
        Assert.Equal("OpeningRevisedActaRequired", Code(() => round.CompleteReview(T0.AddMinutes(19), "engineer", "{}")));
        Assert.Throws<ArgumentException>(() => round.CompleteReview(T0.AddMinutes(40), "engineer", ""));
        Assert.Throws<ArgumentException>(() => round.CompleteReview(T0.AddMinutes(40), "", "{}"));

        round.CompleteReview(T0.AddMinutes(40), "engineer", "{\"rev\":1}");
        Assert.Equal(OpeningRoundStatus.Revised, round.Status);
        Assert.Equal((revised, T0.AddMinutes(40), "engineer", "{\"rev\":1}"), (round.RevisedDocumentId, round.ReviewCompletedAt, round.ReviewCompletedBySubject, round.FinalSnapshotJson));
        Assert.Equal("OpeningRoundStateConflict", Code(() => round.CompleteReview(T0.AddMinutes(50), "engineer", "{}")));
        Assert.Throws<InvalidOperationException>(() => round.Accept(T0.AddMinutes(50), "advisor", Guid.NewGuid(), "{}"));
        Assert.Equal(OpeningRoundStatus.Revised, round.Status);
        Assert.Null(round.FilingTaskId);
    }

    [Fact]
    public void DeclareAffectedOnlyDuringChangesRequestedAndEveryTransitionAdvancesRevision()
    {
        Assert.Equal("OpeningRoundStateConflict", Code(() => Presented().DeclareAffected()));
        var round = NewRound();
        var revisions = new List<long> { round.Revision };
        round.Present(T0, "advisor"); revisions.Add(round.Revision);
        round.RequestChanges(T0.AddMinutes(1), "advisor", Guid.NewGuid(), null); revisions.Add(round.Revision);
        round.DeclareAffected(); revisions.Add(round.Revision);
        round.SetDocument(Guid.NewGuid(), OpeningDocumentKind.Revised); revisions.Add(round.Revision);
        round.CompleteReview(T0.AddMinutes(2), "engineer", "{}"); revisions.Add(round.Revision);
        Assert.Equal([0L, 1, 2, 3, 4, 5], revisions);
    }

    [Fact]
    public void StructureCarriesOnlyUnaffectedConfirmedApprovalFromPreviousRound()
    {
        var round = Presented();
        var structure = Guid.NewGuid();
        var confirmed = new OpeningRecordStructure(round.Id, structure);
        confirmed.Confirm(round, true);

        var carried = new OpeningRecordStructure(Guid.NewGuid(), structure, confirmed);
        Assert.Equal((OpeningApproval.Confirmed, round.PresentedDocumentId, true), (carried.Approval, carried.ApprovalDocumentId, carried.CarriedApproval));

        var awaiting = new OpeningRecordStructure(round.Id, Guid.NewGuid());
        Assert.Equal(OpeningApproval.AwaitingApproval, new OpeningRecordStructure(Guid.NewGuid(), structure, awaiting).Approval);

        var changed = new OpeningRecordStructure(round.Id, Guid.NewGuid());
        changed.RequestChange(round, "Cambiar vidrio");
        var notCarried = new OpeningRecordStructure(Guid.NewGuid(), structure, changed);
        Assert.Equal((OpeningApproval.AwaitingApproval, (Guid?)null, false, false), (notCarried.Approval, notCarried.ApprovalDocumentId, notCarried.CarriedApproval, notCarried.Affected));

        Assert.Throws<ArgumentException>(() => new OpeningRecordStructure(Guid.Empty, structure));
        Assert.Throws<ArgumentException>(() => new OpeningRecordStructure(round.Id, Guid.Empty));
    }

    [Fact]
    public void StructureConfirmationTogglesOnPresentedRoundAndLocksCarriedOrChangedRows()
    {
        var round = Presented();
        var row = new OpeningRecordStructure(round.Id, Guid.NewGuid());
        row.Confirm(round, true);
        Assert.Equal((OpeningApproval.Confirmed, round.PresentedDocumentId, 1L), (row.Approval, row.ApprovalDocumentId, row.Revision));
        Assert.Equal("OpeningApprovalLocked", Code(() => row.RequestChange(round, "No aplica")));
        row.Confirm(round, false);
        Assert.Equal((OpeningApproval.AwaitingApproval, (Guid?)null, 2L), (row.Approval, row.ApprovalDocumentId, row.Revision));

        row.RequestChange(round, "Cambiar color");
        Assert.Equal((OpeningApproval.ChangeRequested, "Cambiar color", true), (row.Approval, row.ChangeDescription, row.Affected));
        Assert.Equal("OpeningApprovalLocked", Code(() => row.Confirm(round, true)));
        Assert.Equal("OpeningApprovalLocked", Code(() => row.RequestChange(round, "Otra vez")));
        Assert.Throws<ArgumentException>(() => new OpeningRecordStructure(round.Id, Guid.NewGuid()).RequestChange(round, " "));

        var previous = new OpeningRecordStructure(round.Id, Guid.NewGuid()); previous.Confirm(round, true);
        var nextRound = NewRound(); nextRound.Present(T0.AddMinutes(1), "advisor");
        var carried = new OpeningRecordStructure(nextRound.Id, previous.StructureId, previous);
        Assert.Equal("OpeningApprovalLocked", Code(() => carried.Confirm(nextRound, false)));
        Assert.Equal(OpeningApproval.Confirmed, carried.Approval);

        Assert.Equal("OpeningRoundStateConflict", Code(() => new OpeningRecordStructure(round.Id, Guid.NewGuid()).Confirm(NewRound(), true)));
    }

    [Fact]
    public void ReviewMarksRequireChangesRequestedAndCannotDropRequestedStructures()
    {
        var round = Presented();
        var requested = new OpeningRecordStructure(round.Id, Guid.NewGuid());
        var other = new OpeningRecordStructure(round.Id, Guid.NewGuid());
        requested.RequestChange(round, "Cambiar herraje");
        Assert.Equal("OpeningRoundStateConflict", Code(() => other.DeclareAffected(round, true)));
        Assert.Equal("OpeningRoundStateConflict", Code(() => requested.CheckReview(round, true)));

        round.RequestChanges(T0.AddMinutes(20), "advisor", Guid.NewGuid(), null);
        Assert.Equal("OpeningAffectedStructuresRequired", Code(() => requested.DeclareAffected(round, false)));
        Assert.True(requested.Affected);
        Assert.Equal("OpeningStructureNotInReview", Code(() => other.CheckReview(round, true)));

        other.DeclareAffected(round, true);
        other.CheckReview(round, true);
        Assert.True(other.ReviewCompleted);
        var revision = other.Revision;
        other.DeclareAffected(round, true);
        Assert.Equal(revision, other.Revision);
        other.DeclareAffected(round, false);
        Assert.False(other.Affected); Assert.False(other.ReviewCompleted);
        Assert.Equal(revision + 1, other.Revision);
    }

    [Fact]
    public void OpeningDocumentRequiresEngineeringDocumentOfSameProjectAndValidMetadata()
    {
        var round = NewRound();
        ProjectDocument Doc(Guid project, bool engineering = true, bool receipt = false)
            => new(Guid.NewGuid(), project, "acta.pdf", "application/pdf", 10, Guid.NewGuid(), T0, "engineer", null, null, isPaymentReceipt: receipt, isEngineeringDocument: engineering);

        var document = Doc(round.ProjectId);
        var acta = new OpeningRecordDocument(round, document, OpeningDocumentKind.Signed, 1, Hash, "[]");
        Assert.Equal((document.Id, round.Id, round.ProjectId, OpeningDocumentKind.Signed, 1), (acta.DocumentId, acta.RoundId, acta.ProjectId, acta.Kind, acta.Version));

        Assert.Throws<ArgumentException>(() => new OpeningRecordDocument(round, Doc(round.ProjectId, engineering: false), OpeningDocumentKind.Signed, 1, Hash, "[]"));
        Assert.Throws<ArgumentException>(() => new OpeningRecordDocument(round, Doc(round.ProjectId, engineering: false, receipt: true), OpeningDocumentKind.Signed, 1, Hash, "[]"));
        Assert.Throws<ArgumentException>(() => new OpeningRecordDocument(round, Doc(Guid.NewGuid()), OpeningDocumentKind.Signed, 1, Hash, "[]"));
        Assert.Throws<ArgumentException>(() => new OpeningRecordDocument(round, document, (OpeningDocumentKind)99, 1, Hash, "[]"));
        Assert.Throws<ArgumentException>(() => new OpeningRecordDocument(round, document, OpeningDocumentKind.Revised, 0, Hash, "[]"));
        Assert.Throws<ArgumentException>(() => new OpeningRecordDocument(round, document, OpeningDocumentKind.Revised, 1, new string('a', 63), "[]"));
    }

    [Fact]
    public void EventCapturesRoundRevisionAtTheMomentOfTheAction()
    {
        var round = Presented();
        var key = Guid.NewGuid();
        var evt = new OpeningRecordEvent(round, key, "fp", "Present", "{}", T0, "advisor", "Asesor");
        round.SetDocument(Guid.NewGuid(), OpeningDocumentKind.Signed);
        Assert.Equal((round.Id, key, "Present", 1L), (evt.RoundId, evt.RequestKey, evt.Action, evt.Revision));
        Assert.Equal(2, round.Revision);
    }
}
