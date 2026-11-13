using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SnG.Control.Application.Features.ProjectTasks;
using SnG.Control.Application.Features.ProjectTasks.ListByArea;
using SnG.Control.Domain.Enums;

namespace SnG.Control.Api.Controllers;

[ApiController]
[Route("api/project-tasks")]
public class ProjectTasksController(ListProjectTasksByAreaUseCase list) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ProjectTaskResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<ProjectTaskResponse>>> List(
        [FromQuery, BindRequired] string area,
        [FromQuery, BindRequired] string status,
        CancellationToken cancellationToken)
    {
        if (!TryParseName<OperationalArea>(area, out var parsedArea))
        {
            ModelState.AddModelError(nameof(area), "Area must be a defined operational area name.");
        }
        if (!TryParseName<ProjectTaskStatus>(status, out var parsedStatus))
        {
            ModelState.AddModelError(nameof(status), "Status must be a defined task status name.");
        }
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var result = await list.ExecuteAsync(parsedArea, parsedStatus, cancellationToken);
        return result is null ? Forbid() : Ok(result);
    }

    private static bool TryParseName<TEnum>(string value, out TEnum parsed) where TEnum : struct, Enum =>
        Enum.TryParse(value, ignoreCase: true, out parsed)
        && Enum.IsDefined(parsed)
        && string.Equals(Enum.GetName(parsed), value, StringComparison.OrdinalIgnoreCase);
}
