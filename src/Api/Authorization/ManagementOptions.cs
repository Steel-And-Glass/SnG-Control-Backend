using Microsoft.Extensions.Options;
using SnG.Control.Application.Identity;

namespace SnG.Control.Api.Authorization;

public sealed class ManagementOptions
{
    public string ManagementSubject { get; set; } = "";
}
public sealed class ConfiguredManagementIdentity(IOptions<ManagementOptions> options) : IManagementIdentity
{
    public string Subject => options.Value.ManagementSubject;
}
