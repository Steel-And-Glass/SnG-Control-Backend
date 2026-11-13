using Microsoft.AspNetCore.Mvc;
using SnG.Control.Api.Contracts;
using SnG.Control.Api.Filters;
using SnG.Control.Application.Features.Engineering;
using SnG.Control.Infrastructure.Persistence.Stores;
namespace SnG.Control.Api.Controllers;
[ApiController]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/projects/{id:guid}/opening-filings")]
public sealed class OpeningFilingController(OpeningFilingService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Read(Guid id, CancellationToken token) => await service.ReadAsync(id, token) is { } state ? Ok(state) : NotFound();
    [HttpPost("{filingId:guid}/commands")]
    public async Task<IActionResult> Command(Guid id, Guid filingId, OpeningFilingCommand command, CancellationToken token)
    { await service.CommandAsync(id, filingId, command, token); return NoContent(); }
    [HttpPost("{filingId:guid}/documents")]
    [DisableFormValueModelBinding]
    [RequestSizeLimit(EfEngineeringStore.MaxPdfBytes + 200_000)]
    public async Task<IActionResult> Upload(Guid id, Guid filingId, CancellationToken token)
    { service.RequireWrite(); var upload = await OpeningFilingMultipartReader.OpenAsync(Request, token); await service.UploadAsync(id, filingId, upload.Manifest, upload.Files, token); return NoContent(); }
    [HttpGet("{filingId:guid}/versions/{versionId:guid}/documents/{documentId:guid}/download")]
    public async Task<IActionResult> Download(Guid id, Guid filingId, Guid versionId, Guid documentId, CancellationToken token)
    { var file = await service.DownloadAsync(id, filingId, versionId, documentId, token); Response.Headers["X-Content-Type-Options"] = "nosniff"; return File(file.Content, file.ContentType, file.FileName); }
}
