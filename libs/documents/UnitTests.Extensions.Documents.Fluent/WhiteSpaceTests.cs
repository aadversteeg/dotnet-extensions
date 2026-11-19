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
/// White space as Chromium sets it, per run by its <c>white-space-collapse</c> (CSS Text 4): collapsed, preserved, or with
/// only its line feeds preserved, and preserved spaces hanging at a line's end (CSS Text 3 §4.1.2). Cases <c>ws-*</c>: Lato
/// 12 pt on 18 pt lines, the same documents written with the code front-end.
/// </summary>
/// <remarks>
/// THE COMPARISON is that of <see cref="InlineImageTests"/>: each word starts where Chromium prints it within one layout unit
/// for each item before it on its line plus one, and each baseline within one layout unit for each line above it. Where a
/// case holds a paragraph of a value that is not built (<c>break-spaces</c>), the engine's document has a gap of the same
/// number of lines there and Chromium's words of those lines are left out. In a case with right-aligned or centred lines a
/// word may lie two layout units further: such a line starts at the available width, which Chromium truncates to a layout
/// unit, less the line's width, its text items each ceiled to one (README.md, "The comparison rule"), so one unit for each
/// side of that difference.
/// </remarks>
public class WhiteSpaceTests
{
    private const WhiteSpaceCollapse Collapse = WhiteSpaceCollapse.Collapse;
    private const WhiteSpaceCollapse Preserve = WhiteSpaceCollapse.Preserve;
    private const WhiteSpaceCollapse PreserveBreaks = WhiteSpaceCollapse.PreserveBreaks;

    private static PageSetup Area(double height) => Page(300.Pt(), height.Pt()).Margin(12.Pt());

    private static Result<PageDocument, Error> Laid(double pageHeight, params Element[] body) =>
        DocumentTranslator.ToModel(Document([.. body], Area(pageHeight), title: "Title", language: "en").FontFamily("Lato").FontSize(12.Pt()).LineHeight(18.Pt())
                .FontMetricRounding(FontMetricRounding.WholePixels))
            .OnSuccessBind(model => DocumentLayout.Run(model, new FontSet([TestFonts.Regular, TestFonts.Bold])));

    private static PrintedPages Engine(double pageHeight, params Element[] body)
    {
        var printed = Laid(pageHeight, body).OnSuccessMap(PrintedPages.Engine);
        printed.IsSuccess.Should().BeTrue(printed.IsFailure ? printed.Error.ToString() : string.Empty);
        return printed.Value;
    }

    /// <summary>A paragraph of one text in a mode, its content box <paramref name="width"/> wide on the 276 pt page area.</summary>
    private static ParagraphElement P(WhiteSpaceCollapse mode, string text, double width = 276, TextAlign align = TextAlign.Left) =>
        Paragraph([Text(text)]).WhiteSpaceCollapse(mode).PaddingRight((276 - width).Pt()).TextAlign(align);

    /// <summary>The marker paragraph the cases put after each paragraph.</summary>
    private static ParagraphElement X => Paragraph("x");

    /// <summary>A gap of <paramref name="lines"/> lines where a case holds a paragraph of a value that is not built.</summary>
    private static ParagraphElement Gap(int lines) => Paragraph([Text(" ")]).PaddingTop((lines * 18).Pt());

    /// <summary>Chromium's print of a case without the words on the given baselines (the lines of a <see cref="Gap"/>).</summary>
    private static PrintedPages Chromium(string caseName, params double[] leftOut) =>
        new([.. PrintedPages.Chromium(caseName).Pages.Select(page => page with
        {
            Words = [.. page.Words.Where(word => !leftOut.Any(baseline => Math.Abs(word.Baseline - baseline) < 0.01))],
        })]);

    private static void ShouldMatch(PrintedPages engine, PrintedPages chromium, bool aligned = false)
    {
        var breaches = InlineImageTests.Breaches(engine, chromium, halfPixelBaselines: false, extraUnits: aligned ? 2 : 0);
        breaches.Should().BeEmpty(string.Join("\n", breaches));
    }

    /// <summary>The texts of the collapsing cases, each followed by the marker paragraph.</summary>
    private static readonly string[] CollapseTexts = ["a   b    c", "   lead", "trail   ", "one\ntwo", "one\n   two", "one\n\ntwo", "one\n", "\n", "   "];

    private static Element[] CollapseCase(WhiteSpaceCollapse mode) => [.. CollapseTexts.SelectMany(text => new Element[] { P(mode, text), X })];

    [Theory]
    [InlineData("ws-pre-wrap", Preserve)]
    [InlineData("ws-pre-line", PreserveBreaks)]
    [InlineData("ws-normal", Collapse)]
    public void Spaces_and_line_feeds_collapse_or_stay_by_the_mode_like_Chromium(string caseName, WhiteSpaceCollapse mode) =>
        ShouldMatch(Engine(420, CollapseCase(mode)), PrintedPages.Chromium(caseName));

    [Fact]
    public void Preserved_spaces_hang_at_a_soft_wrap_and_take_the_room_that_is_left_at_the_paragraph_end_like_Chromium()
    {
        // ws-hang-pre-wrap: per alignment a 40 pt paragraph whose spaces reach past a soft wrap, a 120 pt one that ends with
        // spaces, and one that wraps at single spaces.
        var body = new[] { TextAlign.Left, TextAlign.Right, TextAlign.Center, TextAlign.Justify }.SelectMany(align => new Element[]
        {
            P(Preserve, "aa bb          cc dd", 40, align), P(Preserve, "aa    ", 120, align), P(Preserve, "aa bb cc dd ee ff gg", 40, align), X,
        });

        ShouldMatch(Engine(600, [.. body]), PrintedPages.Chromium("ws-hang-pre-wrap"), aligned: true);
    }

    [Fact]
    public void Collapsible_spaces_at_a_line_end_go_whatever_the_alignment_like_Chromium()
    {
        var body = new[] { TextAlign.Left, TextAlign.Right, TextAlign.Center, TextAlign.Justify }.SelectMany(align => new Element[]
        {
            P(Collapse, "aa bb          cc dd", 40, align), P(Collapse, "aa    ", 120, align), P(Collapse, "aa bb cc dd ee ff gg", 40, align), X,
        });

        ShouldMatch(Engine(600, [.. body]), PrintedPages.Chromium("ws-hang-normal"), aligned: true);
    }

    [Fact]
    public void Preserved_spaces_before_a_forced_break_keep_only_the_room_that_is_left_like_Chromium()
    {
        var twenty = new string(' ', 20);
        ShouldMatch(Engine(500,
            P(Preserve, "aa   \nbb", 40, TextAlign.Right),
            P(Preserve, $"aa{twenty}\nbb", 40, TextAlign.Right),
            P(Preserve, $"aa{twenty}", 40, TextAlign.Right),
            P(Preserve, $"aa{twenty}\nbb", 40),
            Paragraph([Text("aa   "), LineBreak(), Text("bb")]).WhiteSpaceCollapse(Preserve).PaddingRight(236.Pt()).TextAlign(TextAlign.Right),
            P(PreserveBreaks, "aa   \nbb", 40, TextAlign.Right),
            // The case's two pre paragraphs (preserved spaces that do not wrap): they end with a forced break, where nothing
            // wraps either way, so preserved spaces that wrap lay out the same.
            P(Preserve, "aa   \nbb", 40, TextAlign.Right),
            P(Preserve, $"aa{twenty}\nbb", 40, TextAlign.Right),
            X), PrintedPages.Chromium("ws-forced"), aligned: true);
    }

    [Fact]
    public void Preserved_spaces_at_the_start_of_a_narrow_paragraph_make_a_line_of_their_own_like_Chromium() =>
        ShouldMatch(Engine(400,
            P(Preserve, "   lead", 6), P(Preserve, "aa\n   bb", 6), P(Preserve, "  aa bb cc dd", 40), P(Preserve, "          aa", 6), X),
            PrintedPages.Chromium("ws-lead"));

    [Fact]
    public void Every_preserved_space_inside_a_justified_line_widens_like_Chromium() =>
        // The case's third paragraph is break-spaces (not built): two lines at 97.5 and 115.5.
        ShouldMatch(Engine(300,
            P(Preserve, "a  b c dd ee ff gg hh", 60, TextAlign.Justify), P(Preserve, "a b c dd ee ff gg hh", 60, TextAlign.Justify), Gap(2), X),
            Chromium("ws-justify", 97.5, 115.5));

    /// <summary>The min-content and max-content widths of a paragraph of one text in a mode.</summary>
    private static IntrinsicWidths Measured(WhiteSpaceCollapse mode, string text)
    {
        var model = DocumentTranslator.ToModel(Document([Paragraph([Text(text)]).WhiteSpaceCollapse(mode)], Area(400), title: "Title", language: "en")
            .FontFamily("Lato").FontSize(12.Pt()).LineHeight(18.Pt())).Value;
        return LayoutEnvironment.Create(new FontSet([TestFonts.Regular])).Measure((Box)model.Root.Children[0]).Value;
    }

    [Theory]
    // Each width is a table cell of the case (boxes.txt) less 0.75 pt of grid lines: the cell in a 20 pt table is as wide as its
    // minimum, in a table of automatic width as wide as its maximum. Within one layout unit for each text item and one for each
    // half of a grid line.
    [InlineData(Preserve, "aa   bb", 14.191, 35.344)]
    [InlineData(Preserve, "  aa", 12.680, 18.832)]
    [InlineData(Preserve, "aa    ", 12.680, 24.973)]
    [InlineData(PreserveBreaks, "aa   bb\ncc", null, 29.191)]
    [InlineData(Preserve, "aa   \nbb", null, 21.902)]
    public void Hanging_spaces_count_for_the_maximum_width_but_not_for_the_minimum_like_Chromium(WhiteSpaceCollapse mode, string text, double? minimumCell, double maximumCell)
    {
        var widths = Measured(mode, text);

        if (minimumCell is { } minimum)
        {
            ChromiumLayout.DifferenceInLayoutUnits(widths.MinContent + 0.75, minimum).Should().BeLessThanOrEqualTo(3, "min-content");
        }

        ChromiumLayout.DifferenceInLayoutUnits(widths.MaxContent + 0.75, maximumCell).Should().BeLessThanOrEqualTo(4, "max-content");
    }

    [Fact]
    public void A_paragraph_of_preserved_spaces_or_of_a_preserved_line_feed_has_a_line_and_one_of_collapsible_spaces_has_none()
    {
        static double Height(WhiteSpaceCollapse mode, string text)
        {
            var laid = Laid(200, P(mode, text), X);
            laid.IsSuccess.Should().BeTrue(laid.IsFailure ? laid.Error.ToString() : string.Empty);
            return PrintedPages.Engine(laid.Value).Pages[0].Words.Single().Baseline;
        }

        // The marker's baseline: 25.5 after nothing, 43.5 after one line (cases ws-pre-wrap, ws-pre-line, ws-normal).
        Height(Preserve, "   ").Should().BeApproximately(43.5, 1e-6);
        Height(PreserveBreaks, "\n").Should().BeApproximately(43.5, 1e-6);
        Height(PreserveBreaks, "   ").Should().BeApproximately(25.5, 1e-6);
        Height(Collapse, "\n").Should().BeApproximately(25.5, 1e-6);
    }

    [Fact]
    public void A_tab_in_preserved_text_is_refused_with_a_message_that_says_tab_stops_are_not_built()
    {
        var laid = Laid(200, P(Preserve, "a\tb"));

        laid.IsFailure.Should().BeTrue("tab stops are not built (Chromium moves the b to the next tab stop, case ws-tab)");
        laid.Error.Code.Should().Be(DocumentErrorCodes.Input.Unsupported.PreservedTab);
        laid.Error.Message.Should().Contain("tab stops are not built");
        laid.Error.Metadata![ErrorMetadataKeys.Offset].Should().Be(1);
    }

    [Fact]
    public void A_tab_where_white_space_collapses_is_a_space_as_before()
    {
        Laid(200, P(Collapse, "a\tb")).IsSuccess.Should().BeTrue();
        Laid(200, P(PreserveBreaks, "a\tb")).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void A_white_space_collapse_value_outside_its_enum_is_refused()
    {
        var laid = Laid(200, P((WhiteSpaceCollapse)7, "a"));

        laid.IsFailure.Should().BeTrue();
        laid.Error.Code.Should().Be(DocumentErrorCodes.Input.Invalid.WhiteSpace);
    }
}
