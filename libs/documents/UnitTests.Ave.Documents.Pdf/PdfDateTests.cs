using Ave.Documents.Pdf;

namespace UnitTests.Ave.Documents.Pdf;

/// <summary>Date strings, ISO 32000-1 §7.9.4: the offset from UT is written as HH'mm, minutes included.</summary>
public class PdfDateTests
{
    [Theory]
    [InlineData(5, 30, "(D:20261115092205+05'30)")]
    [InlineData(-3, -30, "(D:20261115092205-03'30)")]
    [InlineData(12, 45, "(D:20261115092205+12'45)")]
    public void An_offset_with_minutes_keeps_its_minutes(int hours, int minutes, string expected) =>
        PdfSerializer.Serialize(PdfDate.Format(new DateTimeOffset(2026, 11, 15, 9, 22, 5, new TimeSpan(hours, minutes, 0)))).Value
            .Should().Be(expected);
}
