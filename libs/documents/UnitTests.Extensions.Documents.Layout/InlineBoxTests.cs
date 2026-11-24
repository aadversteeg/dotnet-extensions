using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.ErrorPaths;
using static UnitTests.Extensions.Documents.Layout.Models;

namespace UnitTests.Extensions.Documents.Layout;

/// <summary>
/// Inline groups with a box of their own (<see cref="InlineGroup.Box"/>, THE INLINE BOX STYLE) through layout and painting:
/// their edges take room on the line, their background and border are painted where their part opens as a layout artifact
/// (THE RULE FOR PAINTING AN INLINE BOX), and a box over two lines is refused. Lato at 10 pt: ascent 9.87 pt, descent
/// 2.13 pt (<see cref="Models"/>), exact metrics unless a test sets the profile.
/// </summary>
public class InlineBoxTests
{
    private const double Descent = LineBoxHeight - Ascent;

    private static readonly Color Red = new(255, 0, 0);
    private static readonly Color Blue = new(0, 0, 255);

    private static TextRun Run(int id, string text) => new(new NodeId(id), text, Text(), At(0));

    private static InlineBoxStyle Padded(double side, double top = 0, Color? background = null, Border border = default) =>
        new(Length.Zero, Length.Zero, new Edges(top.Pt(), side.Pt(), top.Pt(), side.Pt()), border, background ?? Red);

    private static InlineGroup Boxed(int id, InlineBoxStyle box, params Node[] children) =>
        new(new NodeId(id), null, Text(), [.. children], At(id)) { Box = box };

    private static Box Line(int id, ValueList<Node> children, BoxStyle? style = null) =>
        new(new NodeId(id), SemanticRole.Paragraph, LayoutAlgorithmId.Inline, style ?? BoxStyle.Initial, children, At(0));

    private static PageDocument Laid(StyledDocument document)
    {
        var laid = DocumentLayout.Run(document, TestFonts.Set);
        laid.IsSuccess.Should().BeTrue(laid.IsFailure ? laid.Error.ToString() : string.Empty);
        return laid.Value;
    }

    private static Error Refused(StyledDocument document) => DocumentLayout.Run(document, TestFonts.Set).Error;

    private static GlyphRun RunWith(Page page, string text) =>
        page.Items.OfType<GlyphRun>().Single(run => string.Concat(run.Glyphs.Select(glyph => glyph.Text)) == text);

    private static int IndexOf(Page page, DrawItem item) => page.Items.ToList().IndexOf(item);

    [Fact]
    public void The_edges_take_room_before_the_first_character_and_after_the_last()
    {
        var plain = Laid(Document(Line(1, [Run(2, "aa "), Run(3, "bb"), Run(4, " cc")]))).Pages[0];
        var boxed = Laid(Document(Line(1, [Run(2, "aa "), Boxed(3, Padded(4) with { MarginLeft = 2.Pt(), MarginRight = 1.Pt() }, Run(5, "bb")), Run(4, " cc")]))).Pages[0];

        RunWith(boxed, "bb").Origin.X.Should().BeApproximately(RunWith(plain, "bb").Origin.X + 6, 1e-9, "the left margin and padding come before the text");
        RunWith(boxed, " cc").Origin.X.Should().BeApproximately(RunWith(plain, " cc").Origin.X + 11, 1e-9, "both edges come before what follows");
    }

    [Fact]
    public void The_background_fills_the_border_box_from_the_font_s_content_area_and_the_vertical_padding_without_making_the_line_taller()
    {
        var plain = Laid(Document(Line(1, [Run(2, "aa "), Run(3, "bb")]))).Pages[0];
        var page = Laid(Document(Line(1, [Run(2, "aa "), Boxed(3, Padded(4, top: 3) with { MarginLeft = 2.Pt() }, Run(5, "bb"))]))).Pages[0];

        var bb = RunWith(page, "bb");
        var fill = page.Items.OfType<FillRectangle>().Should().ContainSingle().Which;
        fill.Color.Should().Be(Red);
        fill.Area.Origin.X.Should().BeApproximately(bb.Origin.X - 4, 1e-9, "the margin lies outside the box, the padding inside it");
        fill.Area.Origin.Y.Should().BeApproximately(bb.Origin.Y - Ascent - 3, 1e-9);
        fill.Area.Size.Height.Should().BeApproximately(Ascent + Descent + 6, 1e-9);
        bb.Origin.Y.Should().BeApproximately(RunWith(plain, "bb").Origin.Y, 1e-9, "vertical padding does not move the line");
    }

    [Fact]
    public void A_group_with_a_box_and_no_role_opens_no_structure_element_and_paints_its_box_as_a_layout_artifact_before_its_text()
    {
        var border = Border.All(new BorderSide(1.Pt(), Blue));
        var page = Laid(Document(Line(1, [Run(2, "aa "), Boxed(3, Padded(2, border: border), Run(5, "bb")), Run(4, " cc")]))).Pages[0];

        page.Items.OfType<StructureStart>().Should().NotContain(start => start.Node == new NodeId(3));
        var items = page.Items.ToList();
        var fills = items.OfType<FillRectangle>().ToList();
        fills.Select(fill => fill.Color).Should().Equal(Red, Blue, Blue, Blue, Blue);
        var first = items.IndexOf(fills[0]);
        items[first - 1].Should().BeOfType<ArtifactStart>().Which.Kind.Should().Be(ArtifactKind.Layout);
        first.Should().BeGreaterThan(IndexOf(page, RunWith(page, "aa ")), "the box is painted after the text before it");
        items.IndexOf(fills[^1]).Should().BeLessThan(IndexOf(page, RunWith(page, "bb")), "and before its own text");
    }

    [Fact]
    public void A_group_with_a_box_and_a_language_paints_its_box_inside_its_span_element()
    {
        var group = Boxed(3, Padded(2), Run(5, "bb")) with { Role = SemanticRole.Span, Language = "en" };
        var page = Laid(Document(Line(1, [Run(2, "aa "), group]))).Pages[0];

        var items = page.Items.ToList();
        var start = items.FindIndex(item => item is StructureStart { Node.Value: 3 });
        start.Should().BeGreaterThan(-1);
        items.FindIndex(item => item is FillRectangle).Should().BeGreaterThan(start);
    }

    [Fact]
    public void The_box_of_a_later_line_is_painted_over_the_text_of_the_line_before()
    {
        var page = Laid(Document(Line(1, [Run(2, "first line\u2028"), Boxed(3, Padded(0, top: 6), Run(5, "second"))]))).Pages[0];

        var first = page.Items.OfType<GlyphRun>().First(run => run.Glyphs[0].Text.StartsWith('f'));
        IndexOf(page, page.Items.OfType<FillRectangle>().Single()).Should().BeGreaterThan(IndexOf(page, first));
    }

    [Fact]
    public void An_empty_box_is_painted_with_the_room_of_its_edges()
    {
        var page = Laid(Document(Line(1, [Run(2, "aa "), Boxed(3, Padded(5)), Run(4, "bb")]))).Pages[0];

        var fill = page.Items.OfType<FillRectangle>().Should().ContainSingle().Which;
        fill.Area.Size.Width.Should().BeApproximately(10, 1e-9);
        RunWith(page, "bb").Origin.X.Should().BeApproximately(fill.Area.Origin.X + 10, 1e-9);
    }

    [Fact]
    public void A_box_that_breaks_over_two_lines_is_refused_at_the_group()
    {
        var error = Refused(Document(Line(1, [Run(2, "aa "), Boxed(3, Padded(2), Run(5, string.Join(" ", Enumerable.Repeat("word", 30))))])));

        error.Code.Should().Be(DocumentErrorCodes.Input.Unsupported.InlineBox);
        error.Message.Should().Contain("two lines");
        error.Metadata![ErrorMetadataKeys.SourceLine].Should().Be(3);
    }

    [Fact]
    public void A_box_alone_in_a_paragraph_without_text_is_refused()
    {
        Refused(Document(Line(1, [Boxed(3, Padded(5))]))).Code.Should().Be(DocumentErrorCodes.Input.Unsupported.InlineBox);
    }

    [Theory]
    [InlineData(InlineBoxSnapping.Exact, 0)]
    [InlineData(InlineBoxSnapping.WholePixels, 0.75)]
    public void Under_whole_pixels_the_box_is_painted_on_whole_pixels_and_its_text_drawn_from_its_rounded_top(InlineBoxSnapping snapping, double step)
    {
        // A top padding of 0.4 pt (0.53 px): the box's top, 0.4 pt above the line's top, rounds to one pixel above it.
        var document = Document(Line(1, [Run(2, "aa "), Boxed(3, Padded(0.2, top: 0.4), Run(5, "bb"))])) with
        {
            Profile = LayoutProfile.Default with { FontMetricRounding = FontMetricRounding.WholePixels, InlineBoxSnapping = snapping },
        };
        var page = Laid(document).Pages[0];

        var aa = RunWith(page, "aa ");
        var bb = RunWith(page, "bb");
        var fill = page.Items.OfType<FillRectangle>().Single().Area;
        // 10 pt Lato under whole pixels: ascent 9.87 pt (13.16 px) is 13 px, 9.75 pt.
        if (step == 0)
        {
            bb.Origin.Y.Should().BeApproximately(aa.Origin.Y, 1e-9);
            fill.Origin.Y.Should().BeApproximately(aa.Origin.Y - 9.75 - 0.4, 1e-9);
            return;
        }

        fill.Origin.Y.Should().BeApproximately(aa.Origin.Y - 9.75 - 0.75, 1e-9, "the box's top 0.4 pt above the line's top is painted from one pixel above it");
        bb.Origin.Y.Should().BeApproximately(aa.Origin.Y - 0.35, 1e-9, "the text is drawn from the rounded top: 0.75 - 0.4 pt higher");
        foreach (var edge in new[] { fill.Origin.X, fill.Origin.X + fill.Size.Width, fill.Origin.Y + fill.Size.Height })
        {
            (Math.Abs((edge - 10) / 0.75 - Math.Round((edge - 10) / 0.75)) < 1e-9).Should().BeTrue($"{edge} lies on a whole pixel of the line");
        }
    }

    [Fact]
    public void The_model_check_refuses_what_an_inline_box_cannot_draw()
    {
        var link = SemanticRole.Link(LinkTarget.Uri("https://example.com/a"), "the example");
        Refused(Document(Line(1, [new InlineGroup(new NodeId(3), null, Text(), [Run(5, "x")], At(0))]))).Code.Should().Be(DocumentErrorCodes.Input.Invalid.InlineGroup);
        Refused(Document(Line(1, [Boxed(3, Padded(1) with { MarginLeft = (-1).Pt() }, Run(5, "x"))]))).Code.Should().Be(DocumentErrorCodes.Input.Unsupported.NegativeMargin);
        Refused(Document(Line(1, [Boxed(3, Padded(-1), Run(5, "x"))]))).Code.Should().Be(DocumentErrorCodes.Input.Invalid.Length);
        Refused(Document(Line(1, [Boxed(3, Padded(1), new InlineGroup(new NodeId(4), link, Text(), [Run(5, "x")], At(0)))]))).Code.Should().Be(DocumentErrorCodes.Input.Unsupported.InlineBox);
        Refused(Document(Line(1, [new InlineGroup(new NodeId(3), link, Text(), [Boxed(4, Padded(1), Run(5, "x"))], At(0))]))).Code.Should().Be(DocumentErrorCodes.Input.Unsupported.InlineBox);
        Refused(Document(Line(1, [Boxed(3, Padded(1), Run(5, "x")) with { Role = link }]))).Code.Should().Be(DocumentErrorCodes.Input.Unsupported.InlineBox);
        var image = new Box(new NodeId(6), SemanticRole.Figure("a square"), LayoutAlgorithmId.Image, BoxStyle.Initial,
            [new ImageNode(new NodeId(7), TestImages.Load("baseline-rgb.jpg"), 12.Pt(), 12.Pt(), At(0))], At(0));
        Refused(Document(Line(1, [Boxed(3, Padded(1), Run(5, "x"), image)]))).Code.Should().Be(DocumentErrorCodes.Input.Unsupported.AtomicInline);
    }
}
