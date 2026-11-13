using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SnG.Control.Application.Features.Operations;
namespace SnG.Control.Infrastructure.Extraction;
public sealed class PythonDocumentComponentProcessor(IOptions<DocumentExtractionOptions> options) : IDocumentComponentProcessor
{
    public async Task<JsonElement> ProcessAsync(ExtractionInput input,CancellationToken token)
    {
        var config=options.Value;
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(config.TimeoutSeconds));
        var start=new ProcessStartInfo(config.PythonExecutable) { UseShellExecute=false,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true };
        var systemRoot=Environment.GetEnvironmentVariable("SystemRoot");start.Environment.Clear();
        if(systemRoot!=null)start.Environment["SystemRoot"]=systemRoot;
        start.ArgumentList.Add("-I");start.ArgumentList.Add("-B");
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory,"Extraction","Python","runner.py"));
        start.ArgumentList.Add(config.MemoryMb.ToString(System.Globalization.CultureInfo.InvariantCulture));
        using var process=new Process {StartInfo=start};
        try
        {
            if(!process.Start())throw new ExtractionProcessingException("ProcessorUnavailable");
            using var registration=timeout.Token.Register(()=> { try { if(!process.HasExited)process.Kill(entireProcessTree:true); } catch(InvalidOperationException){} });
            var output=ReadBounded(process.StandardOutput,16000000,timeout.Token);
            var errors=ReadBounded(process.StandardError,65536,timeout.Token); // Never log private parser output.
            await JsonSerializer.SerializeAsync(process.StandardInput.BaseStream,input,new JsonSerializerOptions(JsonSerializerDefaults.Web),timeout.Token);
            process.StandardInput.Close();await process.WaitForExitAsync(timeout.Token);
            var text=await output;await errors;
            if(process.ExitCode!=0)throw new ExtractionProcessingException("ProcessorFailed");
            using var result=JsonDocument.Parse(text,new JsonDocumentOptions{MaxDepth=32});
            return result.RootElement.Clone();
        }
        catch(OperationCanceledException) when(!token.IsCancellationRequested) {throw new ExtractionProcessingException("ProcessorTimeout");}
        catch(Exception e) when(e is System.ComponentModel.Win32Exception or IOException or JsonException) {throw new ExtractionProcessingException("ProcessorFailed");}
        finally { try { if(process.Id>0 && !process.HasExited) { process.Kill(entireProcessTree:true);await process.WaitForExitAsync(CancellationToken.None); } }catch(InvalidOperationException){} }
    }
    private static async Task<string> ReadBounded(StreamReader reader,int maximum,CancellationToken token)
    {
        var text=new StringBuilder();var buffer=new char[4096];int count;
        while((count=await reader.ReadAsync(buffer,token))>0)
        { if(text.Length+count>maximum)throw new ExtractionProcessingException("ProcessorOutputLimit");text.Append(buffer,0,count); }
        return text.ToString();
    }
}
