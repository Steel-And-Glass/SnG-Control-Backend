using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SnG.Control.Api.Authorization;
using SnG.Control.Api.Contracts;
using SnG.Control.Api.Filters;
using SnG.Control.Application.Features.Budgets.CommercialHandoff;
using SnG.Control.Application.Features.CommercialQuotations;
using SnG.Control.Infrastructure.Quotations;

namespace SnG.Control.Api.Controllers;

[ApiController]
[Route("api/projects/{id}/commercial-quotation")]
public sealed class CommercialQuotationController(PreviewCommercialQuotationUseCase preview, ConfirmCommercialQuotationUseCase confirm,
    DownloadCommercialQuotationFileUseCase download, IOptions<BudgetQuotationParserOptions> options) : ControllerBase
{
    [HttpPost("preview")]
    [Authorize(Policy = CommercialHeadRequirement.PolicyName)]
    [DisableFormValueModelBinding]
    [ProducesResponseType<CommercialQuotationPreviewResponse>(200)]
    [ProducesResponseType<ProblemDetails>(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType<ProblemDetails>(404)]
    [ProducesResponseType<ProblemDetails>(409)]
    public async Task<IActionResult> Preview(Guid id, CancellationToken token)
    {
        var upload = await QuotationMultipartReader.ReadAsync(Request, false, options.Value.MaxUploadBytes, token);
        if (upload is null) return Failure(CommercialQuotationStatus.InvalidWorkbook);
        var result = await preview.ExecuteAsync(id, upload.File, upload.FileName, token);
        return result.Status == CommercialQuotationStatus.Success ? Ok(result.Response) : Failure(result.Status, result);
    }

    [HttpPost("confirm")]
    [Authorize(Policy = CommercialHeadRequirement.PolicyName)]
    [DisableFormValueModelBinding]
    [ProducesResponseType<CommercialQuotationConfirmationResponse>(201)]
    [ProducesResponseType<ProblemDetails>(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType<ProblemDetails>(404)]
    [ProducesResponseType<ProblemDetails>(409)]
    public async Task<IActionResult> Confirm(Guid id, CancellationToken token)
    {
        var upload = await QuotationMultipartReader.ReadAsync(Request, false, options.Value.MaxUploadBytes, token);
        if (upload is null) return Failure(CommercialQuotationStatus.InvalidWorkbook);
        var result = await confirm.ExecuteAsync(id, upload.File, upload.FileName, token);
        return result.Status == CommercialQuotationStatus.Success ? StatusCode(201, result.Response) : Failure(result.Status, result.Validation);
    }

    [HttpGet("file")]
    [ProducesResponseType<FileContentResult>(200, DownloadCommercialQuotationUseCase.ContentType)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType<ProblemDetails>(404)]
    [ProducesResponseType<ProblemDetails>(409)]
    [ProducesResponseType<ProblemDetails>(500)]
    public async Task<IActionResult> File(Guid id, CancellationToken token)
    {
        var result = await download.ExecuteAsync(id, token);
        return result.Status == CommercialQuotationStatus.Success
            ? File(result.Bytes!, DownloadCommercialQuotationUseCase.ContentType, result.FileName!) : Failure(result.Status);
    }

    private IActionResult Failure(CommercialQuotationStatus status, CommercialQuotationPreviewResult? validation = null)
    {
        if (status == CommercialQuotationStatus.Forbidden) return Forbid();
        var code = status switch
        {
            CommercialQuotationStatus.ProjectNotFound => 404,
            CommercialQuotationStatus.InvalidWorkbook or CommercialQuotationStatus.InvalidEconomics
                or CommercialQuotationStatus.RecalculationRequired or CommercialQuotationStatus.InconsistentFormulaCache => 400,
            _ => 409
        };
        var details = new Dictionary<string, object?> { ["code"] = status.ToString() };
        if (validation?.MissingItems is not null) details["missingItems"] = validation.MissingItems;
        if (validation?.UnexpectedItems is not null) details["unexpectedItems"] = validation.UnexpectedItems;
        if (validation?.FileError is not null) details["fileError"] = validation.FileError.ToString();
        return Problem(statusCode: code, title: "La cotización comercial no está disponible para esta operación.", extensions: details);
    }
}
