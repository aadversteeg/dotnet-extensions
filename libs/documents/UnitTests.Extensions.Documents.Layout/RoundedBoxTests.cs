using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.ErrorPaths;
using static UnitTests.Extensions.Documents.Layout.Models;

namespace UnitTests.Extensions.Documents.Layout;

/// <summary>Rounded corners (<see cref="BoxStyle.BorderRadius"/>, <see cref="RoundedBox"/>): the used radii, the corner
/// construction, the ring of a border and the background under it, a box over a page break, and what the model refuses;
/// the reference cases <c>br-*</c> hold Chromium's prints.</summary>
public class RoundedBoxTests
{
    private const double Pixel = 0.75;

    private static CornerRadius Px(double pixels) => new((pixels * Pixel).Pt());

    /// <summary>A page of 300 × 120 pt with margins of 12 pt: the page area's edges on whole CSS pixels (16 px), 276 × 96 pt.</summary>
    private static StyledDocument Document(params Node[] children) =>
        new(new DocumentMetadata("Test", null), new PageStyle(300.Pt(), 120.Pt(), Edges.All(12.Pt())), Flow(0, [.. children]));

    private static IReadOnlyList<FillPath> Paths(StyledDocument document, int page = 0) =>
        [.. DocumentLayout.Run(document, TestFonts.Set).Value.Pages[page].Items.OfType<FillPath>()];

    private static Error Refused(StyledDocument document) => DocumentLayout.Run(document, TestFonts.Set).Error;

    [Fact]
    public void Radii_larger_than_a_side_are_all_scaled_by_one_factor()
    {
        // br-bg-clamp: 40 px on a box of 162 × 44 px is 22 px; 999 px on a box 24 px high is a pill of 12 px.
        RoundedBox.UsedRadii(BorderRadius.All(Px(40)), 162 * Pixel, 44 * Pixel, false, false)
            .Should().AllSatisfy(corner => corner.Should().Be((22 * Pixel, 22 * Pixel)));
        RoundedBox.UsedRadii(BorderRadius.All(Px(999)), 132 * Pixel, 24 * Pixel, false, false)[0].X.Should().BeApproximately(12 * Pixel, 1e-9);

        // One factor for all four: the 30 px corners of a side of 40 px take the 10 px of the others down with them.
        var used = RoundedBox.UsedRadii(new BorderRadius(Px(30), Px(10), Px(10), Px(30)), 200 * Pixel, 40 * Pixel, false, false);
        used.Select(corner => corner.Y / Pixel).Zip(new[] { 20.0, 20.0 / 3, 20.0 / 3, 20.0 }).Should().OnlyContain(pair => Math.Abs(pair.First - pair.Second) < 1e-9);
    }

    [Fact]
    public void A_percentage_is_of_the_width_across_and_of_the_height_down()
    {
        // br-bg-percent: 50% on 150 × 40 px is an ellipse of 75 × 20 px; 30% is 45 × 12 px.
        RoundedBox.UsedRadii(BorderRadius.All(CornerRadius.OfPercent(50)), 150 * Pixel, 40 * Pixel, false, false)[2].Should().Be((75 * Pixel, 20 * Pixel));
        RoundedBox.UsedRadii(BorderRadius.All(CornerRadius.OfPercent(30)), 150 * Pixel, 40 * Pixel, false, false)[1].Should().Be((45 * Pixel, 12 * Pixel));
    }

    [Fact]
    public void A_corner_is_quadratic_parts_of_equal_angle_written_as_cubics_as_many_as_Chromium_writes_for_its_radius()
    {
        var path = Paths(Document(Paragraph(1, "one", BoxStyle.Initial with { Background = Color.Black, BorderRadius = BorderRadius.All(Px(6)) }))).Single();

        var contour = path.Contours.Single();
        RoundedBox.PartsPerQuarter(4.5).Should().Be(8, "6 px");
        contour.Segments.Should().HaveCount((4 * 8) + 4);
        path.Rule.Should().Be(PathFillRule.NonZero);

        // The top right corner of the page area's box (12 pt, 12 pt to 288 pt): radius 6 px, its centre 4.5 pt left of the right edge.
        var (cx, cy, r) = (288 - 4.5, 12 + 4.5, 4.5);
        var step = Math.PI / 2 / 8;
        var first = (PathCubic)contour.Segments[1];
        first.End.X.Should().BeApproximately(cx + (r * Math.Sin(step)), 1e-9);
        first.End.Y.Should().BeApproximately(cy - (r * Math.Cos(step)), 1e-9);

        // Each cubic control point lies on its end's tangent, two thirds of the way to where the tangents cross.
        first.Control1.Y.Should().BeApproximately(12, 1e-9);
        first.Control1.X.Should().BeApproximately(cx + (2.0 / 3 * r * Math.Tan(step / 2)), 1e-9);
        contour.Segments.Skip(1).Take(8).Cast<PathCubic>().Should().OnlyContain(cubic => Math.Abs(double.Hypot(cubic.End.X - cx, cubic.End.Y - cy) - r) < 1e-9,
            "every part ends on the circle");
    }

    [Fact]
    public void A_border_is_one_ring_of_two_contours_and_the_background_reaches_its_centre_line()
    {
        var border = Border.All(new BorderSide((3 * Pixel).Pt(), Color.Black));
        var paths = Paths(Document(Paragraph(1, "one", BoxStyle.Initial with { Background = new Color(0xf0, 0xe0, 0xa0), Border = border, BorderRadius = BorderRadius.All(Px(8)) })));

        paths.Should().HaveCount(2);
        var (background, ring) = (paths[0], paths[1]);
        background.Contours.Single().Start.Should().Be(new Point(12 + (1.5 * Pixel) + (6.5 * Pixel), 12 + (1.5 * Pixel)), "inset half the width, radius 8 − 1.5 px");
        ring.Rule.Should().Be(PathFillRule.EvenOdd);
        ring.Contours.Should().HaveCount(2);
        ring.Contours[0].Start.Should().Be(new Point(12 + (8 * Pixel), 12));
        ring.Contours[1].Start.Should().Be(new Point(12 + (3 * Pixel) + (5 * Pixel), 12 + (3 * Pixel)), "inset the width, radius 8 − 3 px");
        ring.Contours.Sum(contour => contour.Segments.Count).Should().Be((4 * 8) + 4 + (4 * 4) + 4, "8 parts a quarter at 8 px, 4 at 5 px");
    }

    [Fact]
    public void Below_the_width_the_inner_corner_is_square_and_the_background_a_rectangle()
    {
        // br-border-1px, box "under": 1 px border, radius 0.5 px: Chromium fills the background as a rectangle.
        var border = Border.All(new BorderSide(Pixel.Pt(), Color.Black));
        var page = DocumentLayout.Run(Document(Paragraph(1, "one", BoxStyle.Initial with { Background = Color.White, Border = border, BorderRadius = BorderRadius.All(Px(0.5)) })), TestFonts.Set).Value.Pages[0];

        page.Items.OfType<FillRectangle>().Single(fill => fill.Color == Color.White).Area.Origin.Should().Be(new Point(12 + (Pixel / 2), 12 + (Pixel / 2)));
        var inner = page.Items.OfType<FillPath>().Single().Contours[1];
        inner.Segments.OfType<PathCubic>().Should().BeEmpty();
    }

    [Fact]
    public void A_box_over_a_page_break_is_square_where_the_break_cuts_it()
    {
        // br-break: the first part keeps its top corners, the second its bottom ones.
        var style = BoxStyle.Initial with { Background = Color.Black, BorderRadius = BorderRadius.All(Px(12)) };
        var document = Document(Paragraph(1, Words(100), style with { Orphans = 1, Widows = 1 }));

        Paths(document, 0).Single().Contours.Single().Segments.OfType<PathCubic>().Should().HaveCount(2 * 8);
        Paths(document, 1).Single().Contours.Single().Segments.OfType<PathCubic>().Should().HaveCount(2 * 8);
        var top = Paths(document, 1).Single().Contours.Single();
        top.Start.Y.Should().Be(12, "the cut edge is straight along the page area's top");
        top.Start.X.Should().Be(12, "the top left corner is square");
    }

    [Fact]
    public void A_rounded_box_with_a_border_that_a_page_break_cuts_is_refused()
    {
        var style = BoxStyle.Initial with { Border = Border.All(new BorderSide(1.Pt(), Color.Black)), BorderRadius = BorderRadius.All(Px(6)), Orphans = 1, Widows = 1 };

        var error = Refused(Document(Paragraph(1, Words(100), style)));

        error.Code.Should().Be(LayoutErrorCodes.RoundedBorderBreak);
        error.Metadata![ErrorMetadataKeys.Node].Should().Be("#1");
        DocumentLayout.Run(Document(Paragraph(1, Words(3), style)), TestFonts.Set).IsSuccess.Should().BeTrue("on one page it is drawn");
    }

    [Theory]
    [InlineData(-1.0, 0.0)]
    [InlineData(1.0, -5.0)]
    [InlineData(double.NaN, 0.0)]
    [InlineData(1.0, double.PositiveInfinity)]
    public void A_negative_or_unbounded_radius_is_refused(double points, double percent)
    {
        var error = Refused(Document(Paragraph(1, "one", BoxStyle.Initial with { BorderRadius = new BorderRadius(CornerRadius.Zero, new CornerRadius(points.Pt(), percent), CornerRadius.Zero, CornerRadius.Zero) })));

        error.Code.Should().Be(DocumentErrorCodes.Input.Invalid.Length);
    }

    [Fact]
    public void A_radius_beside_sides_of_different_widths_colours_or_styles_is_refused()
    {
        var side = new BorderSide(1.Pt(), Color.Black);
        StyledDocument With(Border border) => Document(Paragraph(1, "one", BoxStyle.Initial with { Border = border, BorderRadius = BorderRadius.All(Px(4)) }));

        Refused(With(Border.All(side) with { Left = side with { Width = 2.Pt() } })).Code.Should().Be(DocumentErrorCodes.Input.Unsupported.BorderRadius);
        Refused(With(Border.All(side) with { Top = side with { Color = Color.White } })).Code.Should().Be(DocumentErrorCodes.Input.Unsupported.BorderRadius);
        Refused(With(Border.All(side with { Style = BorderStyle.Inset }))).Code.Should().Be(DocumentErrorCodes.Input.Unsupported.BorderRadius);
        Refused(With(Border.None with { Bottom = side })).Code.Should().Be(DocumentErrorCodes.Input.Unsupported.BorderRadius);
        DocumentLayout.Run(With(Border.All(side)), TestFonts.Set).IsSuccess.Should().BeTrue();
        DocumentLayout.Run(With(Border.None), TestFonts.Set).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void A_radius_on_a_table_cell_is_drawn_and_beside_border_sides_of_two_colours_refused()
    {
        static Box Table(Border border) =>
            new(new NodeId(500), SemanticRole.Table, LayoutAlgorithmId.Tracks, BoxStyle.Initial with { Columns = [new TrackSize.Preferred(50.Pt())] },
                [new Box(new NodeId(501), SemanticRole.TableBody, LayoutAlgorithmId.Flow, BoxStyle.Initial with { TableDisplay = TableDisplay.RowGroup },
                    [new Box(new NodeId(502), SemanticRole.TableRow, LayoutAlgorithmId.Flow, BoxStyle.Initial with { TableDisplay = TableDisplay.Row },
                        [new Box(new NodeId(503), SemanticRole.TableCell, LayoutAlgorithmId.Flow, BoxStyle.Initial with { Background = Color.Black, Border = border, BorderRadius = BorderRadius.All(Px(4)) }, [Paragraph(1, "x")], At(0))], At(0))], At(0))], At(0));

        // bri-cell: a cell in the separate model is rounded as a block box.
        Paths(Document(Table(Border.None))).Should().ContainSingle(path => path.Color == Color.Black);

        var error = Refused(Document(Table(Border.All(new BorderSide(Pixel.Pt(), Color.Black)) with { Top = new BorderSide(Pixel.Pt(), Color.White) })));

        error.Code.Should().Be(DocumentErrorCodes.Input.Unsupported.BorderRadius);
        error.Metadata![ErrorMetadataKeys.Node].Should().Be("#503");
    }
    [Fact]
    public void Chromium_s_count_of_parts_grows_with_the_radius()
    {
        // br-border-1px (0.5 px centre line), br-bg-radii (3, 6, 8, 20 px), br-bg-clamp (22, 30 px), bri-iblk-fraction (11 px).
        new[] { 0.5, 1, 2, 3, 5, 5.5, 6, 11, 20, 22, 30, 75 }.Select(pixels => RoundedBox.PartsPerQuarter(pixels * Pixel))
            .Should().Equal(2, 2, 4, 4, 4, 8, 8, 8, 8, 16, 16, 16);
    }

    [Fact]
    public void An_inline_box_fills_its_background_to_the_outer_edge_of_its_border_and_rounds_only_the_sides_its_part_holds()
    {
        // bri-pill: the background to the border box's outer edge with the full radius, then the ring.
        var box = new InlineBoxStyle(Length.Zero, Length.Zero, Edges.All(Length.Zero), Border.All(new BorderSide(Pixel.Pt(), Color.Black)), Color.White)
        {
            BorderRadius = BorderRadius.All(Px(999)),
        };
        var area = new Rect(new Point(30, 12), new Size(33, 15.75));
        var whole = RoundedBox.InlinePart(box, area, holdsStart: true, holdsEnd: true).Cast<FillPath>().ToList();
        whole.Should().HaveCount(2);
        whole[0].Contours.Single().Start.Should().Be(new Point(30 + (10.5 * Pixel), 12), "a pill of 21 px: the radius half the height, from the outer edge");
        whole[1].Contours.Should().HaveCount(2);

        // bri-broken: the first part has its left corners, the last its right ones, a part between them is a rectangle.
        var background = box with { Border = Border.None };
        var first = (FillPath)RoundedBox.InlinePart(background, area, holdsStart: true, holdsEnd: false).Single();
        first.Contours.Single().Segments.OfType<PathCubic>().Should().HaveCount(2 * 8);
        first.Contours.Single().Segments[0].Should().Be(new PathLine(new Point(63, 12)), "the top runs to the square right corner");
        var last = (FillPath)RoundedBox.InlinePart(background, area, holdsStart: false, holdsEnd: true).Single();
        last.Contours.Single().Start.Should().Be(new Point(30, 12), "the top left corner is square");
        RoundedBox.InlinePart(background, area, holdsStart: false, holdsEnd: false).Single().Should().Be(new FillRectangle(area, Color.White));
    }

    [Fact]
    public void A_rounded_inline_box_with_a_border_over_two_lines_is_refused_and_one_without_a_border_is_drawn()
    {
        static StyledDocument Broken(Border border)
        {
            var box = new InlineBoxStyle(Length.Zero, Length.Zero, Edges.All(Length.Zero), border, Color.White) { BorderRadius = BorderRadius.All(Px(8)) };
            var group = new InlineGroup(new NodeId(3), null, Text(), [new TextRun(new NodeId(4), Words(40), Text(), At(4))], At(3)) { Box = box };
            return Document(new Box(new NodeId(1), SemanticRole.Paragraph, LayoutAlgorithmId.Inline, BoxStyle.Initial, [group], At(1)));
        }

        var error = Refused(Broken(Border.All(new BorderSide(Pixel.Pt(), Color.Black))));
        error.Code.Should().Be(LayoutErrorCodes.RoundedBorderLineBreak);
        error.Metadata![ErrorMetadataKeys.Node].Should().Be("#3");

        DocumentLayout.Run(Broken(Border.None), TestFonts.Set).IsSuccess.Should().BeTrue();
    }
}
