using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using Ave.Documents.Core;
using Ave.Documents.Fonts;
using Ave.Documents.PageModel;
using Ave.Documents.Render.Pdf;
using Ave.Documents.TestSupport;
using Ave.Documents.Typesetting;

namespace UnitTests.Ave.Documents.Render.Pdf;

public class PdfRendererTests
{
    private static readonly PdfRenderOptions Options = new(new DateTimeOffset(2026, 11, 15, 9, 22, 0, TimeSpan.FromHours(1)));

    /// <summary>A glyph run built the way layout builds one: shaped glyphs placed by their advances.</summary>
    private static GlyphRun Run(string text, TrueTypeFont font, double size, Point origin)
    {
        var glyphs = Shaper.Shape(text, font, size).Value;
        var x = 0.0;
        var positioned = ImmutableArray.CreateBuilder<PositionedGlyph>();
        foreach (var glyph in glyphs)
        {
            positioned.Add(new PositionedGlyph(glyph.Glyph, x, glyph.Text));
            x += glyph.Advance;
        }

        return new GlyphRun(font, size, Color.Black, origin, positioned.ToImmutable());
    }

    private static PageDocument TwoPages() => new(
        new DocumentMetadata("Test é", "Tester"),
        [
            new Page(new Size(300, 200),
            [
                new FillRectangle(new Rect(new Point(20, 20), new Size(100, 30)), new Color(255, 0, 0)),
                Run("Hello, world", TestFonts.Regular, 12, new Point(20, 40)),
                Run("Bold €", TestFonts.Bold, 14, new Point(20, 80)),
            ]),
            new Page(new Size(300, 200), [Run("Page two", TestFonts.Regular, 10, new Point(30, 30))]),
        ]);

    private static string Latin1(ImmutableArray<byte> pdf) => Encoding.Latin1.GetString(pdf.AsSpan());

    [Fact]
    public void Text_reads_back_with_an_independent_reader_where_the_page_model_put_it()
    {
        var model = TwoPages();

        var pdf = PdfRenderer.Render(model, Options).Value;

        PdfReadBack.Differences(model, pdf).Should().BeEmpty();
        PdfReadBack.Letters(pdf).Select(page => string.Concat(page.Select(letter => letter.Value)))
            .Should().Equal("Hello, worldBold €", "Page two");
    }

    [Fact]
    public void Same_input_gives_byte_identical_output_and_another_date_does_not()
    {
        var first = PdfRenderer.Render(TwoPages(), Options).Value;
        var second = PdfRenderer.Render(TwoPages(), Options).Value;
        var otherDate = PdfRenderer.Render(TwoPages(), Options with { CreationDate = Options.CreationDate.AddDays(1) }).Value;

        first.SequenceEqual(second).Should().BeTrue();
        first.SequenceEqual(otherDate).Should().BeFalse();
    }

    [Fact]
    public void Each_font_is_embedded_once_as_a_composite_font_with_a_to_unicode_map()
    {
        var text = Latin1(PdfRenderer.Render(TwoPages(), Options).Value);

        Regex.Matches(text, "/Subtype /Type0").Should().HaveCount(2, "two fonts, although Lato Regular is used on both pages");
        Regex.Matches(text, "/Subtype /CIDFontType2").Should().HaveCount(2);
        text.Should().MatchRegex(@"/BaseFont /[A-Z]{6}\+Lato-Regular /Encoding /Identity-H", "a subset font name starts with a six-letter tag (§9.6.4)");
        text.Should().Contain("/CIDToGIDMap /Identity");
        Regex.Matches(text, @"/Length1 (\d+)").Select(match => int.Parse(match.Groups[1].Value))
            .Should().HaveCount(2).And.OnlyContain(length => length < TestFonts.Regular.FileData.Length / 10, "only the used glyphs are embedded");
    }

    [Fact]
    public void Widths_in_the_pdf_are_the_hmtx_advances_in_thousandths_of_an_em()
    {
        var text = Latin1(PdfRenderer.Render(TwoPages(), Options).Value);

        // Lato Regular, 2000 units per em: space (glyph 2) 512 → 256; H (15) 1527 → 763.5; comma (311) 454 → 227.
        var regularWidths = Regex.Match(text, @"/BaseFont /[A-Z]{6}\+Lato-Regular /CIDSystemInfo .*? /W \[(.*?)\] /CIDToGIDMap").Groups[1].Value;
        regularWidths.Should().StartWith("2 [256] 15 [763.5] ");
        regularWidths.Should().Contain(" 311 [227] ");
    }

    [Fact]
    public void Each_page_lists_only_the_fonts_it_uses()
    {
        var text = Latin1(PdfRenderer.Render(TwoPages(), Options).Value);

        var resources = Regex.Matches(text, @"/Resources << /Font << ([^>]*) >> >>").Select(match => match.Groups[1].Value).ToList();
        resources.Should().HaveCount(2);
        resources[0].Should().Contain("/F1").And.Contain("/F2");
        resources[1].Should().Contain("/F1").And.NotContain("/F2");
    }

    [Fact]
    public void The_document_information_has_title_author_producer_and_the_given_dates()
    {
        var text = Latin1(PdfRenderer.Render(TwoPages(), Options).Value);

        text.Should().Contain("/Author (Tester) /Producer (Ave.Documents) /CreationDate (D:20261115092200+01'00) /ModDate (D:20261115092200+01'00)");
        text.Should().Contain(@"/Title (\376\377\000T\000e\000s\000t\000 \000\351)");
    }

    [Theory]
    [InlineData((ushort)0x0002)]
    [InlineData((ushort)0x0200)]
    public void A_font_whose_licence_forbids_embedding_is_an_error(ushort fsType)
    {
        // OS/2 fsType: 0x0002 "Restricted License embedding" alone, 0x0200 "Bitmap embedding only" (a PDF embeds outlines).
        var restricted = FontParser.Parse(WithFsType(TestFonts.Bytes("Lato-Regular.ttf"), fsType)).Value;
        var model = new PageDocument(new DocumentMetadata(null, null), [new Page(new Size(100, 100), [Run("x", restricted, 10, new Point(0, 50))])]);

        var error = PdfRenderer.Render(model, Options).Error;

        error.Code.Should().Be(DocumentErrorCodes.Pdf.Write.FontEmbeddingNotPermitted);
        error.Metadata![ErrorMetadataKeys.FontName].Should().Be("Lato-Regular");
    }

    [Fact]
    public void A_font_whose_licence_forbids_subsetting_is_embedded_whole()
    {
        var bytes = WithFsType(TestFonts.Bytes("Lato-Regular.ttf"), 0x0100);
        var whole = FontParser.Parse(bytes).Value;
        var model = new PageDocument(new DocumentMetadata(null, null), [new Page(new Size(100, 100), [Run("x", whole, 10, new Point(0, 50))])]);

        var text = Latin1(PdfRenderer.Render(model, Options).Value);

        text.Should().Contain("/BaseFont /Lato-Regular").And.NotContain("+Lato-Regular", "only a subset carries a tag (ISO 32000-1 §9.6.4)");
        text.Should().Contain($"/Length1 {bytes.Length}");
    }

    [Fact]
    public void An_empty_page_is_still_a_valid_page() =>
        PdfReadBack.Letters(PdfRenderer.Render(new PageDocument(new DocumentMetadata(null, null), [new Page(new Size(100, 100), [])]), Options).Value)
            .Should().ContainSingle().Which.Should().BeEmpty();

    /// <summary>A copy of a font file with another OS/2 fsType (offset 8 in the table).</summary>
    private static byte[] WithFsType(byte[] font, ushort fsType)
    {
        var copy = (byte[])font.Clone();
        int tableCount = (copy[4] << 8) | copy[5];
        for (var index = 0; index < tableCount; index++)
        {
            var record = 12 + index * 16;
            if (Encoding.ASCII.GetString(copy, record, 4) == "OS/2")
            {
                var offset = (copy[record + 8] << 24) | (copy[record + 9] << 16) | (copy[record + 10] << 8) | copy[record + 11];
                copy[offset + 8] = (byte)(fsType >> 8);
                copy[offset + 9] = (byte)fsType;
            }
        }

        return copy;
    }
}
