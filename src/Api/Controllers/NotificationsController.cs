using Microsoft.AspNetCore.Mvc;
using SnG.Control.Application.Features.Notifications;

namespace SnG.Control.Api.Controllers;

[ApiController]
public sealed class NotificationsController(ListPersonalNotificationsUseCase personal, ListProjectNotificationsUseCase project) : ControllerBase
{
    [HttpGet("api/notifications")]
    [ProducesResponseType<IReadOnlyList<ProjectNotificationResponse>>(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType<ProblemDetails>(400)]
    public async Task<IActionResult> Personal(CancellationToken token)
    {
        if (Request.Query.Count != 0) return Problem(statusCode: 400, title: "This endpoint does not accept recipient or filter parameters.");
        return Ok(await personal.ExecuteAsync(token));
    }
    [HttpGet("api/projects/{id}/notifications")]
    [ProducesResponseType<IReadOnlyList<ProjectNotificationResponse>>(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType<ProblemDetails>(404)]
    public async Task<IActionResult> Project(Guid id, CancellationToken token)
    {
        var result = await project.ExecuteAsync(id, token);
        return result is null ? Problem(statusCode: 404, title: "Project not found.") : Ok(result);
    }
}
