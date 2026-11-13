using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Entities.Incremental;
using SnG.Control.Domain.Enums;
namespace SnG.Control.Tests.Domain;
public sealed class CommercialOpeningTests {
 [Fact]public void PartialResponsesPreserveFirstResponseAndContinueUntilExplicitResolution(){
 var at=new DateTime(2026,11,10,12,0,0,DateTimeKind.Utc);var project=Guid.NewGuid();var task=new ProjectTask(project,OperationalArea.Commercial,ProjectTaskType.PresentOpeningRecord,"synthetic-advisor",at);task.StartUntracked(at);
 var round=new CommercialOpeningRound(Guid.NewGuid(),project,Guid.NewGuid(),1,task,Guid.NewGuid(),at);Assert.Null(task.DeadlineAt);Assert.Null(round.PresentedAt);
 Assert.Throws<InvalidOperationException>(()=>round.Respond(at));round.Present(at.AddMinutes(1));round.Respond(at.AddMinutes(4));round.Respond(at.AddMinutes(10));Assert.Equal(at.AddMinutes(4),round.FirstResponseAt);Assert.Null(round.ResolvedAt);Assert.Equal(at.AddMinutes(10),round.LastActivityAt);
 round.Resolve(at.AddMinutes(11));Assert.Equal(at.AddMinutes(11),round.ResolvedAt);Assert.Throws<InvalidOperationException>(()=>round.Respond(at.AddMinutes(12)));
 }
 [Fact]public void NewRequestInvalidatesDeclarationAndDocumentButNotOtherReviewChecks(){
 var at=new DateTime(2026,11,10,12,0,0,DateTimeKind.Utc);var project=Guid.NewGuid();var task=new ProjectTask(project,OperationalArea.Commercial,ProjectTaskType.PresentOpeningRecord,"synthetic",at);var round=new CommercialOpeningRound(Guid.NewGuid(),project,Guid.NewGuid(),1,task,Guid.NewGuid(),at);
 var review=new CommercialOpeningReview(round,new(project,OperationalArea.Engineering,ProjectTaskType.ReviewOpeningRecord,"engineer",at),at);review.Declare();review.SetDocument(Guid.NewGuid());review.Invalidate();Assert.False(review.Declared);Assert.Null(review.DocumentId);Assert.Throws<InvalidOperationException>(()=>review.Complete(at));
 }
}
