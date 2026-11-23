using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.ErrorPaths;
using static UnitTests.Extensions.Documents.Layout.Models;

namespace UnitTests.Extensions.Documents.Layout;

/// <summary>
/// The height of a block box in block flow (<see cref="BoxStyle.SpecifiedHeight"/>, <see cref="BoxStyle.MinHeight"/>,
/// <see cref="BoxStyle.MaxHeight"/>, <see cref="BoxStyle.BoxSizing"/>; CSS 2.1 §10.6.3, §10.7, CSS Fragmentation 3 §5.3), as
/// Chromium prints the cases <c>sz-height*</c>, <c>sz-min-*</c> and <c>sz-max-height</c>, on the test page of
/// <see cref="Models"/> (a page area of 180 × 80 pt, lines of 12 pt). Each box has a background, so its extent on each
/// page is a painted rectangle.
/// </summary>
public class BlockHeightTests
{
    private static readonly Color Grey = new(221, 221, 221);

    private static BoxStyle Sized(double? height = null, double? min = null, double? max = null) => BoxStyle.Initial with
    {
        Background = Grey,
        SpecifiedHeight = height?.Pt(),
        MinHeight = min?.Pt(),
        MaxHeight = max?.Pt(),
    };

    /// <summary>A box with a background holding <paramref name="lines"/> words, one per line (20 pt wide).</summary>
    private static Box Lines(int id, int lines, BoxStyle style) =>
        Flow(id, [Paragraph(id + 1, Words(lines), BoxStyle.Initial with { SpecifiedWidth = new SizeValue.Fixed(20.Pt()) })], style);

    private static PrintedPages Run(params Node[] children)
    {
        var pages = DocumentLayout.Run(Document(children), TestFonts.Set);
        pages.IsSuccess.Should().BeTrue(pages.IsFailure ? pages.Error.ToString() : string.Empty);
        return PrintedPages.Engine(pages.Value);
    }

    private static Error Refused(params Node[] children)
    {
        var pages = DocumentLayout.Run(Document(children), TestFonts.Set);
        pages.IsFailure.Should().BeTrue();
        return pages.Error;
    }

    /// <summary>The painted extent of the box on each page: top and bottom of its background.</summary>
    private static (int Page, double Top, double Bottom)[] Extents(PrintedPages pages) =>
        [.. pages.Pages.SelectMany(page => page.Rectangles.Where(box => box.Colour == "#dddddd").Select(box => (page.Number, R(box.Y0), R(box.Y1))))];

    private static (int Page, double Baseline) Word(PrintedPages pages, string text) =>
        pages.Pages.SelectMany(page => page.Words).Where(word => word.Text == text).Select(word => (word.Page, R(word.Baseline))).Single();

    /// <summary>A position to a thousandth of a point, as the print files hold it.</summary>
    private static double R(double value) => Math.Round(value, 3);

    [Fact]
    public void A_height_smaller_than_the_content_ends_the_box_and_what_follows_starts_at_its_end_over_the_overflow()
    {
        // sz-height: a 10 pt box holding three lines; the next paragraph starts at the box's end, the lines pass it.
        var pages = Run(Lines(1, 3, Sized(height: 10)), Paragraph(5, "after"));

        Extents(pages).Should().Equal((1, 10, 20));
        Word(pages, "word3").Baseline.Should().BeApproximately(10 + 24 + Ascent, 1e-3, "the content is drawn below the box's end");
        Word(pages, "after").Should().Be((1, R(10 + 10 + Ascent)));
    }

    [Fact]
    public void A_height_larger_than_the_content_takes_its_room()
    {
        var pages = Run(Lines(1, 1, Sized(height: 40)), Paragraph(5, "after"));

        Extents(pages).Should().Equal((1, 10, 50));
        Word(pages, "after").Should().Be((1, R(50 + Ascent)));
    }

    [Fact]
    public void A_height_cut_by_a_page_break_goes_on_with_what_the_page_before_did_not_show()
    {
        // sz-height-break: 130 pt below a line, in a page area of 80 pt: 68 pt on the first page, the other 62 on the second.
        var pages = Run(Paragraph(1, "first"), Lines(2, 1, Sized(height: 130)), Paragraph(5, "after"));

        Extents(pages).Should().Equal((1, 22, 90), (2, 10, 72));
        Word(pages, "after").Should().Be((2, R(72 + Ascent)));
    }

    [Fact]
    public void The_rest_of_a_height_on_the_next_page_may_be_shorter_than_a_line()
    {
        // sz-height-rest-short: 70 pt below a line leaves 2 pt for the second page, and what follows starts there.
        var pages = Run(Paragraph(1, "first"), Lines(2, 1, Sized(height: 70)), Paragraph(5, "after"));

        Extents(pages).Should().Equal((1, 22, 90), (2, 10, 12));
        Word(pages, "after").Should().Be((2, R(12 + Ascent)));
    }

    [Fact]
    public void A_minimum_height_is_shared_over_the_pages_as_a_height_is()
    {
        // sz-min-height-break: 130 pt at least, eight lines of content (96 pt): five lines and 68 pt on the first page, three
        // lines and the other 62 pt on the second.
        var pages = Run(Paragraph(1, "first"), Lines(2, 8, Sized(min: 130)), Paragraph(5, "after"));

        Extents(pages).Should().Equal((1, 22, 90), (2, 10, 72));
        Word(pages, "word6").Should().Be((2, R(10 + Ascent)));
        Word(pages, "after").Should().Be((2, R(72 + Ascent)));
    }

    [Fact]
    public void A_maximum_height_below_the_content_ends_the_box_and_one_above_it_has_no_effect()
    {
        // sz-max-height: 20 pt over three lines, 60 pt over one.
        var pages = Run(Lines(1, 3, Sized(max: 20)), Paragraph(5, "after"), Lines(6, 1, Sized(max: 40)), Paragraph(9, "end"));

        Extents(pages).Should().Equal((1, 10, 30), (1, 42, 54));
        Word(pages, "after").Should().Be((1, R(30 + Ascent)));
        Word(pages, "end").Should().Be((1, R(54 + Ascent)));
    }

    [Fact]
    public void The_minimum_wins_over_the_height_and_the_maximum_and_the_maximum_over_the_height()
    {
        // sz-min-max-height: height 30 with min-height 50 is 50; height 50 with max-height 30 is 30; min 50 over max 30 is 50;
        // min-height 20 below two lines of content is the content's 24.
        BlockSizes.Of(Sized(height: 30, min: 50)).Should().Be(new BlockSizes(50, 50));
        BlockSizes.Of(Sized(height: 50, max: 30)).Should().Be(new BlockSizes(30, 30));
        BlockSizes.Of(Sized(min: 50, max: 30)).Should().Be(new BlockSizes(50, 50));
        BlockSizes.Of(Sized(min: 20)).Should().Be(new BlockSizes(20, double.PositiveInfinity));
        BlockSizes.Of(BoxStyle.Initial).Should().BeNull();

        var pages = Run(Lines(1, 1, Sized(min: 50, max: 30)), Lines(3, 2, Sized(min: 20)));
        Extents(pages).Should().Equal((1, 10, 60), (1, 60, 84));
    }

    [Fact]
    public void Padding_and_border_are_added_to_a_content_box_height_and_lie_inside_a_border_box_height()
    {
        // sz-height-box-sizing: 40 pt with 6 pt padding and a 3 pt border is 58 pt as a content box, 40 as a border box.
        var style = Sized(height: 40) with { Padding = Edges.All(6.Pt()), Border = Border.All(new BorderSide(3.Pt(), Color.Black)) };

        BlockSizes.Of(style).Should().Be(new BlockSizes(58, 58));
        BlockSizes.Of(style with { BoxSizing = BoxSizing.BorderBox }).Should().Be(new BlockSizes(40, 40));
        BlockSizes.Of(style with { BoxSizing = BoxSizing.BorderBox, SpecifiedHeight = 10.Pt() }).Should().Be(new BlockSizes(18, 18), "a border box is never lower than its padding and border");
    }

    [Fact]
    public void A_border_box_height_over_a_page_break_has_its_top_border_on_the_first_page_and_its_bottom_border_on_the_last()
    {
        // sz-height-box-sizing-break: the 130 pt border box holds the padding and border; 68 pt and 62 pt as without them.
        var style = Sized(height: 130) with { BoxSizing = BoxSizing.BorderBox, Padding = Edges.All(6.Pt()), Border = Border.All(new BorderSide(3.Pt(), Color.Black)) };
        var pages = Run(Paragraph(1, "first"), Lines(2, 1, style), Paragraph(5, "after"));

        Extents(pages).Select(extent => (extent.Page, extent.Top)).Should().Equal((1, 22.0), (2, 10.0));
        Word(pages, "after").Should().Be((2, R(72 + Ascent)));
        var black = pages.Pages.Select(page => page.Rectangles.Where(box => box.Colour == "#000000" && box.X1 - box.X0 > 100).Select(box => box.Y0).ToArray()).ToArray();
        black[0].Should().Equal([22.0], "only the top border on the first page");
        black[1].Should().Equal([69.0], "only the bottom border on the second");
    }

    [Fact]
    public void Content_that_goes_on_past_a_page_break_after_the_box_s_height_ended_is_refused()
    {
        // sz-height-overflow-page: Chromium ends the 40 pt box on the first page, starts what follows below it there and
        // prints the rest of the content on the next page.
        Refused(Paragraph(1, "first"), Lines(2, 8, Sized(height: 40)), Paragraph(5, "after")).Code.Should().Be(LayoutErrorCodes.OverflowAcrossPages);

        // sz-height-forced: the same after a forced break inside the box.
        var forced = Flow(2, [Paragraph(3, "before"), Paragraph(4, "after", BoxStyle.Initial with { BreakBefore = BreakBetween.Page })], Sized(height: 40));
        var error = Refused(Paragraph(1, "first"), forced);
        error.Code.Should().Be(LayoutErrorCodes.OverflowAcrossPages);
        error.Metadata![ErrorMetadataKeys.Node].Should().Be("#2", "the box whose height ends");
    }

    [Fact]
    public void The_last_child_s_bottom_margin_stays_inside_a_box_with_a_height()
    {
        // CSS 2.1 §8.3.1: the bottom margin of a last child collapses with its parent's only where the parent's height is auto.
        var child = Paragraph(2, "a", BoxStyle.Initial with { Margin = new Edges(0.Pt(), 0.Pt(), 30.Pt(), 0.Pt()) });

        Word(Run(Flow(1, [child], Sized(height: 20)), Paragraph(5, "after")), "after").Should().Be((1, R(30 + Ascent)));
        Word(Run(Flow(1, [child], BoxStyle.Initial), Paragraph(5, "after")), "after").Should().Be((1, R(52 + Ascent)), "with height auto the margin leaves the box");
    }

    [Fact]
    public void An_empty_box_with_a_height_takes_its_room()
    {
        var pages = Run(Flow(1, [], Sized(height: 20)), Paragraph(5, "after"));

        Extents(pages).Should().Equal((1, 10, 30));
        Word(pages, "after").Should().Be((1, R(30 + Ascent)));
    }

    [Fact]
    public void A_height_inside_a_table_is_refused_and_a_negative_one_is_invalid()
    {
        var cell = new Box(new NodeId(3), SemanticRole.TableCell, LayoutAlgorithmId.Flow, BoxStyle.Initial, [Paragraph(4, "a", BoxStyle.Initial with { SpecifiedHeight = 20.Pt() })], At(0));
        var row = new Box(new NodeId(2), SemanticRole.TableRow, LayoutAlgorithmId.Flow, BoxStyle.Initial with { TableDisplay = TableDisplay.Row }, [cell], At(0));
        var table = new Box(new NodeId(1), SemanticRole.Table, LayoutAlgorithmId.Tracks, BoxStyle.Initial with { Columns = [TrackSize.Auto.Instance] }, [row], At(0));

        Refused(table).Code.Should().Be(DocumentErrorCodes.Input.Unsupported.Size);
        Refused(Paragraph(1, "a", BoxStyle.Initial with { MinHeight = (-1).Pt() })).Code.Should().Be(DocumentErrorCodes.Input.Invalid.Length);
    }
}
