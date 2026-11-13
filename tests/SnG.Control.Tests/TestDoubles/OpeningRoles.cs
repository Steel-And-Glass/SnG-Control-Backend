using SnG.Control.Application.Identity;
using SnG.Control.Domain.Enums;
namespace SnG.Control.Tests.TestDoubles;
public sealed class OpeningRoles : IRoleResolver
{
 public IReadOnlyList<SystemRole> Resolve(string subject) => subject switch { "advisor" => [SystemRole.CommercialAdvisor], "engineer" => [SystemRole.Engineering], _ => [] };
}
