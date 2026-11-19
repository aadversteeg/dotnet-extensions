using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.Documents.Fonts.Tables;
using Ave.Extensions.Documents.TestSupport;

namespace UnitTests.Extensions.Documents.Fonts;

/// <summary>
/// Which table a font's line metrics come from (<see cref="FontMetrics.Line"/>), as Chromium reads them on Linux: OS/2
/// typographic metrics when the font sets USE_TYPO_METRICS (fsSelection bit 7), otherwise 'hhea', and where 'hhea' gives
/// no ascender and no descender, OS/2 typographic and then Windows metrics. The test fonts all set the bit (Lato with
/// typographic metrics equal to 'hhea', Theano Didot with other ones); the other paths are checked on Theano Didot with
/// its bytes changed.
/// </summary>
public class LineMetricsTests
{
    [Fact]
    public void Theano_Didot_sets_USE_TYPO_METRICS_and_its_line_metrics_are_the_typographic_ones()
    {
        var metrics = TestFonts.TheanoDidot.Metrics;

        metrics.Line.Should().Be(new LineMetrics(1580, -468, 560, LineMetricsSource.Typo));
        (metrics.Ascender, metrics.Descender, metrics.LineGap).Should().Be((1962, -630, 560), "'hhea' stays as it is, for the PDF font descriptor");
    }

    [Fact]
    public void Lato_sets_USE_TYPO_METRICS_with_the_same_values_as_hhea()
    {
        TestFonts.Regular.Metrics.Line.Should().Be(new LineMetrics(1974, -426, 0, LineMetricsSource.Typo));
        TestFonts.Regular.Metrics.Line.Ascender.Should().Be(TestFonts.Regular.Metrics.Ascender);
    }

    [Fact]
    public void Without_USE_TYPO_METRICS_the_line_metrics_are_hhea()
    {
        var font = Theano(os2: table => table[63] &= 0x7F);

        font.Metrics.Line.Should().Be(new LineMetrics(1962, -630, 560, LineMetricsSource.Hhea));
    }

    [Fact]
    public void Without_USE_TYPO_METRICS_and_without_hhea_ascender_and_descender_the_typographic_then_the_Windows_metrics_are_used()
    {
        static void NoHhea(Span<byte> table)
        {
            BinaryPrimitives.WriteInt16BigEndian(table[4..], 0);
            BinaryPrimitives.WriteInt16BigEndian(table[6..], 0);
        }

        Theano(os2: table => table[63] &= 0x7F, hhea: NoHhea).Metrics.Line.Should().Be(new LineMetrics(1580, -468, 560, LineMetricsSource.Typo));
        Theano(os2: table =>
        {
            table[63] &= 0x7F;
            table[68..74].Clear();
        }, hhea: NoHhea).Metrics.Line.Should().Be(new LineMetrics(1962, -630, 0, LineMetricsSource.Win));
    }

    private delegate void Patch(Span<byte> table);

    /// <summary>Theano Didot with its 'OS/2' and 'hhea' tables changed in place (checksums are not checked).</summary>
    private static TrueTypeFont Theano(Patch? os2 = null, Patch? hhea = null)
    {
        var bytes = TestFonts.Bytes("TheanoDidot-Regular.ttf");
        var tables = TableDirectory.Parse(bytes).Value.Tables;
        foreach (var (tag, patch) in new[] { ("OS/2", os2), ("hhea", hhea) })
        {
            if (patch is not null)
            {
                MemoryMarshal.TryGetArray(tables[tag], out var table).Should().BeTrue();
                patch(bytes.AsSpan(table.Offset, table.Count));
            }
        }

        return FontParser.Parse(bytes).Value;
    }
}
