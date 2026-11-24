using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.ErrorPaths;
using static UnitTests.Extensions.Documents.Layout.Models;

namespace UnitTests.Extensions.Documents.Layout;

/// <summary>
/// Inline groups (<see cref="InlineGroup"/>) through layout and painting: transparent for the lines, a structure marker
/// around each line part, and inside a link's marker one link area per line part and per image in it (THE RULE FOR A
/// LINK'S AREA); none inside an artifact. Lato at 10 pt: ascent 9.87 pt, descent 2.13 pt (<see cref="Models"/>).
/// </summary>
public class LinkAreaTests
{
    private const double Descent = LineBoxHeight - Ascent;

    private static readonly SemanticRole ToExample = SemanticRole.Link(LinkTarget.Uri("https://example.com/a"), "the example");

    private static InlineGroup Link(int id, params Node[] children) => new(new NodeId(id), ToExample, Text(), [.. children], At(0));

    private static TextRun Run(int id, string text, double size = 10) => new(new NodeId(id), text, Text(size), At(0));

    private static Box Line(int id, ValueList<Node> children, BoxStyle? style = null) =>
        new(new NodeId(id), SemanticRole.Paragraph, LayoutAlgorithmId.Inline, style ?? BoxStyle.Initial, children, At(0));

    private static Box ImageBox(int id) =>
        new(new NodeId(id), SemanticRole.Figure("a square"), LayoutAlgorithmId.Image, BoxStyle.Initial,
            [new ImageNode(new NodeId(id + 1), TestImages.Load("baseline-rgb.jpg"), 12.Pt(), 12.Pt(), At(0))], At(0));

    private static PageDocument Laid(StyledDocument document)
    {
        var laid = DocumentLayout.Run(document, TestFonts.Set);
        laid.IsSuccess.Should().BeTrue(laid.IsFailure ? laid.Error.ToString() : string.Empty);
        return laid.Value;
    }

    /// <summary>The link areas of a page, each checked to stand right inside a structure start of its link, in a bracket
    /// that ends on the page.</summary>
    private static List<LinkArea> Areas(Page page)
    {
        var items = page.Items.ToList();
        var areas = new List<LinkArea>();
        for (var index = 0; index < items.Count; index++)
        {
            if (items[index] is LinkArea area)
            {
                var start = items.Take(index).OfType<StructureStart>().Last();
                start.Node.Should().Be(area.Link, "a link area stands inside the structure marker of its link");
                start.Role.Should().BeOfType<SemanticRole.LinkRole>();
                areas.Add(area);
            }
        }

        return areas;
    }

    private static GlyphRun RunWith(Page page, string text) =>
        page.Items.OfType<GlyphRun>().Single(run => string.Concat(run.Glyphs.Select(glyph => glyph.Text)) == text);

    private static double End(GlyphRun run) => run.Origin.X + run.Glyphs[^1].X + run.Font.Metrics.ToPoints(run.Font.AdvanceWidth(run.Glyphs[^1].Glyph), run.FontSize);

    [Fact]
    public void A_link_on_one_line_has_one_area_from_its_text_s_start_to_its_end_and_its_own_font_s_ascent_to_descent()
    {
        var page = Laid(Document(Line(1, [Run(2, "See "), Link(3, Run(4, "here")), Run(5, " now")]))).Pages[0];

        var area = Areas(page).Should().ContainSingle().Which;
        var here = RunWith(page, "here");
        area.Area.Origin.X.Should().BeApproximately(here.Origin.X, 1e-9);
        (area.Area.Origin.X + area.Area.Size.Width).Should().BeApproximately(End(here), 1e-9);
        area.Area.Origin.Y.Should().BeApproximately(here.Origin.Y - Ascent, 1e-9);
        area.Area.Size.Height.Should().BeApproximately(Ascent + Descent, 1e-9);
        page.Items.OfType<StructureStart>().Count(start => start.Node == new NodeId(3)).Should().Be(1);
    }

    [Fact]
    public void A_bigger_run_inside_a_link_does_not_make_its_area_taller()
    {
        // As Chromium prints it: the area is the link's own font's content area, whatever the runs inside it.
        var page = Laid(Document(Line(1, [Link(3, Run(4, "small "), Run(5, "big", size: 14))]))).Pages[0];

        var area = Areas(page).Should().ContainSingle().Which;
        area.Area.Size.Height.Should().BeApproximately(LineBoxHeight, 1e-9);
        area.Area.Origin.Y.Should().BeApproximately(RunWith(page, "big").Origin.Y - Ascent, 1e-9, "the area sits on the line's baseline");
    }

    [Fact]
    public void A_link_over_two_lines_has_one_area_and_one_bracket_per_line()
    {
        var page = Laid(Document(Line(1, [Run(2, "Before "), Link(3, Run(4, "one\u2028two")), Run(5, " after")]))).Pages[0];

        var areas = Areas(page);
        areas.Should().HaveCount(2);
        areas[1].Area.Origin.Y.Should().BeApproximately(areas[0].Area.Origin.Y + LineBoxHeight, 1e-9);
        areas[0].Area.Origin.X.Should().BeApproximately(RunWith(page, "one").Origin.X, 1e-9);
        areas[1].Area.Origin.X.Should().BeApproximately(10, 1e-9, "the second part starts the line");
        page.Items.OfType<StructureStart>().Count(start => start.Node == new NodeId(3)).Should().Be(2);
    }

    [Fact]
    public void A_link_over_a_page_break_has_its_areas_on_each_page()
    {
        var filler = Paragraph(1, "a\u2028b\u2028c\u2028d\u2028e");
        var broken = Line(2, [Link(3, Run(4, "one\u2028two\u2028three"))], BoxStyle.Initial with { Orphans = 1, Widows = 1 });

        var pages = Laid(Document(filler, broken)).Pages;

        pages.Should().HaveCount(2);
        Areas(pages[0]).Should().ContainSingle("one line of the link fits on the first page");
        Areas(pages[1]).Should().HaveCount(2);
        pages.SelectMany(page => page.Items.OfType<LinkArea>()).Select(area => area.Link).Distinct().Should().Equal(new NodeId(3));
    }

    [Fact]
    public void A_link_around_an_image_has_an_area_for_its_line_part_and_one_for_the_image()
    {
        var page = Laid(Document(Line(1, [Link(3, Run(4, "x "), ImageBox(10))]))).Pages[0];

        var areas = Areas(page);
        areas.Should().HaveCount(2);
        areas[1].Area.Should().Be(page.Items.OfType<DrawImage>().Single().Area, "the image's area is its border box");
        areas[0].Area.Size.Height.Should().BeApproximately(LineBoxHeight, 1e-9);
        var figure = page.Items.ToList().FindIndex(item => item is StructureStart { Role: SemanticRole.FigureRole });
        page.Items.ToList().FindIndex(item => item is StructureStart { Role: SemanticRole.LinkRole }).Should().BeLessThan(figure, "the figure is content of the link");
    }

    [Fact]
    public void A_link_in_a_page_header_has_no_area_and_no_marker_and_its_text_is_drawn()
    {
        var header = new PageArea(Flow(20, [Line(21, [Link(22, Run(23, "home"))])]), null);
        var document = Document(Paragraph(1, "Body")) with
        {
            Page = new PageStyle(200.Pt(), 100.Pt(), new Edges(30.Pt(), 10.Pt(), 10.Pt(), 10.Pt())) { Template = new PageTemplate(header, null) },
        };

        var page = Laid(document).Pages[0];

        page.Items.OfType<LinkArea>().Should().BeEmpty();
        page.Items.OfType<StructureStart>().Should().NotContain(start => start.Role is SemanticRole.LinkRole);
        RunWith(page, "home").Should().NotBeNull();
    }

    [Fact]
    public void A_span_group_is_a_structure_marker_without_an_area_and_changes_no_line()
    {
        var span = new InlineGroup(new NodeId(3), SemanticRole.Span, Text(), [Run(4, "inside")], At(0));
        var grouped = Laid(Document(Line(1, [Run(2, "out "), span, Run(5, " out")]))).Pages[0];
        var plain = Laid(Document(Line(1, [Run(2, "out "), Run(4, "inside"), Run(5, " out")]))).Pages[0];

        grouped.Items.OfType<LinkArea>().Should().BeEmpty();
        grouped.Items.OfType<StructureStart>().Should().Contain(start => start.Node == new NodeId(3) && start.Role == SemanticRole.Span);
        grouped.Items.OfType<GlyphRun>().Should().Equal(plain.Items.OfType<GlyphRun>(), "a group is transparent for layout");
    }

    [Theory]
    [InlineData("javascript:alert(1)", "Documents.Input.Invalid.Link")]
    [InlineData("page.html", "Documents.Input.Invalid.Link")]
    public void A_model_with_a_link_the_rule_refuses_is_not_laid_out(string uri, string code)
    {
        var link = new InlineGroup(new NodeId(3), SemanticRole.Link(LinkTarget.Uri(uri), "x"), Text(), [Run(4, "x")], At(0));

        var result = DocumentLayout.Run(Document(Line(1, [link])), TestFonts.Set);

        result.Error.Code.ToString().Should().Be(code);
    }

    [Fact]
    public void A_link_inside_a_link_a_group_outside_a_line_a_blank_description_and_a_link_role_on_a_box_are_refused()
    {
        Error Refused(StyledDocument document) => DocumentLayout.Run(document, TestFonts.Set).Error;
        var blank = new InlineGroup(new NodeId(3), SemanticRole.Link(LinkTarget.Uri("https://example.com"), " "), Text(), [Run(4, "x")], At(0));
        var boxWithLinkRole = new Box(new NodeId(1), ToExample, LayoutAlgorithmId.Inline, BoxStyle.Initial, [Run(2, "x")], At(0));

        Refused(Document(Line(1, [Link(3, Link(4, Run(5, "x")))]))).Code.Should().Be(DocumentErrorCodes.Input.Invalid.InlineGroup);
        Refused(Document(Link(3, Run(4, "x")))).Code.Should().Be(DocumentErrorCodes.Input.Invalid.InlineGroup);
        Refused(Document(Line(1, [blank]))).Code.Should().Be(DocumentErrorCodes.Input.Invalid.Link);
        Refused(Document(boxWithLinkRole)).Code.Should().Be(DocumentErrorCodes.Input.Invalid.InlineGroup);
        Refused(Document(Line(1, [new InlineGroup(new NodeId(3), SemanticRole.Span, Text(), [Run(4, "x")], At(0)) { Language = "not a tag" }]))).Code
            .Should().Be(DocumentErrorCodes.Input.Invalid.Language);
    }

    private static InlineBoxStyle Padded(double side, double top) =>
        new(Length.Zero, Length.Zero, new Edges(top.Pt(), side.Pt(), top.Pt(), side.Pt()), default, new Color(255, 0, 0));

    /// <summary>A link with a box of its own has its border box as its area, on each line (THE RULE FOR A LINK'S AREA): as
    /// Chromium 159 writes a link styled as a button, at its background (cases <c>ib-link-button</c>,
    /// <c>ib-link-button-break</c>, <c>ib-link-cell</c>).</summary>
    [Fact]
    public void A_link_with_a_box_of_its_own_has_its_border_box_as_its_area()
    {
        var link = Link(3, Run(4, "here")) with { Box = Padded(3, 2) };
        var page = Laid(Document(Line(1, [Run(2, "See "), link, Run(5, " now")]))).Pages[0];

        Areas(page).Should().ContainSingle().Which.Area.Should().Be(page.Items.OfType<FillRectangle>().Single().Area);
    }

    /// <summary>A link around a box (a styled span inside it) keeps one area per line part, from its first to its last piece
    /// as before, and as tall as the box's border box where that reaches above or below the link's font: the whole link is
    /// one thing to click (case <c>ib-link-span-inside</c>, where Chromium writes three rectangles).</summary>
    [Fact]
    public void A_box_inside_a_link_makes_the_link_s_area_as_tall_as_the_box_and_does_not_split_it()
    {
        var page = Laid(Document(Line(1, [Run(2, "See "), Link(3, Run(4, "aa "), new InlineGroup(new NodeId(6), null, Text(), [Run(7, "bb")], At(0)) { Box = Padded(2, 3) }, Run(5, " cc"))]))).Pages[0];

        var area = Areas(page).Should().ContainSingle().Which.Area;
        var fill = page.Items.OfType<FillRectangle>().Single().Area;
        area.Origin.X.Should().BeApproximately(RunWith(page, "aa ").Origin.X, 1e-9);
        (area.Origin.X + area.Size.Width).Should().BeApproximately(End(RunWith(page, " cc")), 1e-9);
        area.Origin.Y.Should().BeApproximately(fill.Origin.Y, 1e-9);
        (area.Origin.Y + area.Size.Height).Should().BeApproximately(fill.Origin.Y + fill.Size.Height, 1e-9);
    }

    /// <summary>Under the body's clip a link's area is the part the clip shows: a box that reaches above the page area at
    /// the top of the page gives an area from the page area's top, as Chromium 159 writes it (case <c>ib-link-button</c>);
    /// without the clip the area is the whole border box.</summary>
    [Theory]
    [InlineData(BodyClip.BodyArea)]
    [InlineData(BodyClip.None)]
    public void Under_the_body_clip_a_link_s_area_is_what_the_clip_shows(BodyClip clip)
    {
        var link = Link(3, Run(4, "here")) with { Box = Padded(3, 4) };
        var document = Document(Line(1, [Run(2, "See "), link])) with { Profile = LayoutProfile.Default with { BodyClip = clip } };
        var page = Laid(document).Pages[0];

        var fill = page.Items.OfType<FillRectangle>().Single().Area;
        var area = Areas(page).Should().ContainSingle().Which.Area;
        fill.Origin.Y.Should().BeLessThan(10, "the box reaches above the page area, which starts at the 10 pt margin");
        area.Origin.Y.Should().BeApproximately(clip == BodyClip.BodyArea ? 10 : fill.Origin.Y, 1e-9);
        (area.Origin.Y + area.Size.Height).Should().BeApproximately(fill.Origin.Y + fill.Size.Height, 1e-9);
        (area.Origin.X, area.Size.Width).Should().Be((fill.Origin.X, fill.Size.Width));
    }
}
