using Microsoft.Extensions.Options;
using SnG.Control.Application.Identity;
namespace SnG.Control.Api.Authorization;
public sealed class InstallationAssigneeOptions { public string InstallationAssigneeSubject { get; set; } = ""; }
public sealed class ConfiguredInstallationAssigneeIdentity(IOptions<InstallationAssigneeOptions> options) : IInstallationAssigneeIdentity
{ public string Subject => options.Value.InstallationAssigneeSubject; }
