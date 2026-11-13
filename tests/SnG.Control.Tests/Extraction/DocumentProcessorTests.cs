using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using SnG.Control.Application.Features.Operations;
using SnG.Control.Infrastructure.Extraction;
namespace SnG.Control.Tests.Extraction;
public sealed class ExtractionPythonFactAttribute:FactAttribute
{
    public ExtractionPythonFactAttribute(){if(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SNG_EXTRACTION_TEST_PYTHON")))Skip="Requires isolated test Python with pinned PDFium; no operational environment used.";}
}
public class DocumentProcessorTests
{
    private static DocumentExtractionOptions Config()=>new(){Enabled=true,PythonExecutable=Environment.GetEnvironmentVariable("SNG_EXTRACTION_TEST_PYTHON")!};
    internal static byte[] Pdf(params string[] lines)
    {
        static string Escape(string s)=>s.Replace("\\","\\\\").Replace("(","\\(").Replace(")","\\)");
        var content="BT /F1 8 Tf 20 800 Td "+string.Join(" 0 -14 Td ",lines.Select(l=>$"({Escape(l)}) Tj"))+" ET";
        var objects=new[]{"<< /Type /Catalog /Pages 2 0 R >>","<< /Type /Pages /Kids [3 0 R] /Count 1 >>","<< /Type /Page /Parent 2 0 R /MediaBox [0 0 1200 842] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>","<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",$"<< /Length {Encoding.Latin1.GetByteCount(content)} >>\nstream\n{content}\nendstream"};
        var text="%PDF-1.4\n";var offsets=new List<int>();for(var i=0;i<objects.Length;i++){offsets.Add(Encoding.Latin1.GetByteCount(text));text+=$"{i+1} 0 obj\n{objects[i]}\nendobj\n";}
        var xref=Encoding.Latin1.GetByteCount(text);text+="xref\n0 6\n0000000000 65535 f \n"+string.Concat(offsets.Select(x=>$"{x:0000000000} 00000 n \n"))+$"trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF";return Encoding.Latin1.GetBytes(text);
    }
    private static ExtractionInput Input(byte[]? cut=null)
    {
        var a=cut??Pdf("Lista de corte (No Optimizada) - 2 Piezas (1 of 1)","(SERIES) REF1","Longitud total: 3 m Peso Total: 2 kg PROFILE","Pieza ID Numero Medida (mm) CT (mm) Angulo IZ Angulo DR Estructura","OP100-001.0001.00.MAR.H 2 1470 1374 45 90 1");
        var g=Pdf("Lista Vidrios - Numero piezas: 2 (1 of 1)","(VIDRIOS) 08MM","Cantidad Medidas (LxH) Perimetro Area (m2) Tipo Estructura: Peso Unitario Peso Total","2 200 x 300 1 m 0,1 m2 OP100 1 2 kg 4 kg");
        ExtractionInputDocument Doc(byte[] b,string kind)=>new(Guid.NewGuid(),kind,Convert.ToHexString(SHA256.HashData(b)),b.Length,b);
        return new(new string('A',64),["OP100"],100,[Doc(a,"aluminum"),Doc(g,"glass")]);
    }
    [ExtractionPythonFact] public async Task RealProcessPreservesNumbersRegionsAndUnverifiedGlass()
    {
        var processor=new PythonDocumentComponentProcessor(Options.Create(Config()));var input=Input();
        var first=await processor.ProcessAsync(input,default);var second=await processor.ProcessAsync(input,default);Assert.Equal(first.GetRawText(),second.GetRawText());
        var a=first.GetProperty("documents")[0];Assert.Equal(2,a.GetProperty("calculatedTotal").GetInt32());
        var row=a.GetProperty("rows")[0];Assert.Equal("1470",row.GetProperty("measure").GetString());Assert.Equal("1374",row.GetProperty("ct").GetString());Assert.Equal("SERIES",row.GetProperty("series").GetString());Assert.Equal(4,row.GetProperty("source").GetProperty("bbox").GetArrayLength());
        Assert.False(first.GetProperty("documents")[1].GetProperty("rows")[0].GetProperty("materialVerified").GetBoolean());Assert.False(a.TryGetProperty("units",out _));
    }
    [ExtractionPythonFact] public async Task MalformedPdfAndWrongHashNeverBecomeSuccess()
    {
        var processor=new PythonDocumentComponentProcessor(Options.Create(Config()));var malformed=await processor.ProcessAsync(Input([1,2,3]),default);Assert.Equal("DOCUMENT_UNREADABLE_OR_LIMIT",malformed.GetProperty("documents")[0].GetProperty("issues")[0].GetProperty("code").GetString());
        var input=Input();input.Documents[0]=input.Documents[0] with {Sha256=new string('0',64)};
        await Assert.ThrowsAsync<ExtractionProcessingException>(()=>processor.ProcessAsync(input,default));
    }
    [ExtractionPythonFact] public async Task PageLimitAndCancellationStopProcessor()
    {
        var processor=new PythonDocumentComponentProcessor(Options.Create(Config()));
        var limited=await processor.ProcessAsync(Input() with{MaxPages=0},default);Assert.Empty(limited.GetProperty("documents")[0].GetProperty("rows").EnumerateArray());Assert.Equal("error",limited.GetProperty("documents")[0].GetProperty("issues")[0].GetProperty("severity").GetString());
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>processor.ProcessAsync(Input(),cancellation.Token));
    }
    [ExtractionPythonFact] public async Task UnknownOpAndTotalMismatchRemainBlockingDiagnostics()
    {
        var input=Input();input=input with{Ops=["OP200"]};var result=await new PythonDocumentComponentProcessor(Options.Create(Config())).ProcessAsync(input,default);
        Assert.Contains(result.GetProperty("documents")[0].GetProperty("issues").EnumerateArray(),i=>i.GetProperty("code").GetString()=="UNKNOWN_OP");
    }
}
