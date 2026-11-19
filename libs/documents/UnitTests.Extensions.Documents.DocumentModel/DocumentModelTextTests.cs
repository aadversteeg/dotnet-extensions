using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.TestSupport;

namespace UnitTests.Extensions.Documents.DocumentModel;

public class DocumentModelTextTests
{
    private static readonly SourceLocation Here = new("/src/Letter.cs", 10);

    private static StyledDocument Sample()
    {
        var text = new TextStyle("Lato", 11.Pt(), FontWeight.Regular, Color.Black);
        var heading = new Box(new NodeId(1), SemanticRole.Heading(1), LayoutAlgorithmId.Inline,
            BoxStyle.Initial with { Margin = Edges.OnlyBottom(6.Pt()) },
            [new TextRun(new NodeId(2), "Invoice \"42\"", text with { FontSize = 22.Pt(), FontWeight = FontWeight.Bold }, Here with { Line = 11 })],
            Here with { Line = 11 });
        var note = new Box(new NodeId(3), SemanticRole.Generic, LayoutAlgorithmId.Flow,
            BoxStyle.Initial with { Margin = Edges.Zero, Padding = Edges.All(4.Pt()), Background = new Color(0xDD, 0xEB, 0xF7), BreakBefore = BreakBetween.Page },
            [new Box(new NodeId(4), SemanticRole.Paragraph, LayoutAlgorithmId.Inline, BoxStyle.Initial,
                [new TextRun(new NodeId(5), "Thank you.", text with { Color = new Color(0x1F, 0x4E, 0x79) }, Here with { Line = 13 })],
                Here with { Line = 13 })],
            Here with { Line = 12 });
        var root = new Box(new NodeId(0), SemanticRole.Generic, LayoutAlgorithmId.Flow, BoxStyle.Initial, [heading, note], Here);
        return new StyledDocument(new DocumentMetadata("Letter", null), new PageStyle(210.Mm(), 297.Mm(), Edges.All(2.Cm())), root);
    }

    [Fact]
    public void Serialisation_matches_the_golden_file() =>
        Golden.Compare("sample.model.txt", DocumentModelText.Write(Sample())).Should().BeNull();

    [Fact]
    public void Page_area_is_the_page_minus_its_margins()
    {
        var page = Sample().Page;

        page.AreaWidth.Points.Should().BeApproximately(210.Mm().Points - 2 * 2.Cm().Points, 1e-9);
        page.AreaHeight.Points.Should().BeApproximately(297.Mm().Points - 2 * 2.Cm().Points, 1e-9);
    }

    [Fact]
    public void A_run_whose_white_space_does_not_collapse_or_wrap_says_so_and_one_with_the_initial_values_says_nothing()
    {
        var text = new TextStyle("Lato", 11.Pt(), FontWeight.Regular, Color.Black);
        var paragraph = new Box(new NodeId(1), SemanticRole.Paragraph, LayoutAlgorithmId.Inline, BoxStyle.Initial,
            [new TextRun(new NodeId(2), "a", text with { WhiteSpaceCollapse = WhiteSpaceCollapse.PreserveBreaks, TextWrapMode = TextWrapMode.NoWrap }, Here),
             new TextRun(new NodeId(3), "b", text, Here)], Here);
        var written = DocumentModelText.Write(new StyledDocument(new DocumentMetadata("T", null), new PageStyle(210.Mm(), 297.Mm(), Edges.All(2.Cm())),
            new Box(new NodeId(0), SemanticRole.Generic, LayoutAlgorithmId.Flow, BoxStyle.Initial, [paragraph], Here)));

        written.Split('\n').Where(line => line.Contains("white-space-collapse=preserve-breaks", StringComparison.Ordinal)).Should().ContainSingle()
            .Which.Should().Contain("\"a\"").And.Contain("text-wrap-mode=nowrap");
        written.Should().NotContain("text-wrap-mode=wrap");
    }

    [Fact]
    public void A_run_with_decorations_writes_each_in_its_order_and_one_without_writes_none()
    {
        var text = new TextStyle("Lato", 11.Pt(), FontWeight.Regular, Color.Black);
        ValueList<TextDecoration> decorations =
        [
            new(TextDecorationLine.Underline, Color.Black, "Lato", 11.Pt(), FontWeight.Regular),
            new(TextDecorationLine.LineThrough, new Color(0xCC, 0, 0), "Theano", 22.Pt(), FontWeight.Bold, FontStyle.Italic),
        ];
        var paragraph = new Box(new NodeId(1), SemanticRole.Paragraph, LayoutAlgorithmId.Inline, BoxStyle.Initial,
            [new TextRun(new NodeId(2), "a", text with { Decorations = decorations }, Here), new TextRun(new NodeId(3), "b", text, Here)], Here);
        var written = DocumentModelText.Write(new StyledDocument(new DocumentMetadata("T", null), new PageStyle(210.Mm(), 297.Mm(), Edges.All(2.Cm())),
            new Box(new NodeId(0), SemanticRole.Generic, LayoutAlgorithmId.Flow, BoxStyle.Initial, [paragraph], Here)));

        var line = written.Split('\n').Should().ContainSingle(line => line.Contains("text-decoration=", StringComparison.Ordinal)).Which;
        line.Should().Contain("\"a\"");
        line.IndexOf("text-decoration=(underline", StringComparison.Ordinal).Should().BeLessThan(line.IndexOf("text-decoration=(line-through", StringComparison.Ordinal));
        line.Should().Contain("Theano 22pt w700 italic");
    }
}
