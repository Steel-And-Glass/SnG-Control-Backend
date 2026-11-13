using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SnG.Control.Api.Authorization;
using SnG.Control.Application.Features.Management;
using SnG.Control.Domain.Enums;
namespace SnG.Control.Api.Controllers;

[ApiController]
[Route("api/management/analytics/surfaces")]
[Authorize(Policy = ManagementRequirement.PolicyName)]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class ManagementSurfacesController(IManagementSurfaceStore store) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ManagementSurfacesResponse>> Read([FromQuery] Guid? projectId,
        [FromQuery] Guid? clientId,[FromQuery] string? city,[FromQuery] string? productLine,
        [FromQuery] string? area,[FromQuery] string? status,[FromQuery] int skip=0,
        [FromQuery] bool ascending=false,CancellationToken token=default)
    {
        string[] allowed=["projectId","clientId","city","productLine","area","status","skip","ascending"];
        if(Request.Query.Any(q=>!allowed.Contains(q.Key)||q.Value.Count!=1) || skip<0 || skip>1000000 || city?.Length>200 ||
            projectId==Guid.Empty || clientId==Guid.Empty ||
            !Valid<ProductLine>(productLine)||!Valid<OperationalArea>(area)||!Valid<ProjectTaskStatus>(status)) return BadRequest();
        return Ok(await store.ReadAsync(new(projectId,clientId,city,Parse<ProductLine>(productLine),Parse<OperationalArea>(area),Parse<ProjectTaskStatus>(status),skip,ascending),token));
    }
    private static bool Valid<T>(string? value) where T:struct,Enum => value==null || Enum.GetNames<T>().Contains(value,StringComparer.Ordinal);
    private static T? Parse<T>(string? value) where T:struct,Enum => value==null?null:Enum.Parse<T>(value);
}
