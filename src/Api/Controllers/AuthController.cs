using Microsoft.AspNetCore.Mvc;
using SnG.Control.Application.Identity;
using SnG.Control.Domain.Enums;

namespace SnG.Control.Api.Controllers;

public sealed record CurrentUserResponse(string Subject, string? Email, string? Name, bool IsManagement, IReadOnlyList<SystemRole> Roles);

[ApiController]
[Route("api/auth")]
public class AuthController(ICurrentUser currentUser, IRoleResolver roleResolver) : ControllerBase
{
    [HttpGet("me")]
    [ProducesResponseType<CurrentUserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<CurrentUserResponse> Me()
    {
        var roles = roleResolver.Resolve(currentUser.Subject);
        return Ok(new CurrentUserResponse(currentUser.Subject, currentUser.Email, currentUser.Name, roles.Contains(SystemRole.Management), roles));
    }
}
