using Microsoft.AspNetCore.Mvc;
using SnG.Control.Api.Contracts;
using SnG.Control.Api.Filters;
using SnG.Control.Application.Features.Engineering;
using SnG.Control.Infrastructure.Persistence.Stores;
namespace SnG.Control.Api.Controllers;
[ApiController]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
[Route("api/projects/{id:guid}/engineering")]
public sealed class EngineeringOperationsController(EngineeringOperationsService service):ControllerBase
{
    [HttpPost("workspace/commands")]
    public async Task<IActionResult> Command(Guid id,EngineeringCommand command,CancellationToken token)=>Ok(await service.ExecuteAsync(id,command,token));
    [HttpPost("opening-records/{actaId:guid}/documents")]
    [DisableFormValueModelBinding]
    [RequestSizeLimit(EfEngineeringStore.MaxPdfBytes+100_000)]
    public async Task<IActionResult> Upload(Guid id,Guid actaId,[FromQuery] Guid requestKey,[FromQuery] long expectedRevision,[FromQuery] string scopeHash,CancellationToken token)
    { service.RequireWrite();await service.UploadAsync(id,actaId,requestKey,expectedRevision,scopeHash,ProjectDocumentMultipartReader.ReadAsync(Request,token),token);return NoContent(); }
    [HttpGet("opening-records/{actaId:guid}/documents/{documentId:guid}/download")]
    public async Task<IActionResult> Download(Guid id,Guid actaId,Guid documentId,CancellationToken token)
    { var file=await service.DownloadAsync(id,actaId,documentId,token);Response.Headers["X-Content-Type-Options"]="nosniff";return File(file.Content,"application/pdf",file.FileName); }
}
