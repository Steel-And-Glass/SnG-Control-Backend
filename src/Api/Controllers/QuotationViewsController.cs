using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SnG.Control.Application.Features.QuotationViews;

namespace SnG.Control.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/projects/{id:guid}")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class QuotationViewsController(QuotationReadService service) : ControllerBase
{
    [HttpGet("budget-quotation/view")]
    [ProducesResponseType<ConfirmedBudgetQuotationView>(200)]
    [ProducesResponseType<ProblemDetails>(404)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> Budget(Guid id, CancellationToken token) => Result(await service.BudgetAsync(id, token));

    [HttpGet("commercial-quotation/view")]
    [ProducesResponseType<ConfirmedCommercialQuotationView>(200)]
    [ProducesResponseType<ProblemDetails>(404)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> Commercial(Guid id, CancellationToken token) => Result(await service.CommercialAsync(id, token));

    [HttpGet("budget-quotation/items/{itemId:guid}/image")]
    [ProducesResponseType(typeof(FileStreamResult), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType<ProblemDetails>(404)]
    public async Task<IActionResult> Image(Guid id, Guid itemId, CancellationToken token)
    {
        var result = await service.ImageAsync(id, itemId, token);
        if (result.Status != QuotationReadStatus.Success) return Result(result);
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(result.Value!.Content, result.Value.ContentType);
    }
    private IActionResult Result<T>(QuotationReadResult<T> result) => result.Status switch
    {
        QuotationReadStatus.Success => Ok(result.Value),
        QuotationReadStatus.Forbidden => Forbid(),
        _ => Problem(statusCode: 404, title: "Confirmed quotation or image not found.")
    };
}
