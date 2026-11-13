using System.Text.Json;
using SnG.Control.Application.Features.Measurements;
namespace SnG.Control.Tests.TestDoubles;

internal static class BrowserSketchFixture
{
    public static SketchUpload Upload()
    {
        using var json = Open("drawing.json");
        var drawing = JsonSerializer.Deserialize<SketchDrawing>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        using var image = Open("browser.webp"); using var bytes = new MemoryStream(); image.CopyTo(bytes);
        return new(drawing, bytes.ToArray());
    }
    private static Stream Open(string name) => typeof(BrowserSketchFixture).Assembly.GetManifestResourceStream(
        $"SnG.Control.Tests.Fixtures.BrowserSketch.{name}")!;
}
