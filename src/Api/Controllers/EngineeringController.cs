using Microsoft.AspNetCore.Mvc;
using SnG.Control.Api.Contracts;
using SnG.Control.Api.Filters;
using SnG.Control.Application.Features.Engineering;
using SnG.Control.Infrastructure.Persistence.Stores;
namespace SnG.Control.Api.Controllers;
[ApiController]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/projects/{id:guid}/engineering")]
public sealed class EngineeringController(EngineeringService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Read(Guid id, CancellationToken token) => await service.ReadAsync(id, token) is { } state ? Ok(state) : NotFound();
    [HttpPut("structures/{structureId:guid}/check")]
    public async Task<IActionResult> Check(Guid id, Guid structureId, EngineeringCheck command, CancellationToken token)
    { await service.CheckAsync(id, structureId, command, token); return NoContent(); }
    [HttpPost("complete")]
    public async Task<IActionResult> Complete(Guid id, EngineeringClose command, CancellationToken token)
    { await service.CloseAsync(id, command, token); return NoContent(); }
    [HttpPost("initialize-historical")]
    public async Task<IActionResult> Historical(Guid id, EngineeringHistorical command, CancellationToken token)
    { await service.InitializeHistoricalAsync(id, command, token); return NoContent(); }
    [HttpPost("acta")]
    [DisableFormValueModelBinding]
    [RequestSizeLimit(EfEngineeringStore.MaxPdfBytes + 100_000)]
    public async Task<IActionResult> Upload(Guid id, [FromQuery] Guid requestKey, [FromQuery] long expectedRevision, CancellationToken token)
    { service.RequireWrite(); await service.UploadAsync(id, requestKey, expectedRevision, ProjectDocumentMultipartReader.ReadAsync(Request, token), token); return NoContent(); }
    [HttpGet("acta/{documentId:guid}/download")]
    public async Task<IActionResult> Download(Guid id, Guid documentId, CancellationToken token)
    {
        var file = await service.DownloadAsync(id, documentId, token);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(file.Content, "application/pdf", file.FileName);
    }
    [HttpGet("structures/{structureId:guid}/sketches/{versionId:guid}/image")]
    public async Task<IActionResult> Sketch(Guid id, Guid structureId, Guid versionId, CancellationToken token)
    { Response.Headers["X-Content-Type-Options"] = "nosniff"; return await service.SketchAsync(id, structureId, versionId, token) is { } bytes ? File(bytes, "image/webp") : NotFound(); }
}
