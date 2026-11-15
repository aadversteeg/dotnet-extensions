using Ave.Documents.Core;
using Ave.Documents.DocumentModel;
using Ave.Documents.Fonts;
using Ave.Documents.Layout;
using Ave.Documents.TestSupport;

namespace UnitTests.Ave.Documents.Layout;

/// <summary>
/// <c>line-height: normal</c> with a font that has a line gap. Theano Didot: 2048 units per em, hhea ascender 1962,
/// descender −630, lineGap 560 (fontTools 4.68.0). The line is ascent + descent + line gap high, and the leading — the
/// line gap — is split in half above and half below the glyphs (CSS 2.1 §10.8.1, "half-leading").
/// </summary>
public class HalfLeadingTests
{
    [Fact]
    public void Half_of_the_line_gap_goes_above_the_text_and_half_below()
    {
        const double Size = 20;
        var font = TestFonts.TheanoDidot;
        var style = new TextStyle(font.Names.Family, Size.Pt(), FontWeight.Regular, Color.Black);
        var paragraph = new Box(new NodeId(1), SemanticRole.Paragraph, LayoutAlgorithmId.Inline, BoxStyle.Initial,
            [new TextRun(new NodeId(2), "Ab", style, Models.At(0))], Models.At(0));
        var document = new StyledDocument(new DocumentMetadata("Test", null), new PageStyle(200.Pt(), 100.Pt(), Edges.All(10.Pt())), Models.Flow(0, [paragraph]));

        var page = Paginator.Paginate(LayoutEnvironment.Create(new FontSet([font])), document).Value.Single();

        var line = (LineFragment)((BoxFragment)((BoxFragment)page.Root.Fragment).Children[0].Fragment).Children[0].Fragment;
        line.Size.Height.Should().BeApproximately((1962 + 630 + 560) * Size / 2048, 1e-9);
        var text = line.Children.Single();
        text.Offset.Y.Should().BeApproximately(560 / 2.0 * Size / 2048, 1e-9, "the text's top edge is half the line gap below the line's top");
        ((TextFragment)text.Fragment).Ascent.Should().BeApproximately(1962 * Size / 2048, 1e-9);
    }
}
