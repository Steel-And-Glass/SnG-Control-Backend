using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using SnG.Control.Api.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SnG.Control.Api.Contracts;
using SnG.Control.Api.Filters;
using SnG.Control.Application.Features.Budgets.ConfirmQuotation;
using SnG.Control.Application.Features.Budgets.PreviewQuotation;
using SnG.Control.Infrastructure.Quotations;

namespace SnG.Control.Api.Controllers;

[ApiController]
[Authorize(Policy = BudgetsAssigneeRequirement.PolicyName)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
[Route("api/projects/{id}/budget-quotation")]
public class BudgetQuotationController(PreviewBudgetQuotationUseCase preview, ConfirmBudgetQuotationUseCase confirm,
    IOptions<BudgetQuotationParserOptions> options, IOptions<JsonOptions> jsonOptions) : ControllerBase
{
    [HttpPost("preview")]
    [DisableFormValueModelBinding]
    [ProducesResponseType<BudgetQuotationPreviewResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BudgetQuotationPreviewResponse>> Preview(Guid id, CancellationToken cancellationToken)
    {
        var upload = await QuotationMultipartReader.ReadAsync(Request, false, options.Value.MaxUploadBytes, cancellationToken);
        if (upload is null) return InvalidUpload();
        using var stream = new MemoryStream(upload.File, writable: false);
        var result = await preview.ExecuteAsync(id, stream, upload.FileName, cancellationToken);
        return result switch
        {
            PreviewBudgetQuotationResult.Success success => Ok(success.Response),
            PreviewBudgetQuotationResult.ProjectNotFound => Problem(statusCode: 404, title: "Project not found."),
            PreviewBudgetQuotationResult.InvalidWorkflowState => Problem(statusCode: 409, title: "El proyecto no está listo para el preview de Presupuestos."),
            PreviewBudgetQuotationResult.SgCodeMismatch => Problem(statusCode: 409, title: "El SG del archivo no coincide con el proyecto."),
            PreviewBudgetQuotationResult.InvalidWorkbook invalid => InvalidWorkbook(invalid.Error),
            _ => throw new InvalidOperationException("Unexpected preview result.")
        };
    }

    [HttpPost("confirm")]
    [DisableFormValueModelBinding]
    [ProducesResponseType<ConfirmedBudgetQuotationResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ConfirmedBudgetQuotationResponse>> Confirm(Guid id, CancellationToken cancellationToken)
    {
        var upload = await QuotationMultipartReader.ReadAsync(Request, true, options.Value.MaxUploadBytes, cancellationToken);
        if (upload is null) return InvalidUpload();
        ConfirmBudgetQuotationRequest? request;
        try { request = JsonSerializer.Deserialize<ConfirmBudgetQuotationRequest>(upload.Confirmation!, jsonOptions.Value.JsonSerializerOptions); }
        catch (JsonException) { return InvalidUpload(); }
        if (request is null) return InvalidUpload();
        var result = await confirm.ExecuteAsync(id, upload.File, upload.FileName, request, cancellationToken);
        return result.Status switch
        {
            QuotationConfirmationStatus.Created => StatusCode(201, result.Response),
            QuotationConfirmationStatus.ProjectNotFound => Problem(statusCode: 404, title: "Project not found."),
            QuotationConfirmationStatus.InvalidWorkbook => InvalidWorkbook(result.FileError!.Value),
            QuotationConfirmationStatus.InvalidConfirmation => Problem(statusCode: 400, title: "La confirmación debe cubrir exactamente los ítems del archivo con valores válidos."),
            QuotationConfirmationStatus.AlreadyConfirmed => Problem(statusCode: 409, title: "La cotización inicial ya está confirmada."),
            QuotationConfirmationStatus.SgCodeMismatch => Problem(statusCode: 409, title: "El SG del archivo no coincide con el proyecto."),
            QuotationConfirmationStatus.InvalidWorkflowState => Problem(statusCode: 409, title: "El proyecto no está listo para confirmar la cotización."),
            _ => throw new InvalidOperationException("Unexpected confirmation result.")
        };
    }

    private ObjectResult InvalidWorkbook(BudgetQuotationFileError error) => Problem(statusCode: 400,
        title: "El archivo no corresponde a una cotización de Presupuestos legible y soportada.",
        extensions: new Dictionary<string, object?> { ["code"] = error.ToString() });
    private ObjectResult InvalidUpload() => Problem(statusCode: 400, title: "Carga multipart inválida o fuera de los límites técnicos configurados.");
}
