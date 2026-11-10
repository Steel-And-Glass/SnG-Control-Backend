namespace SnG.Control.Application.Identity;
public interface IEngineeringAssigneeIdentity { string Subject { get; } IReadOnlyList<string> Subjects => string.IsNullOrWhiteSpace(Subject) ? [] : [Subject]; }
