using SnG.Control.Application.Features.ClientQuotationDecisions;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;

namespace SnG.Control.Tests.Application;

public class ClientDecisionTests
{
    internal static readonly DateTime Now = new(2026, 10, 28, 18, 0, 0, DateTimeKind.Utc);
    internal static (CommercialQuotation Quotation, ProjectTask Present, ProjectTask Review) State(Guid? id = null)
    {
        var project = id ?? Guid.NewGuid();
        var quotation = new CommercialQuotation(project, Guid.NewGuid(), Guid.NewGuid(), "synthetic.xlsx", 10, "advisor", null, null);
        var present = new ProjectTask(project, OperationalArea.Commercial, ProjectTaskType.PresentQuotationToClient, "advisor", Now.AddDays(-1));
        present.Start(Now.AddDays(-1), Now.AddDays(1));
        var review = new ProjectTask(project, OperationalArea.Commercial, ProjectTaskType.ReviewQuotation, "head", Now.AddDays(-2));
        review.Start(Now.AddDays(-2), Now); review.Complete(Now.AddDays(-1));
        return (quotation, present, review);
    }
    [Theory]
    [InlineData(ClientQuotationDecisionType.Approved)]
    [InlineData(ClientQuotationDecisionType.ChangesRequested)]
    [InlineData(ClientQuotationDecisionType.Rejected)]
    public void ExplicitDecisionIsImmutableAndWorkflowCanComplete(ClientQuotationDecisionType value)
    {
        var (q, task, review) = State();
        Assert.Equal(DecisionStatus.Success, DecisionWorkflow.Validate(q.ProjectId, q, [task, review], "advisor"));
        var d = new ClientQuotationDecision(q.ProjectId, q.Id, task.Id, value, null, Now, "advisor", null, null);
        task.Complete(Now);
        Assert.Equal(value, d.Decision); Assert.Equal(task.CompletedAt, d.RecordedAt);
        Assert.Equal(ProjectTaskStatus.Completed, task.Status); Assert.False(task.WasOverdue);
        Assert.All(typeof(ClientQuotationDecision).GetProperties(), p => Assert.False(p.SetMethod!.IsPublic));
        Assert.Equal(q.Id, d.CommercialQuotationId); Assert.Equal(task.Id, d.PresentQuotationTaskId);
    }
    [Fact]
    public void InvalidDomainValuesFail()
    {
        var (q, task, _) = State();
        ClientQuotationDecision Create(ClientQuotationDecisionType value, string? note, DateTime now, Guid project) => new(project, q.Id, task.Id, value, note, now, "advisor", null, null);
        Assert.Throws<ArgumentOutOfRangeException>(() => Create((ClientQuotationDecisionType)4, null, Now, q.ProjectId));
        Assert.Throws<ArgumentException>(() => Create(ClientQuotationDecisionType.Approved, new string('a', 2001), Now, q.ProjectId));
        Assert.Throws<ArgumentException>(() => Create(ClientQuotationDecisionType.Approved, null, DateTime.SpecifyKind(Now, DateTimeKind.Unspecified), q.ProjectId));
        Assert.Throws<ArgumentException>(() => Create(ClientQuotationDecisionType.Approved, null, Now, Guid.Empty));
        Assert.Equal(2000, Create(ClientQuotationDecisionType.Approved, new string('a', 2000), Now, q.ProjectId).Note!.Length);
    }
    [Fact]
    public void WorkflowFailsClosed()
    {
        var (q, task, review) = State();
        Assert.Equal(DecisionStatus.QuotationUnavailable, DecisionWorkflow.Validate(q.ProjectId, null, [task, review], "advisor"));
        Assert.Equal(DecisionStatus.Forbidden, DecisionWorkflow.Validate(q.ProjectId, q, [task, review], "other"));
        Assert.Equal(DecisionStatus.InvalidWorkflowState, DecisionWorkflow.Validate(Guid.NewGuid(), q, [task, review], "advisor"));
        Assert.Equal(DecisionStatus.InvalidWorkflowState, DecisionWorkflow.Validate(q.ProjectId, q, [review], "advisor"));
        Assert.Equal(DecisionStatus.InvalidWorkflowState, DecisionWorkflow.Validate(q.ProjectId, q, [task], "advisor"));
        var pending = new ProjectTask(q.ProjectId, OperationalArea.Commercial, ProjectTaskType.PresentQuotationToClient, "advisor", Now);
        Assert.Equal(DecisionStatus.InvalidWorkflowState, DecisionWorkflow.Validate(q.ProjectId, q, [pending, review], "advisor"));
        var wrong = new ProjectTask(q.ProjectId, OperationalArea.Budgets, ProjectTaskType.PresentQuotationToClient, "advisor", Now);
        Assert.Equal(DecisionStatus.InvalidWorkflowState, DecisionWorkflow.Validate(q.ProjectId, q, [wrong, review], "advisor"));
        task.Complete(Now);
        Assert.Equal(DecisionStatus.InvalidWorkflowState, DecisionWorkflow.Validate(q.ProjectId, q, [task, review], "advisor"));
    }
}
