using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using SnG.Control.Api.Authorization;
using SnG.Control.Application.Features.Budgets.CommercialHandoff;

namespace SnG.Control.Api.Controllers;

[ApiController]
[Route("api/projects/{id}")]
public class CommercialHandoffController(SendBudgetQuotationToCommercialUseCase send, DownloadCommercialQuotationUseCase download) : ControllerBase
{
    [HttpPost("send-to-commercial")]
    [Authorize(Policy = BudgetsAssigneeRequirement.PolicyName)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<SendBudgetQuotationToCommercialResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SendBudgetQuotationToCommercialResponse>> Send(Guid id, CancellationToken cancellationToken)
    {
        if (Request.ContentLength is > 0 || Request.Headers.ContainsKey("Transfer-Encoding"))
            return Problem(statusCode: 400, title: "El envío a Comercial no admite un cuerpo de solicitud.");
        var result = await send.ExecuteAsync(id, cancellationToken);
        return result.Status == CommercialHandoffStatus.Sent ? Ok(result.Response) : Failure(result.Status);
    }

    [HttpGet("budget-quotation/commercial-file")]
    [Authorize(Policy = CommercialHeadRequirement.PolicyName)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK, DownloadCommercialQuotationUseCase.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken)
    {
        var result = await download.ExecuteAsync(id, cancellationToken);
        return result.Status == CommercialHandoffStatus.Sent
            ? File(result.Bytes!, DownloadCommercialQuotationUseCase.ContentType, result.FileName!) : Failure(result.Status);
    }

    private ObjectResult Failure(CommercialHandoffStatus status) => Problem(
        statusCode: status == CommercialHandoffStatus.ProjectNotFound ? 404 : 409,
        title: status == CommercialHandoffStatus.ProjectNotFound ? "Project not found." : "La cotización no está disponible para esta operación de Comercial.",
        extensions: new Dictionary<string, object?> { ["code"] = status.ToString() });
}
