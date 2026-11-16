using Ave.Documents.Pdf;
using Ave.Documents.Render.Pdf;

namespace UnitTests.Ave.Documents.Render.Pdf;

/// <summary>Render options are a value: two options with the same date, profile and file identifier are equal, wherever
/// the identifier's bytes came from.</summary>
public class RenderOptionsTests
{
    private static readonly DateTimeOffset Date = new(2026, 11, 16, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Options_with_equal_file_identifiers_are_equal()
    {
        PdfRenderOptions Build() => new(Date, [.. new byte[] { 1, 2, 3 }]) { Profile = PdfProfile.PdfA3b };

        Build().Should().Be(Build());
        Build().GetHashCode().Should().Be(Build().GetHashCode());
        Build().Should().NotBe(Build() with { FileIdentifier = [.. new byte[] { 9 }] });
        Build().Should().NotBe(Build() with { FileIdentifier = null });
    }
}
