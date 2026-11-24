using System.IO;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Fluent;
using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;
using static Ave.Extensions.Documents.Fluent.Elements;

namespace UnitTests.Extensions.Documents.Fluent;

/// <summary>
/// The list-item algorithm (<see cref="ListItemLayout"/>, THE RULE FOR THE LABEL) against the numbers measured in Chromium
/// 159's print (<c>tools/reference/chromium</c>, cases <c>li-symbol-sizes</c>, <c>li-ol-12</c>, <c>li-ol-nested</c>,
/// <c>li-ul-levels</c>, <c>li-first-line</c>, <c>li-page-break</c>, <c>li-marker-clip</c>, <c>li-marker-sheet</c>), built
/// through the code front-end with the layout profile the HTML front-end sets (whole-pixel font metrics, font sizes floored to
/// pixel fractions, text item widths on 1/64 px). Page margin 12 pt and the list's 30 pt indent put the content edge at 42,
/// as in the cases.
/// </summary>
public class ListLayoutTests
{
    /// <summary>The 0.001 pt the measured files are written to.</summary>
    private const double Measured = 0.0015;

    private static FontSet Fonts => new([TestFonts.Regular, TestFonts.Bold, TestFonts.Italic, TestFonts.TheanoDidot]);

    private static Document Chromium(ValueList<Element> body, PageSetup? page = null) =>
        Document(body, page ?? Page(300.Pt(), 300.Pt()).Margin(12.Pt()), title: "Lists", language: "en")
            .FontFamily("Lato").FontSize(10.5.Pt())
            .FontMetricRounding(FontMetricRounding.WholePixels)
            .FontSizeRounding(FontSizeRounding.FlooredToPixelFractions)
            .LengthRounding(LengthRounding.SixtyFourthPixels);

    private static Result<PageDocument, Error> Run(Document document) =>
        DocumentTranslator.ToModel(document).OnSuccessBind(model => DocumentLayout.Run(model, Fonts));

    private static PageDocument Pages(Document document)
    {
        var result = Run(document);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        return result.Value;
    }

    private static IEnumerable<GlyphRun> Runs(Page page) => page.Items.OfType<GlyphRun>();

    private static string TextOf(GlyphRun run) => string.Concat(run.Glyphs.Select(glyph => glyph.Text));

    /// <summary>The geometric box of a bullet: a path's outer contour (a circle's points lie on its box) or a rectangle.</summary>
    private static (double Left, double Top, double Right, double Bottom) BoxOf(DrawItem item)
    {
        if (item is FillRectangle rectangle)
        {
            var area = rectangle.Area;
            return (area.Origin.X, area.Origin.Y, area.Origin.X + area.Size.Width, area.Origin.Y + area.Size.Height);
        }

        var contour = ((FillPath)item).Contours[0];
        Point[] points = [contour.Start, .. contour.Segments.SelectMany(segment => segment is PathCubic cubic ? new[] { cubic.Control1, cubic.Control2, cubic.End } : [segment.End])];
        return (points.Min(point => point.X), points.Min(point => point.Y), points.Max(point => point.X), points.Max(point => point.Y));
    }

    private static DrawItem Bullet(Page page) => page.Items.First(item => item is FillPath or FillRectangle);

    public static TheoryData<string, double, ListBullet, double, double, double> SymbolSizes()
    {
        // (font, size pt, bullet, left, width, top − baseline), case li-symbol-sizes and the analysis's li-font: A the
        // ascent in whole px, o = ⌊2A/3⌋, w = ⌊(o+1)/2⌋, left = E − (o+7) px, top = baseline − A + ⌊3(A−o)/2⌋ px.
        var data = new TheoryData<string, double, ListBullet, double, double, double>();
        foreach (var bullet in new[] { ListBullet.Disc, ListBullet.Circle, ListBullet.Square })
        {
            data.Add("Lato", 7.5, bullet, 32.25, 2.25, -3.0);
            data.Add("Lato", 10.5, bullet, 30.0, 3.75, -5.25);
            data.Add("Lato", 16, bullet, 26.25, 5.25, -8.25);
            data.Add("Theano Didot", 10.5, bullet, 31.5, 3.0, -3.75);
            data.Add("Theano Didot", 16, bullet, 29.25, 3.75, -5.25);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(SymbolSizes))]
    public void A_bullet_is_sized_and_placed_from_its_fonts_ascent(string family, double size, ListBullet bullet, double left, double width, double top)
    {
        var page = Pages(Chromium([BulletedList([ListItem("x")]).Bullet(bullet).FontFamily(family).FontSize(size.Pt())])).Pages[0];
        var baseline = Runs(page).Single(run => TextOf(run) == "x").Origin.Y;
        var item = Bullet(page);
        // A circle is a ring one pixel wide centred on the circle in its box: its outer contour is half a pixel outside.
        var ring = bullet == ListBullet.Circle ? 0.375 : 0;
        var box = BoxOf(item);

        box.Left.Should().BeApproximately(left - ring, Measured);
        (box.Right - box.Left).Should().BeApproximately(width + 2 * ring, Measured);
        (box.Bottom - box.Top).Should().BeApproximately(width + 2 * ring, Measured);
        (box.Top - baseline).Should().BeApproximately(top - ring, Measured);
        item.Should().BeOfType(bullet == ListBullet.Square ? typeof(FillRectangle) : typeof(FillPath));
        if (item is FillPath path)
        {
            path.Rule.Should().Be(bullet == ListBullet.Circle ? PathFillRule.EvenOdd : PathFillRule.NonZero);
            path.Contours.Count.Should().Be(bullet == ListBullet.Circle ? 2 : 1);
        }
    }

    [Fact]
    public void A_number_ends_on_the_content_edge_on_the_sixty_fourth_pixel()
    {
        // Case li-ol-12 (Lato 10.5 pt, E 42): "N. " right-aligned so that its advance, the space included, ends on E, the
        // width ceiled to 1/64 px. One digit starts at 30.738 and "N." ends at 39.306, two digits at 24.645 and 39.302
        // (li-ol-12.words.txt); the space starts where "N." ends.
        var page = Pages(Chromium([NumberedList([.. Enumerable.Range(1, 12).Select(n => ListItem($"item {n}"))])])).Pages[0];
        foreach (var number in Enumerable.Range(1, 12))
        {
            var run = Runs(page).Single(run => TextOf(run) == $"{number}. ");
            (run.Origin.X + run.Glyphs[0].X).Should().BeApproximately(number < 10 ? 30.738 : 24.645, Measured, $"the start of {number}.");
            (run.Origin.X + run.Glyphs[^1].X).Should().BeApproximately(number < 10 ? 39.306 : 39.302, Measured, $"the end of {number}.");
            var first = Runs(page).Single(text => TextOf(text) == $"item {number}");
            run.Origin.Y.Should().Be(first.Origin.Y, "the number sits on its item's first baseline");
        }
    }

    [Fact]
    public void Without_the_sixty_fourth_pixel_a_number_starts_where_Chromium_does_not()
    {
        // The exact width of "1. " (2,144 units at 14 px, 15.008 px) puts its start 0.006 pt right of Chromium's.
        var page = Pages(Chromium([NumberedList([ListItem("x")])]).LengthRounding(LengthRounding.Exact)).Pages[0];
        var run = Runs(page).Single(run => TextOf(run) == "1. ");
        (run.Origin.X - 30.738).Should().BeGreaterThan(0.005);
    }

    [Fact]
    public void Nested_lists_indent_by_level_and_take_the_bullet_of_their_level()
    {
        // Case li-ul-levels: disc, circle, square, square at E 42, 72, 102, 132, each 12 pt (16 px) left of its edge.
        var list = BulletedList([ListItem([Paragraph("a"), BulletedList([ListItem([Paragraph("b"), BulletedList([ListItem([Paragraph("c"),
            BulletedList([ListItem("d")])])])])])])]);
        var page = Pages(Chromium([list])).Pages[0];
        var bullets = page.Items.Where(item => item is FillPath or FillRectangle).ToArray();

        bullets.Select(item => BoxOf(item).Left + (item is FillPath { Contours.Count: 2 } ? 0.375 : 0)).Should().Equal([30.0, 60.0, 90.0, 120.0],
            (a, b) => Math.Abs(a - b) < Measured);
        bullets.Select(item => item switch
        {
            FillPath { Contours.Count: 2 } => "circle",
            FillPath => "disc",
            _ => "square",
        }).Should().Equal("disc", "circle", "square", "square");
    }

    [Fact]
    public void Nested_numbered_lists_restart_at_one_and_a_bulleted_list_inside_takes_the_circle()
    {
        // Case li-ol-nested: "1." at 30.738, 60.738, 90.738; a ul inside an ol item gets the circle (box 60–63.75).
        var list = NumberedList([ListItem([Paragraph("a"), NumberedList([ListItem([Paragraph("b"), NumberedList([ListItem("c")])])])]),
            ListItem([Paragraph("e"), BulletedList([ListItem("f")])])]);
        var page = Pages(Chromium([list])).Pages[0];

        Runs(page).Where(run => TextOf(run) == "1. ").Select(run => run.Origin.X + run.Glyphs[0].X).Should().Equal([30.738, 60.738, 90.738],
            (a, b) => Math.Abs(a - b) < Measured);
        Runs(page).Should().ContainSingle(run => TextOf(run) == "2. ");
        var circle = (FillPath)Bullet(page);
        circle.Contours.Count.Should().Be(2);
        (BoxOf(circle).Left + 0.375).Should().BeApproximately(60.0, Measured);
    }

    [Fact]
    public void A_numbered_list_counts_from_its_start()
    {
        var page = Pages(Chromium([NumberedList([ListItem("a"), ListItem("b"), ListItem("c")]).Start(-1)])).Pages[0];
        Runs(page).Select(TextOf).Where(text => text.EndsWith(". ", StringComparison.Ordinal)).Should().Equal("-1. ", "0. ", "1. ");
    }

    [Fact]
    public void The_label_sits_on_the_first_line_of_the_body_whatever_that_line_holds()
    {
        // Case li-first-line: a first line with a larger span, an inline image, a nested block, a line height of 30 pt. The
        // bullet keeps the item's size (3.75) and sits 5.25 above that line's baseline.
        var figure = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "chromium", "figure.png"));
        ValueList<ListItemElement> items =
        [
            ListItem([Paragraph([Text("big").FontSize(21.Pt()), Text(" small")])]),
            ListItem([Paragraph([InlineImage(Image(figure).Alt("figure").Width(21.Pt())), Text(" image")])]),
            ListItem([Block([Block([Paragraph("nested")])])]),
            ListItem([Paragraph("tall").LineHeight(30.Pt())]),
        ];
        var page = Pages(Chromium([BulletedList(items)], Page(300.Pt(), 400.Pt()).Margin(12.Pt()))).Pages[0];
        var bullets = page.Items.OfType<FillPath>().Select(BoxOf).ToArray();
        string[] firsts = ["big", "image", "nested", "tall"];

        bullets.Should().HaveCount(4);
        foreach (var (box, word) in bullets.Zip(firsts))
        {
            var baseline = Runs(page).First(run => TextOf(run).Contains(word, StringComparison.Ordinal)).Origin.Y;
            (box.Top - baseline).Should().BeApproximately(-5.25, Measured, word);
            (box.Bottom - box.Top).Should().BeApproximately(3.75, Measured, word);
        }
    }

    [Fact]
    public void An_empty_item_gets_one_line_and_a_block_image_first_puts_the_label_at_the_items_top()
    {
        // Case li-first-line: an empty item is one line high (12.75 pt, Lato 14 px), its bullet on that line; with a block image
        // first, the bullet's line is at the item's top: baseline = top + A (12 + 10.5).
        var figure = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "chromium", "figure.png"));
        var empty = Pages(Chromium([BulletedList([ListItem([]), ListItem("after")])])).Pages[0];
        var emptyBullets = empty.Items.OfType<FillPath>().Select(BoxOf).ToArray();
        var after = Runs(empty).Single(run => TextOf(run) == "after").Origin.Y;

        (emptyBullets[1].Top - emptyBullets[0].Top).Should().BeApproximately(12.75, Measured);
        (emptyBullets[1].Top - after).Should().BeApproximately(-5.25, Measured);
        (emptyBullets[0].Top - (12 + 10.5)).Should().BeApproximately(-5.25, Measured);

        var image = Pages(Chromium([BulletedList([ListItem([Image(figure).Alt("figure")])])])).Pages[0];
        (BoxOf(Bullet(image)).Top - (12 + 10.5)).Should().BeApproximately(-5.25, Measured);
    }

    [Fact]
    public void Across_a_page_break_the_label_stays_with_the_first_line()
    {
        // Case li-page-break: "1." only on page 1 with the first line, the continuation on page 2 has none, "2." on page 2.
        var lines = string.Join(" ", Enumerable.Range(1, 40).Select(n => $"word{n}"));
        var pages = Pages(Chromium([NumberedList([ListItem(lines), ListItem("second")])], Page(150.Pt(), 120.Pt()).Margin(12.Pt()))).Pages;

        pages.Count.Should().BeGreaterThan(1);
        var labels = pages.Select(page => Runs(page).Select(TextOf).Where(text => text.EndsWith(". ", StringComparison.Ordinal)).ToArray()).ToArray();
        labels[0].Should().Equal("1. ");
        labels.Skip(1).SelectMany(texts => texts).Should().Equal("2. ");
        var first = Runs(pages[0]).First(run => TextOf(run).StartsWith("word1", StringComparison.Ordinal));
        Runs(pages[0]).Single(run => TextOf(run) == "1. ").Origin.Y.Should().Be(first.Origin.Y);
    }

    [Fact]
    public void A_label_left_of_the_sheet_is_refused_with_its_items_source()
    {
        // Case li-marker-sheet: margin 0, padding 10.5 pt puts the disc at −1.5; Chromium cuts it, the engine refuses it.
        var result = Run(Chromium([BulletedList([ListItem("x")]).PaddingLeft(10.5.Pt())], Page(300.Pt(), 300.Pt())));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(LayoutErrorCodes.MarkerOutsideSheet);
        result.Error.Message.Should().Contain(nameof(ListLayoutTests));
    }

    [Fact]
    public void A_label_wholly_outside_the_clipped_body_is_not_drawn_and_one_across_its_edge_is()
    {
        // Case li-marker-clip: page margin 12, padding 0 puts disc and "1." left of the body's area: Chromium leaves both
        // out. Padding 10.5 puts the disc across the edge (10.5–14.25): drawn, and the clip cuts it.
        Document Clipped(ListElement list) => Chromium([list]).BodyClip(BodyClip.BodyArea);
        var outside = Pages(Clipped(BulletedList([ListItem("x")]).PaddingLeft(0.Pt()))).Pages[0];
        var number = Pages(Clipped(NumberedList([ListItem("x")]).PaddingLeft(0.Pt()))).Pages[0];
        var across = Pages(Clipped(BulletedList([ListItem("x")]).PaddingLeft(10.5.Pt()))).Pages[0];
        var unclipped = Pages(Chromium([BulletedList([ListItem("x")]).PaddingLeft(0.Pt())])).Pages[0];

        outside.Items.OfType<FillPath>().Should().BeEmpty();
        outside.Items.OfType<StructureStart>().Should().NotContain(start => start.Role is SemanticRole.ListLabelRole);
        Runs(number).Select(TextOf).Should().Equal("x");
        BoxOf(Bullet(across)).Left.Should().BeApproximately(10.5, Measured);
        unclipped.Items.OfType<FillPath>().Should().ContainSingle("a page whose body is not clipped draws it");
    }

    [Fact]
    public void A_list_is_the_list_structure_of_the_document_model()
    {
        var model = DocumentTranslator.ToModel(Document([BulletedList([ListItem("a")]), NumberedList([ListItem("b")]), BulletedList([ListItem("c")]).Bullet(ListBullet.None)])
            .FontFamily("Lato")).Value;
        var lines = DocumentModelText.Write(model).Split('\n').Select(line => line.Split(" @")[0]).Skip(2).ToArray();

        lines.Should().StartWith(
        [
            "box #0 flow document",
            "  box #1 flow list numbering=disc padding=0,0,0,30",
            "    box #2 list-item list-item",
            "      box #3 inline list-label actual-text=\"•\" strut=(Lato 12pt w400 #000000 white-space-collapse=preserve text-wrap-mode=nowrap)",
            "        marker #4 disc Lato 12pt w400 #000000",
            "      box #5 flow list-body",
            "        box #6 inline paragraph strut=(Lato 12pt w400 #000000)",
            "          text #7 \"a\" Lato 12pt w400 #000000",
            "  box #8 flow list numbering=decimal padding=0,0,0,30",
            "    box #9 list-item list-item",
            "      box #10 inline list-label strut=(Lato 12pt w400 #000000 white-space-collapse=preserve text-wrap-mode=nowrap)",
            "        text #11 \"1. \" Lato 12pt w400 #000000 white-space-collapse=preserve text-wrap-mode=nowrap",
            "      box #12 flow list-body",
            "        box #13 inline paragraph strut=(Lato 12pt w400 #000000)",
            "          text #14 \"b\" Lato 12pt w400 #000000",
            "  box #15 flow list numbering=none padding=0,0,0,30",
            "    box #16 list-item list-item",
            "      box #17 flow list-body",
        ]);
    }
}
