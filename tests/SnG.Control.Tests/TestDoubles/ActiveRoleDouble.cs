using SnG.Control.Application.Identity;
using SnG.Control.Domain.Enums;
namespace SnG.Control.Tests.TestDoubles;

public sealed record ActiveRoleDouble(SystemRole? Role) : IActiveRoleContext;
