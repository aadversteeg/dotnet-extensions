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
/// White space as Chromium sets it, per run by its <c>white-space-collapse</c> (CSS Text 4 §4.1): collapsed, preserved, or with
/// only its line feeds preserved, and preserved spaces hanging at a line's end (CSS Text 3 §4.1.2); and text that does not wrap
/// (<c>text-wrap-mode: nowrap</c>, CSS Text 4 §5.1). Cases <c>ws-*</c>: Lato
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
    private const WhiteSpaceCollapse BreakSpaces = WhiteSpaceCollapse.BreakSpaces;
    private const TextWrapMode Wrap = TextWrapMode.Wrap;
    private const TextWrapMode NoWrap = TextWrapMode.NoWrap;

    /// <summary>The colour of the cases' class <c>r</c> (#c00): Chromium's print splits a word where its colour changes.</summary>
    private static readonly Color Red = new(0xCC, 0x00, 0x00);

    private static byte[] Figure => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "chromium", "figure.png"));

    /// <summary>The 7 × 10 px figure 8.4 pt wide, in a line.</summary>
    private static Span Fig => InlineImage(Image(Figure).Alt("figure").Width(8.4.Pt()));

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
    private static ParagraphElement P(WhiteSpaceCollapse mode, string text, double width = 276, TextAlign align = TextAlign.Left, TextWrapMode wrap = Wrap) =>
        Spans(mode, [Text(text)], width, align, wrap);

    /// <summary>A paragraph of spans in a mode, its content box <paramref name="width"/> wide.</summary>
    private static ParagraphElement Spans(WhiteSpaceCollapse mode, ValueList<Span> spans, double width = 276, TextAlign align = TextAlign.Left, TextWrapMode wrap = Wrap) =>
        Paragraph(spans).WhiteSpaceCollapse(mode).TextWrapMode(wrap).PaddingRight((276 - width).Pt()).TextAlign(align);

    /// <summary>A red span (the cases' <c>class="r"</c>) in a mode.</summary>
    private static Span R(string text, WhiteSpaceCollapse mode = Collapse, TextWrapMode wrap = Wrap) =>
        Text(text).Color(Red).WhiteSpaceCollapse(mode).TextWrapMode(wrap);

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

    private static Element[] CollapseCase(WhiteSpaceCollapse mode, TextWrapMode wrap) => [.. CollapseTexts.SelectMany(text => new Element[] { P(mode, text, wrap: wrap), X })];

    [Theory]
    [InlineData("ws-pre", Preserve, NoWrap)]
    [InlineData("ws-pre-wrap", Preserve, Wrap)]
    [InlineData("ws-break-spaces", BreakSpaces, Wrap)]
    [InlineData("ws-pre-line", PreserveBreaks, Wrap)]
    [InlineData("ws-nowrap", Collapse, NoWrap)]
    [InlineData("ws-normal", Collapse, Wrap)]
    public void Spaces_and_line_feeds_collapse_or_stay_by_the_mode_like_Chromium(string caseName, WhiteSpaceCollapse mode, TextWrapMode wrap) =>
        ShouldMatch(Engine(420, CollapseCase(mode, wrap)), PrintedPages.Chromium(caseName));

    [Fact]
    public void Preserved_spaces_that_do_not_wrap_take_their_room_at_a_line_end_and_the_line_overflows_like_Chromium()
    {
        var body = new[] { TextAlign.Left, TextAlign.Right, TextAlign.Center, TextAlign.Justify }.SelectMany(align => new Element[]
        {
            P(Preserve, "aa bb          cc dd", 40, align, NoWrap), P(Preserve, "aa    ", 120, align, NoWrap), P(Preserve, "aa bb cc dd ee ff gg", 40, align, NoWrap), X,
        });

        ShouldMatch(Engine(600, [.. body]), PrintedPages.Chromium("ws-hang-pre"), aligned: true);
    }

    /// <summary>The paragraphs of ws-border (6 pt wide from the fourth on); the three where the engine and Chromium differ are
    /// gaps of Chromium's lines unless <paramref name="withDifferences"/>.</summary>
    private static Element[] Border(bool withDifferences)
    {
        ParagraphElement N(ValueList<Span> spans, TextWrapMode wrap = Wrap) => Spans(Collapse, spans, 6, wrap: wrap);
        ParagraphElement Or(ParagraphElement paragraph, int lines) => withDifferences ? paragraph : Gap(lines);
        return
        [
            Spans(Collapse, [Text("a "), Text("  b").WhiteSpaceCollapse(Preserve).TextWrapMode(NoWrap)]),
            Spans(Collapse, [Text("a  ").WhiteSpaceCollapse(Preserve).TextWrapMode(NoWrap), Text(" b")]),
            Spans(Collapse, [Text("a "), Text(" ").WhiteSpaceCollapse(Preserve).TextWrapMode(NoWrap), Text(" b")]),
            N([Text("aa"), R(" bb", wrap: NoWrap)]),
            N([R("aa ", wrap: NoWrap), Text("bb")]),
            N([Text("aa "), R("bb", wrap: NoWrap)]),
            N([R("aa", wrap: NoWrap), Text(" bb")]),
            Or(N([R("aa-", wrap: NoWrap), Text("bb")]), 2),
            N([Text("aa-"), R("bb", wrap: NoWrap)]),
            N([R("aa bb", wrap: NoWrap), Text("cc dd")]),
            N([Text("aa"), R(" bb")], NoWrap),
            N([R("aa "), Text("bb")], NoWrap),
            N([Text("aa "), R("bb")], NoWrap),
            Or(N([R("aa"), Text(" bb")], NoWrap), 2),
            N([R("aa-"), Text("bb")], NoWrap),
            N([Text("aa-"), R("bb")], NoWrap),
            Or(N([Text("aa "), R("bb cc"), Text(" dd")], NoWrap), 3),
            X,
        ];
    }

    [Fact]
    public void A_soft_wrap_opportunity_follows_the_wrap_mode_of_the_character_before_it_like_Chromium() =>
        // Left out: the paragraphs 8 (187.5, 205.5), 14 (385.5, 403.5) and 17 (475.5, 493.5, 511.5), where Chromium decides by
        // the end tag of an element that wraps differently from its parent (pinned below).
        ShouldMatch(Engine(700, Border(withDifferences: false)), Chromium("ws-border", 187.5, 205.5, 385.5, 403.5, 475.5, 493.5, 511.5));

    [Fact]
    public void Where_an_element_that_wraps_differently_from_its_parent_ends_the_engine_follows_the_character_before_not_Chromium()
    {
        // Listed difference (docs/architecture.md): Chromium breaks after a nowrap "aa-" before "bb" of a wrapping parent, and
        // after a wrapping "aa" or "bb cc" before a nowrap " bb" or " dd" of the parent (Blink LineBreaker::HandleCloseTag: "A
        // break opportunity before a close tag always propagates to after the close tag", and a wrapping element may break
        // before a breakable space after its end). The engine takes the mode of the character before the opportunity, so these
        // lines stay whole.
        var engine = Engine(700, Border(withDifferences: true)).Pages[0].Words;
        string Line(double baseline) => string.Join(" ", engine.Where(word => Math.Abs(word.Baseline - baseline) < 0.01).Select(word => word.Text));

        // Each paragraph that is one line short of Chromium's moves the later ones up by one 18 pt line.
        Line(187.5).Should().Be("aa- bb", "paragraph 8 is one line: the hyphen is in nowrap text (Chromium: two lines)");
        Line(385.5 - 18).Should().Be("aa bb", "paragraph 14 is one line: the space is in nowrap text (Chromium: two lines)");
        Line(475.5 - 36).Should().Be("aa bb", "paragraph 17 breaks after the wrapping \"bb \" only (Chromium: also before \" dd\")");
        Line(493.5 - 36).Should().Be("cc dd");
        Line(529.5 - 54).Should().Be("x");
    }

    [Fact]
    public void Nowrap_text_wider_than_its_paragraph_overflows_it_start_aligned_and_a_forced_break_still_breaks_like_Chromium() =>
        ShouldMatch(Engine(300,
            P(Collapse, "one two three four five six seven", 100, wrap: NoWrap),
            P(Collapse, "one two three four five six seven", 100, TextAlign.Right, NoWrap),
            P(Collapse, "one two three four five six seven", 100, TextAlign.Center, NoWrap),
            Spans(Collapse, [Text("one two"), LineBreak(), Text("three four five six seven")], 100, wrap: NoWrap),
            P(Preserve, "one two three four five six seven", 100, wrap: NoWrap),
            X), PrintedPages.Chromium("ws-overflow"));

    /// <summary>The words without soft hyphens, zero width spaces and U+2010: in the PDF text layer the engine drops a soft
    /// hyphen and Chromium keeps it and adds U+2010 at a shown hyphen (a listed difference of the text layer, docs/architecture.md).</summary>
    private static PrintedPages WithoutInvisibles(PrintedPages pages) =>
        new([.. pages.Pages.Select(page => page with
        {
            Words = [.. page.Words.Select(word => word with { Text = string.Concat(word.Text.Where(character => character is not ('\u00AD' or '\u200B' or '\u2010'))) })],
        })]);

    [Fact]
    public void A_soft_hyphen_and_a_zero_width_space_in_nowrap_text_do_not_break_it_like_Chromium() =>
        ShouldMatch(WithoutInvisibles(Engine(300,
            P(Collapse, "aa\u00ADbb\u200Bcc", 6, wrap: NoWrap),
            P(Collapse, "aa\u00ADbb\u200Bcc", 6),
            P(Preserve, "aa\u00ADbb\u200Bcc", 6, wrap: NoWrap),
            P(Preserve, "aa\u00ADbb\u200Bcc", 6),
            X)), WithoutInvisibles(PrintedPages.Chromium("ws-soft")));

    [Fact]
    public void An_image_beside_preserved_spaces_and_in_nowrap_text_is_set_like_Chromium() =>
        ShouldMatch(Engine(400,
            Spans(Preserve, [Text("ab   "), Fig, Text("   cd")], wrap: NoWrap),
            Spans(Preserve, [Text("ab   "), Fig, Text("   cd")], 6),
            Spans(Collapse, [Text("ab "), Fig, Text(" cd")], 6, wrap: NoWrap),
            Spans(Collapse, [Text("ab"), Fig, Text("cd")], 6, wrap: NoWrap),
            Spans(Preserve, [Fig, Text("\n"), Fig], wrap: NoWrap),
            X), PrintedPages.Chromium("ws-image"));

    [Fact]
    public void Kerning_crosses_a_border_where_only_the_white_space_or_the_wrap_mode_changes_like_Chromium() =>
        ShouldMatch(Engine(300,
            Paragraph("To"),
            Paragraph([Text("T"), R("o")]),
            Paragraph([Text("T"), R("o", Preserve, NoWrap)]),
            Paragraph([Text("T"), R("o", Collapse, NoWrap)]),
            Paragraph([Text("T"), R("o", Preserve)]),
            Paragraph([Text("T"), R("o", PreserveBreaks)]),
            Paragraph([Text("T"), R("o", BreakSpaces)]),
            X), PrintedPages.Chromium("ws-kern"));

    [Fact]
    public void The_longhand_values_Chromium_prints_are_the_model_s_and_preserve_spaces_and_discard_print_as_collapse() =>
        ShouldMatch(Engine(500,
            P(Preserve, "a   b\nc"),
            P(Collapse, "a   b\nc"),
            P(PreserveBreaks, "a   b\nc"),
            P(BreakSpaces, "a   b\nc"),
            P(Collapse, "a   b\nc"),
            P(Collapse, "a   b\nc"),
            P(Collapse, "aa bb", 6, wrap: NoWrap),
            P(Collapse, "aa bb", 6, wrap: NoWrap),
            P(Preserve, "aa  bb", 6, wrap: NoWrap),
            P(Collapse, "aa  bb", 6, wrap: NoWrap),
            P(PreserveBreaks, "aa  bb", 6),
            X), PrintedPages.Chromium("ws-longhands"));

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
    public void Spaces_of_break_spaces_take_their_room_and_wrap_like_Chromium()
    {
        var body = new[] { TextAlign.Left, TextAlign.Right, TextAlign.Center, TextAlign.Justify }.SelectMany(align => new Element[]
        {
            P(BreakSpaces, "aa bb          cc dd", 40, align), P(BreakSpaces, "aa    ", 120, align), P(BreakSpaces, "aa bb cc dd ee ff gg", 40, align), X,
        });

        ShouldMatch(Engine(600, [.. body]), PrintedPages.Chromium("ws-hang-break-spaces"), aligned: true);
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
        ShouldMatch(Engine(300,
            P(Preserve, "a  b c dd ee ff gg hh", 60, TextAlign.Justify), P(Preserve, "a b c dd ee ff gg hh", 60, TextAlign.Justify),
            P(BreakSpaces, "a  b c dd ee ff gg hh", 60, TextAlign.Justify), X),
            PrintedPages.Chromium("ws-justify"));

    /// <summary>The min-content and max-content widths of a paragraph of one text in a mode.</summary>
    private static IntrinsicWidths Measured(WhiteSpaceCollapse mode, string text, TextWrapMode wrap = Wrap) => Measured(Paragraph([Text(text)]).WhiteSpaceCollapse(mode).TextWrapMode(wrap));

    private static IntrinsicWidths Measured(ParagraphElement paragraph)
    {
        var model = DocumentTranslator.ToModel(Document([paragraph], Area(400), title: "Title", language: "en")
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
    // break-spaces: the spaces never hang, and a line may break after each (ws-table: the minimum is "aa" and one space).
    [InlineData(BreakSpaces, "aa    ", 15.750, 24.973)]
    // Text that does not wrap: its minimum is its widest line (cases ws-table: a nowrap and a pre cell, squeezed and not).
    [InlineData(Collapse, "aa bb cc", 43.617, 43.617, NoWrap)]
    [InlineData(Preserve, "aa  bb\ncc", 32.262, 32.262, NoWrap)]
    // Preserved spaces that do not wrap never hang, so they count for the minimum too (ws-intrinsic, the last table).
    [InlineData(Preserve, "aa    ", 24.973, 24.973, NoWrap)]
    public void Hanging_spaces_count_for_the_maximum_width_but_not_for_the_minimum_like_Chromium(WhiteSpaceCollapse mode, string text, double? minimumCell, double maximumCell, TextWrapMode wrap = Wrap)
    {
        var widths = Measured(mode, text, wrap);

        if (minimumCell is { } minimum)
        {
            ChromiumLayout.DifferenceInLayoutUnits(widths.MinContent + 0.75, minimum).Should().BeLessThanOrEqualTo(3, "min-content");
        }

        ChromiumLayout.DifferenceInLayoutUnits(widths.MaxContent + 0.75, maximumCell).Should().BeLessThanOrEqualTo(4, "max-content");
    }

    [Fact]
    public void The_minimum_width_of_wrapping_text_is_its_widest_nowrap_stretch_like_Chromium()
    {
        // ws-table, the last table: "aa ", a nowrap "bb cc", " dd" in a 20 pt table is 28.617 pt wide, "bb cc" and the grid lines.
        var widths = Measured(Paragraph([Text("aa "), Text("bb cc").TextWrapMode(NoWrap), Text(" dd")]));

        ChromiumLayout.DifferenceInLayoutUnits(widths.MinContent + 0.75, 28.617).Should().BeLessThanOrEqualTo(3);
    }

    [Fact]
    public void A_text_wrap_mode_value_outside_its_enum_is_refused()
    {
        var laid = Laid(200, P(Collapse, "a", wrap: (TextWrapMode)7));

        laid.IsFailure.Should().BeTrue();
        laid.Error.Code.Should().Be(DocumentErrorCodes.Input.Invalid.WhiteSpace);
        laid.Error.Metadata![ErrorMetadataKeys.Property].Should().Be("text-wrap-mode");
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
