using Microsoft.AspNetCore.Mvc;
using SnG.Control.Api.Contracts;
using SnG.Control.Api.Filters;
using SnG.Control.Application.Features.Engineering;
using SnG.Control.Infrastructure.Persistence.Stores;
namespace SnG.Control.Api.Controllers;
[ApiController]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
[Route("api/projects/{id:guid}/incremental-opening-records")]
public sealed class CommercialOpeningController(CommercialOpeningService service):ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Read(Guid id,CancellationToken token)=>await service.ReadAsync(id,token) is {} state?Ok(state):NotFound();
    [HttpPost("{actaId:guid}/commands")]
    public async Task<IActionResult> Command(Guid id,Guid actaId,CommercialOpeningCommand command,CancellationToken token)=>Ok(await service.CommandAsync(id,actaId,command,token));
    [HttpPost("{actaId:guid}/rounds/{roundId:guid}/documents")]
    [DisableFormValueModelBinding]
    [RequestSizeLimit(EfEngineeringStore.MaxPdfBytes+200_000)]
    public async Task<IActionResult> Upload(Guid id,Guid actaId,Guid roundId,CancellationToken token)
    {service.RequireWrite();var upload=await CommercialOpeningMultipartReader.OpenAsync(Request,token);await service.UploadAsync(id,actaId,roundId,upload.Manifest,upload.Files,token);return NoContent();}
    [HttpGet("{actaId:guid}/documents/{documentId:guid}/download")]
    public async Task<IActionResult> Download(Guid id,Guid actaId,Guid documentId,CancellationToken token)
    {var file=await service.DownloadAsync(id,actaId,documentId,token);Response.Headers["X-Content-Type-Options"]="nosniff";return File(file.Content,"application/pdf",file.FileName);}
}
