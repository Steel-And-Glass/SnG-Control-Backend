using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SnG.Control.Api.Authorization;
using SnG.Control.Application.Features.ClientQuotationDecisions;
using SnG.Control.Domain.Enums;

namespace SnG.Control.Api.Controllers;

public sealed record RecordClientDecisionRequest([Required] string Decision, [MaxLength(2000)] string? Note);

[ApiController]
[Route("api/projects/{id}/client-quotation-decision")]
public sealed class ClientQuotationDecisionController(ClientQuotationDecisionService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<DecisionResponse>(200)]
    public async Task<IActionResult> Read(Guid id, CancellationToken token)
    {
        var result = await service.ReadAsync(id, token);
        return result.Status == DecisionStatus.Success ? Ok(result.Response) : Failure(result.Status);
    }
    [HttpPost]
    [Authorize(Policy = ActiveRoleRegistration.CommercialAdvisorPolicy)]
    [ProducesResponseType<RecordedDecisionResponse>(201)]
    public async Task<IActionResult> Record(Guid id, RecordClientDecisionRequest request, CancellationToken token)
    {
        if (request.Decision is not ("Approved" or "ChangesRequested" or "Rejected")) return Failure(DecisionStatus.InvalidRequest);
        var result = await service.RecordAsync(id, Enum.Parse<ClientQuotationDecisionType>(request.Decision), request.Note, token);
        return result.Status == DecisionStatus.Success ? StatusCode(201, result.Recorded) : Failure(result.Status);
    }
    private IActionResult Failure(DecisionStatus status) => Problem(statusCode: status switch
    {
        DecisionStatus.Forbidden => 403,
        DecisionStatus.ProjectNotFound => 404,
        DecisionStatus.InvalidRequest => 400,
        _ => 409
    }, title: "No se pudo consultar o registrar la respuesta del cliente.", extensions: new Dictionary<string, object?> { ["code"] = status.ToString() });
}
