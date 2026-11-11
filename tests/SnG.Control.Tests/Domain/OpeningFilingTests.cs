using SnG.Control.Domain.Entities.Incremental;
namespace SnG.Control.Tests.Domain;
public sealed class OpeningFilingTests
{
    private static readonly DateTime At = new(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);
    private static OpeningFilingWorkspace Workspace() => new(new OpeningFilingDelivery(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), "synthetic-engineer", At);
    [Theory]
    [InlineData(" op 5773 ", "OP5773")]
    [InlineData("1162001", "1162001")]
    public void IdentifiersPreserveOriginalAndNormalize(string original, string normalized)
    { var w = Workspace(); w.SetNumber(original); Assert.Equal(original, w.NumberOriginal); Assert.Equal(normalized, w.NumberNormalized); }
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("OP\n5773")]
    [InlineData("<script>")]
    [InlineData("OP\uFF15")]
    public void RejectsUnsafeIdentifiers(string original) => Assert.Throws<ArgumentException>(() => OpeningFilingRules.NormalizeIdentifier(original));
    [Fact]
    public void ConfirmedNumberAndVersionCannotBeOverwritten()
    {
        var w = Workspace(); w.SetNumber("1162001"); var v = new OpeningFilingVersion(w, 1, null, At, "synthetic-engineer");
        var doc = new OpeningFilingDocument(v, Guid.NewGuid(), "Plans", new string('A', 64), Guid.NewGuid(), 1);
        v.Confirm(At, "synthetic-engineer", "{\"version\":1}"); w.Confirm(At);
        var release = new OpeningFilingRelease(v);
        Assert.Equal(v.ManifestCanonical, release.Canonical); Assert.Equal(v.ManifestSha256, release.Sha256);
        Assert.Throws<InvalidOperationException>(() => w.SetNumber("1162002"));
        Assert.Throws<InvalidOperationException>(() => v.Confirm(At, "synthetic-engineer", "{}"));
        Assert.Throws<InvalidOperationException>(() => doc.Retire(v, false));
        Assert.Throws<InvalidOperationException>(() => new OpeningFilingMember(v, Guid.NewGuid(), "OP1", true));
    }
    [Fact]
    public void AmendmentKeepsFilingAndOriginalRelease()
    {
        var w = Workspace(); w.SetNumber("1162001"); var first = new OpeningFilingVersion(w, 1, null, At, "synthetic-engineer");
        first.Confirm(At, "synthetic-engineer", "{\"version\":1}"); w.Confirm(At); var original = new OpeningFilingRelease(first);
        Assert.Throws<ArgumentException>(() => new OpeningFilingVersion(w, 2, " ", At, "synthetic-engineer"));
        var amendment = new OpeningFilingVersion(w, 2, "Replace drawing", At, "synthetic-engineer");
        amendment.Confirm(At, "synthetic-engineer", "{\"version\":2}");
        Assert.Equal(first.FilingId, amendment.FilingId); Assert.NotEqual(first.Id, amendment.Id);
        Assert.Equal("{\"version\":1}", original.Canonical); Assert.NotEqual(original.Sha256, amendment.ManifestSha256);
    }
    [Fact]
    public void WithdrawalPreservesDocumentIdentityAndDoesNotPermitSecondRetirement()
    {
        var w = Workspace(); var v = new OpeningFilingVersion(w, 1, null, At, "synthetic-engineer"); var id = Guid.NewGuid();
        var doc = new OpeningFilingDocument(v, id, "Sections", new string('A', 64), id, 1); doc.Retire(v, false);
        Assert.Equal(id, doc.DocumentId); Assert.Equal("Withdrawn", doc.State);
        Assert.Throws<InvalidOperationException>(() => doc.Retire(v, true));
    }
}
