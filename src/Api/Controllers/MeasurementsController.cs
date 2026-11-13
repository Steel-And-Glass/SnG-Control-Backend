using Microsoft.AspNetCore.Mvc;
using SnG.Control.Api.Contracts;
using SnG.Control.Api.Filters;
using SnG.Control.Application.Features.Measurements;
using SnG.Control.Application.Features.ProjectDocuments;
namespace SnG.Control.Api.Controllers;

[ApiController]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/projects/{id:guid}/measurements")]
public sealed class MeasurementsController(MeasurementService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<MeasurementState>(200)]
    public async Task<IActionResult> Read(Guid id, CancellationToken token) => await service.ReadAsync(id, token) is { } state ? Ok(state) : NotFound();
    [HttpGet("submissions/{submissionId:guid}/report")]
    public async Task<IActionResult> Report(Guid id, Guid submissionId, CancellationToken token)
    {
        Response.Headers.CacheControl = "no-store"; Response.Headers["X-Content-Type-Options"] = "nosniff";
        return await service.ReadReportAsync(id, submissionId, token) is { } report ? File(report.Bytes, "application/pdf", report.FileName) : NotFound();
    }
    [HttpPost("submissions")]
    [RequestSizeLimit(131072)]
    public async Task<IActionResult> Submit(Guid id, MeasurementSubmissionCommand body, CancellationToken token) => StatusCode(201, await service.SubmitAsync(id, body, token));
    [HttpPost("initialize")]
    public async Task<IActionResult> Initialize(Guid id, CancellationToken token)
    {
        await service.InitializeAsync(id, token); return NoContent();
    }
    [HttpGet("structures/{structureId:guid}/sketches/{versionId:guid}")]
    public async Task<IActionResult> Sketch(Guid id, Guid structureId, Guid versionId, CancellationToken token) =>
        await service.ReadSketchAsync(id, structureId, versionId, token) is { } drawing ? Ok(drawing) : NotFound();
    [HttpGet("structures/{structureId:guid}/sketches/{versionId:guid}/image")]
    public async Task<IActionResult> SketchImage(Guid id, Guid structureId, Guid versionId, CancellationToken token)
    {
        Response.Headers.CacheControl = "no-store"; Response.Headers["X-Content-Type-Options"] = "nosniff";
        return await service.ReadSketchImageAsync(id, structureId, versionId, token) is { } bytes ? File(bytes, "image/webp") : NotFound();
    }
    [HttpPost("commands")]
    [RequestSizeLimit(MeasurementSketch.MaxRequestBytes)]
    public async Task<IActionResult> Execute(Guid id, MeasurementCommand body, CancellationToken token)
    {
        await service.ExecuteAsync(id, body, token); return NoContent();
    }
    [HttpPost("documents/batch")]
    [DisableFormValueModelBinding]
    [RequestSizeLimit(ProjectDocumentMultipartReader.MaxRequestBytes)]
    [ProducesResponseType<DocumentBatchResponse>(201)]
    public async Task<IActionResult> Upload(Guid id, [FromQuery] Guid requestKey, [FromServices] IMeasurementDocumentStore documents, CancellationToken token)
    {
        if (requestKey == Guid.Empty) throw new DocumentException(400, "InvalidDocumentBatch");
        await documents.RequireMeasurementUploadAccessAsync(id, token);
        try { return StatusCode(201, await documents.UploadMeasurementsAsync(id, ProjectDocumentMultipartReader.ReadAsync(Request, token), token, requestKey)); }
        catch (BadHttpRequestException e) when (e.StatusCode == 413) { throw new DocumentException(413, "BatchTooLarge"); }
    }
}
