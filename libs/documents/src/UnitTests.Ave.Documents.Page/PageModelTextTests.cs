using Ave.Documents.Core;
using Ave.Documents.Fonts;
using Ave.Documents.Page;
using Ave.Documents.TestSupport;

namespace UnitTests.Ave.Documents.Page;

public class PageModelTextTests
{
    private static PageDocument Sample() => new(
        new DocumentInfo("Letter", "Someone"),
        [
            new PageContent(new Size(200, 100),
            [
                new FillRectangle(new Rect(new Point(10, 10), new Size(180, 20)), new Color(0xDD, 0xEB, 0xF7)),
                new StructureStart(new NodeId(1), SemanticRole.Paragraph),
                new GlyphRun(TestFonts.Regular, 10, Color.Black, new Point(10, 19.87),
                [
                    new PositionedGlyph(new GlyphId(15), 0, "H"),
                    new PositionedGlyph(new GlyphId(98), 7.635, "i"),
                ]),
                new StructureEnd(new NodeId(1)),
            ]),
            new PageContent(new Size(200, 100), []),
        ]);

    [Fact]
    public void Serialisation_matches_the_golden_file() =>
        Golden.Compare("sample.page.txt", PageModelText.Write(Sample())).Should().BeNull();
}
