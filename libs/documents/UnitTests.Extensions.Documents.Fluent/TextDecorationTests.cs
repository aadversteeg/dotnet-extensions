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
/// Underline, line-through and overline as Chromium draws them (CSS Text Decoration 3 §2): cases <c>dl-*</c>, Lato 12 pt on
/// 18 pt lines unless a row says otherwise, the same documents written with the code front-end, under
/// <see cref="FontMetricRounding.WholePixels"/>.
/// </summary>
/// <remarks>
/// THE COMPARISON: the words as <see cref="InlineImageTests"/> compares them (a decoration moves no word and no baseline); and
/// the filled rectangles, Chromium's and the engine's each joined where two touch or overlap on the same rows (Chromium makes one per
/// text item, the engine one per piece of a run, and a hanging space is an item of its own in Chromium). The top and the
/// height of each are equal (both whole pixels); each end lies within one layout unit for each of Chromium's rectangles
/// on its rows that ends at or before it, plus one (the items before it, each ceiled to a layout unit, README.md "The comparison
/// rule"), and two more on right-aligned or centred lines (<see cref="WhiteSpaceTests"/>).
/// </remarks>
public class TextDecorationTests
{
    private static readonly Color Red = new(0xCC, 0x00, 0x00);
    private static readonly Color Blue = new(0x00, 0x00, 0xCC);

    private static byte[] Figure => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "chromium", "figure.png"));

    private static Span Fig => InlineImage(Image(Figure).Alt("figure").Width(8.4.Pt()));

    private static readonly FontSet Fonts = new([TestFonts.Regular, TestFonts.Bold, TestFonts.Italic, TestFonts.TheanoDidot]);

    private static Result<PageDocument, Error> Laid(double pageHeight, Func<StyledDocument, StyledDocument> change, params Element[] body) =>
        DocumentTranslator.ToModel(Document([.. body], Page(300.Pt(), pageHeight.Pt()).Margin(12.Pt()), title: "Title", language: "en")
                .FontFamily("Lato").FontSize(12.Pt()).LineHeight(18.Pt()).FontMetricRounding(FontMetricRounding.WholePixels))
            .OnSuccessBind(model => DocumentLayout.Run(change(model), Fonts));

    private static PageDocument Painted(double pageHeight, params Element[] body) => Painted(pageHeight, model => model, body);

    private static PageDocument Painted(double pageHeight, Func<StyledDocument, StyledDocument> change, params Element[] body)
    {
        var laid = Laid(pageHeight, change, body);
        laid.IsSuccess.Should().BeTrue(laid.IsFailure ? laid.Error.ToString() : string.Empty);
        return laid.Value;
    }

    private static void ShouldMatch(PageDocument engine, string caseName, bool aligned = false)
    {
        var (mine, theirs) = (PrintedPages.Engine(engine), PrintedPages.Chromium(caseName));
        var breaches = InlineImageTests.Breaches(mine, theirs, halfPixelBaselines: false, extraUnits: aligned ? 2 : 0);
        breaches.AddRange(RectangleBreaches(mine.Pages[0].Rectangles, theirs.Pages[0].Rectangles, aligned ? 2 : 0));
        breaches.Should().BeEmpty(string.Join("\n", breaches));
    }

    /// <summary>The rectangles joined where two touch or overlap on the same rows, ordered by their top and then their start.</summary>
    private static List<PrintedBox> Joined(IEnumerable<PrintedBox> rectangles)
    {
        var joined = new List<PrintedBox>();
        foreach (var box in rectangles.OrderBy(box => Math.Round(box.Y0, 2)).ThenBy(box => Math.Round(box.Y1, 2)).ThenBy(box => box.X0))
        {
            if (joined.Count > 0 && joined[^1] is var last && Math.Abs(last.Y0 - box.Y0) < 0.01 && Math.Abs(last.Y1 - box.Y1) < 0.01 && box.X0 <= last.X1 + 0.05)
            {
                joined[^1] = last with { X1 = Math.Max(last.X1, box.X1) };
                continue;
            }

            joined.Add(box);
        }

        return joined;
    }

    private static IEnumerable<string> RectangleBreaches(IReadOnlyList<PrintedBox> engine, IReadOnlyList<PrintedBox> chromium, double extraUnits)
    {
        var (mine, theirs) = (Joined(engine), Joined(chromium));
        if (mine.Count != theirs.Count)
        {
            yield return $"{mine.Count} rectangles, Chromium {theirs.Count}: engine [{string.Join("; ", mine)}], Chromium [{string.Join("; ", theirs)}]";
            yield break;
        }

        for (var index = 0; index < mine.Count; index++)
        {
            var (box, measured) = (mine[index], theirs[index]);
            int Before(double x) => chromium.Count(other => Math.Abs(other.Y0 - measured.Y0) < 0.01 && other.X1 <= x + 0.05);
            var x0 = ChromiumLayout.DifferenceInLayoutUnits(box.X0, measured.X0);
            var x1 = ChromiumLayout.DifferenceInLayoutUnits(box.X1, measured.X1);
            if (Math.Abs(box.Y0 - measured.Y0) > 0.001 || Math.Abs(box.Y1 - measured.Y1) > 0.001
                || x0 > Before(measured.X0) + 1 + extraUnits || x1 > Before(measured.X1) + 1 + extraUnits)
            {
                yield return $"rectangle {index}: engine {box.X0:0.000},{box.Y0:0.000}–{box.X1:0.000},{box.Y1:0.000}, Chromium {measured.X0:0.000},{measured.Y0:0.000}–{measured.X1:0.000},{measured.Y1:0.000} ({x0:0.0}/{x1:0.0} LU)";
            }
        }
    }

    private static ParagraphElement Decorated(ParagraphElement paragraph, TextDecorationLine line, Color? color = null) => line switch
    {
        TextDecorationLine.Underline => paragraph.Underline(color),
        TextDecorationLine.LineThrough => paragraph.LineThrough(color),
        _ => paragraph.Overline(color),
    };

    private static Span Decorated(Span span, TextDecorationLine line) => line switch
    {
        TextDecorationLine.Underline => span.Underline(),
        TextDecorationLine.LineThrough => span.LineThrough(),
        _ => span.Overline(),
    };

    private static readonly TextDecorationLine[] Lines = [TextDecorationLine.Underline, TextDecorationLine.LineThrough, TextDecorationLine.Overline];

    /// <summary>The marker paragraph some cases put after a paragraph.</summary>
    private static ParagraphElement X => Paragraph("x");

    /// <summary>A paragraph whose content box is <paramref name="width"/> wide on the 276 pt page area.</summary>
    private static ParagraphElement Narrow(double width, ValueList<Span> spans, TextAlign align = TextAlign.Left) =>
        Paragraph(spans).PaddingRight((276 - width).Pt()).TextAlign(align);

    public static TheoryData<string, string> FontCases => new()
    {
        { "dl-lato", "regular" }, { "dl-lato-bold", "bold" }, { "dl-lato-italic", "italic" }, { "dl-theano", "theano" },
    };

    [Theory]
    [MemberData(nameof(FontCases))]
    public void Each_line_lies_where_Chromium_draws_it_in_each_font_at_two_sizes(string caseName, string face)
    {
        ParagraphElement Face(ParagraphElement paragraph) => face switch
        {
            "bold" => paragraph.Bold(),
            "italic" => paragraph.Italic(),
            "theano" => paragraph.FontFamily("Theano Didot"),
            _ => paragraph,
        };

        var body = new[] { (12, 24), (24, 48) }.SelectMany(size => Lines.Select(line =>
            (Element)Decorated(Face(Paragraph("Hxg ab")).FontSize(size.Item1.Pt()).LineHeight(size.Item2.Pt()), line)));

        ShouldMatch(Painted(400, [.. body]), caseName);
    }

    [Fact]
    public void Several_lines_on_one_text_and_the_colours_are_Chromiums()
    {
        var page = Painted(300,
            Paragraph("Hxg ab").Underline().LineThrough(),
            Paragraph("Hxg ab").FontSize(24.Pt()).LineHeight(48.Pt()).Underline().Overline().LineThrough(),
            Paragraph("Hxg ab").Underline(Red),
            Paragraph("Hxg ab").Color(Blue).Underline());

        ShouldMatch(page, "dl-lines");
        page.Pages[0].Items.OfType<FillRectangle>().Select(fill => fill.Color).Should().Equal(
            [Color.Black, Color.Black, Color.Black, Color.Black, Color.Black, Red, Blue], "the lines' colours in Chromium's drawing order (dl-lines)");
    }

    [Fact]
    public void The_decorating_box_gives_the_thickness_and_the_underline_one_straight_line_like_Chromium()
    {
        // Rows 1–12 decorate with a span around the whole text in Chromium; a paragraph of the same font draws the same lines.
        var body = Lines.SelectMany(line => new[]
        {
            Decorated(Paragraph([Text("ab "), Text("cd").FontSize(24.Pt()), Text(" ef")]), line),
            Decorated(Paragraph([Text("ab "), Text("cd").FontSize(8.Pt()), Text(" ef")]), line),
            Decorated(Paragraph([Text("ab "), Text("cd").Bold(), Text(" ef")]), line),
            Decorated(Paragraph([Text("ab "), Text("cd").Italic(), Text(" ef")]), line),
        }).Concat(
        [
            Paragraph([Text("ab "), Text("cd").FontSize(24.Pt()), Text(" ef")]).Underline(),
            X,
            X,
            Paragraph([Text("ab "), Text("cd").FontSize(24.Pt())]).LineThrough(),
            Paragraph([Text("ab "), Text("cd").FontSize(24.Pt())]).Overline(),
        ]);

        // Rows 14 and 15 (a 24 pt decorating box around 8 pt text in a 12 pt paragraph) are written in the model: the code
        // front-end has no box around runs, and a 24 pt paragraph would make another strut.
        ShouldMatch(Painted(900, BigAroundSmall(13, 14), [.. body]), "dl-whose");
    }

    /// <summary>The runs of the given paragraphs (by index in the body) set as Chromium's rows "a 24 pt decorating box around
    /// "ab ", 8 pt "cd" and " ef"" and "around "ab " and 8 pt "cd"": the text sizes and one underline in Lato 24 pt on all.</summary>
    private static Func<StyledDocument, StyledDocument> BigAroundSmall(params int[] paragraphs) => document =>
    {
        var underline = new TextDecoration(TextDecorationLine.Underline, Color.Black, "Lato", 24.Pt(), FontWeight.Regular);
        var texts = new[] { new[] { "ab ", "cd", " ef" }, ["ab ", "cd"] };
        var body = document.Root.Children.ToArray();
        for (var row = 0; row < paragraphs.Length; row++)
        {
            var box = (Box)body[paragraphs[row]];
            var sizes = new[] { 24.0, 8, 24 };
            body[paragraphs[row]] = box with
            {
                Children = [.. texts[row].Select((text, index) => (Node)new TextRun(new NodeId(box.Id.Value + 100 + index), text,
                    ((TextRun)box.Children[0]).Style with { FontSize = sizes[index].Pt(), Decorations = [underline] }, box.Source))],
            };
        }

        return document with { Root = document.Root with { Children = [.. body] } };
    };

    [Fact]
    public void Nested_decorations_are_drawn_outermost_first_and_an_inner_none_does_not_stop_the_outer_line_like_Chromium()
    {
        var page = Painted(300, NestedSizes,
            Paragraph([Text("ab "), Text("cd").Underline(Red), Text(" ef")]).Underline(),
            Paragraph([Text("ab "), Text("cd"), Text(" ef")]).Underline(),
            Paragraph([Text("ab "), Text("cd").LineThrough(), Text(" ef")]).Underline(),
            X,
            // The case's class="u s" on "cd": both set text-decoration, so the later one, line-through, wins.
            Paragraph([Text("ab "), Text("cd").LineThrough(), Text(" ef")]).Overline());

        ShouldMatch(page, "dl-nest");
        page.Pages[0].Items.OfType<FillRectangle>().Take(4).Select(fill => fill.Color).Should().Equal([Color.Black, Color.Black, Red, Color.Black],
            "on \"cd\" the outer black underline comes before the inner red one");
    }

    /// <summary>Row 4 of dl-nest in the model: a 24 pt underlined box around "ab ", an 8 pt underlined "cd" and " ef".</summary>
    private static StyledDocument NestedSizes(StyledDocument document)
    {
        var outer = new TextDecoration(TextDecorationLine.Underline, Color.Black, "Lato", 24.Pt(), FontWeight.Regular);
        var inner = outer with { FontSize = 8.Pt() };
        var body = document.Root.Children.ToArray();
        var box = (Box)body[3];
        var style = ((TextRun)box.Children[0]).Style;
        body[3] = box with
        {
            Children =
            [
                new TextRun(new NodeId(box.Id.Value + 100), "ab ", style with { FontSize = 24.Pt(), Decorations = [outer] }, box.Source),
                new TextRun(new NodeId(box.Id.Value + 101), "cd", style with { FontSize = 8.Pt(), Decorations = [outer, inner] }, box.Source),
                new TextRun(new NodeId(box.Id.Value + 102), " ef", style with { FontSize = 24.Pt(), Decorations = [outer] }, box.Source),
            ],
        };
        return document with { Root = document.Root with { Children = [.. body] } };
    }

    [Fact]
    public void Neighbouring_runs_of_one_decorating_box_draw_touching_pieces_like_Chromium() =>
        ShouldMatch(Painted(300,
            Paragraph([Text("ab"), Text("cd").Bold(), Text("ef")]).Underline(),
            Paragraph([Text("ab"), Text("cd").Color(Red), Text("ef")]).Underline(),
            Paragraph([Text("ab"), Text("cd").FontSize(24.Pt()), Text("ef")]).Underline(),
            Paragraph([Text("ab"), Text("cd").Bold(), Text("ef")]).LineThrough()), "dl-join");

    [Fact]
    public void A_line_ends_where_the_text_ends_crosses_justified_spaces_and_hanging_ones_like_Chromium()
    {
        Element[] Aligned(TextAlign align) => [Narrow(40, [Text("aa bb cc dd ee ff")], align).Underline()];
        var preserve = WhiteSpaceCollapse.Preserve;
        var page = Painted(700,
        [
            Paragraph("aa bb").Underline(),
            .. new[] { TextAlign.Left, TextAlign.Right, TextAlign.Center, TextAlign.Justify }.SelectMany(Aligned),
            Paragraph([Text("aa ").Underline(), Text("bb")]),
            Paragraph([Text("aa"), Text(" bb").Underline()]),
            Narrow(40, [Text("aa "), Text("bb cc").Underline(), Text(" dd ee ff")], TextAlign.Justify),
            Narrow(40, [Text("aa bb          cc dd")]).WhiteSpaceCollapse(preserve).Underline(),
            Narrow(40, [Text("aa bb          cc dd")], TextAlign.Right).WhiteSpaceCollapse(preserve).Underline(),
            Paragraph("aa    ").WhiteSpaceCollapse(preserve).Underline(),
            Paragraph([Text("aa    ").Underline(), Text("bb")]).WhiteSpaceCollapse(preserve),
            Narrow(40, [Text("aa bb cc dd ee ff")], TextAlign.Justify).LineThrough(),
            Narrow(40, [Text("aa bb    ")]).WhiteSpaceCollapse(preserve).Underline(),
            Narrow(40, [Text("aa bb").Underline(), Text(" cc")]),
            Narrow(40, [Text("aa bb ").Underline(), Text("cc dd")]),
            Narrow(40, [Text("aa bb          cc dd")], TextAlign.Justify).WhiteSpaceCollapse(preserve).Overline(),
        ]);

        ShouldMatch(page, "dl-space", aligned: true);
    }

    [Fact]
    public void An_image_in_a_decorated_line_gets_no_line_like_Chromium() =>
        ShouldMatch(Painted(300,
            Paragraph([Text("ab "), Fig, Text(" cd")]).Underline(),
            Paragraph([Text("ab"), Fig, Text("cd")]).Underline(),
            Paragraph([Text("ab "), Fig, Text(" cd")]).LineThrough(),
            Paragraph([Fig]).Underline()), "dl-image");

    [Fact]
    public void Decorations_do_not_change_the_height_of_a_line_like_Chromium() =>
        ShouldMatch(Painted(300,
            Paragraph("Hxg").FontSize(24.Pt()).LineHeight(6.Pt()), X,
            Paragraph("Hxg").FontSize(24.Pt()).LineHeight(6.Pt()).Underline().Overline().LineThrough(), X,
            Paragraph("Hxg").LineHeightNormal(), X,
            Paragraph("Hxg").LineHeightNormal().Underline().Overline().LineThrough(), X), "dl-height");

    [Fact]
    public void Underlines_and_overlines_are_painted_before_the_glyphs_and_line_throughs_after_them_as_layout_artifacts()
    {
        var items = Painted(300, Paragraph("Hxg ab").FontSize(24.Pt()).LineHeight(48.Pt()).Underline().Overline().LineThrough()).Pages[0].Items;
        var order = items.Select(item => item switch
        {
            FillRectangle => "line",
            GlyphRun => "text",
            ArtifactStart { Kind: ArtifactKind.Layout } => "artifact",
            ArtifactEnd => "end",
            _ => null,
        }).OfType<string>();

        order.Should().Equal("artifact", "line", "end", "artifact", "line", "end", "text", "artifact", "line", "end");
    }

    [Fact]
    public void An_underline_is_one_whole_line_where_Chromium_leaves_gaps_for_descenders_by_default()
    {
        // LIMIT (docs/architecture.md): text-decoration-skip-ink: auto, Chromium's initial value, clips the underline of
        // "gjpqy Hxg" at five glyphs (case dl-skip, an even-odd clip around one filled rectangle); the engine draws it whole.
        var lines = Painted(300, Paragraph("gjpqy Hxg").FontSize(24.Pt()).LineHeight(48.Pt()).Underline()).Pages[0].Items;

        lines.OfType<FillRectangle>().Should().ContainSingle().Which.Area.Size.Width.Should().BeApproximately(105.738, 0.05);
        lines.OfType<ClipStart>().Should().BeEmpty();
    }

    private static Result<StyledDocument, Error> Model(params Element[] body) =>
        DocumentTranslator.ToModel(Document([.. body], Page(300.Pt(), 300.Pt()).Margin(12.Pt()), title: "Title", language: "en").FontFamily("Lato").FontSize(12.Pt()));

    private static ValueList<TextDecoration> DecorationsOf(StyledDocument model, int run) =>
        ((TextRun)((Box)model.Root.Children[0]).Children[run]).Style.Decorations;

    [Fact]
    public void A_paragraph_s_lines_come_first_in_its_font_then_a_span_s_own_in_the_span_s_font_each_box_in_painting_order()
    {
        var model = Model(Paragraph([Text("ab"), Text("cd").FontSize(24.Pt()).Bold().LineThrough().Underline(Red)]).Overline().Underline()).Value;

        DecorationsOf(model, 0).Should().Equal(
            new TextDecoration(TextDecorationLine.Underline, Color.Black, "Lato", 12.Pt(), FontWeight.Regular),
            new TextDecoration(TextDecorationLine.Overline, Color.Black, "Lato", 12.Pt(), FontWeight.Regular));
        DecorationsOf(model, 1).Should().Equal(
            new TextDecoration(TextDecorationLine.Underline, Color.Black, "Lato", 12.Pt(), FontWeight.Regular),
            new TextDecoration(TextDecorationLine.Overline, Color.Black, "Lato", 12.Pt(), FontWeight.Regular),
            new TextDecoration(TextDecorationLine.Underline, Red, "Lato", 24.Pt(), FontWeight.Bold),
            new TextDecoration(TextDecorationLine.LineThrough, Color.Black, "Lato", 24.Pt(), FontWeight.Bold));
    }

    [Fact]
    public void A_line_set_again_takes_the_new_colour_and_is_drawn_once()
    {
        var model = Model(Paragraph("ab").Underline().Underline(Red)).Value;

        DecorationsOf(model, 0).Should().ContainSingle().Which.Color.Should().Be(Red);
    }

    [Fact]
    public void A_line_set_on_a_block_decorates_the_text_of_every_paragraph_inside_it()
    {
        var model = Model(Block([Paragraph("ab")]).Underline()).Value;

        ((TextRun)((Box)((Box)model.Root.Children[0]).Children[0]).Children[0]).Style.Decorations.Should().ContainSingle()
            .Which.Line.Should().Be(TextDecorationLine.Underline);
    }

    [Fact]
    public void A_line_outside_its_enum_is_refused_by_the_front_end()
    {
        var plain = Paragraph("ab");
        var paragraph = plain with { Format = plain.Format with { Text = plain.Format.Text with { Decorations = [new TextDecorationFormat((TextDecorationLine)9)] } } };

        var error = Model(paragraph).Error;

        error.Code.Should().Be(DocumentErrorCodes.Input.Invalid.TextDecoration);
    }

    [Fact]
    public void A_line_set_in_a_style_decorates_as_one_set_on_the_element_and_the_element_s_own_lines_win()
    {
        var underlined = Style.Empty.Underline(Red);

        DecorationsOf(Model(Paragraph("ab").With(underlined)).Value, 0).Should().ContainSingle().Which.Color.Should().Be(Red);
        DecorationsOf(Model(Paragraph("ab").With(underlined).Overline()).Value, 0).Should().ContainSingle().Which.Line.Should().Be(TextDecorationLine.Overline);
    }

    [Fact]
    public void A_line_that_ends_in_a_decorated_run_at_a_space_is_shaped_again_like_Chromium()
    {
        // Lato kerns "V" with the space after it: "xx AV" is 29.790 pt kept with the paragraph's glyphs, 30.222 pt shaped again.
        // Chromium (case dl-end) shapes the end again when the space at the line end lies in the decorated element (rows 1, 2,
        // 5: the underline ends at 42.223; at 30 pt "AV" no longer fits), and keeps the kerning when the space lies outside it
        // (rows 3, 7: 41.801, and "xx AV" fits in 30 pt as in the control row 6).
        ParagraphElement W(double width, params Span[] spans) => Narrow(width, [.. spans]);
        ShouldMatch(Painted(300,
            W(40, Text("xx AV To").Underline()),
            W(40, Text("xx AV ").Underline(), Text("To")),
            W(40, Text("xx AV").Underline(), Text(" To")),
            W(40, Text("xx AV To")),
            W(30, Text("xx AV To").Underline()),
            W(30, Text("xx AV To")),
            W(30, Text("xx AV").Underline(), Text(" To"))), "dl-end");
    }
}
