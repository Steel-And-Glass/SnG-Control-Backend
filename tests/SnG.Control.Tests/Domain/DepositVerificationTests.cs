using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;

namespace SnG.Control.Tests.Domain;

public sealed class DepositVerificationTests
{
    private static readonly DateTime T0 = new(2026, 11, 2, 13, 0, 0, DateTimeKind.Utc);

    private static AccountingHandoff Handoff(Guid? project = null)
        => new(project ?? Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), T0.AddDays(-1), "advisor", null, null);

    private static DepositVerification Pending(AccountingHandoff? handoff = null) => new(handoff ?? Handoff(), Guid.NewGuid(), T0, "advisor");

    private static ProjectDocument Receipt(Guid project, bool receipt = true)
        => new(Guid.NewGuid(), project, "soporte.pdf", "application/pdf", 10, Guid.NewGuid(), T0.AddHours(-1), "advisor", null, null, isPaymentReceipt: receipt);

    private static ProjectTask CorrectionTask(Guid project, string subject = "advisor", bool start = true, ProjectTaskType type = ProjectTaskType.CorrectDepositReceipts)
    {
        var task = new ProjectTask(project, OperationalArea.Commercial, type, subject, T0);
        if (start) task.StartUntracked(T0);
        return task;
    }

    [Fact]
    public void SubmissionStartsPendingAndKeepsHandoffTraceability()
    {
        var handoff = Handoff(); var key = Guid.NewGuid();
        var review = new DepositVerification(handoff, key, T0, "advisor");
        Assert.Equal((handoff.ProjectId, handoff.Id, key, T0, "advisor"), (review.ProjectId, review.AccountingHandoffId, review.RequestKey, review.SubmittedAt, review.SubmittedByGoogleSubject));
        Assert.Equal(DepositVerificationStatus.Pending, review.Status);
        Assert.Null(review.DecidedAt); Assert.Null(review.Note); Assert.Null(review.CorrectionTaskId);
    }

    [Fact]
    public void SubmissionRejectsEmptyKeyNonUtcAndBlankSubject()
    {
        Assert.Throws<ArgumentException>(() => new DepositVerification(Handoff(), Guid.Empty, T0, "advisor"));
        Assert.Throws<ArgumentException>(() => new DepositVerification(Handoff(), Guid.NewGuid(), DateTime.SpecifyKind(T0, DateTimeKind.Local), "advisor"));
        Assert.Throws<ArgumentException>(() => new DepositVerification(Handoff(), Guid.NewGuid(), T0, " "));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RejectionRequiresObservationAndLeavesReviewPending(string? note)
    {
        var review = Pending();
        Assert.Throws<ArgumentException>(() => review.Decide(false, note, T0.AddHours(1), "accountant", null, null));
        Assert.Equal(DepositVerificationStatus.Pending, review.Status);
        Assert.Null(review.DecidedAt);
    }

    [Fact]
    public void RejectionTrimsObservationAndRecordsDecider()
    {
        var review = Pending();
        review.Decide(false, "  Soporte ilegible  ", T0.AddHours(1), "accountant", "c@example.com", "Contabilidad");
        Assert.Equal(DepositVerificationStatus.Rejected, review.Status);
        Assert.Equal(("Soporte ilegible", T0.AddHours(1), "accountant", "c@example.com", "Contabilidad"),
            (review.Note, review.DecidedAt, review.DecidedByGoogleSubject, review.DecidedByEmail, review.DecidedByName));
    }

    [Fact]
    public void ApprovalAllowsMissingObservationAndNormalizesBlankToNull()
    {
        var review = Pending();
        review.Decide(true, "  ", T0, "accountant", null, null);
        Assert.Equal(DepositVerificationStatus.Approved, review.Status);
        Assert.Null(review.Note);
    }

    [Fact]
    public void ObservationLengthIsBoundedAt2000AfterTrimming()
    {
        var accepted = Pending();
        accepted.Decide(false, " " + new string('x', 2000) + " ", T0, "accountant", null, null);
        Assert.Equal(2000, accepted.Note!.Length);
        var tooLong = Pending();
        Assert.Throws<ArgumentException>(() => tooLong.Decide(true, new string('x', 2001), T0, "accountant", null, null));
        Assert.Equal(DepositVerificationStatus.Pending, tooLong.Status);
    }

    [Fact]
    public void DecisionRejectsNonUtcBeforeSubmissionAndBlankDecider()
    {
        var review = Pending();
        Assert.Throws<ArgumentException>(() => review.Decide(true, null, T0.AddTicks(-1), "accountant", null, null));
        Assert.Throws<ArgumentException>(() => review.Decide(true, null, DateTime.SpecifyKind(T0.AddHours(1), DateTimeKind.Unspecified), "accountant", null, null));
        Assert.Throws<ArgumentException>(() => review.Decide(true, null, T0.AddHours(1), " ", null, null));
        Assert.Equal(DepositVerificationStatus.Pending, review.Status);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DecisionIsImmutableOnceRecorded(bool approved)
    {
        var review = Pending();
        review.Decide(approved, "Primera decisión", T0.AddHours(1), "accountant", null, null);
        Assert.Throws<InvalidOperationException>(() => review.Decide(!approved, "Cambio", T0.AddHours(2), "accountant", null, null));
        Assert.Equal(("Primera decisión", T0.AddHours(1)), (review.Note, review.DecidedAt));
    }

    [Fact]
    public void CorrectionTaskIsLinkedOnlyOnceToRejectedReviewOfSubmittingAdvisor()
    {
        var review = Pending();
        Assert.Throws<InvalidOperationException>(() => review.AssignCorrectionTask(CorrectionTask(review.ProjectId)));
        review.Decide(false, "Falta soporte", T0.AddHours(1), "accountant", null, null);

        Assert.Throws<InvalidOperationException>(() => review.AssignCorrectionTask(CorrectionTask(Guid.NewGuid())));
        Assert.Throws<InvalidOperationException>(() => review.AssignCorrectionTask(CorrectionTask(review.ProjectId, subject: "other-advisor")));
        Assert.Throws<InvalidOperationException>(() => review.AssignCorrectionTask(CorrectionTask(review.ProjectId, start: false)));
        Assert.Throws<InvalidOperationException>(() => review.AssignCorrectionTask(CorrectionTask(review.ProjectId, type: ProjectTaskType.PresentQuotationToClient)));
        Assert.Null(review.CorrectionTaskId);

        var task = CorrectionTask(review.ProjectId);
        review.AssignCorrectionTask(task);
        Assert.Equal(task.Id, review.CorrectionTaskId);
        Assert.Throws<InvalidOperationException>(() => review.AssignCorrectionTask(CorrectionTask(review.ProjectId)));
        Assert.Equal(task.Id, review.CorrectionTaskId);
    }

    [Fact]
    public void ApprovedReviewNeverReceivesCorrectionTask()
    {
        var review = Pending();
        review.Decide(true, null, T0.AddHours(1), "accountant", null, null);
        Assert.Throws<InvalidOperationException>(() => review.AssignCorrectionTask(CorrectionTask(review.ProjectId)));
    }

    [Fact]
    public void ReviewDocumentsMustBeCurrentReceiptsOfTheSameProject()
    {
        var review = Pending();
        var receipt = Receipt(review.ProjectId);
        var link = new DepositVerificationDocument(review, receipt);
        Assert.Equal((review.Id, receipt.Id, review.ProjectId), (link.DepositVerificationId, link.ProjectDocumentId, link.ProjectId));

        Assert.Throws<ArgumentException>(() => new DepositVerificationDocument(review, Receipt(Guid.NewGuid())));
        Assert.Throws<ArgumentException>(() => new DepositVerificationDocument(review, Receipt(review.ProjectId, receipt: false)));

        var removed = Receipt(review.ProjectId); removed.Remove(T0);
        Assert.Throws<ArgumentException>(() => new DepositVerificationDocument(review, removed));
        var replaced = Receipt(review.ProjectId); replaced.ReplaceWith(Receipt(review.ProjectId), T0);
        Assert.Throws<ArgumentException>(() => new DepositVerificationDocument(review, replaced));
    }
}
