using Ave.Documents.Core;
using Ave.Documents.Fluent;
using Ave.Documents.DocumentModel;
using static Ave.Documents.Fluent.Elements;

namespace UnitTests.Ave.Documents.Fluent;

public class TranslationTests
{
    private static readonly TextFormat Lato = new(FontFamily: "Lato");

    private static StyledDocument Translate(Document document)
    {
        var result = DocumentTranslator.ToModel(document);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        return result.Value;
    }

    [Fact]
    public void Node_ids_are_assigned_in_pre_order_starting_with_the_root()
    {
        var model = Translate(Document(
            [Paragraph([Text("a"), Bold("b")]), Block([Paragraph("c")]), Paragraph("d")],
            Lato));

        PreOrder(model.Root).Select(node => node.Id.Value).Should().Equal(Enumerable.Range(0, 9), "root, paragraph, two runs, block, paragraph, run, paragraph, run");
    }

    private static IEnumerable<Node> PreOrder(Node node) =>
        node is Box box ? [box, .. box.Children.SelectMany(PreOrder)] : [node];

    [Fact]
    public void Paragraphs_become_inline_boxes_and_blocks_become_flow_boxes()
    {
        var root = Translate(Document([Block([Paragraph("x")])], Lato)).Root;

        root.Layout.Should().Be(LayoutAlgorithmId.Flow);
        var block = (Box)root.Children.Single();
        block.Layout.Should().Be(LayoutAlgorithmId.Flow);
        block.Role.Should().Be(SemanticRole.Generic);
        var paragraph = (Box)block.Children.Single();
        paragraph.Layout.Should().Be(LayoutAlgorithmId.Inline);
        paragraph.Role.Should().Be(SemanticRole.Paragraph);
        paragraph.Children.Single().Should().BeOfType<TextRun>();
    }

    [Fact]
    public void Text_formatting_inherits_down_the_tree_and_the_nearest_setting_wins()
    {
        var blue = new Color(0, 0, 255);
        var model = Translate(Document(
            [Block([Paragraph([Text("plain"), Text("big", new TextFormat(FontSize: 20.Pt())), Bold("bold")])], format: new TextFormat(Color: blue))],
            new TextFormat(FontFamily: "Lato", FontSize: 11.Pt())));

        var runs = ((Box)((Box)model.Root.Children[0]).Children[0]).Children.Cast<TextRun>().ToList();
        runs.Select(run => run.Style).Should().Equal(
            new TextStyle("Lato", 11.Pt(), FontWeight.Regular, blue),
            new TextStyle("Lato", 20.Pt(), FontWeight.Regular, blue),
            new TextStyle("Lato", 11.Pt(), FontWeight.Bold, blue));
    }

    [Fact]
    public void Unset_document_values_get_the_css_initial_values()
    {
        var run = (TextRun)((Box)Translate(Document([Paragraph("x")], Lato)).Root.Children[0]).Children[0];

        run.Style.Should().Be(new TextStyle("Lato", 12.Pt(), FontWeight.Regular, Color.Black));
    }

    [Theory]
    [InlineData(1, 22.0)]
    [InlineData(2, 16.5)]
    [InlineData(6, 7.37)]
    public void Headings_are_bold_and_sized_like_the_html_user_agent_style_sheet(int level, double size)
    {
        var heading = (Box)Translate(Document([Heading(level, "Title")], new TextFormat(FontFamily: "Lato", FontSize: 11.Pt()))).Root.Children[0];

        heading.Role.Should().Be(SemanticRole.Heading(level));
        var style = ((TextRun)heading.Children[0]).Style;
        style.FontWeight.Should().Be(FontWeight.Bold);
        style.FontSize.Points.Should().BeApproximately(size, 1e-9);
    }

    [Fact]
    public void The_authors_format_overrides_the_heading_defaults()
    {
        var heading = (Box)Translate(Document([Heading(1, "Title", format: new TextFormat(FontSize: 9.Pt(), FontWeight: FontWeight.Regular))], Lato)).Root.Children[0];

        ((TextRun)heading.Children[0]).Style.Should().Be(new TextStyle("Lato", 9.Pt(), FontWeight.Regular, Color.Black));
    }

    [Fact]
    public void A_page_break_sets_break_before_on_the_next_element_and_is_not_a_node()
    {
        var root = Translate(Document([Paragraph("a"), PageBreak(), PageBreak(), Paragraph("b")], Lato)).Root;

        root.Children.Should().HaveCount(2);
        root.Children.Cast<Box>().Select(box => box.Style.BreakBefore).Should().Equal(BreakBefore.Auto, BreakBefore.Page);
    }

    [Fact]
    public void Box_formatting_becomes_the_box_style()
    {
        var box = (Box)Translate(Document(
            [Paragraph("x", new BoxFormat(Margin: Edges.OnlyBottom(3.Pt()), Padding: Edges.All(2.Pt()), Background: Color.White))],
            Lato)).Root.Children[0];

        box.Style.Should().Be(new BoxStyle(Edges.OnlyBottom(3.Pt()), Edges.All(2.Pt()), Color.White, BreakBefore.Auto));
    }

    [Fact]
    public void Nodes_carry_the_file_and_line_of_the_call_that_made_them()
    {
        var line = CurrentLine() + 1;
        var document = Document([Paragraph("x")], Lato);

        var paragraph = Translate(document).Root.Children[0];
        paragraph.Source.Line.Should().Be(line);
        paragraph.Source.File.Should().EndWith("TranslationTests.cs");
    }

    private static int CurrentLine([System.Runtime.CompilerServices.CallerLineNumber] int line = 0) => line;
}
