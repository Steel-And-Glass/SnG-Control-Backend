using Microsoft.AspNetCore.Mvc;
using SnG.Control.Application.Features.Operations;
namespace SnG.Control.Api.Controllers;
[ApiController]
[Route("api/operations")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class OperationsPreparationsController(ProductionPreparationService service, IDocumentExtractionStore extraction, SnG.Control.Application.Identity.ICurrentUser current) : ControllerBase
{
    [HttpPost("preparations/{id:guid}/extraction")]
    public async Task<IActionResult> Extract(Guid id,CancellationToken token)
    {
        service.RequireWrite();
        if(Request.ContentLength is >0 || Request.ContentType!=null || Request.Headers.ContainsKey("Transfer-Encoding"))return BadRequest();
        if(await Request.Body.ReadAsync(new byte[1],token)!=0)return BadRequest();
        return Accepted(await extraction.QueueAsync(id,current.Subject,token));
    }
    [HttpGet("preparations/{id:guid}/extraction")]
    public async Task<IActionResult> Extraction(Guid id,CancellationToken token)
    {service.RequireRead();return await extraction.ReadAsync(id,token) is {} job?Ok(job):NotFound();}
    [HttpGet("preparations/{id:guid}/candidates")]
    public async Task<IActionResult> Candidates(Guid id,[FromQuery]int skip=0,CancellationToken token=default)
    {service.RequireRead();return Ok(await extraction.CandidatesAsync(id,skip,token));}
    [HttpGet("releases")]
    public async Task<IActionResult> Releases([FromQuery] int skip = 0, CancellationToken token = default) => Ok(await service.ReleasesAsync(skip, token));
    [HttpGet("preparations")]
    public async Task<IActionResult> List([FromQuery] int skip = 0, CancellationToken token = default) => Ok(await service.ListAsync(skip, token));
    [HttpGet("preparations/{id:guid}")]
    public async Task<IActionResult> Read(Guid id, CancellationToken token) => await service.ReadAsync(id, token) is { } detail ? Ok(detail) : NotFound();
    [HttpPost("releases/{versionId:guid}/preparation")]
    public async Task<IActionResult> Create(Guid versionId, CancellationToken token)
    {
        service.RequireWrite();
        // Bodyless command: clients cannot inject state, components, approval or an alternative manifest.
        if (Request.ContentLength is > 0 || Request.ContentType != null || Request.Headers.ContainsKey("Transfer-Encoding")) return BadRequest();
        var probe = new byte[1]; if (await Request.Body.ReadAsync(probe, token) != 0) return BadRequest();
        return Ok(await service.CreateAsync(versionId, token));
    }
}
