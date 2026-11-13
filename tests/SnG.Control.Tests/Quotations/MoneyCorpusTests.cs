using System.IO.Compression;
using SnG.Control.Application.Features.CommercialQuotations;
using SnG.Control.Domain.ValueObjects;
using SnG.Control.Tests.Fixtures;

namespace SnG.Control.Tests.Quotations;

public sealed class MoneyCorpusFactAttribute : FactAttribute
{
    public MoneyCorpusFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SNG_MONEY_CORPUS_ZIP")))
            Skip = "Optional read-only quotation corpus, kept outside Git.";
    }
}

public sealed class MoneyCorpusTests
{
    [MoneyCorpusFact]
    public async Task ReadOnlyCorpusPreservesTechnicalValuesAndNormalizesAllMoney()
    {
        using var zip = ZipFile.OpenRead(Environment.GetEnvironmentVariable("SNG_MONEY_CORPUS_ZIP")!);
        int accepted = 0, rejected = 0, items = 0;
        foreach (var entry in zip.Entries.Where(e => e.FullName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)))
        {
            await using var stream = entry.Open(); using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            var result = await CommercialQuotationScenario.Parser().ParseAsync(buffer.ToArray(), "synthetic.xlsx", default);
            if (result.Status != CommercialQuotationStatus.Success) { rejected++; continue; }
            accepted++; items += result.Quotation!.Items.Count;
            Assert.Equal(result.Quotation.ProjectTotal, Money.Normalize(result.Quotation.ProjectTotal));
            foreach (var item in result.Quotation.Items)
            {
                Assert.Equal(item.UnitPrice, Money.Normalize(item.UnitPrice));
                Assert.Equal(item.TotalPrice, Money.Normalize(item.TotalPrice));
            }
        }
        // Existing approved corpus baseline. No names, prices, SGs or document content in output.
        Assert.Equal(62, accepted); Assert.Equal(4, rejected); Assert.Equal(1038, items);
    }
}
