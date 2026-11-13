using SnG.Control.Domain.Enums;

namespace SnG.Control.Application.Identity;

// Only a role validated against the authenticated identity may enter this context.
public interface IActiveRoleContext
{
    SystemRole? Role { get; }
}
