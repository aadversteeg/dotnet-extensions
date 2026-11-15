using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Ave.Documents.Core;
using Ave.Documents.Fonts;
using Ave.Documents.Images;
using Ave.Documents.PageModel;
using Ave.Documents.Pdf;
using Ave.Documents.Render.Pdf;
using Ave.Documents.TestSupport;
using Ave.Documents.Typesetting;

namespace UnitTests.Ave.Documents.Render.Pdf;

/// <summary>
/// Values the back-end writes that only a reader of the file sees: no other test read them back, so the mutation run
/// could change each of them without a test failing. Each test reads the value from the written file — with PdfPig,
/// or from the file's own objects — and compares it with what the specification asks for.
/// </summary>
public class WrittenValueTests
{
    private static readonly DateTimeOffset Date = new(2026, 11, 15, 9, 22, 0, TimeSpan.FromHours(1));

    private static readonly PdfRenderOptions Plain = new(Date);

    private static readonly SourceLocation Here = new("WrittenValueTests.cs", 1);

    private static GlyphRun Run(string text, TrueTypeFont font, Point origin)
    {
        var glyphs = Shaper.Shape(text, font, 10).Value;
        var x = 0.0;
        var positioned = ImmutableArray.CreateBuilder<PositionedGlyph>();
        foreach (var glyph in glyphs)
        {
            positioned.Add(new PositionedGlyph(glyph.Glyph, x, glyph.Text));
            x += glyph.Advance;
        }

        return new GlyphRun(font, 10, Color.Black, origin, positioned.ToImmutable());
    }

    private static PageDocument OnePage(DocumentMetadata metadata, params DrawItem[] items) => new(metadata, [new Page(new Size(200, 100), [.. items])]);

    private static StructureStart Start(int node, SemanticRole role) => new(new NodeId(node), role, Here);

    private static StructureEnd End(int node) => new(new NodeId(node));

    private static string Latin1(ImmutableArray<byte> pdf) => Encoding.Latin1.GetString(pdf.AsSpan());

    /// <summary>The data of stream object <paramref name="number"/>, decoded when it is Flate-compressed.</summary>
    private static byte[] StreamData(string file, int number)
    {
        var match = Regex.Match(file, $@"\n{number} 0 obj\n(<<.*?>>)\nstream\n", RegexOptions.Singleline);
        match.Success.Should().BeTrue($"object {number} is a stream");
        var dictionary = match.Groups[1].Value;
        var length = int.Parse(Regex.Match(dictionary, @"/Length (\d+)").Groups[1].Value, CultureInfo.InvariantCulture);
        var data = Encoding.Latin1.GetBytes(file.Substring(match.Index + match.Length, length));
        if (!dictionary.Contains("/Filter /FlateDecode", StringComparison.Ordinal))
        {
            return data;
        }

        using var zlib = new ZLibStream(new MemoryStream(data), CompressionMode.Decompress);
        using var decoded = new MemoryStream();
        zlib.CopyTo(decoded);
        return decoded.ToArray();
    }

    private static int Reference(string file, string key) =>
        int.Parse(Regex.Match(file, $@"/{key} (\d+) 0 R").Groups[1].Value, CultureInfo.InvariantCulture);

    [Fact]
    public void An_attachment_states_its_size_in_bytes()
    {
        // ISO 32000-1 §7.11.4, Table 46: /Size is "the size of the uncompressed embedded file, in bytes".
        var attachment = new Attachment("data.xml", [.. "<data>twelve</data>"u8], "application/xml", Here) { Relationship = AttachmentRelationship.Data };
        var document = OnePage(new DocumentMetadata("Attachment", null) { Attachments = [attachment] }, Run("x", TestFonts.Regular, new Point(10, 50)));

        var file = Latin1(PdfRenderer.Render(document, Plain).Value);

        Regex.Match(file, @"/Params << /Size (\d+)").Groups[1].Value.Should().Be("19");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void PDF_UA_refuses_a_blank_title(string title)
    {
        // ISO 14289-1 §7.1: the title must identify the document; blank text identifies nothing.
        var document = OnePage(new DocumentMetadata(title, null) { Language = "en" },
            Start(0, SemanticRole.Document), Start(1, SemanticRole.Paragraph), Run("x", TestFonts.Regular, new Point(10, 50)), End(1), End(0));

        PdfRenderer.Render(document, Plain with { Profile = PdfProfile.PdfUa1 }).Error.Code.Should().Be(PdfRenderErrorCodes.Conformance.PdfUa1.MissingTitle);
    }

    [Fact]
    public void The_italic_flag_is_set_for_an_italic_font_only()
    {
        // ISO 32000-1 §9.8.2, Table 123: bit 7 (value 64) is Italic; bit 3 (value 4) Symbolic, which this writer always sets.
        var document = OnePage(new DocumentMetadata("Fonts", null), Run("upright", TestFonts.Regular, new Point(10, 30)), Run("italic", TestFonts.Italic, new Point(10, 60)));

        var file = Latin1(PdfRenderer.Render(document, Plain).Value);

        int Flags(string font) => int.Parse(Regex.Match(file, $@"/FontName /[A-Z]{{6}}\+{font} /Flags (\d+)").Groups[1].Value, CultureInfo.InvariantCulture);
        Flags("Lato-Italic").Should().Be(4 | 64);
        Flags("Lato-Regular").Should().Be(4);
    }

    [Fact]
    public void The_font_descriptor_states_the_cap_height_of_the_font()
    {
        // ISO 32000-1 §9.8.1, Table 122: CapHeight is required, in glyph space (thousandths of an em); the value is
        // OS/2 sCapHeight scaled by 1000 / unitsPerEm.
        var metrics = TestFonts.Regular.Metrics;
        metrics.CapHeight.Should().NotBeNull("Lato has an OS/2 table of version 2 or later");
        var expected = PdfSerializer.Serialize(PdfNumber.From(metrics.CapHeight!.Value * 1000.0 / metrics.UnitsPerEm)).Value;

        var file = Latin1(PdfRenderer.Render(OnePage(new DocumentMetadata("Font", null), Run("Cap", TestFonts.Regular, new Point(10, 50))), Plain).Value);

        Regex.Match(file, @"/CapHeight (\S+)").Groups[1].Value.Should().Be(expected);
        expected.Should().NotBe("0");
    }

    [Fact]
    public void The_soft_mask_holds_the_alpha_values_of_the_image()
    {
        // ISO 32000-1 §11.6.5.3: the soft mask's samples are the image's alpha values.
        var image = TestImages.Load("rgba8.png");
        var alpha = ((RasterImage)image).Alpha!.Value;
        alpha.Should().Contain(value => value != 255, "the test image is partly transparent");
        var document = OnePage(new DocumentMetadata("Image", null), new DrawImage(new Rect(new Point(10, 10), new Size(70, 50)), image));

        var file = Latin1(PdfRenderer.Render(document, Plain).Value);

        StreamData(file, Reference(file, "SMask")).Should().Equal(alpha);
    }

    [Fact]
    public void Xmp_repeats_the_title_and_states_the_modification_date()
    {
        // ISO 32000-1 §14.3.3 and ISO 19005-3 §6.6.3: XMP dc:title and xmp:ModifyDate are equivalent to Info /Title and
        // /ModDate and must hold the same values.
        var document = OnePage(new DocumentMetadata("Test é", null), Run("x", TestFonts.Regular, new Point(10, 50)));

        var file = Latin1(PdfRenderer.Render(document, Plain with { Profile = PdfProfile.PdfA3b }).Value);

        var xmp = XDocument.Parse(Encoding.UTF8.GetString(StreamData(file, Reference(file, "Metadata"))));
        xmp.Descendants(XName.Get("title", "http://purl.org/dc/elements/1.1/")).Single().Value.Should().Be("Test é");
        xmp.Descendants(XName.Get("ModifyDate", "http://ns.adobe.com/xap/1.0/")).Single().Value.Should().Be("2026-11-15T09:22:00+01:00");
        file.Should().Contain("/ModDate (D:20261115092200+01'00)");
    }

    [Fact]
    public void Every_tagged_page_asks_for_the_structure_order_for_tabbing()
    {
        // ISO 14289-1 §7.18.3: every page with annotations shall have /Tabs /S; this writer sets it on every tagged page.
        var document = new PageDocument(new DocumentMetadata("Tabs", null) { Language = "en" },
        [
            new Page(new Size(200, 100), [Start(0, SemanticRole.Document), Start(1, SemanticRole.Paragraph), Run("one", TestFonts.Regular, new Point(10, 50)), End(1), End(0)]),
            new Page(new Size(200, 100), [Start(0, SemanticRole.Document), Start(2, SemanticRole.Paragraph), Run("two", TestFonts.Regular, new Point(10, 50)), End(2), End(0)]),
        ]);

        var file = Latin1(PdfRenderer.Render(document, Plain with { Profile = PdfProfile.PdfA3a }).Value);

        Regex.Matches(file, "/Type /Page ").Should().HaveCount(2);
        Regex.Matches(file, "/Tabs /S").Should().HaveCount(2);
    }

    [Fact]
    public void Tagged_content_is_marked_with_its_structure_type_and_a_footer_as_a_footer_artifact()
    {
        // ISO 32000-1 §14.7.4.2: the tag of marked content is the structure type of its element; §14.8.2.2.2, Table 330:
        // a running footer is an artifact of type Pagination, subtype Footer.
        var document = OnePage(new DocumentMetadata("Marks", null) { Language = "en" },
            Start(0, SemanticRole.Document),
            Start(1, SemanticRole.Paragraph), Run("body", TestFonts.Regular, new Point(10, 40)), End(1),
            Start(2, SemanticRole.Heading(1)), Run("head", TestFonts.Regular, new Point(10, 20)), End(2),
            End(0),
            new ArtifactStart(ArtifactKind.PageFooter), Run("foot", TestFonts.Regular, new Point(10, 90)), ArtifactEnd.Instance);

        var pdf = PdfRenderer.Render(document, Plain with { Profile = PdfProfile.PdfA3a }).Value;

        using var read = UglyToad.PdfPig.PdfDocument.Open(pdf.ToArray());
        var marks = read.GetPage(1).GetMarkedContents();
        marks.Where(mark => !mark.IsArtifact).Select(mark => (mark.Tag, string.Concat(mark.Letters.Select(letter => letter.Value))))
            .Should().Equal(("P", "body"), ("H1", "head"));
        var footer = marks.Single(mark => mark.IsArtifact);
        footer.Properties.Data["Type"].ToString().Should().Be("/Pagination");
        footer.Properties.Data["Subtype"].ToString().Should().Be("/Footer");
    }

    [Fact]
    public void A_glyph_raised_off_the_baseline_is_drawn_where_the_page_model_put_it()
    {
        // TJ moves the pen only along the baseline (ISO 32000-1 §9.4.3), so a glyph at another height needs its own
        // text position; the page model's y grows downwards, so -3 is 3 pt higher.
        var font = TestFonts.Regular;
        var shaped = Shaper.Shape("ab", font, 10).Value;
        var run = new GlyphRun(font, 10, Color.Black, new Point(20, 50),
            [new PositionedGlyph(shaped[0].Glyph, 0, "a"), new PositionedGlyph(shaped[1].Glyph, shaped[0].Advance, "b", -3)]);
        var document = OnePage(new DocumentMetadata("Raised", null), run);

        var pdf = PdfRenderer.Render(document, Plain).Value;

        PdfReadBack.Differences(document, pdf).Should().BeEmpty();
        var letters = PdfReadBack.Letters(pdf).Single();
        (letters[1].Y - letters[0].Y).Should().BeApproximately(3, PdfReadBack.PositionTolerance);
    }

    [Fact]
    public void The_text_map_has_at_most_100_entries_per_block()
    {
        // Adobe Technical Note #5014 (the CMap format ISO 32000-1 §9.10.3 refers to): "No more than 100 … mappings may
        // appear in one bfchar block". 150 distinct letters need two blocks.
        var text = string.Concat(Enumerable.Range(0x21, 94).Concat(Enumerable.Range(0xC0, 56)).Select(code => (char)code));
        var font = TestFonts.Regular;
        var glyphs = Shaper.Shape(text, font, 10).Value;
        var run = new GlyphRun(font, 10, Color.Black, new Point(0, 50), [.. glyphs.Select((glyph, index) => new PositionedGlyph(glyph.Glyph, index * 1.0, glyph.Text))]);
        var distinct = glyphs.Select(glyph => glyph.Glyph).Distinct().Count();
        distinct.Should().BeGreaterThan(100);

        var file = Latin1(PdfRenderer.Render(OnePage(new DocumentMetadata("Map", null), run), Plain).Value);

        var cmap = Encoding.ASCII.GetString(StreamData(file, Reference(file, "ToUnicode")));
        var blocks = Regex.Matches(cmap, @"(\d+) beginbfchar").Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)).ToList();
        blocks.Should().OnlyContain(count => count <= 100);
        blocks.Sum().Should().Be(distinct);
    }

    [Fact]
    public void A_glyph_that_shows_two_texts_reads_back_as_each_of_them()
    {
        // A ToUnicode map holds one text per glyph code; the other text is written as replacement text
        // (/ActualText, ISO 32000-1 §14.9.4) around the glyph that shows it.
        var font = TestFonts.Regular;
        var glyph = Shaper.Shape("a", font, 10).Value[0];
        var page = new Page(new Size(200, 100),
        [
            new GlyphRun(font, 10, Color.Black, new Point(10, 30), [new PositionedGlyph(glyph.Glyph, 0, "a")]),
            new GlyphRun(font, 10, Color.Black, new Point(10, 60), [new PositionedGlyph(glyph.Glyph, 0, "ª")]),
        ]);

        var pdf = PdfRenderer.Render(new PageDocument(new DocumentMetadata("Map", null), [page]), Plain).Value;

        PdfReadBack.Letters(pdf).Single().Select(letter => letter.Value).Should().Equal("a", "ª");
    }

    [Fact]
    public void The_ToUnicode_text_of_a_glyph_is_the_text_it_shows_most_often()
    {
        // Two uses as "ª", one (the first) as "a": the map says "ª", and only the "a" needs replacement text.
        var font = TestFonts.Regular;
        var glyph = Shaper.Shape("a", font, 10).Value[0];
        var page = new Page(new Size(200, 100),
        [
            new GlyphRun(font, 10, Color.Black, new Point(10, 30), [new PositionedGlyph(glyph.Glyph, 0, "a")]),
            new GlyphRun(font, 10, Color.Black, new Point(10, 50), [new PositionedGlyph(glyph.Glyph, 0, "ª")]),
            new GlyphRun(font, 10, Color.Black, new Point(10, 70), [new PositionedGlyph(glyph.Glyph, 0, "ª")]),
        ]);

        var pdf = PdfRenderer.Render(new PageDocument(new DocumentMetadata("Map", null), [page]), Plain).Value;

        PdfReadBack.Letters(pdf).Single().Select(letter => letter.Value).Should().Equal("a", "ª", "ª");
        PdfReadBack.ActualTexts(pdf).Single().Should().Equal(["a"], "one replacement, for the one a");
    }
}
