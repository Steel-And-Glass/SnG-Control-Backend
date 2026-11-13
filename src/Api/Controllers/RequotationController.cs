using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SnG.Control.Api.Authorization;
using SnG.Control.Application.Features.Requotations;
namespace SnG.Control.Api.Controllers;

public sealed record RequotationRequestBody([Required, MaxLength(4000)] string RequestedChanges);
[ApiController]
[Route("api/projects/{id}")]
public sealed class RequotationController(RequotationService service) : ControllerBase
{
    [HttpGet("quotation-rounds")]
    [ProducesResponseType<QuotationRoundsResponse>(200)]
    public async Task<IActionResult> Read(Guid id, CancellationToken token) => await service.ReadAsync(id, token) is { } result
        ? Ok(result) : Problem(statusCode: 404, title: "Project unavailable", extensions: new Dictionary<string, object?> { ["code"] = "ProjectNotFound" });
    [HttpPost("requotation-request")]
    [Authorize(Policy = ActiveRoleRegistration.CommercialAdvisorPolicy)]
    [ProducesResponseType<RequestedRequotation>(201)]
    public async Task<IActionResult> Create(Guid id, RequotationRequestBody body, CancellationToken token)
    {
        var result = await service.RequestAsync(id, body.RequestedChanges, token);
        return result.Status == RequotationStatus.Success ? StatusCode(201, result.Response) : Problem(statusCode: result.Status switch
        {
            RequotationStatus.Forbidden => 403,
            RequotationStatus.ProjectNotFound => 404,
            RequotationStatus.InvalidRequest => 400,
            _ => 409
        }, title: "Requotation unavailable", extensions: new Dictionary<string, object?> { ["code"] = result.Status.ToString() });
    }
}
