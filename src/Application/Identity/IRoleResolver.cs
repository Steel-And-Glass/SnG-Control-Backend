using SnG.Control.Domain.Enums;

namespace SnG.Control.Application.Identity;

public interface IRoleResolver
{
    IReadOnlyList<SystemRole> Resolve(string subject);
}
