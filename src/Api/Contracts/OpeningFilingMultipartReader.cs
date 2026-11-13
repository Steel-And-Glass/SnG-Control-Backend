using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using SnG.Control.Application.Features.Engineering;
using SnG.Control.Application.Features.ProjectDocuments;
namespace SnG.Control.Api.Contracts;
public static class OpeningFilingMultipartReader
{
    private const int ManifestLimit=131072;
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web){UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,MaxDepth=6};
    private static DocumentException Invalid()=>new(400,"InvalidDocumentBatch");
    public static async Task<(OpeningFilingUpload Manifest,IAsyncEnumerable<DocumentUpload> Files)> OpenAsync(HttpRequest request,CancellationToken token)
    {
        if(!MediaTypeHeaderValue.TryParse(request.ContentType,out var type)||!type.MediaType.Equals("multipart/form-data",StringComparison.OrdinalIgnoreCase))throw Invalid();
        var boundary=HeaderUtilities.RemoveQuotes(type.Boundary).Value;if(string.IsNullOrEmpty(boundary)||boundary.Length>128)throw Invalid();
        var reader=new MultipartReader(boundary,request.Body){HeadersCountLimit=16,HeadersLengthLimit=8192};
        var first=await Next(reader,token)??throw Invalid();var disposition=Disposition(first);
        if(HeaderUtilities.RemoveQuotes(disposition.Name).Value!="manifest"||!MediaTypeHeaderValue.TryParse(first.ContentType,out var json)||!json.MediaType.Equals("application/json",StringComparison.OrdinalIgnoreCase)||(json.Charset.HasValue&&!HeaderUtilities.RemoveQuotes(json.Charset).Equals("utf-8",StringComparison.OrdinalIgnoreCase)))throw Invalid();
        var bytes=new byte[ManifestLimit+1];var length=0;int n;
        try{while(length<bytes.Length&&(n=await first.Body.ReadAsync(bytes.AsMemory(length),token))>0)length+=n;}catch(IOException){throw Invalid();}catch(InvalidDataException){throw Invalid();}
        if(length==0||length>ManifestLimit)throw Invalid();OpeningFilingUpload manifest;
        try{manifest=JsonSerializer.Deserialize<OpeningFilingUpload>(bytes.AsSpan(0,length),Json)??throw Invalid();}catch(JsonException){throw Invalid();}
        return(manifest,ReadFile(reader,token));
    }
    private static async IAsyncEnumerable<DocumentUpload> ReadFile(MultipartReader reader,[EnumeratorCancellation]CancellationToken token)
    {
        var part=await Next(reader,token)??throw Invalid();var disposition=Disposition(part);
        var name=HeaderUtilities.RemoveQuotes(disposition.FileNameStar.HasValue?disposition.FileNameStar:disposition.FileName).Value;
        if(HeaderUtilities.RemoveQuotes(disposition.Name).Value!="file"||name==null)throw Invalid();
        yield return new("file",name,part.ContentType,null,part.Body);
        if(await Next(reader,token)!=null)throw Invalid();
    }
    private static ContentDispositionHeaderValue Disposition(MultipartSection part)
    {if(!ContentDispositionHeaderValue.TryParse(part.ContentDisposition,out var d)||!d.DispositionType.Equals("form-data",StringComparison.OrdinalIgnoreCase))throw Invalid();return d;}
    private static async Task<MultipartSection?> Next(MultipartReader reader,CancellationToken token)
    {try{return await reader.ReadNextSectionAsync(token);}catch(IOException){throw Invalid();}catch(InvalidDataException){throw Invalid();}}
}
