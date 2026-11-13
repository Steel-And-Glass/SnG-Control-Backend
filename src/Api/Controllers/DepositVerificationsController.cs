using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SnG.Control.Api.Authorization;
using SnG.Control.Api.Contracts;
using SnG.Control.Api.Filters;
using SnG.Control.Application.Features.Accounting;
using SnG.Control.Application.Features.ProjectDocuments;
namespace SnG.Control.Api.Controllers;

public sealed record SubmitVerificationRequest(Guid RequestKey, [Required, MinLength(1), MaxLength(15)] Guid[] DocumentIds);
public sealed record DecideVerificationRequest(bool Approve, [MaxLength(48)] string? Amount, [MaxLength(40)] string? ReceivedAt, [MaxLength(2000)] string? Note);
[ApiController]
[Route("api/projects/{id}/deposit-verifications")]
public sealed class DepositVerificationsController(DepositVerificationService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<VerificationState>(200)]
    public async Task<IActionResult> Read(Guid id, CancellationToken token) => await service.ReadAsync(id, token) is { } state ? Ok(state) : NotFound();
    [HttpPost]
    [Authorize(Policy = ActiveRoleRegistration.CommercialAdvisorPolicy)]
    public async Task<IActionResult> Submit(Guid id, SubmitVerificationRequest body, CancellationToken token)
        => Ok(new { reviewId = await service.SubmitAsync(id, new(body.RequestKey, body.DocumentIds), token) });
    [HttpPost("{reviewId:guid}/decision")]
    [Authorize(Policy = AccountingAssigneeRequirement.PolicyName)]
    public async Task<IActionResult> Decide(Guid id, Guid reviewId, DecideVerificationRequest body, CancellationToken token)
    {
        await service.DecideAsync(id, reviewId, body.Approve, body.Amount, body.ReceivedAt, body.Note, token); return NoContent();
    }
    [HttpPost("receipts/batch")]
    [Authorize(Policy = ActiveRoleRegistration.CommercialAdvisorPolicy)]
    [DisableFormValueModelBinding]
    [RequestSizeLimit(ProjectDocumentMultipartReader.MaxRequestBytes)]
    [ProducesResponseType<DocumentBatchResponse>(201)]
    public async Task<IActionResult> Upload(Guid id, [FromServices] IReceiptDocumentStore receipts, CancellationToken token)
    {
        await receipts.RequireReceiptUploadAccessAsync(id, token);
        try { return StatusCode(201, await receipts.UploadReceiptsAsync(id, ProjectDocumentMultipartReader.ReadAsync(Request, token), token)); }
        catch (BadHttpRequestException e) when (e.StatusCode == 413) { throw new DocumentException(413, "BatchTooLarge"); }
    }
}
