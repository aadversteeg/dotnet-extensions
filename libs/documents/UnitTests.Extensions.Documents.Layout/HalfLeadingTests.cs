using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;

namespace UnitTests.Extensions.Documents.Layout;

/// <summary>
/// <c>line-height: normal</c> with a font that has a line gap. Theano Didot: 2048 units per em; it sets USE_TYPO_METRICS,
/// so its line metrics are the OS/2 typographic ones, sTypoAscender 1580, sTypoDescender −468, sTypoLineGap 560
/// (fontTools 4.57.0; 'hhea' says 1962, −630, 560). The line is ascent + descent + line gap high, and the leading — the
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
        line.Size.Height.Should().BeApproximately((1580 + 468 + 560) * Size / 2048, 1e-9);
        var text = line.Children.Single();
        text.Offset.Y.Should().BeApproximately(560 / 2.0 * Size / 2048, 1e-9, "the text's top edge is half the line gap below the line's top");
        ((TextFragment)text.Fragment).Ascent.Should().BeApproximately(1580 * Size / 2048, 1e-9);
    }
}
