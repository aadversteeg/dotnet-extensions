using Ave.Extensions.Documents.Pdf;
using Ave.Extensions.Documents.Render.Pdf;

namespace UnitTests.Extensions.Documents.Render.Pdf;

/// <summary>Render options are a value: two options are equal exactly when they write the same file — the same date
/// (the same instant in the same offset, since the offset is written), profile and file identifier, wherever the
/// identifier's bytes came from.</summary>
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

    [Fact]
    public void The_same_instant_in_another_offset_is_another_date_since_it_is_written_differently()
    {
        // 08:00 UTC and 09:00 at +01:00 are one instant; the file writes D:20261116080000Z or D:20261116090000+01'00',
        // so the options differ. Equal options write the same bytes.
        var utc = new PdfRenderOptions(Date);
        var amsterdam = new PdfRenderOptions(Date.ToOffset(TimeSpan.FromHours(1)));

        utc.CreationDate.Should().Be(amsterdam.CreationDate, "DateTimeOffset compares instants");
        utc.Should().NotBe(amsterdam);
        utc.Should().Be(new PdfRenderOptions(Date));
        utc.GetHashCode().Should().Be(new PdfRenderOptions(Date).GetHashCode());
        PdfDate.Format(utc.CreationDate).Should().NotBe(PdfDate.Format(amsterdam.CreationDate));
    }
}
