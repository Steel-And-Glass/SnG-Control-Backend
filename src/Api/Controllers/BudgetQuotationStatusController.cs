using SnG.Control.Application.Access;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SnG.Control.Api.Authorization;
using SnG.Control.Application.Features.Budgets.Status;

namespace SnG.Control.Api.Controllers;

[ApiController]
[Authorize(Policy = BudgetsAssigneeRequirement.PolicyName)]
[Route("api/projects/{id}/budget-quotation/status")]
public sealed class BudgetQuotationStatusController(IBudgetQuotationStatusStore store, ProjectReadAccess access) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BudgetQuotationStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BudgetQuotationStatusResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        if (Request.ContentLength is > 0 || Request.Headers.ContainsKey("Transfer-Encoding"))
            return Problem(statusCode: 400, title: "La consulta de estado no admite un cuerpo.");
        var response = await store.GetAsync(id, access.Scope, cancellationToken);
        return response is null ? Problem(statusCode: 404, title: "Project not found.") : Ok(response);
    }
}