using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.ErrorPaths;
using static UnitTests.Extensions.Documents.Layout.Models;

namespace UnitTests.Extensions.Documents.Layout;

/// <summary>
/// The row of page-margin boxes of one page edge (<see cref="MarginBoxesLayout"/>, CSS Paged Media 3 §5.3.2) against
/// Chromium 159's prints, in the pinned container, with Lato from the repository.
/// </summary>
/// <remarks>
/// <para>The measured pages are 400 × 300 pt with <c>margin: 60pt 50pt</c> (a sheet of 400.08 × 300 pt); each margin box
/// has a background, and the numbers are the edges of the painted rectangles, at whole pixels. Chromium lays the row out
/// from the left page margin cut to 1/64 px (66.65625 px, 49.992 pt: where its left-aligned text starts) to the page
/// area's right edge at a whole pixel (468 px, 351.0 pt: where its right-aligned text ends), a row of 301.008 pt, and
/// paints each rectangle at the nearest whole pixels. The tests give the row that extent (header and footer of 60 pt,
/// the page's left margin 49.992 pt) and compare the edges as the comparison of whole prints does: snapped to the nearest
/// whole pixel, then within its bound of 0.1 pt. The A4 pages (<c>margin: 4cm 1cm 2cm</c>) have their row from 28.336 pt
/// (1 cm cut to 1/64 px) to 567.75 pt (757 px).</para>
/// </remarks>
public class MarginBoxesLayoutTests
{
    /// <summary>The bound of the comparison of whole prints (0.1 pt), after the snapping of rectangle edges to whole
    /// pixels that it applies too.</summary>
    private const double Bound = 0.1;

    private const double RowLeft = 66.65625 * 0.75;
    private const double RowRight = 468 * 0.75;

    private static readonly BoxStyle Middle = BoxStyle.Initial with { VerticalAlign = VerticalAlign.Middle };

    private static double WholePixel(double length) => Math.Round(length / 0.75, MidpointRounding.AwayFromZero) * 0.75;

    private static TextStyle Lato(double size, bool bold = false) => new("Lato", size.Pt(), bold ? FontWeight.Bold : FontWeight.Regular, Color.Black);

    /// <summary>A generated margin box with the UA defaults of its place: text aligned to its side, content centred in
    /// the height.</summary>
    private static Box MarginBox(int id, MarginBoxPlace place, Node[] content, BoxStyle? style = null, TextStyle? strut = null, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0) =>
        new(new NodeId(id), SemanticRole.Generic, LayoutAlgorithmId.Inline,
            (style ?? Middle) with
            {
                MarginBoxPlace = place,
                TextAlign = place switch { MarginBoxPlace.Start => TextAlign.Left, MarginBoxPlace.Center => TextAlign.Center, _ => TextAlign.Right },
                Text = strut ?? Lato(10),
            },
            [.. content], At(line));

    private static Box Text(int id, MarginBoxPlace place, string text, double size = 10, bool bold = false, BoxStyle? style = null, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0) =>
        MarginBox(id, place, [new TextRun(new NodeId(id + 1), text, Lato(size, bold), At(line))], style, Lato(size, bold), line);

    /// <summary>A margin box with <c>content: ""</c>: generated, no runs.</summary>
    private static Box Empty(int id, MarginBoxPlace place, BoxStyle? style = null, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0) =>
        MarginBox(id, place, [], style, null, line);

    private static Box Row(int id, params Box[] boxes) =>
        new(new NodeId(id), SemanticRole.Generic, LayoutAlgorithmId.MarginBoxes, BoxStyle.Initial, [.. boxes], At(0));

    /// <summary>A page of the measured 400 × 300 cases with its top and bottom rows, 60 pt each, and one paragraph.</summary>
    private static StyledDocument Page(Box? top, Box? bottom) =>
        new(new DocumentMetadata("Test", null),
            new PageStyle(400.08.Pt(), 300.Pt(), new Edges(0.Pt(), (400.08 - RowRight).Pt(), 0.Pt(), RowLeft.Pt()))
            {
                Template = new PageTemplate(top is null ? null : new PageArea(top, 60.Pt()), bottom is null ? null : new PageArea(bottom, 60.Pt())),
            },
            Flow(0, [Paragraph(1, "one")]));

    private static PageFragment Laid(StyledDocument document)
    {
        var pages = Paginator.Paginate(LayoutEnvironment.Create(TestFonts.Set), document);
        pages.IsSuccess.Should().BeTrue(pages.IsFailure ? pages.Error.ToString() : string.Empty);
        return pages.Value[0];
    }

    /// <summary>The painted left and right edges of each margin box of a row, at whole pixels, in the order of the row.</summary>
    private static double[] Edges(PlacedFragment row) =>
        [.. ((BoxFragment)row.Fragment).Children.SelectMany(box => new[] { row.Offset.X + box.Offset.X, row.Offset.X + box.Offset.X + box.Fragment.Size.Width }).Select(WholePixel)];

    private static void ShouldBeAt(double[] edges, params double[] measured)
    {
        edges.Should().HaveCount(measured.Length);
        foreach (var (ours, theirs) in edges.Zip(measured))
        {
            ours.Should().BeApproximately(theirs, Bound, $"the edges are [{string.Join(", ", edges)}], Chromium's [{string.Join(", ", measured)}]");
        }
    }

    /// <summary>The painted baselines of a row's text, at whole pixels, top to bottom.</summary>
    private static double[] Baselines(PlacedFragment row)
    {
        var baselines = new List<double>();
        var pending = new Stack<(Fragment Fragment, double Y)>();
        pending.Push((row.Fragment, row.Offset.Y));
        while (pending.Count > 0)
        {
            var (fragment, y) = pending.Pop();
            switch (fragment)
            {
                case TextFragment text:
                    baselines.Add(WholePixel(y + text.Ascent));
                    break;
                case BoxFragment box:
                    foreach (var child in box.Children)
                    {
                        pending.Push((child.Fragment, y + child.Offset.Y));
                    }

                    break;
                case LineFragment line:
                    foreach (var child in line.Children)
                    {
                        pending.Push((child.Fragment, y + child.Offset.Y));
                    }

                    break;
            }
        }

        return [.. baselines.Distinct().Order()];
    }

    [Fact]
    public void Two_side_boxes_whose_max_content_widths_fit_share_the_row_in_proportion_to_them()
    {
        // "Text" beside "Rrrrrrrr Rrrrrrrr", both fit: the space left over them is shared in proportion to their widths.
        var page = Laid(Page(Row(10, Text(20, MarginBoxPlace.Start, "Text"), Text(30, MarginBoxPlace.End, "Rrrrrrrr Rrrrrrrr")), null));
        var row = (BoxFragment)page.Header!.Fragment;
        var environment = LayoutEnvironment.Create(TestFonts.Set);
        var text = environment.Measure((Box)((Box)row.Box).Children[0]).Value.MaxContent;
        var other = environment.Measure((Box)((Box)row.Box).Children[1]).Value.MaxContent;

        row.Children[0].Fragment.Size.Width.Should().BeApproximately((RowRight - RowLeft) * text / (text + other), 1e-9);
        row.Children[1].Fragment.Size.Width.Should().BeApproximately((RowRight - RowLeft) * other / (text + other), 1e-9);
    }

    [Fact]
    public void A_side_box_beside_one_that_needs_the_rest_of_the_row_keeps_its_own_width()
    {
        // "Aaaa" beside three words of sixteen letters, which together just fit the row: 21.75 and 279.0 pt.
        var page = Laid(Page(Row(10, Text(20, MarginBoxPlace.Start, "Aaaa"), Text(30, MarginBoxPlace.End, "Bbbbbbbbbbbbbbbb Bbbbbbbbbbbbbbbb Bbbbbbbbbbbbbbbb")), null));

        ShouldBeAt(Edges(page.Header!), 50.25, 72.0, 72.0, 351.0);
    }

    [Fact]
    public void An_empty_box_counts_its_padding()
    {
        // "Text" beside an empty box with 10 pt padding on each side: 144.0 and 156.75 pt; at the bottom an empty box with
        // that padding beside "R": 228.75 and 72.0 pt.
        var padded = Middle with { Padding = new Edges(0.Pt(), 10.Pt(), 0.Pt(), 10.Pt()) };
        var page = Laid(Page(
            Row(10, Text(20, MarginBoxPlace.Start, "Text"), Empty(30, MarginBoxPlace.End, padded)),
            Row(40, Empty(50, MarginBoxPlace.Start, padded), Text(60, MarginBoxPlace.End, "R"))));

        ShouldBeAt(Edges(page.Header!), 50.25, 194.25, 194.25, 351.0);
        ShouldBeAt(Edges(page.Footer!), 50.25, 279.0, 279.0, 351.0);
    }

    [Theory]
    [InlineData(MarginBoxPlace.Start)]
    [InlineData(MarginBoxPlace.Center)]
    [InlineData(MarginBoxPlace.End)]
    public void A_box_alone_takes_the_whole_row(MarginBoxPlace place)
    {
        // Measured with "BR", with "R" and with "Text" alone, and with an empty box alone at the start, centre and end.
        var page = Laid(Page(Row(10, Text(20, place, "Text")), Row(40, Empty(50, place))));

        ShouldBeAt(Edges(page.Header!), 50.25, 351.0);
        ShouldBeAt(Edges(page.Footer!), 50.25, 351.0);
    }

    [Fact]
    public void A_centre_box_whose_max_content_fits_takes_its_share_and_is_centred_with_the_sides_equal_even_without_an_end_box()
    {
        // 20 pt bold: "TL" "TC" and no end box: the centre box 107.25 pt from 147.0; with "TR" 99.75 pt from 150.75;
        // "BL" "BC" "BR" 101.25 pt from 150.0.
        var page = Laid(Page(
            Row(10, Text(20, MarginBoxPlace.Start, "TL", 20, bold: true), Text(30, MarginBoxPlace.Center, "TC", 20, bold: true)),
            Row(40, Text(50, MarginBoxPlace.Start, "BL", 20, bold: true), Text(60, MarginBoxPlace.Center, "BC", 20, bold: true), Text(70, MarginBoxPlace.End, "BR", 20, bold: true))));
        var withEnd = Laid(Page(
            Row(10, Text(20, MarginBoxPlace.Start, "TL", 20, bold: true), Text(30, MarginBoxPlace.Center, "TC", 20, bold: true), Text(35, MarginBoxPlace.End, "TR", 20, bold: true)), null));

        ShouldBeAt(Edges(page.Header!), 50.25, 147.0, 147.0, 254.25);
        ShouldBeAt(Edges(withEnd.Header!), 50.25, 150.75, 150.75, 250.5, 250.5, 351.0);
        ShouldBeAt(Edges(page.Footer!), 50.25, 150.0, 150.0, 251.25, 251.25, 351.0);
    }

    [Fact]
    public void A_centre_box_whose_max_content_does_not_fit_takes_its_min_content_and_a_share_of_the_rest_in_proportion_to_max_minus_min()
    {
        // "L", a centre text of two lines and "Right side text that is longer": the centre box 147.75 pt, from 126.75 to
        // 274.5; its two lines centred in the 60 pt box (baselines 267.75 and 279.75), the side boxes' one line at 273.75.
        var page = Laid(Page(null, Row(40,
            Text(50, MarginBoxPlace.Start, "L"),
            Text(60, MarginBoxPlace.Center, "Wide centre text that takes much more room than the sides"),
            Text(70, MarginBoxPlace.End, "Right side text that is longer"))));

        ShouldBeAt(Edges(page.Footer!), 50.25, 126.75, 126.75, 274.5, 274.5, 351.0);
        Baselines(page.Footer!).Should().Equal(267.75, 273.75, 279.75);
    }

    [Fact]
    public void A_narrow_centre_box_beside_wide_side_boxes_takes_its_max_content_and_a_little_more()
    {
        // "L", "Centre text" and "Right side text that is longer": the centre box 50.25 pt, from 175.5.
        var page = Laid(Page(null, Row(40,
            Text(50, MarginBoxPlace.Start, "L"),
            Text(60, MarginBoxPlace.Center, "Centre text"),
            Text(70, MarginBoxPlace.End, "Right side text that is longer"))));

        ShouldBeAt(Edges(page.Footer!), 50.25, 175.5, 175.5, 225.75, 225.75, 351.0);
    }

    [Fact]
    public void Boxes_whose_max_content_widths_are_all_zero_share_the_row_by_the_boxes_they_stand_for()
    {
        // Two empty boxes without padding: half the row each. An empty centre box beside an empty start box: a third, the
        // start box another third.
        var page = Laid(Page(
            Row(10, Empty(20, MarginBoxPlace.Start), Empty(30, MarginBoxPlace.End)),
            Row(40, Empty(50, MarginBoxPlace.Start), Empty(60, MarginBoxPlace.Center))));

        ShouldBeAt(Edges(page.Header!), 50.25, 200.25, 200.25, 351.0);
        ShouldBeAt(Edges(page.Footer!), 50.25, 150.0, 150.0, 250.5);
    }

    [Fact]
    public void A_centre_box_with_padding_only_shares_the_row_with_twice_the_wider_side_box()
    {
        // An empty start box with 10 pt left padding and an empty centre box with 30 pt left padding: the centre box from
        // 110.25 to 291.0. A centre box without width beside "Aaaa": none, and the start box half the row.
        var page = Laid(Page(
            Row(10, Empty(20, MarginBoxPlace.Start, Middle with { Padding = new Edges(0.Pt(), 0.Pt(), 0.Pt(), 10.Pt()) }), Empty(30, MarginBoxPlace.Center, Middle with { Padding = new Edges(0.Pt(), 0.Pt(), 0.Pt(), 30.Pt()) })),
            Row(40, Text(50, MarginBoxPlace.Start, "Aaaa"), Empty(60, MarginBoxPlace.Center))));

        ShouldBeAt(Edges(page.Header!), 50.25, 110.25, 110.25, 291.0);
        ShouldBeAt(Edges(page.Footer!), 50.25, 200.25, 200.25, 200.25);
    }

    [Fact]
    public void One_line_is_centred_in_the_height_of_its_box()
    {
        // One 10 pt line in the 60 pt top box: baseline 33.75.
        var page = Laid(Page(Row(10, Text(20, MarginBoxPlace.Start, "Aaaa")), null));

        Baselines(page.Header!).Should().Equal(33.75);
    }

    [Fact]
    public void Margins_padding_and_border_lie_inside_the_row_s_height()
    {
        // "Text" with a 3 pt bottom border: the border from 57 to 60, the box from 0. A box with a 5 pt top margin and
        // 6 pt top padding is 55 pt tall and starts 5 pt down; its line is centred in the 49 pt below the padding.
        var bordered = Middle with { Border = new Border(BorderSide.None, BorderSide.None, new BorderSide(3.Pt(), Color.Black), BorderSide.None) };
        var spaced = Middle with { Margin = new Edges(5.Pt(), 0.Pt(), 0.Pt(), 0.Pt()), Padding = new Edges(6.Pt(), 0.Pt(), 0.Pt(), 0.Pt()) };
        var page = Laid(Page(Row(10, Text(20, MarginBoxPlace.Start, "Text", style: bordered), Text(30, MarginBoxPlace.End, "R", style: spaced)), null));

        var boxes = ((BoxFragment)page.Header!.Fragment).Children;
        boxes[0].Offset.Y.Should().Be(0);
        boxes[0].Fragment.Size.Height.Should().Be(60);
        boxes[1].Offset.Y.Should().Be(5);
        boxes[1].Fragment.Size.Height.Should().Be(55);
        var line = ((BoxFragment)boxes[1].Fragment).Children.Single();
        line.Offset.Y.Should().BeApproximately(6 + ((49 - LineBoxHeight) / 2), 1e-9);
    }

    [Fact]
    public void Top_and_bottom_alignment_move_the_content_to_the_box_s_edges()
    {
        var page = Laid(Page(Row(10,
            Text(20, MarginBoxPlace.Start, "Top", style: BoxStyle.Initial with { VerticalAlign = VerticalAlign.Top }),
            Text(30, MarginBoxPlace.End, "Bottom", style: BoxStyle.Initial with { VerticalAlign = VerticalAlign.Bottom })), null));

        var boxes = ((BoxFragment)page.Header!.Fragment).Children;
        ((BoxFragment)boxes[0].Fragment).Children.Single().Offset.Y.Should().Be(0);
        ((BoxFragment)boxes[1].Fragment).Children.Single().Offset.Y.Should().BeApproximately(60 - LineBoxHeight, 1e-9);
    }

    /// <summary>The row of the A4 pages with <c>margin: 4cm 1cm 2cm</c>: from 1 cm cut to 1/64 px to the page area's
    /// right edge at 757 px.</summary>
    private const double SheetRowLeft = 37.78125 * 0.75;
    private const double SheetRowRight = 757 * 0.75;

    [Theory]
    [InlineData("Jansen & Zn. Installatietechniek", "SmallBusiness onderhoud", "2026-10-01 - 2026-10-31", 357.0)]
    [InlineData("Bakkerij De Gouden Korenaar B.V.", "Platformteam Q3", "2026-09-01 - 2026-09-30", 347.25)]
    public void The_top_row_of_the_timesheets_shares_the_row_between_the_logo_title_and_the_three_lines_at_the_right(string first, string second, string third, double edge)
    {
        // The start box: the 2.5 cm logo and an em space and "Urenstaat" in 24 pt bold; the end box: three preserved lines
        // of 9 pt. Both fit, so the row is shared in proportion to their max-content widths: Chromium's boxes meet at 357.0
        // and 347.25 pt. Only the widths: the start box's line (the logo on the baseline and the descent below it) is
        // taller than the box Chromium gives it, 78.98 pt with the border against 73.56 pt, and the layout of the row
        // refuses that; Chromium draws the baseline of "Urenstaat" at 99.36 pt, below the top of the border at 99.0 pt.
        var title = MarginBox(20, MarginBoxPlace.Start,
        [
            new Box(new NodeId(21), SemanticRole.Figure("logo"), LayoutAlgorithmId.Image, BoxStyle.Initial,
                [new ImageNode(new NodeId(22), TestImages.Load("jpeg-1x1.jpg"), 2.5.Cm(), null, At(0))], At(0)),
            new TextRun(new NodeId(23), "\u2003Urenstaat", Lato(24, bold: true), At(0)),
        ], strut: Lato(24, bold: true));
        var lines = MarginBox(30, MarginBoxPlace.End,
            [new TextRun(new NodeId(31), $"{first}\n{second}\n{third}", Lato(9) with { WhiteSpaceCollapse = WhiteSpaceCollapse.Preserve, LineHeight = new LineHeight.Factor(1.5) }, At(0))],
            strut: Lato(9) with { LineHeight = new LineHeight.Factor(1.5) });
        var environment = LayoutEnvironment.Create(TestFonts.Set);

        var (start, _, _) = MarginBoxesLayout.Widths(environment.Measure(title).Value, null, environment.Measure(lines).Value, SheetRowRight - SheetRowLeft);

        WholePixel(SheetRowLeft + start).Should().BeApproximately(edge, Bound);
    }

    [Fact]
    public void The_bottom_row_of_the_timesheets_centres_the_middle_box_between_side_boxes_of_equal_width()
    {
        // 8 pt: "Rekening: NL00 BANK 0123 4567 89", "BTW: NL000000000B00", two em spaces, "KvK: 00000000", and "Pagina 1 van 2";
        // each with a 0.4 cm top margin, 6 pt top padding and a 3 pt top border. Chromium's borders: 28.5 to 198.0, 198.0
        // to 398.25 and 398.25 to 567.75, at 796.5 pt.
        var boxStyle = BoxStyle.Initial with
        {
            Margin = new Edges(0.4.Cm(), 0.Pt(), 0.Pt(), 0.Pt()),
            Padding = new Edges(6.Pt(), 0.Pt(), 0.Pt(), 0.Pt()),
            Border = new Border(new BorderSide(3.Pt(), Color.Black), BorderSide.None, BorderSide.None, BorderSide.None),
        };
        var counters = MarginBox(70, MarginBoxPlace.End,
        [
            new TextRun(new NodeId(71), "Pagina ", Lato(8), At(0)),
            new CounterRun(new NodeId(72), PageCounter.PageNumber, Lato(8), At(0)),
            new TextRun(new NodeId(73), " van ", Lato(8), At(0)),
            new CounterRun(new NodeId(74), PageCounter.PageCount, Lato(8), At(0)),
        ], boxStyle, Lato(8));
        var row = Row(40,
            Text(50, MarginBoxPlace.Start, "Rekening: NL00 BANK 0123 4567 89", 8, style: boxStyle),
            Text(60, MarginBoxPlace.Center, "BTW: NL000000000B00\u2003\u2003KvK: 00000000", 8, style: boxStyle),
            counters);
        var document = new StyledDocument(new DocumentMetadata("Test", null),
            new PageStyle(594.96.Pt(), 841.92.Pt(), new Edges((151 * 0.75).Pt(), (594.96 - SheetRowRight).Pt(), 0.Pt(), SheetRowLeft.Pt()))
            {
                Template = new PageTemplate(null, new PageArea(row, (841.92 - (1047 * 0.75)).Pt())),
            },
            Flow(0, [Paragraph(1, "one"), Paragraph(2, "two", BoxStyle.Initial with { BreakBefore = BreakBetween.Page })]));

        var page = Laid(document);

        ShouldBeAt(Edges(page.Footer!), 28.5, 198.0, 198.0, 398.25, 398.25, 567.75);
        WholePixel(page.Footer!.Offset.Y + ((BoxFragment)page.Footer.Fragment).Children[0].Offset.Y).Should().BeApproximately(796.5, Bound);
    }

    [Fact]
    public void Content_wider_than_its_box_is_refused_at_the_box()
    {
        // Measured at 12 pt: "Left" beside "Averyveryverylongunbreakablewordthatisfarwiderthanthewholepagearea", which is
        // wider than the row: Chromium shrinks "Left" to 15.0 pt, below its 20.2 pt, and draws both words over their
        // boxes, the long one past the sheet (to x 402.0 on a sheet of 400.08). The engine refuses that, at the first box
        // its content does not fit.
        var result = Paginator.Paginate(LayoutEnvironment.Create(TestFonts.Set), Page(null, Row(40,
            Text(50, MarginBoxPlace.Start, "Left", 12),
            Text(60, MarginBoxPlace.End, "Averyveryverylongunbreakablewordthatisfarwiderthanthewholepagearea", 12))));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(LayoutErrorCodes.MarginBoxOverflow);
        result.Error.Metadata![ErrorMetadataKeys.Node].Should().Be("#50");
        result.Error.Metadata![ErrorMetadataKeys.Property].Should().Be("width");
        result.Error.Metadata![ErrorMetadataKeys.SourceLine].Should().NotBeNull("the HTML front-end points at the margin rule through the box's source");
    }

    [Fact]
    public void Content_taller_than_its_box_is_refused_at_the_box()
    {
        // Measured at 12 pt: "one" to "seven" on seven preserved lines in the 60 pt top box: Chromium starts them at the
        // box's top and lets them run over the body (baselines 12.0 to 97.5). The engine refuses that.
        var preserved = Lato(12) with { WhiteSpaceCollapse = WhiteSpaceCollapse.Preserve };
        var box = MarginBox(20, MarginBoxPlace.Start, [new TextRun(new NodeId(21), "one\ntwo\nthree\nfour\nfive\nsix\nseven", preserved, At(0))], strut: Lato(12));
        var result = Paginator.Paginate(LayoutEnvironment.Create(TestFonts.Set), Page(Row(10, box), null));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(LayoutErrorCodes.MarginBoxOverflow);
        result.Error.Metadata![ErrorMetadataKeys.Node].Should().Be("#20");
        result.Error.Metadata![ErrorMetadataKeys.Property].Should().Be("height");
    }

    private static Error ModelError(StyledDocument document)
    {
        var result = Paginator.Paginate(LayoutEnvironment.Create(TestFonts.Set), document);
        result.IsFailure.Should().BeTrue("the model is not valid");
        result.Error.Code.Should().Be(DocumentErrorCodes.Input.Invalid.MarginBoxes, result.Error.ToString());
        return result.Error;
    }

    [Fact]
    public void A_row_in_the_body_is_a_model_error()
    {
        var error = ModelError(Page(null, null) with { Root = Flow(0, [Row(10, Text(20, MarginBoxPlace.Start, "a"))]) });

        error.Metadata![ErrorMetadataKeys.Node].Should().Be("#10");
    }

    [Fact]
    public void A_row_in_an_area_without_a_fixed_height_is_a_model_error()
    {
        var document = Page(null, null);
        var error = ModelError(document with { Page = document.Page with { Template = new PageTemplate(new PageArea(Row(10, Text(20, MarginBoxPlace.Start, "a")), null), null) } });

        error.Metadata![ErrorMetadataKeys.Node].Should().Be("#10");
    }

    [Fact]
    public void A_row_inside_an_area_s_content_is_a_model_error()
    {
        var error = ModelError(Page(Flow(5, [Row(10, Text(20, MarginBoxPlace.Start, "a"))]), null));

        error.Metadata![ErrorMetadataKeys.Node].Should().Be("#10");
    }

    [Fact]
    public void A_child_of_a_row_without_a_place_is_a_model_error()
    {
        var error = ModelError(Page(Row(10, Paragraph(20, "a")), null));

        error.Metadata![ErrorMetadataKeys.Node].Should().Be("#20");
    }

    [Fact]
    public void A_run_as_a_child_of_a_row_is_a_model_error()
    {
        var row = Row(10) with { Children = [new TextRun(new NodeId(20), "a", Lato(10), At(0))] };

        ModelError(Page(row, null)).Metadata![ErrorMetadataKeys.Node].Should().Be("#20");
    }

    [Fact]
    public void Two_children_at_the_same_place_are_a_model_error()
    {
        var error = ModelError(Page(Row(10, Text(20, MarginBoxPlace.End, "a"), Text(30, MarginBoxPlace.End, "b")), null));

        error.Metadata![ErrorMetadataKeys.Node].Should().Be("#30", "the second box at the place is the one reported");
    }

    [Fact]
    public void A_place_on_a_box_outside_a_row_is_a_model_error()
    {
        var error = ModelError(Page(null, null) with { Root = Flow(0, [Text(20, MarginBoxPlace.Center, "a")]) });

        error.Metadata![ErrorMetadataKeys.Node].Should().Be("#20");
    }

    [Fact]
    public void A_row_without_boxes_is_an_empty_area_of_its_height()
    {
        var page = Laid(Page(Row(10), null));

        page.Header!.Fragment.Size.Should().Be(new Size(RowRight - RowLeft, 60));
        page.Root.Offset.Y.Should().Be(60);
    }

    /// <summary>The page of the HTML front-end: the page area from the margin rounded to a whole pixel (50.25 pt), the
    /// areas from the margin cut to 1/64 px (49.992 pt), so they start 0.258 pt in the page margin.</summary>
    private static StyledDocument InMargin(Box top, double startInMargin)
    {
        var document = Page(null, null);
        var page = document.Page with { Margins = document.Page.Margins with { Left = 50.25.Pt() } };
        return document with { Page = page with { Template = new PageTemplate(new PageArea(top, 60.Pt()) { StartInMargin = startInMargin.Pt() }, null) } };
    }

    [Fact]
    public void An_area_that_starts_in_the_page_margin_is_wider_by_that_length_and_ends_at_the_page_area_s_end()
    {
        var page = Laid(InMargin(Row(10, Text(20, MarginBoxPlace.Start, "Aaaa")), 50.25 - RowLeft));

        page.Header!.Offset.X.Should().BeApproximately(RowLeft, 1e-9, "the row starts in the left margin");
        page.Header.Fragment.Size.Width.Should().BeApproximately(RowRight - RowLeft, 1e-9, "and ends where the page area ends");
        page.Root.Offset.X.Should().Be(50.25, "the body stays at the page area's start");
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(50.5)]
    public void How_far_an_area_starts_in_the_page_margin_is_0_or_more_and_at_most_the_left_margin(double startInMargin)
    {
        var result = Paginator.Paginate(LayoutEnvironment.Create(TestFonts.Set), InMargin(Row(10, Text(20, MarginBoxPlace.Start, "a")), startInMargin));

        result.IsFailure.Should().BeTrue("the model is not valid");
        result.Error.Code.Should().Be(DocumentErrorCodes.Input.Invalid.Length);
        result.Error.Metadata![ErrorMetadataKeys.Property].Should().Be("start-in-margin");
    }
}
