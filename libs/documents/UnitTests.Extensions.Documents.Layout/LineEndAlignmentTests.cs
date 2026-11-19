using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;

namespace UnitTests.Extensions.Documents.Layout;

/// <summary>
/// The layout tells the line breaker what to do at a line end at a space from the paragraph's alignment, as Chromium
/// decides whether a line needs an accurate end position: left-aligned text keeps the paragraph's glyphs there, so a
/// letter that kerns with the space after it keeps the pair, and the line is measured with it; right-aligned, centred and
/// justified text shapes that end again without the space. Chromium 159 on `xx AV To` in Lato 75 pt and a box of
/// 250 px (187.5 pt): left `xx AV | To`, the three others `xx | AV | To` (case `t-line-ends-*`, index of width 250 px, in
/// the Typesetting tests' <c>Reference/Chromium</c>).
/// </summary>
public class LineEndAlignmentTests
{
    private static List<string> Lines(TextAlign align)
    {
        var style = new TextStyle("Lato", 75.Pt(), FontWeight.Regular, Color.Black);
        var paragraph = new Box(new NodeId(1), SemanticRole.Paragraph, LayoutAlgorithmId.Inline, BoxStyle.Initial with { TextAlign = align },
            [new TextRun(new NodeId(2), "xx AV To", style, Models.At(0))], Models.At(0));
        var document = new StyledDocument(new DocumentMetadata("Test", null), new PageStyle(207.5.Pt(), 600.Pt(), Edges.All(10.Pt())), Models.Flow(0, [paragraph]));

        var page = Paginator.Paginate(LayoutEnvironment.Create(TestFonts.Set), document).Value.Single();
        var box = (BoxFragment)((BoxFragment)page.Root.Fragment).Children[0].Fragment;
        return [.. box.Children.Select(line => string.Concat(((LineFragment)line.Fragment).Children.Select(text => string.Concat(((TextFragment)text.Fragment).Glyphs.Select(glyph => glyph.Text)))).Trim())];
    }

    [Fact]
    public void Left_aligned_text_keeps_the_kerning_of_the_last_letter_with_the_space_and_fits_more()
    {
        Lines(TextAlign.Left).Should().Equal("xx AV", "To");
    }

    [Theory]
    [InlineData(TextAlign.Right)]
    [InlineData(TextAlign.Center)]
    [InlineData(TextAlign.Justify)]
    public void Other_alignments_shape_the_end_again_before_the_space(TextAlign align)
    {
        Lines(align).Should().Equal("xx", "AV", "To");
    }
}
