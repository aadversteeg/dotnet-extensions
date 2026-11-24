using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Html;
using Ave.Extensions.Documents.Html.Boxes;
using Ave.Extensions.Documents.Html.Parsing;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace UnitTests.Extensions.Documents.Html.Boxes;

/// <summary>Box generation of <c>text-decoration</c> and <c>text-decoration-skip-ink</c>: the decorations applied to each
/// run (<see cref="TextStyle.Decorations"/>) and whether they skip the ink (<see cref="TextStyle.DecorationSkipInk"/>); the
/// reference cases <c>dl-*</c> and <c>sk-*</c> hold Chromium's prints.</summary>
public class TextDecorationMappingTests
{
    private static string Doc(string body, string css = "") =>
        $"<!doctype html><html lang=\"en\"><head><title>t</title><style>body {{ font-family: Lato; margin: 0 }} {css}</style></head><body>{body}</body></html>";

    private static Result<HtmlBoxes, Error> Boxes(string html, UnsupportedPolicy policy = UnsupportedPolicy.Refuse) =>
        HtmlParser.Parse(html, new HtmlParseOptions("t.html"))
            .OnSuccessBind(parsed => HtmlStyleResolver.Resolve(parsed, new HtmlStyleOptions { Unsupported = policy, Resources = Resources }))
            .OnSuccessBind(styled => HtmlBoxGenerator.Generate(styled, new HtmlBoxOptions { Fonts = TestFonts.SetWithItalic, Unsupported = policy, Resources = Resources }));

    private static readonly byte[] Figure = System.IO.File.ReadAllBytes(System.IO.Path.Combine(HtmlCases.Folder(), "figure.png"));

    private static HtmlResource? Resources(string url) => url.EndsWith("figure.png", StringComparison.Ordinal) ? new HtmlResource(Figure) : null;

    /// <summary>Every text run of the document in order, also inside inline groups (links), the empty runs of inline elements left out.</summary>
    private static List<TextRun> Runs(string html, UnsupportedPolicy policy = UnsupportedPolicy.Refuse)
    {
        var result = Boxes(html, policy);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        var runs = new List<TextRun>();
        var pending = new Stack<Node>([result.Value.Document.Root]);
        while (pending.Count > 0)
        {
            switch (pending.Pop())
            {
                case TextRun { Text.Length: > 0 } run:
                    runs.Add(run);
                    break;
                case Box box:
                    foreach (var child in box.Children.Reverse())
                    {
                        pending.Push(child);
                    }

                    break;
                case InlineGroup group:
                    foreach (var child in group.Children.Reverse())
                    {
                        pending.Push(child);
                    }

                    break;
            }
        }

        return runs;
    }

    private static TextRun Run(string html, string text) => Runs(html).Single(run => run.Text == text);

    private static string Lines(TextRun run) => string.Join(" ", run.Style.Decorations.Select(decoration => decoration.Line));

    private static Error Refusal(string html) => Boxes(html).Error.Parts().Single();

    private static string? Property(Error error) => error.Metadata is { } metadata && metadata.TryGetValue(ErrorMetadataKeys.Property, out var value) ? (string)value : null;

    [Fact]
    public void The_applied_lines_are_the_parent_s_then_the_element_s_own_in_the_order_underline_overline_line_through()
    {
        var html = Doc("<p><span style=\"text-decoration: line-through overline\">a<span style=\"text-decoration-line: line-through underline overline\">b</span></span>c</p>");

        Lines(Run(html, "a")).Should().Be("Overline LineThrough");
        Lines(Run(html, "b")).Should().Be("Overline LineThrough Underline Overline LineThrough");
        Run(html, "c").Style.Decorations.Should().BeEmpty("text-decoration is not inherited: only the text inside the element is decorated");
    }

    [Fact]
    public void Each_line_has_the_font_and_the_decoration_colour_of_the_element_that_draws_it()
    {
        var html = Doc("<p style=\"color: #102030\"><b style=\"text-decoration: underline; font-size: 24pt\">a<span style=\"font-size: 8pt; font-weight: 400\"><i style=\"text-decoration: overline #c00\">b</i></span></b></p>");

        var outer = Run(html, "a").Style.Decorations.Single();
        (outer.Line, outer.Color, outer.FontFamily, outer.FontSize.Points, outer.FontWeight.Value, outer.FontStyle)
            .Should().Be((TextDecorationLine.Underline, new Color(0x10, 0x20, 0x30), "Lato", 24, 700, FontStyle.Normal), "currentcolor is the element's computed colour");

        var lines = Run(html, "b").Style.Decorations;
        lines[0].Should().Be(outer, "the outer element's line keeps the outer element's font on text of another size");
        (lines[1].Line, lines[1].Color, lines[1].FontSize.Points, lines[1].FontWeight.Value, lines[1].FontStyle)
            .Should().Be((TextDecorationLine.Overline, new Color(0xcc, 0, 0), 8, 400, FontStyle.Italic));
    }

    [Fact]
    public void None_adds_nothing_and_takes_nothing_away()
    {
        var html = Doc("<p><u>a<span style=\"text-decoration: none\">b</span></u></p>");
        Lines(Run(html, "b")).Should().Be("Underline", "a decoration of an ancestor is drawn across the text of its descendants whatever their own value");
    }

    [Fact]
    public void The_lines_go_down_through_block_boxes()
    {
        var html = Doc("<div style=\"text-decoration: underline\">a<p>b</p><div><p style=\"text-decoration: line-through\">c</p></div></div>");

        Lines(Run(html, "a")).Should().Be("Underline");
        Lines(Run(html, "b")).Should().Be("Underline");
        Lines(Run(html, "c")).Should().Be("Underline LineThrough");
    }

    [Fact]
    public void An_image_in_a_decorated_line_gets_no_line()
    {
        var result = Boxes(Doc("<p><u>a<img src=\"figure.png\" alt=\"f\">b</u></p>"));
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        var p = (Box)((Box)result.Value.Document.Root.Children.Single()).Children.Single();

        var image = p.Children.OfType<Box>().Single();
        image.Style.Text!.Decorations.Should().BeEmpty();
        p.Children.OfType<TextRun>().Where(run => run.Text.Length > 0).Should().OnlyContain(run => run.Style.Decorations.Count == 1);
    }

    [Theory]
    [InlineData("<u>x</u>", "Underline")]
    [InlineData("<ins>x</ins>", "Underline")]
    [InlineData("<s>x</s>", "LineThrough")]
    [InlineData("<del>x</del>", "LineThrough")]
    [InlineData("<strike>x</strike>", "LineThrough")]
    [InlineData("<a href=\"https://example.com/t.html\">x</a>", "Underline")]
    [InlineData("<a>x</a>", "")]
    public void The_elements_the_user_agent_style_sheet_decorates_are_drawn_with_their_lines(string element, string lines)
    {
        var run = Run(Doc($"<p>{element}</p>"), "x");
        Lines(run).Should().Be(lines);
        run.Style.Decorations.Where(decoration => decoration.Color != run.Style.Color).Should().BeEmpty("the user-agent style sheet sets no decoration colour");
    }

    [Fact]
    public void A_link_is_underlined_in_its_colour()
    {
        var run = Run(Doc("<p><a href=\"https://example.com/t.html\">x</a></p>"), "x");
        (run.Style.Color, run.Style.Decorations.Single().Color).Should().Be((new Color(0, 0, 0xEE), new Color(0, 0, 0xEE)));
    }

    [Theory]
    [InlineData("auto", "none", TextDecorationSkipInk.None)]
    [InlineData("none", "auto", TextDecorationSkipInk.Auto)]
    [InlineData("none", null, TextDecorationSkipInk.None)]
    [InlineData(null, null, TextDecorationSkipInk.Auto)]
    public void Skip_ink_is_the_value_of_the_element_the_text_lies_in_not_of_the_decorating_element(string? decorating, string? inner, TextDecorationSkipInk expected)
    {
        var outer = decorating is null ? string.Empty : $"; text-decoration-skip-ink: {decorating}";
        var own = inner is null ? string.Empty : $" style=\"text-decoration-skip-ink: {inner}\"";
        var run = Run(Doc($"<p><span style=\"text-decoration: underline{outer}\">a<span{own}>b</span></span></p>"), "b");
        run.Style.DecorationSkipInk.Should().Be(expected);
    }

    [Theory]
    [InlineData("text-decoration-style: dotted", "text-decoration-style")]
    [InlineData("text-decoration-style: wavy", "text-decoration-style")]
    [InlineData("text-decoration: underline double", "text-decoration")]
    [InlineData("text-decoration-thickness: 2px", "text-decoration-thickness")]
    [InlineData("text-decoration-thickness: from-font", "text-decoration-thickness")]
    [InlineData("text-underline-offset: 2px", "text-underline-offset")]
    [InlineData("text-underline-position: under", "text-underline-position")]
    [InlineData("text-decoration-skip-ink: all", "text-decoration-skip-ink")]
    [InlineData("text-decoration-line: blink", "text-decoration-line")]
    public void What_is_not_built_is_refused_by_the_style_sheet_with_its_value_and_place(string declaration, string property)
    {
        var refusal = Refusal(Doc("<p>\n<span>a</span></p>", $"span {{ {declaration} }}"));

        refusal.Code.Should().Be(CssErrorCodes.Unsupported.Value);
        refusal.Message.Should().Contain(property);
        refusal.Metadata!.Should().ContainKeys(ErrorMetadataKeys.SourceLine, ErrorMetadataKeys.SourceColumn);
    }

    [Theory]
    [InlineData("text-decoration-thickness: auto")]
    [InlineData("text-underline-offset: auto")]
    [InlineData("text-underline-position: auto")]
    [InlineData("text-decoration-style: solid")]
    [InlineData("text-decoration-skip-ink: auto")]
    public void The_initial_values_are_drawn(string declaration) =>
        Lines(Run(Doc("<p><u>a</u></p>", $"u {{ {declaration} }}"), "a")).Should().Be("Underline");

    [Fact]
    public void A_decoration_colour_that_is_not_opaque_is_refused_at_the_start_tag()
    {
        var refusal = Refusal(Doc("<p>\n<u style=\"text-decoration-color: rgb(0 0 0 / 50%)\">a</u></p>"));

        (refusal.Code, Property(refusal)).Should().Be((HtmlStyleErrorCodes.Unsupported.Value, "text-decoration-color"));
        refusal.Metadata![ErrorMetadataKeys.SourceLine].Should().Be(2);
        Runs(Doc("<p><span style=\"text-decoration-color: rgb(0 0 0 / 50%)\">a</span></p>")).Single().Style.Decorations.Should().BeEmpty("the colour of no line is drawn");
    }

    [Fact]
    public void A_text_with_more_lines_than_the_model_holds_is_refused_with_their_number_at_the_start_tag_of_its_element()
    {
        var nested = string.Concat(Enumerable.Repeat("<span>", 6)) + "a" + string.Concat(Enumerable.Repeat("</span>", 6));
        var html = Doc($"<p>\n{nested}</p>", "span { text-decoration: underline overline line-through }");

        var refusal = Refusal(html);
        (refusal.Code, Property(refusal), refusal.Metadata![ErrorMetadataKeys.Value]).Should().Be((HtmlStyleErrorCodes.Unsupported.Value, "text-decoration-line", "18"));
        (refusal.Metadata[ErrorMetadataKeys.SourceLine], refusal.Metadata[ErrorMetadataKeys.SourceColumn]).Should().Be((2, 31), "the innermost span, whose text has 18 lines");

        Runs(html, UnsupportedPolicy.Warn).Single(run => run.Text == "a").Style.Decorations.Should().HaveCount(ModelLimits.MaxDecorations, "under Warn the outermost lines are kept");
        var five = string.Concat(Enumerable.Repeat("<span>", 5)) + "a<u>b</u>" + string.Concat(Enumerable.Repeat("</span>", 5));
        Runs(Doc($"<p>{five}</p>", "span { text-decoration: underline overline line-through }")).Select(run => run.Style.Decorations.Count)
            .Should().Equal([15, ModelLimits.MaxDecorations], "a text with exactly as many lines as the model holds is drawn");
    }

    /// <summary>
    /// Case <c>dw-gap-fraction</c>: underlines and links over descenders on lines whose top lies 0, 0.25, 0.5 and 0.75 px past
    /// a whole pixel of the page. Chromium draws the text from the line's rounded top and cuts the gaps with the band at the
    /// unrounded one; under <see cref="InkGapBand.AtUnroundedLine"/> every piece of every line ends where Chromium's does
    /// (within its single-precision 0.005 pt), under <see cref="InkGapBand.AtText"/> only those of the lines whose top lies
    /// on a whole pixel. The line itself is painted at the same place under both.
    /// </summary>
    [Theory]
    [InlineData(InkGapBand.AtUnroundedLine)]
    [InlineData(InkGapBand.AtText)]
    public void The_gaps_of_an_underline_are_cut_with_the_band_at_its_line_s_unrounded_place_on_the_page(InkGapBand band)
    {
        var boxes = HtmlCases.Boxes("dw-gap-fraction").Value;
        boxes.Document.Profile.InkGapBand.Should().Be(InkGapBand.AtUnroundedLine);
        var pages = Ave.Extensions.Documents.Layout.DocumentLayout.Run(boxes.Document with { Profile = boxes.Document.Profile with { InkGapBand = band } }, boxes.Fonts).Value;
        var engine = Lines(PrintedPages.Engine(pages).Pages[0].Rectangles);
        var chromium = Lines(HtmlCases.ChromiumPrint("dw-gap-fraction").Pages[0].Rectangles);

        engine.Keys.Should().Equal(chromium.Keys, "each line is painted where Chromium paints it");
        var equal = engine.Keys.Where(top => engine[top].Count == chromium[top].Count
            && engine[top].Zip(chromium[top]).All(pair => Math.Abs(pair.First.X0 - pair.Second.X0) < 0.005 && Math.Abs(pair.First.X1 - pair.Second.X1) < 0.005)).ToList();
        if (band == InkGapBand.AtUnroundedLine)
        {
            equal.Should().Equal(engine.Keys);
            return;
        }

        // The lines whose top lies on a whole pixel: the first of the 10.5 pt underlines, of the links and of the 12 pt lines.
        equal.Should().Equal(23.25, 75, 128.25);

        // The pieces of the underlines and links, line by line from the top: a line is the rectangles one pixel high on one row.
        static SortedDictionary<double, List<PrintedBox>> Lines(IEnumerable<PrintedBox> boxes) => new(boxes
            .Where(box => box.Y1 - box.Y0 < 0.8 && box.X1 - box.X0 < 280)
            .GroupBy(box => ChromiumLayout.WholePixel(box.Y0))
            .ToDictionary(row => row.Key, row => row.OrderBy(box => box.X0).ToList()));
    }
}
