using Microsoft.AspNetCore.Mvc;
using SnG.Control.Application.Features.Operations;
using SnG.Control.Application.Identity;
namespace SnG.Control.Api.Controllers;
[ApiController]
[Route("api/operations/preparations/{preparation:guid}/reviews")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class ProductionReviewsController(ProductionReviewAccess access,IProductionReviewStore store,ICurrentUser current):ControllerBase
{
    [HttpGet] public async Task<IActionResult> List(Guid preparation,CancellationToken token){access.Read();return Ok(await store.ListAsync(preparation,token));}
    [HttpGet("{review:guid}")] public async Task<IActionResult> Read(Guid preparation,Guid review,CancellationToken token){access.Read();return await store.ReadAsync(preparation,review,token) is {} view?Ok(view):NotFound();}
    [HttpGet("history")] public async Task<IActionResult> History(Guid preparation,[FromQuery]int skip=0,CancellationToken token=default){access.Read();return Ok(await store.HistoryAsync(preparation,skip,token));}
    [HttpPost("commands")][RequestSizeLimit(1048576)] public async Task<IActionResult> Command(Guid preparation,[FromBody]ProductionReviewCommand command,CancellationToken token){access.Write();return Ok(await store.CommandAsync(preparation,command,current.Subject,token));}
    [HttpPost("evidence")][RequestSizeLimit(27000000)][RequestFormLimits(MultipartBodyLengthLimit=27000000)]
    public async Task<IActionResult> Upload(Guid preparation,[FromForm]Guid requestKey,[FromForm]IFormFile file,CancellationToken token)
    {
        access.Write();if(file.Length is <1 or >26214400)return StatusCode(413);
        await using var input=file.OpenReadStream();using var output=new MemoryStream();var buffer=new byte[65536];int count;
        while((count=await input.ReadAsync(buffer,token))>0){if(output.Length+count>26214400)return StatusCode(413);await output.WriteAsync(buffer.AsMemory(0,count),token);}
        return Ok(await store.UploadAsync(preparation,requestKey,file.FileName,file.ContentType,output.ToArray(),current.Subject,token));
    }
    [HttpGet("evidence/{document:guid}")]
    public async Task<IActionResult> Attachment(Guid preparation,Guid document,CancellationToken token){access.Read();var result=await store.DocumentAsync(preparation,document,true,token);Response.Headers["X-Content-Type-Options"]="nosniff";return File(result.Bytes,result.ContentType,result.Name);}
    [HttpGet("sources/{document:guid}")]
    public async Task<IActionResult> Source(Guid preparation,Guid document,CancellationToken token){access.Read();var result=await store.DocumentAsync(preparation,document,false,token);Response.Headers["X-Content-Type-Options"]="nosniff";return File(result.Bytes,result.ContentType,result.Name);}
}
