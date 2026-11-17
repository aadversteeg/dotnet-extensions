using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.Documents.TestSupport;

namespace UnitTests.Extensions.Documents.PageModel;

public class PageModelTextTests
{
    private static PageDocument Sample() => new(
        new DocumentMetadata("Letter", "Someone"),
        [
            new Page(new Size(200, 100),
            [
                new FillRectangle(new Rect(new Point(10, 10), new Size(180, 20)), new Color(0xDD, 0xEB, 0xF7)),
                new StructureStart(new NodeId(1), SemanticRole.Paragraph, default),
                new GlyphRun(TestFonts.Regular, 10, Color.Black, new Point(10, 19.87),
                [
                    new PositionedGlyph(new GlyphId(15), 0, "H"),
                    new PositionedGlyph(new GlyphId(98), 7.635, "i"),
                ]),
                new StructureEnd(new NodeId(1)),
            ]),
            new Page(new Size(200, 100), []),
        ]);

    [Fact]
    public void Serialisation_matches_the_golden_file() =>
        Golden.Compare("sample.page.txt", PageModelText.Write(Sample())).Should().BeNull();

    [Fact]
    public void A_glyph_off_the_baseline_shows_its_offset()
    {
        // A mark raised 3.5 pt above its base is written "g<id>@<x>,<y>", so a golden notices a change to the offset;
        // a glyph on the baseline keeps the short form.
        var run = new GlyphRun(TestFonts.Regular, 10, Color.Black, new Point(10, 19.87),
            [new PositionedGlyph(new GlyphId(15), 0, "H"), new PositionedGlyph(new GlyphId(98), 7.635, "i", -3.5)]);

        PageModelText.Write(new PageDocument(new DocumentMetadata(null, null), [new Page(new Size(200, 100), [run])]))
            .Should().Contain("g15@0 g98@7.635,-3.5");
    }
}
