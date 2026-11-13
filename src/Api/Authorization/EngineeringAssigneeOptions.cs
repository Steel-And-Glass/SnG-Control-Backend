using Microsoft.Extensions.Options;
using SnG.Control.Application.Identity;
namespace SnG.Control.Api.Authorization;
public sealed class EngineeringAssigneeOptions { public string EngineeringAssigneeSubject { get; set; } = ""; public string[] EngineeringWorkerSubjects { get; set; } = []; }
public sealed class ConfiguredEngineeringAssigneeIdentity(IOptions<EngineeringAssigneeOptions> options) : IEngineeringAssigneeIdentity
{ public string Subject => options.Value.EngineeringAssigneeSubject; public IReadOnlyList<string> Subjects => options.Value.EngineeringWorkerSubjects.Append(Subject).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.Ordinal).ToArray(); }
