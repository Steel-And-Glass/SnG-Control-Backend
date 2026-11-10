using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;
using SnG.Control.Application.Features.Notifications;
using SnG.Control.Application.Scheduling;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
using SnG.Control.Infrastructure.Persistence;
using SnG.Control.Infrastructure.Persistence.Configurations;
using SnG.Control.Infrastructure.Persistence.Stores;
using SnG.Control.Tests.Fixtures;

namespace SnG.Control.Tests.Persistence;

public class NotificationPersistenceTests
{
    [Fact]
    public void Model_HasOnlyRequiredIndexesRestrictiveHistoryAndNullableLegacy()
    {
        using var db = new SnGControlDbContext(new DbContextOptionsBuilder<SnGControlDbContext>().UseNpgsql().Options);
        var model = db.GetService<IDesignTimeModel>().Model; var entity = model.FindEntityType(typeof(ProjectNotification))!;
        Assert.Equal(12, entity.GetProperties().Count()); Assert.Equal(4, entity.GetIndexes().Count());
        Assert.Equal(ProjectNotificationConfiguration.EventKeyIndex, Assert.Single(entity.GetIndexes(), i => i.IsUnique).GetDatabaseName());
        Assert.All(entity.GetForeignKeys(), fk => Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior)); Assert.Equal(2, entity.GetForeignKeys().Count());
        Assert.Equal(3, entity.GetCheckConstraints().Count());
        Assert.All(entity.GetProperties(), p => Assert.Equal(p.Name == nameof(ProjectNotification.ResolvedAt), p.IsNullable));
        var task = model.FindEntityType(typeof(ProjectTask))!; Assert.True(task.FindProperty("DeadlineAt")!.IsNullable); Assert.True(task.FindProperty("WasOverdue")!.IsNullable);
        Assert.Equal(27, db.Database.GetMigrations().Count()); Assert.False(db.Database.HasPendingModelChanges());
        Assert.Equal(58, model.GetEntityTypes().Count()); Assert.DoesNotContain(model.GetEntityTypes(), e => e.Name.Contains("User"));
        Assert.Contains("ProjectNotifications", db.ProjectNotifications.ToQueryString());
        var queries = new EfProjectNotificationStore(db);
        Assert.Contains("ORDER BY", queries.PersonalQuery("synthetic").ToQueryString());
        Assert.Contains("DESC", queries.PersonalQuery("synthetic").ToQueryString());
        Assert.Contains("WHERE", queries.ProjectQuery(Guid.NewGuid()).ToQueryString());
        foreach (var kind in Enum.GetValues<NotificationKind>()) Assert.Equal((int)kind, entity.FindProperty("Kind")!.GetTypeMapping().Converter!.ConvertToProvider(kind));
        foreach (var state in Enum.GetValues<NotificationState>()) Assert.Equal((int)state, entity.FindProperty("State")!.GetTypeMapping().Converter!.ConvertToProvider(state));
    }
    [Theory]
    [InlineData("23505", ProjectNotificationConfiguration.EventKeyIndex, true)]
    [InlineData("23505", "OtherIndex", false)]
    [InlineData("23514", ProjectNotificationConfiguration.EventKeyIndex, false)]
    public void OnlyKnownUniqueViolation_IsRecognized(string code, string constraint, bool expected) =>
        Assert.Equal(expected, EfProjectNotificationSweepStore.IsDuplicateEvent(new("Synthetic", new PostgresException("Synthetic", "ERROR", "ERROR", code, constraintName: constraint))));

    [Fact]
    public async Task AssignmentNotification_IsInSameSaveAsPendingPrepare()
    {
        using var db = new CommercialQuotationScenario.SaveContext();
        var now = new DateTime(2026, 10, 14, 15, 0, 0, DateTimeKind.Utc); var timing = new TaskTiming(new ControlledTimeProvider(now), new());
        var task = new ProjectTask(Guid.NewGuid(), OperationalArea.Budgets, ProjectTaskType.PrepareQuotation, "budgets-synthetic", now);
        Assert.True(await new EfProjectTaskStore(db, timing, new NotificationWriterDouble(db)).TryAddPrepareQuotationAsync(task, default));
        Assert.Equal(1, db.Calls); var notification = Assert.Single(db.ProjectNotifications.Local);
        Assert.Equal(task.Id, notification.ProjectTaskId); Assert.Equal(task.AssignedToGoogleSubject, notification.RecipientGoogleSubject);
        Assert.Null(task.DeadlineAt); Assert.Null(task.WasOverdue); Assert.Equal(now, notification.CreatedAt);
    }
    [Fact]
    public async Task FailedFileTransaction_RestoresActiveOverdueAndDetachesOnlyNewNotifications()
    {
        using var scenario = new CommercialQuotationScenario(); await scenario.InitializeAsync(); var state = scenario.State!;
        var review = state.Tasks.Single(t => t.Type == ProjectTaskType.ReviewQuotation); var now = review.DeadlineAt!.Value.AddHours(1);
        var clock = new ControlledTimeProvider(now); var timing = new TaskTiming(clock, new());
        review.DetectOverdue(now);
        var overdue = new ProjectNotification(review, NotificationKind.Overdue, "Revisión atrasada", "Detalle", review.DeadlineAt.Value, now);
        using var db = new CommercialQuotationScenario.SaveContext(); db.Attach(overdue); var unrelated = new Client("Unrelated"); db.Attach(unrelated);
        var preview = (await scenario.Preview().ExecuteAsync(state.Project.Id, scenario.Bytes, "test.xlsx", default)).Response!;
        var quotation = new CommercialQuotation(state.Project.Id, state.Version!.Id, state.Catalog!.Id, "test.xlsx", 1, "google-test-subject", null, null);
        using var transaction = new CommercialQuotationScenario.TransactionDouble { FailCommit = true };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new EfCommercialQuotationStore(db, scenario, timing, new NotificationWriterDouble(db))
            .PersistAsync(state, quotation, [], scenario.Bytes, preview, transaction, default));
        Assert.Equal(NotificationState.Active, overdue.State); Assert.Null(overdue.ResolvedAt); Assert.True(review.WasOverdue);
        Assert.Null(review.CompletedAt); Assert.Single(db.ProjectNotifications.Local); Assert.Equal(2, db.ChangeTracker.Entries().Count());
        Assert.Equal(EntityState.Unchanged, db.Entry(overdue).State); Assert.Equal(EntityState.Unchanged, db.Entry(unrelated).State);
    }
    [Fact]
    public async Task CommercialConfirmation_StartsPresentationWithDeadlineAndAssignment()
    {
        using var scenario = new CommercialQuotationScenario(); await scenario.InitializeAsync(); var state = scenario.State!;
        var review = state.Tasks.Single(t => t.Type == ProjectTaskType.ReviewQuotation); var now = review.DeadlineAt!.Value.AddMinutes(1);
        var timing = new TaskTiming(new ControlledTimeProvider(now), new());
        using var db = new CommercialQuotationScenario.SaveContext(); using var transaction = new CommercialQuotationScenario.TransactionDouble();
        var preview = (await scenario.Preview().ExecuteAsync(state.Project.Id, scenario.Bytes, "test.xlsx", default)).Response!;
        var quotation = new CommercialQuotation(state.Project.Id, state.Version!.Id, state.Catalog!.Id, "test.xlsx", 1, "google-test-subject", null, null);
        await new EfCommercialQuotationStore(db, scenario, timing, new NotificationWriterDouble(db)).PersistAsync(state, quotation, [], scenario.Bytes, preview, transaction, default);
        var present = Assert.Single(db.ProjectTasks.Local, t => t.Type == ProjectTaskType.PresentQuotationToClient);
        Assert.Equal(now, present.StartedAt); Assert.Equal(timing.Deadline(present.Type, now), present.DeadlineAt); Assert.False(present.WasOverdue);
        var assignment = Assert.Single(db.ProjectNotifications.Local, n => n.Kind == NotificationKind.TaskAssigned);
        Assert.Equal(state.Project.CreatedByGoogleSubject, assignment.RecipientGoogleSubject);
        var late = Assert.Single(db.ProjectNotifications.Local, n => n.Kind == NotificationKind.Overdue);
        Assert.Equal(NotificationState.Resolved, late.State); Assert.Equal(now, late.ResolvedAt); Assert.True(review.WasOverdue);
    }
    [Fact]
    public async Task Handoff_StartsReviewWithDeadline_AndPreservesBudgetLateEvidence()
    {
        var scenario = await CommercialHandoffScenario.CreateAsync(); var state = scenario.State!; var budget = state.Tasks.Single();
        typeof(ProjectTask).GetProperty(nameof(ProjectTask.AssignedToGoogleSubject))!.SetValue(budget, "budgets-synthetic");
        var now = budget.DeadlineAt!.Value.AddHours(1); var timing = new TaskTiming(new ControlledTimeProvider(now), new());
        using var db = new CommercialQuotationScenario.SaveContext(); using var transaction = new CommercialQuotationScenario.TransactionDouble();
        var result = await new EfBudgetQuotationCommercialHandoffStore(db, scenario, timing, new NotificationWriterDouble(db))
            .PersistAsync(state, new byte[] { 1, 2 }, transaction, "head-synthetic", default);
        var review = Assert.Single(db.ProjectTasks.Local, t => t.Type == ProjectTaskType.ReviewQuotation);
        Assert.Equal(timing.Deadline(review.Type, now), review.DeadlineAt); Assert.False(review.WasOverdue); Assert.Equal(now, review.StartedAt);
        Assert.Equal(now, budget.CompletedAt); Assert.True(budget.WasOverdue);
        var assigned = Assert.Single(db.ProjectNotifications.Local, n => n.Kind == NotificationKind.TaskAssigned); Assert.Equal("head-synthetic", assigned.RecipientGoogleSubject);
        var overdue = Assert.Single(db.ProjectNotifications.Local, n => n.Kind == NotificationKind.Overdue);
        Assert.Equal("budgets-synthetic", overdue.RecipientGoogleSubject); Assert.Equal(NotificationState.Resolved, overdue.State); Assert.Equal(now, overdue.ResolvedAt);
        Assert.Equal(review.DeadlineAt, result.Response!.CommercialTask.DeadlineAt);
    }
    [Fact]
    public void EventKey_UsesTaskKindOpaqueRecipientAndStableAssignmentInstant()
    {
        var time = new DateTime(2026, 10, 14, 15, 0, 0, DateTimeKind.Utc);
        var task = new ProjectTask(Guid.NewGuid(), OperationalArea.Budgets, ProjectTaskType.PrepareQuotation, " Sub ", time);
        var key = ProjectNotification.Key(task, NotificationKind.TaskAssigned);
        Assert.Equal(key, ProjectNotification.Key(task, NotificationKind.TaskAssigned));
        Assert.NotEqual(key, ProjectNotification.Key(task, NotificationKind.Overdue));
        typeof(ProjectTask).GetProperty(nameof(ProjectTask.AssignedToGoogleSubject))!.SetValue(task, "Sub");
        Assert.NotEqual(key, ProjectNotification.Key(task, NotificationKind.TaskAssigned));
        typeof(ProjectTask).GetProperty(nameof(ProjectTask.AssignedToGoogleSubject))!.SetValue(task, " Sub ");
        typeof(ProjectTask).GetProperty(nameof(ProjectTask.AssignedAt))!.SetValue(task, time.AddMinutes(1));
        Assert.NotEqual(key, ProjectNotification.Key(task, NotificationKind.TaskAssigned)); Assert.DoesNotContain(" Sub ", key);
    }
}
