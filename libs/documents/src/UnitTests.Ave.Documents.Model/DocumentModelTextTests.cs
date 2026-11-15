using Ave.Documents.Core;
using Ave.Documents.Model;
using Ave.Documents.TestSupport;

namespace UnitTests.Ave.Documents.Model;

public class DocumentModelTextTests
{
    private static readonly SourceLocation Here = new("/src/Letter.cs", 10);

    private static DocumentModel Sample()
    {
        var text = new TextStyle("Lato", 11.Pt(), FontWeight.Regular, Color.Black);
        var heading = new Box(new NodeId(1), SemanticRole.Heading(1), LayoutAlgorithmId.Inline,
            BoxStyle.Initial with { Margin = Edges.OnlyBottom(6.Pt()) },
            [new TextRun(new NodeId(2), "Invoice \"42\"", text with { FontSize = 22.Pt(), FontWeight = FontWeight.Bold }, Here with { Line = 11 })],
            Here with { Line = 11 });
        var note = new Box(new NodeId(3), SemanticRole.Generic, LayoutAlgorithmId.Flow,
            new BoxStyle(Edges.Zero, Edges.All(4.Pt()), new Color(0xDD, 0xEB, 0xF7), BreakBefore.Page),
            [new Box(new NodeId(4), SemanticRole.Paragraph, LayoutAlgorithmId.Inline, BoxStyle.Initial,
                [new TextRun(new NodeId(5), "Thank you.", text with { Color = new Color(0x1F, 0x4E, 0x79) }, Here with { Line = 13 })],
                Here with { Line = 13 })],
            Here with { Line = 12 });
        var root = new Box(new NodeId(0), SemanticRole.Generic, LayoutAlgorithmId.Flow, BoxStyle.Initial, [heading, note], Here);
        return new DocumentModel(new DocumentMetadata("Letter", null), new PageStyle(210.Mm(), 297.Mm(), Edges.All(2.Cm())), root);
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
}
