using System.IO;
using Ave.Extensions.Documents;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Html;
using Ave.Extensions.Documents.Html.Boxes;
using Ave.Extensions.Documents.Html.Parsing;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.Documents.Pdf;
using Ave.Extensions.Documents.Render.Pdf;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace UnitTests.Extensions.Documents.Html.Boxes;

/// <summary>
/// Lists through box generation (<c>HtmlBoxGenerator.Lists.cs</c>): the list and its items, each label's text for the
/// attributes and styles of the measured cases (<c>li-ol-attrs</c>, <c>li-counter-styles</c>, <c>li-types</c>,
/// <c>li-ol-nested</c>, <c>li-ul-levels</c>, <c>li-none</c>, <c>li-div-list-item</c>, <c>li-font</c>), an item outside a
/// list (<c>li-bare</c>), and what is refused, at its line and column.
/// </summary>
public class ListMappingTests
{
    private static string Doc(string body, string css = "") =>
        $"<!doctype html><html lang=\"en\"><head><title>Lists</title><style>body {{ font-family: Lato; margin: 0 }} {css}</style></head><body>{body}</body></html>";

    private static Result<HtmlBoxes, Error> Boxes(string html) =>
        HtmlParser.Parse(html, new HtmlParseOptions("t.html"))
            .OnSuccessBind(parsed => HtmlStyleResolver.Resolve(parsed, new HtmlStyleOptions()))
            .OnSuccessBind(styled => HtmlBoxGenerator.Generate(styled, new HtmlBoxOptions { Fonts = TestFonts.SetWithItalic }));

    private static Box Body(string html)
    {
        var result = Boxes(html);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        return (Box)result.Value.Document.Root.Children.Single();
    }

    private static IEnumerable<Box> Descendants(Box root)
    {
        var pending = new Stack<Box>([root]);
        while (pending.Count > 0)
        {
            var box = pending.Pop();
            yield return box;
            foreach (var child in box.Children.OfType<Box>().Reverse())
            {
                pending.Push(child);
            }
        }
    }

    /// <summary>Each item's label in document order: its text, the symbol's shape, or "none".</summary>
    private static string Labels(string body, string css = "") => string.Join("|", Descendants(Body(Doc(body, css)))
        .Where(box => box.Role is SemanticRole.ListItemRole)
        .Select(item => item.Children[0] is Box { Role: SemanticRole.ListLabelRole } label
            ? label.Children.Single() switch
            {
                TextRun run => run.Text,
                MarkerSymbol symbol => symbol.Shape.ToString().ToLowerInvariant(),
                var other => other.GetType().Name,
            }
            : "none"));

    private static Error Refusal(string html) => Boxes(html).Error.Parts().First();

    [Theory]
    // li-ol-attrs.
    [InlineData("<ol start=5><li>a<li>b</ol>", "5. |6. ")]
    [InlineData("<ol><li>a<li value=10>b<li>c</ol>", "1. |10. |11. ")]
    [InlineData("<ol reversed><li>a<li>b<li>c</ol>", "3. |2. |1. ")]
    [InlineData("<ol reversed start=2><li>a<li>b<li>c<li>d</ol>", "2. |1. |0. |-1. ")]
    [InlineData("<ol type=a><li>a<li>b</ol>", "a. |b. ")]
    [InlineData("<ol type=I start=4><li>a<li>b</ol>", "IV. |V. ")]
    [InlineData("<ol start=-2><li>a<li>b<li>c</ol>", "-2. |-1. |0. ")]
    [InlineData("<ul><li value=7>a</ul>", "disc")]
    // li-counter-styles, li-types: the ranges and the fallback to decimal.
    [InlineData("<ol style=\"list-style-type: decimal-leading-zero\"><li value=1>a<li value=9>a<li value=10>a<li value=100>a<li value=0>a<li value=-5>a</ol>", "01. |09. |10. |100. |00. |-5. ")]
    [InlineData("<ol style=\"list-style-type: upper-alpha\"><li value=1>a<li value=26>a<li value=27>a<li value=0>a</ol>", "A. |Z. |AA. |0. ")]
    [InlineData("<ol style=\"list-style-type: upper-latin\"><li value=703>a</ol>", "AAA. ")]
    [InlineData("<ol style=\"list-style-type: lower-latin\"><li value=2>a<li value=28>a</ol>", "b. |ab. ")]
    [InlineData("<ol style=\"list-style-type: lower-alpha\"><li value=702>a<li value=703>a</ol>", "zz. |aaa. ")]
    [InlineData("<ol style=\"list-style-type: lower-roman\"><li value=4>a<li value=40>a<li value=90>a<li value=400>a<li value=900>a<li value=3888>a<li value=3999>a<li value=4000>a<li value=-1>a</ol>", "iv. |xl. |xc. |cd. |cm. |mmmdccclxxxviii. |mmmcmxcix. |4000. |-1. ")]
    [InlineData("<ol style=\"list-style-type: upper-roman\"><li value=9>a<li value=14>a<li value=49>a<li value=1999>a<li value=0>a</ol>", "IX. |XIV. |XLIX. |MCMXCIX. |0. ")]
    [InlineData("<ul style=\"list-style-type: decimal\"><li>a<li>b</ul>", "1. |2. ")]
    [InlineData("<ol style=\"list-style-type: square\"><li>a</ol>", "square")]
    // li-none: no label, and the item still counts.
    [InlineData("<ol style=\"list-style-type: none\"><li>a<li>b</ol>", "none|none")]
    [InlineData("<ol><li>a<li style=\"list-style-type: none\">b<li>c</ol>", "1. |none|3. ")]
    // li-ol-nested, li-ul-levels: each list restarts; the bullet by level from the user-agent sheet.
    [InlineData("<ol><li>a<ol><li>b<li>c<ol><li>d<li>e</ol></ol><li>f<li>g<ul><li>h</ul></ol>", "1. |1. |2. |1. |2. |2. |3. |circle")]
    [InlineData("<ul><li>a<ol start=3><li>b<li>c</ol></ul>", "disc|3. |4. ")]
    [InlineData("<ul><li>a<ul><li>b<ul><li>c<ul><li>d</ul></ul></ul></ul>", "disc|circle|square|square")]
    [InlineData("<menu><li>a</menu><dir><li>b</dir>", "disc|disc")]
    public void Each_label_is_the_item_s_number_in_its_counter_style_or_its_symbol(string body, string labels) =>
        Labels(body).Should().Be(labels);

    [Fact]
    public void The_list_item_counter_runs_on_through_the_siblings_of_items_outside_a_list()
    {
        // li-div-list-item: a disc div, then "2." and "3." on the next two; the paragraph item after a paragraph counts on.
        Labels("<p>x</p><div class=i>a</div><div class=i style=\"list-style-type: decimal\">b</div><div class=i style=\"list-style-type: decimal\">c</div><p>y</p><p class=i style=\"list-style-type: decimal\">d</p>",
            ".i { display: list-item }").Should().Be("disc|2. |3. |4. ");
    }

    [Fact]
    public void A_list_is_tagged_with_its_own_numbering_and_its_items_are_label_and_body()
    {
        var list = (Box)Body(Doc("<ol type=i><li>a<li style=\"list-style-type: square\">b</ol>")).Children.Single();
        list.Role.Should().Be(SemanticRole.List(ListNumbering.LowerRoman));
        var item = (Box)list.Children[0];
        (item.Role, item.Layout).Should().Be((SemanticRole.ListItem, LayoutAlgorithmId.ListItem));
        var label = (Box)item.Children[0];
        (label.Role, label.Layout).Should().Be((SemanticRole.ListLabel(null), LayoutAlgorithmId.Inline));
        var body = (Box)item.Children[1];
        (body.Role, body.Layout).Should().Be((SemanticRole.ListBody, LayoutAlgorithmId.Flow));
        ((Box)body.Children.Single()).Layout.Should().Be(LayoutAlgorithmId.Inline, "the item's text is the body's lines");
        ((Box)((Box)list.Children[1]).Children[0]).Role.Should().Be(SemanticRole.ListLabel("▪"), "a bullet's tag carries its character");
        Body(Doc("<ul></ul>")).Children.OfType<Box>().Single().Role.Should().Be(SemanticRole.Generic, "an empty list has no item to tag");
    }

    [Fact]
    public void A_label_is_in_the_item_s_own_font_size_and_colour_not_its_first_line_s()
    {
        // li-font: a 16 pt "1." beside 16 pt text, a red one beside red text.
        var item = Descendants(Body(Doc("<ol><li style=\"font-size: 16pt; color: #ff0000; text-align: right\"><span style=\"font-size: 8pt; color: #0000ff\">a</span></li></ol>")))
            .Single(box => box.Role is SemanticRole.ListItemRole);
        var run = (TextRun)((Box)item.Children[0]).Children.Single();
        (run.Style.FontSize.Points, run.Style.Color, run.Style.WhiteSpaceCollapse, run.Style.TextWrapMode).Should().Be((16.0, new Color(255, 0, 0), WhiteSpaceCollapse.Preserve, TextWrapMode.NoWrap));
    }

    [Fact]
    public void Items_outside_a_list_element_are_wrapped_in_a_list_of_their_own_for_each_run_among_siblings()
    {
        // li-bare: a bare li draws a disc as in a ul; PDF has no LI outside an L.
        var body = Body(Doc("<p>x</p><li>a</li><li>b</li>\n<div><li>c</li></div><p>y</p>"));
        body.Children.OfType<Box>().Select(box => box.Role).Should().Equal(SemanticRole.Paragraph, SemanticRole.List(ListNumbering.Disc), SemanticRole.Generic, SemanticRole.Paragraph);
        var implied = (Box)body.Children[1];
        (implied.Layout, implied.Style).Should().Be((LayoutAlgorithmId.Flow, BoxStyle.Initial));
        implied.Children.Select(child => ((Box)child).Role).Should().Equal(SemanticRole.ListItem, SemanticRole.ListItem);
        ((Box)((Box)body.Children[2]).Children.Single()).Role.Should().Be(SemanticRole.List(ListNumbering.Disc));
        Boxes(Doc("<li>a</li>")).Value.Document.Root.Should().NotBeNull();
    }

    [Theory]
    [InlineData("<ol start=2147483648><li>a</ol>", "start", 1, 124)]
    [InlineData("<ol><li value=-2147483649>a</ol>", "value", 1, 128)]
    [InlineData("<ol start=2147483647><li>a<li>b</ol>", "2147483648", 1, 150)]
    [InlineData("<ol reversed><li value=3>a</ol>", "value", 1, 137)]
    [InlineData("<ul style=\"list-style-type: decimal\"><li>a<li value=3>b</ul>", "value", 1, 166)]
    [InlineData("<ul><p>a</p></ul>", "p", 1, 128)]
    [InlineData("<ol>\ntext<li>a</ol>", "text", 1, 128)]
    [InlineData("<ol><li>a<section><li>b</li></section></ol>", "list-item", 1, 142)]
    [InlineData("<ol><li>a</ol><li>b</li>", "list-item", 1, 138)]
    [InlineData("<li>a</li><div><li style=\"list-style-type: decimal\">b</li></div>", "list-item", 1, 139)]
    [InlineData("<ol><li style=\"text-decoration: underline\">a</ol>", "text-decoration", 1, 128)]
    public void What_the_list_model_does_not_have_is_refused_at_its_line_and_column(string body, string value, int line, int column)
    {
        var refusal = Refusal(Doc(body));
        refusal.Code.Should().Be(HtmlStyleErrorCodes.Unsupported.List, refusal.ToString());
        $"{refusal.Metadata![ErrorMetadataKeys.Value]} at {refusal.Metadata[ErrorMetadataKeys.SourceLine]}:{refusal.Metadata[ErrorMetadataKeys.SourceColumn]}".Should().Be($"{value} at {line}:{column}");
    }

    [Theory]
    [InlineData("<ul style=\"list-style-position: inside\"><li>a</ul>", "list-style-position")]
    [InlineData("<ul style=\"list-style-image: url(x.png)\"><li>a</ul>", "list-style-image")]
    [InlineData("<ol style=\"counter-reset: list-item 3\"><li>a</ol>", "counter-reset")]
    [InlineData("<ol><li style=\"counter-increment: list-item 2\">a</ol>", "counter-increment")]
    [InlineData("<ol><li style=\"counter-set: list-item 2\">a</ol>", "counter-set")]
    public void Inside_markers_images_and_counter_properties_are_refused_at_their_place(string body, string property)
    {
        var refusal = Refusal(Doc(body));
        refusal.ToString().Should().Contain(property);
        refusal.Metadata!.ContainsKey(ErrorMetadataKeys.SourceColumn).Should().BeTrue();
    }

    [Fact]
    public void A_marker_rule_with_a_property_the_marker_does_not_draw_is_refused_at_its_declaration()
    {
        var refusal = Refusal(Doc("<ul><li>a</ul>", "li::marker { font-size: 20pt }"));
        refusal.ToString().Should().Contain("::marker");
        refusal.Metadata!.ContainsKey(ErrorMetadataKeys.SourceColumn).Should().BeTrue();
    }

    [Fact]
    public void A_label_left_of_the_sheet_is_refused_at_its_item()
    {
        // li-marker-sheet: margin 0, padding 10.5 pt: the disc would start 1.5 pt left of the sheet.
        var boxes = Boxes(Doc("<p>x</p>\n<ul style=\"padding-left: 10.5pt\"><li>a</li></ul>", "@page { size: 300pt 300pt; margin: 0 } body { font-size: 10.5pt }"));
        var pages = DocumentLayout.Run(boxes.Value.Document, boxes.Value.Fonts);
        pages.IsFailure.Should().BeTrue();
        var error = pages.Error.Parts().Single();
        (error.Code, error.Metadata![ErrorMetadataKeys.SourceLine], error.Metadata[ErrorMetadataKeys.Value]).Should().Be((LayoutErrorCodes.MarkerOutsideSheet, 2, "-1.5"));
    }

    [Theory]
    [InlineData("disc", "")]
    [InlineData("square", "")]
    [InlineData("disc", "table { border-collapse: collapse } td { padding: 2.3pt 4pt; border: 0.75pt solid }")]
    public void A_bullet_lies_on_whole_pixels_of_the_page_where_the_list_starts_between_pixels(string type, string css)
    {
        // li-after-block, li-after-table, li-after-edge: a line 13 pt high (17.33 px), or a table with padding in points,
        // ends between pixels, and a padding of 20 pt puts the content edge at 26.67 px. Chromium paints the bullet's box from
        // the nearest whole pixel of the page; the same document under MarkerSnapping.Exact has it where its item puts it.
        var before = css.Length == 0 ? "<p style=\"line-height: 13pt\">x</p>" : "<table><tr><td>x</td></tr></table>";
        var html = Doc($"{before}<ul style=\"padding-left: 20pt; list-style-type: {type}\"><li>a</li></ul>",
            "@page { size: 300pt 300pt; margin: 12pt } body { font-size: 10.5pt } ul { margin: 0 } p { margin: 0 } " + css);
        var boxes = Boxes(html).Value;
        boxes.Document.Profile.MarkerSnapping.Should().Be(MarkerSnapping.WholePixels);

        var snapped = Bullet(DocumentLayout.Run(boxes.Document, boxes.Fonts).Value);
        var exact = Bullet(DocumentLayout.Run(boxes.Document with { Profile = boxes.Document.Profile with { MarkerSnapping = MarkerSnapping.Exact } }, boxes.Fonts).Value);

        (Math.Abs(exact.Y / 0.75 - Math.Round(exact.Y / 0.75)) > 0.1).Should().BeTrue($"the bullet's top {exact.Y} lies between pixels where its item puts it");
        (Math.Abs(exact.X / 0.75 - Math.Round(exact.X / 0.75)) > 0.1).Should().BeTrue($"the bullet's left edge {exact.X} lies between pixels where its item puts it");
        snapped.X.Should().BeApproximately(Math.Round(exact.X / 0.75, MidpointRounding.AwayFromZero) * 0.75, 1e-9);
        snapped.Y.Should().BeApproximately(Math.Round(exact.Y / 0.75, MidpointRounding.AwayFromZero) * 0.75, 1e-9);

        // The top-left corner of the bullet's box (5 px, 3.75 pt): a disc's path starts at its right end, on its centre line.
        static Point Bullet(PageDocument pages) => pages.Pages[0].Items.OfType<FillPath>().Select(path => path.Contours[0].Start).Select(start => new Point(start.X - 3.75, start.Y - 1.875))
            .Concat(pages.Pages[0].Items.OfType<FillRectangle>().Where(fill => Math.Abs(fill.Area.Size.Width - 3.75) < 1e-9 && Math.Abs(fill.Area.Size.Height - 3.75) < 1e-9).Select(fill => fill.Area.Origin))
            .Single();
    }

    [DockerTheory]
    [Trait("Category", "Reference")]
    [InlineData("li-ol-nested", "3a")]
    [InlineData("li-ol-nested", "ua1")]
    [InlineData("li-bare", "3a")]
    [InlineData("li-bare", "ua1")]
    public void A_case_printed_as_a_tagged_PDF_passes_veraPDF(string name, string flavour)
    {
        // The case as measured, with the title and the language PDF/UA asks for.
        var path = Path.GetFullPath(Path.Combine(HtmlCases.Folder(), name + ".html"));
        var html = File.ReadAllText(path).Replace("<html>", "<html lang=\"en\">", StringComparison.Ordinal).Replace("<head>", "<head><title>Lists</title>", StringComparison.Ordinal);
        var options = new HtmlRenderOptions { Style = HtmlStyleOptions.Default with { Page = HtmlCases.Letter, Resources = HtmlCases.Resource, BaseUrl = new Uri(path).AbsoluteUri } };
        var profile = flavour == "3a" ? PdfProfile.PdfA3a : PdfProfile.PdfUa1;
        var pdf = HtmlRendering.ToPdf(html, options, new PdfRenderOptions(new DateTimeOffset(2026, 11, 23, 12, 0, 0, TimeSpan.Zero)) { Profile = profile });
        pdf.IsSuccess.Should().BeTrue(pdf.IsFailure ? pdf.Error.ToString() : string.Empty);
        var folder = Directory.CreateTempSubdirectory("ave-extensions-documents-html-lists-").FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(folder, name + ".pdf"), [.. pdf.Value.Output]);

            var result = VeraPdf.Validate(folder, flavour).Single();

            result.IsCompliant.Should().BeTrue(result.ToString());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
