using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.ErrorPaths;
using static UnitTests.Extensions.Documents.Layout.Models;

namespace UnitTests.Extensions.Documents.Layout;

/// <summary>
/// The width of a block box in block flow (<see cref="BoxStyle.SpecifiedWidth"/>, <see cref="BoxStyle.MinWidth"/>,
/// <see cref="BoxStyle.MaxWidth"/>, <see cref="BoxStyle.BoxSizing"/>, <see cref="BoxStyle.AutoMargins"/>; CSS 2.1 §10.3.3,
/// §10.4), with the numbers Chromium prints for the cases <c>sz-*</c> (page area 276 pt, 368 px).
/// </summary>
public class BlockWidthTests
{
    private const double Unit = 0.75 / 64;

    private static (double Width, double Left) Horizontal(BoxStyle style, double container = 276, LengthRounding rounding = LengthRounding.SixtyFourthPixels) =>
        InFlowChild.Horizontal(style, container, rounding);

    private static SizeValue.Fixed Points(double points) => new(points.Pt());

    [Fact]
    public void Without_a_size_the_box_fills_its_container_less_its_margins()
    {
        Horizontal(BoxStyle.Initial with { Margin = new Edges(0.Pt(), 6.Pt(), 0.Pt(), 20.Pt()), AutoMargins = AutoMargins.Both }).Should().Be((250.0, 20.0));
    }

    [Fact]
    public void A_percentage_is_of_the_container_s_content_width_and_floored_to_a_sixty_fourth_pixel()
    {
        // sz-width-percent: 40 % of 368 px is 147.2 px, printed 147.1875 px (110.391 pt); exact without the policy.
        Horizontal(BoxStyle.Initial with { SpecifiedWidth = new SizeValue.Percentage(40) }).Width.Should().Be(9420 * Unit);
        Horizontal(BoxStyle.Initial with { SpecifiedWidth = new SizeValue.Percentage(40) }, rounding: LengthRounding.Exact).Width.Should().BeApproximately(110.4, 1e-9);

        // sz-width-percent-parent: 33.3 % of 266.65625 px (a 200 pt parent) is 88.7965 px, printed 88.78125 px.
        Horizontal(BoxStyle.Initial with { SpecifiedWidth = new SizeValue.Percentage(33.3) }, container: 17066 * Unit).Width.Should().Be(5682 * Unit);
    }

    [Fact]
    public void Two_auto_margins_share_the_width_left_over_and_the_left_one_is_floored_to_a_sixty_fourth_pixel()
    {
        // sz-margin-auto: a 121 pt box (161.328125 px) leaves 206.671875 px; Chromium's left margin is 103.328125 px.
        var (width, left) = Horizontal(BoxStyle.Initial with { SpecifiedWidth = Points(10325 * Unit), AutoMargins = AutoMargins.Both });

        width.Should().Be(10325 * Unit);
        left.Should().Be(6613 * Unit);
        Horizontal(BoxStyle.Initial with { SpecifiedWidth = Points(120), AutoMargins = AutoMargins.Both }).Left.Should().Be(78);
    }

    [Fact]
    public void An_auto_left_margin_takes_all_the_width_left_over_and_an_auto_right_margin_none()
    {
        Horizontal(BoxStyle.Initial with { SpecifiedWidth = Points(120), AutoMargins = AutoMargins.Left }).Should().Be((120.0, 156.0));
        Horizontal(BoxStyle.Initial with { SpecifiedWidth = Points(120), Margin = new Edges(0.Pt(), 0.Pt(), 0.Pt(), 20.Pt()), AutoMargins = AutoMargins.Right })
            .Should().Be((120.0, 20.0));
    }

    [Fact]
    public void The_minimum_wins_over_the_width_and_the_maximum_and_the_maximum_over_the_width()
    {
        // sz-min-max: 100 pt under a minimum of 150 pt; 200 pt under a maximum of 120 pt; a minimum of 150 over a maximum of 100.
        Horizontal(BoxStyle.Initial with { SpecifiedWidth = Points(100), MinWidth = Points(150) }).Width.Should().Be(150);
        Horizontal(BoxStyle.Initial with { SpecifiedWidth = Points(200), MaxWidth = Points(120) }).Width.Should().Be(120);
        Horizontal(BoxStyle.Initial with { MinWidth = Points(150), MaxWidth = Points(100) }).Width.Should().Be(150);
        Horizontal(BoxStyle.Initial with { MaxWidth = new SizeValue.Percentage(50) }).Width.Should().Be(138);
    }

    [Fact]
    public void Padding_and_border_are_added_to_a_content_box_width_and_lie_inside_a_border_box_width()
    {
        // sz-box-sizing: 120 pt, 6 pt padding, 3 pt border: 138 pt as content box, 120 pt as border box.
        var style = BoxStyle.Initial with
        {
            SpecifiedWidth = Points(120),
            Padding = Edges.All(6.Pt()),
            Border = Border.All(new BorderSide(3.Pt(), Color.Black)),
        };

        Horizontal(style).Width.Should().Be(138);
        Horizontal(style with { BoxSizing = BoxSizing.BorderBox }).Width.Should().Be(120);
        Horizontal(style with { BoxSizing = BoxSizing.BorderBox, SpecifiedWidth = Points(10) }).Width.Should().Be(18, "a border box is never narrower than its padding and border");
    }

    [Fact]
    public void A_box_wider_than_its_container_starts_at_its_left_margin_whatever_its_auto_margins()
    {
        // sz-width-overflow: 200 pt boxes in a 150 pt parent, with margin: 0 auto and margin-left: auto, start at its left.
        Horizontal(BoxStyle.Initial with { SpecifiedWidth = Points(200), AutoMargins = AutoMargins.Both }, container: 150).Should().Be((200.0, 0.0));
        Horizontal(BoxStyle.Initial with { SpecifiedWidth = Points(200), AutoMargins = AutoMargins.Left }, container: 150).Should().Be((200.0, 0.0));
    }

    [Fact]
    public void A_box_with_a_width_lays_out_its_lines_in_that_width_at_the_left_its_auto_margin_gives()
    {
        // A page area of 180 pt: a box of 60 pt with margin-left: auto starts at 120 pt and wraps its words in 60 pt.
        var document = Document(Flow(1, [Paragraph(2, Words(6))], BoxStyle.Initial with { SpecifiedWidth = Points(60), AutoMargins = AutoMargins.Left }));

        var pages = DocumentLayout.Run(document, TestFonts.Set);

        pages.IsSuccess.Should().BeTrue(pages.IsFailure ? pages.Error.ToString() : string.Empty);
        var words = PrintedPages.Engine(pages.Value).Pages[0].Words;
        words.Should().OnlyContain(word => word.X0 >= 10 + 120 - 1e-9 && word.X1 <= 10 + 180 + 1e-9);
        words.Select(word => word.Baseline).Distinct().Count().Should().BeGreaterThan(1, "six words do not fit in 60 pt on one line");
    }

    [Fact]
    public void A_size_inside_a_table_or_on_a_box_of_another_algorithm_is_refused_and_a_negative_one_is_invalid()
    {
        var sized = BoxStyle.Initial with { SpecifiedWidth = Points(50) };
        var cell = new Box(new NodeId(3), SemanticRole.TableCell, LayoutAlgorithmId.Flow, BoxStyle.Initial, [Paragraph(4, "a", sized)], At(0));
        var row = new Box(new NodeId(2), SemanticRole.TableRow, LayoutAlgorithmId.Flow, BoxStyle.Initial with { TableDisplay = TableDisplay.Row }, [cell], At(0));
        var table = new Box(new NodeId(1), SemanticRole.Table, LayoutAlgorithmId.Tracks, BoxStyle.Initial with { Columns = [TrackSize.Auto.Instance] }, [row], At(0));

        Error Refused(StyledDocument document)
        {
            var result = DocumentLayout.Run(document, TestFonts.Set);
            result.IsFailure.Should().BeTrue();
            return result.Error;
        }

        Refused(Document(table)).Code.Should().Be(DocumentErrorCodes.Input.Unsupported.Size);
        Refused(Document(table with { Style = table.Style with { AutoMargins = AutoMargins.Both }, Children = [row with { Children = [cell with { Children = [Paragraph(4, "a")] }] }] }))
            .Code.Should().Be(DocumentErrorCodes.Input.Unsupported.Size, "a table takes no auto margins");
        Refused(Document(Paragraph(1, "a", BoxStyle.Initial with { MaxWidth = Points(-1) }))).Code.Should().Be(DocumentErrorCodes.Input.Invalid.Length);
        Refused(Document(Paragraph(1, "a", BoxStyle.Initial with { MinWidth = new SizeValue.Percentage(double.NaN) }))).Code.Should().Be(DocumentErrorCodes.Input.Invalid.Length);
    }
}
