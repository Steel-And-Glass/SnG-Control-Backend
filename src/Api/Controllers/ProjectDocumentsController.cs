using Microsoft.AspNetCore.Mvc;
using SnG.Control.Api.Contracts;
using SnG.Control.Api.Filters;
using SnG.Control.Application.Features.ProjectDocuments;

namespace SnG.Control.Api.Controllers;

[ApiController]
[Route("api/projects/{id}/documents")]
public sealed class ProjectDocumentsController(ProjectDocumentService service) : ControllerBase
{
    [HttpPost("batch")]
    [DisableFormValueModelBinding]
    [RequestSizeLimit(ProjectDocumentMultipartReader.MaxRequestBytes)]
    [ProducesResponseType<DocumentBatchResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    public async Task<ActionResult<DocumentBatchResponse>> Upload(Guid id, CancellationToken token)
    {
        await service.RequireUploadAccessAsync(id, token);
        try { return StatusCode(201, await service.UploadAsync(id, ProjectDocumentMultipartReader.ReadAsync(Request, token), token)); }
        catch (BadHttpRequestException exception) when (exception.StatusCode == 413) { throw new DocumentException(413, "BatchTooLarge"); }
    }
    [HttpGet]
    public Task<DocumentListResponse> List(Guid id, CancellationToken token) => service.ListAsync(id, token);
    [HttpDelete("{documentId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Remove(Guid id, Guid documentId, CancellationToken token)
    {
        await service.RemoveAsync(id, documentId, token);
        return NoContent();
    }
    [HttpGet("{documentId:guid}/file")]
    public async Task<IActionResult> Download(Guid id, Guid documentId, CancellationToken token)
    {
        var file = await service.DownloadAsync(id, documentId, token);
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(file.Content, "application/octet-stream", file.FileName);
    }
    [HttpGet("current.zip")]
    public Task<IActionResult> CurrentZip(Guid id, CancellationToken token) => Zip(id, false, token);
    [HttpGet("initial-handoff.zip")]
    public Task<IActionResult> InitialZip(Guid id, CancellationToken token) => Zip(id, true, token);
    private async Task<IActionResult> Zip(Guid id, bool initial, CancellationToken token)
    {
        var file = await service.ZipAsync(id, initial, token);
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(file.Content, "application/zip", file.FileName);
    }
}
