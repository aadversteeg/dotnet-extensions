using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.Documents.TestSupport;
using static UnitTests.Extensions.Documents.Layout.Models;

namespace UnitTests.Extensions.Documents.Layout;

/// <summary>Borders in the box model: insets, painting, slicing at page breaks, and their effect on margin collapsing.</summary>
public class BorderTests
{
    private static readonly Color Red = new(255, 0, 0);

    private static ValueList<PageFragment> Paginate(params Node[] children)
    {
        var result = Paginator.Paginate(LayoutEnvironment.Create(TestFonts.Set), Document(children));
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        return result.Value;
    }

    private static BoxFragment Child(PageFragment page, int index) => (BoxFragment)((BoxFragment)page.Root.Fragment).Children[index].Fragment;

    [Fact]
    public void A_border_lies_between_margin_and_padding_and_takes_space()
    {
        var style = BoxStyle.Initial with { Border = Border.All(new BorderSide(2.Pt(), Red)), Padding = Edges.All(3.Pt()) };

        var box = Child(Paginate(Paragraph(1, "One", style)).Single(), 0);

        box.Size.Height.Should().Be(2 + 3 + LineBoxHeight + 3 + 2);
        box.Children[0].Offset.Should().Be(new Point(5, 5), "the content starts inside border and padding");
        box.Border.Should().Be(style.Border, "an unbroken box paints all four sides");
    }

    [Fact]
    public void The_painter_draws_each_visible_side_as_a_filled_rectangle_after_the_background()
    {
        var style = BoxStyle.Initial with
        {
            Background = Color.White,
            Border = new Border(new BorderSide(1.Pt(), Red), BorderSide.None, new BorderSide(3.Pt(), Color.Black), BorderSide.None),
        };
        var pages = Paginate(Paragraph(1, "One", style));

        var fills = PagePainter.Paint(new DocumentMetadata(null, null), pages).Pages[0].Items.OfType<FillRectangle>().ToArray();

        fills.Select(fill => fill.Color).Should().Equal(Color.White, Red, Color.Black);
        fills[1].Area.Should().Be(new Rect(new Point(10, 10), new Size(180, 1)));
        fills[2].Area.Should().Be(new Rect(new Point(10, 10 + 1 + LineBoxHeight), new Size(180, 3)));
    }

    [Fact]
    public void A_box_s_own_background_and_border_are_drawn_inside_its_structure_markers()
    {
        // They are a layout artifact, but they are visible content of the box: a figure's or a table's /BBox encloses
        // what is drawn between its markers (ISO 32000-1 §14.8.5.4.3), so they are drawn after the start marker, still
        // before the box's children, which keeps the painting order.
        var style = BoxStyle.Initial with { Background = Color.White, Border = Border.All(new BorderSide(1.Pt(), Red)) };
        var pages = Paginate(Paragraph(1, "One", style));

        var items = PagePainter.Paint(new DocumentMetadata(null, null), pages).Pages[0].Items.ToList();

        var start = items.IndexOf(items.OfType<StructureStart>().Single(marker => marker.Node == new NodeId(1)));
        var end = items.IndexOf(items.OfType<StructureEnd>().Single(marker => marker.Node == new NodeId(1)));
        var fills = items.Select((item, index) => (item, index)).Where(entry => entry.item is FillRectangle).Select(entry => entry.index).ToArray();
        var glyphs = items.IndexOf(items.OfType<GlyphRun>().First());

        fills.Should().HaveCount(5).And.OnlyContain(index => index > start && index < glyphs);
        end.Should().BeGreaterThan(glyphs);
        items[start + 1].Should().BeOfType<ArtifactStart>().Which.Kind.Should().Be(ArtifactKind.Layout);
    }

    [Fact]
    public void A_broken_box_paints_its_top_border_on_the_first_fragment_and_its_bottom_border_on_the_last()
    {
        // box-decoration-break: slice (CSS Fragmentation 3 §5.4). 10 one-word lines in a 20 pt wide box.
        var side = new BorderSide(2.Pt(), Red);
        var style = BoxStyle.Initial with { Margin = new Edges(0.Pt(), 160.Pt(), 0.Pt(), 0.Pt()), Border = Border.All(side) };

        var pages = Paginate(Paragraph(1, Words(10), style));

        pages.Should().HaveCount(2);
        Child(pages[0], 0).Border.Should().Be(new Border(side, side, BorderSide.None, side));
        Child(pages[1], 0).Border.Should().Be(new Border(BorderSide.None, side, side, side));
        Child(pages[0], 0).Children.Should().HaveCount(6, "2 + 6 × 12 = 74 fits in 80; the bottom border is not reserved on a fragment that breaks");
    }

    [Fact]
    public void A_top_border_keeps_the_first_child_s_margin_inside_the_parent()
    {
        // §8.3.1: margins adjoin only when "no line boxes, no clearance, no padding and no border separate them".
        var child = Paragraph(2, "Inner", BoxStyle.Initial with { Margin = new Edges(10.Pt(), 0.Pt(), 0.Pt(), 0.Pt()) });
        var withBorder = Flow(1, [child], BoxStyle.Initial with { Border = new Border(new BorderSide(1.Pt(), Red), default, default, default) });
        var withoutBorder = Flow(1, [child]);

        Child(Paginate(withBorder).Single(), 0).Children[0].Offset.Y.Should().Be(1 + 10, "the margin stays inside, below the border");
        var collapsed = Paginate(Paragraph(3, "Above"), withoutBorder).Single();
        Child(collapsed, 1).Children[0].Offset.Y.Should().Be(0, "without a border the child's margin collapses through the parent's top");
    }

    [Fact]
    public void An_empty_box_with_a_border_is_not_empty_and_stops_margins_collapsing_through_it()
    {
        var margins = BoxStyle.Initial with { Margin = new Edges(10.Pt(), 0.Pt(), 10.Pt(), 0.Pt()) };
        var band = Flow(2, [], BoxStyle.Initial with { Border = new Border(default, default, new BorderSide(3.Pt(), Red), default) });

        var page = Paginate(Paragraph(1, "Above", margins), band, Paragraph(3, "Below", margins)).Single();

        var root = (BoxFragment)page.Root.Fragment;
        root.Children[1].Offset.Y.Should().Be(10 + LineBoxHeight + 10, "a margin gap above the band");
        root.Children[1].Fragment.Size.Height.Should().Be(3, "the band is as tall as its border");
        root.Children[2].Offset.Y.Should().Be(10 + LineBoxHeight + 10 + 3 + 10, "and one below it: the margins did not collapse through");
    }
}
