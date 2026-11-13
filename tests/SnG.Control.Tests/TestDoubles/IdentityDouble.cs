using SnG.Control.Application.Identity;

namespace SnG.Control.Tests.TestDoubles;

public sealed record IdentityDouble(string Subject = "google-test-subject", string? Email = "test@example.com", string? Name = "Test User")
    : ICurrentUser, ICommercialHeadIdentity, IBudgetsAssigneeIdentity, IManagementIdentity, IAccountingAssigneeIdentity, IEngineeringAssigneeIdentity;
