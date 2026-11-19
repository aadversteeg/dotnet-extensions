using System.Text;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css.Properties;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.Documents.Html;
using Ave.Extensions.Documents.Html.Boxes;
using Ave.Extensions.Documents.Html.Parsing;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace UnitTests.Extensions.Documents.Html.Boxes;

/// <summary>Box generation (<see cref="HtmlBoxGenerator"/>): the boxes it makes, and its refusal side.</summary>
public class BoxGenerationTests
{
    private static string Doc(string body, string css = "") =>
        $"<!doctype html><html lang=\"en\"><head><title> The  title </title><style>body {{ font-family: Lato; margin: 0 }} {css}</style></head><body>{body}</body></html>";

    private static Result<HtmlBoxes, Error> Boxes(string html, UnsupportedPolicy policy = UnsupportedPolicy.Refuse, ResourceResolver? resources = null) =>
        HtmlParser.Parse(html, new HtmlParseOptions("t.html"))
            .OnSuccessBind(parsed => HtmlStyleResolver.Resolve(parsed, new HtmlStyleOptions { Unsupported = policy, Resources = resources }))
            .OnSuccessBind(styled => HtmlBoxGenerator.Generate(styled, new HtmlBoxOptions { Fonts = TestFonts.SetWithItalic, Unsupported = policy, Resources = resources }));

    private static StyledDocument Model(string html)
    {
        var result = Boxes(html);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        return result.Value.Document;
    }

    /// <summary>The body's box.</summary>
    private static Box Body(string html) => (Box)Model(html).Root.Children.Single();

    private static List<Error> Refusals(string html, UnsupportedPolicy policy = UnsupportedPolicy.Refuse)
    {
        var result = Boxes(html, policy);
        if (policy == UnsupportedPolicy.Warn)
        {
            result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
            return [.. result.Value.Diagnostics.Where(error => HtmlStyleDiagnostics.ClassOf(error.Code) == Ave.Extensions.Documents.Css.DiagnosticClass.Refusal)];
        }

        return result.IsSuccess ? [] : [.. result.Error.Parts()];
    }

    private static string? Property(Error error) => error.Metadata is { } metadata && metadata.TryGetValue(ErrorMetadataKeys.Property, out var value) ? (string)value : null;

    private static string Texts(Box box) => string.Join("|", box.Children.OfType<TextRun>().Select(run => run.Text));

    [Fact]
    public void Every_longhand_of_the_registry_has_a_row_and_every_row_is_a_longhand()
    {
        var names = PropertyRegistry.Longhands.Select(property => property.Name).ToList();
        names.Where(name => !BoxProperties.Rows.ContainsKey(name)).Should().BeEmpty("box generation says for every property what it draws");
        BoxProperties.Rows.Keys.Where(name => !names.Contains(name)).Should().BeEmpty();
    }

    [Fact]
    public void A_paragraph_of_text_and_inline_elements_is_a_box_with_lines_in_each_element_s_style()
    {
        var p = (Box)Body(Doc("<p>one <b>two</b> three</p>")).Children.Single();

        p.Role.Should().Be(SemanticRole.Paragraph);
        p.Layout.Should().Be(LayoutAlgorithmId.Inline);
        // The empty run carries the b element's own line height (CSS 2.1 §10.8.1); the text is as written.
        Texts(p).Should().Be("one ||two| three");
        p.Children.OfType<TextRun>().Select(run => run.Style.FontWeight.Value).Should().Equal(400, 700, 700, 400);
        p.Style.Text.Should().NotBeNull("a box with lines has a strut");
    }

    [Fact]
    public void Ids_are_unique_and_in_pre_order()
    {
        var model = Model(Doc("<div>a<p>b <i>c</i></p>d</div><h2>e</h2>"));
        var ids = new List<int>();
        var pending = new Stack<Node>([model.Root]);
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            ids.Add(node.Id.Value);
            if (node is Box box)
            {
                foreach (var child in box.Children.Reverse())
                {
                    pending.Push(child);
                }
            }
        }

        ids.Should().Equal(Enumerable.Range(0, ids.Count));
    }

    [Fact]
    public void Inline_content_beside_blocks_is_wrapped_in_anonymous_boxes_and_white_space_between_blocks_makes_none()
    {
        var div = (Box)Body(Doc("<div>\n  before <span>x</span>\n<p>in</p>\n  \n<p>in2</p> after</div>")).Children.Single();

        div.Layout.Should().Be(LayoutAlgorithmId.Flow);
        div.Children.Should().HaveCount(4, "anonymous, p, p, anonymous: the white space between the two p makes no box");
        var first = (Box)div.Children[0];
        (first.Role, first.Layout).Should().Be((SemanticRole.Generic, LayoutAlgorithmId.Inline));
        Texts(first).Should().Be("\n  before ||x|\n");
        Texts((Box)div.Children[3]).Should().Be(" after");
        first.Style.Text!.FontFamily.Should().Be("Lato");
    }

    [Fact]
    public void Headings_paragraphs_and_the_rest_get_their_roles_and_the_root_is_the_document()
    {
        var model = Model(Doc("<h1>a</h1><h6>b</h6><p>c</p><div>d</div>"));
        model.Root.Role.Should().Be(SemanticRole.Document);
        ((Box)model.Root.Children.Single()).Children.Cast<Box>().Select(box => box.Role)
            .Should().Equal(SemanticRole.Heading(1), SemanticRole.Heading(6), SemanticRole.Paragraph, SemanticRole.Generic);
    }

    [Fact]
    public void Display_none_makes_nothing()
    {
        var body = Body(Doc("<p>a</p><div style=\"display:none\"><p>gone</p><br><img></div>"));
        body.Children.Should().ContainSingle();
    }

    [Fact]
    public void The_title_and_the_root_language_go_into_the_metadata_and_the_profile_is_Chromium_s()
    {
        var model = Model(Doc("<p>a</p>"));
        (model.Metadata.Title, model.Metadata.Language).Should().Be(("The title", "en"));
        (model.Profile.MonolithicOverflow, model.Profile.EmptyRowParts, model.Profile.FontMetricRounding)
            .Should().Be((MonolithicOverflow.Slice, EmptyRowParts.Allow, FontMetricRounding.WholePixels));
    }

    [Fact]
    public void Box_properties_are_mapped()
    {
        var div = (Box)Body(Doc("<div style=\"margin:1pt 2pt 3pt 4pt;padding:5pt;border:3pt solid #102030;border-left-style:none;background-color:#ff0000;break-before:page;break-inside:avoid-page;orphans:3;widows:4;text-align:center\">x</div>")).Children.Single();
        var style = div.Style;
        style.Margin.Should().Be(new Edges(Length.FromPoints(1), Length.FromPoints(2), Length.FromPoints(3), Length.FromPoints(4)));
        style.Padding.Top.Points.Should().Be(5);
        style.Border.Top.Should().Be(new BorderSide(Length.FromPoints(3), new Color(0x10, 0x20, 0x30)));
        style.Border.Left.Should().Be(BorderSide.None);
        style.Background.Should().Be(new Color(255, 0, 0));
        (style.BreakBefore, style.BreakInside, style.Orphans, style.Widows, style.TextAlign).Should().Be((BreakBetween.Page, BreakInside.Avoid, 3, 4, TextAlign.Center));
    }

    [Fact]
    public void A_forced_break_after_a_box_is_a_forced_break_before_the_next_and_after_the_last_box_it_is_refused()
    {
        var body = Body(Doc("<p style=\"break-after:page\">a</p><p>b</p>"));
        ((Box)body.Children[1]).Style.BreakBefore.Should().Be(BreakBetween.Page);

        // Propagated from the last child to its parent (CSS Fragmentation 3 §3.1), then to the box after the parent.
        var nested = Body(Doc("<div><p style=\"break-after:page\">a</p></div><p>b</p>"));
        ((Box)nested.Children[1]).Style.BreakBefore.Should().Be(BreakBetween.Page);

        Refusals(Doc("<p>a</p><p style=\"break-after:page\">b</p>")).Select(Property).Should().Equal("break-after");
    }

    [Theory]
    [InlineData("1.5", 12, 18)]
    [InlineData("1.3", 11, 19.0625 * 0.75)]
    // 1.21 x 11 pt is 1135.79 layout units: floored to 1135, where rounding would give 1136.
    [InlineData("1.21", 11, 1135 * 0.75 / 64)]
    [InlineData("13.3pt", 10, 17.734375 * 0.75)]
    public void The_line_height_is_given_in_Chromium_s_layout_unit(string value, int size, double points)
    {
        var p = (Box)Body(Doc($"<p style=\"font-size:{size}pt;line-height:{value}\">a</p>")).Children.Single();
        p.Style.Text!.LineHeight.Should().Be(new LineHeight.Fixed(Length.FromPoints(points)));
    }

    [Theory]
    [InlineData("<ul><li>a</li></ul>", "display")]
    [InlineData("<table><tr><td>a</td></tr></table>", "display")]
    [InlineData("<pre>a</pre>", "white-space-collapse")]
    [InlineData("<hr>", "border-top-style")]
    [InlineData("<p><a href=\"x\">a</a></p>", "text-decoration-line")]
    [InlineData("<p><span style=\"vertical-align:top\">a</span></p>", "vertical-align")]
    public void An_element_whose_user_agent_rendering_is_not_drawn_is_refused_through_its_values(string body, string property) =>
        Refusals(Doc(body)).Select(Property).Should().Contain(property);

    [Theory]
    [InlineData("br")]
    [InlineData("wbr")]
    [InlineData("img")]
    [InlineData("q")]
        public void An_element_whose_rendering_no_property_says_is_refused_by_its_own_rule_at_its_start_tag(string element)
    {
        HtmlBoxGenerator.RefusedRenderings.Keys.Should().Contain(element);
        var refusal = Refusals(Doc($"<p>a\n <{element}>b</p>")).Single();
        refusal.Code.Should().Be(HtmlStyleErrorCodes.Unsupported.Rendering);
        (refusal.Metadata![ErrorMetadataKeys.SourceLine], refusal.Metadata[ErrorMetadataKeys.SourceColumn]).Should().Be((2, 2));
    }

    [Theory]
    // Each pair: a value without effect where it is (accepted), and the same value where it changes the print (refused).
    [InlineData("<div style=\"box-sizing:border-box\">a</div>", null)]
    [InlineData("<div style=\"width:10pt\">a</div>", "width")]
    [InlineData("<p><span style=\"width:10pt\">a</span></p>", null)]
    [InlineData("<div style=\"border-collapse:collapse;list-style-type:square\">a</div>", null)]
    [InlineData("<p><span style=\"margin-top:5pt\">a</span></p>", null)]
    [InlineData("<p><span style=\"margin-left:5pt\">a</span></p>", "margin-left")]
    [InlineData("<div style=\"margin-left:auto\">a</div>", null)]
    [InlineData("<div style=\"margin-left:-1pt\">a</div>", "margin-left")]
    [InlineData("<div style=\"padding-left:5%\">a</div>", "padding-left")]
    [InlineData("<p><span style=\"padding-left:2pt\">a</span></p>", "padding-left")]
    [InlineData("<div style=\"vertical-align:middle\">a</div>", null)]
    [InlineData("<div style=\"color:rgba(0,0,0,0.5)\"><p style=\"color:black\">a</p></div>", null)]
    [InlineData("<div style=\"color:rgba(0,0,0,0.5)\">a</div>", "color")]
    [InlineData("<div style=\"text-wrap-mode:nowrap\"><p style=\"text-wrap-mode:wrap\">a</p></div>", null)]
    [InlineData("<p style=\"text-wrap-mode:nowrap\">a</p>", "text-wrap-mode")]
    [InlineData("<div style=\"box-decoration-break:clone\">a</div>", null)]
    [InlineData("<div style=\"box-decoration-break:clone;padding:1pt\">a</div>", "box-decoration-break")]
    [InlineData("<p><span style=\"overflow-x:clip\">a</span></p>", null)]
    [InlineData("<div style=\"overflow-x:clip\">a</div>", "overflow-x")]
    [InlineData("<p><span style=\"aspect-ratio:auto 2/1\">a</span></p>", null)]
    [InlineData("<div style=\"aspect-ratio:auto 2/1\">a</div>", "aspect-ratio")]
    [InlineData("<p><span style=\"background-color:transparent\">a</span></p>", null)]
    [InlineData("<p><span style=\"background-color:red\">a</span></p>", "background-color")]
    [InlineData("<div style=\"background-color:rgba(255,0,0,0.5)\">a</div>", "background-color")]
    [InlineData("<div style=\"border-top:2pt inset red\">a</div>", "border-top-style")]
    [InlineData("<div style=\"border-top-style:inset;border-top-width:0\">a</div>", null)]
    [InlineData("<div style=\"border-top:2pt solid rgba(0,0,0,0.5)\">a</div>", "border-top-color")]
    [InlineData("<div style=\"white-space-collapse:preserve\"><p style=\"white-space-collapse:collapse\">a</p></div>", null)]
    [InlineData("<p style=\"font-kerning:normal\">a</p>", null)]
    public void A_value_is_refused_only_where_it_changes_the_print(string body, string? refused) =>
        Refusals(Doc(body)).Select(Property).Should().Equal(refused is null ? [] : [refused], string.Join("; ", Refusals(Doc(body)).Select(error => error.Message)));

    [Theory]
    [InlineData("html")]
    [InlineData("body")]
    public void A_background_on_the_root_or_body_is_refused_the_model_has_no_page_background(string element) =>
        Refusals(Doc("<p>a</p>", $"{element} {{ background-color: #eee }}")).Single().Code.Should().Be(HtmlStyleErrorCodes.Unsupported.CanvasBackground);

    [Fact]
    public void A_block_inside_an_inline_box_is_refused() =>
        Refusals(Doc("<div><span>a<div>b</div></span></div>")).Single().Code.Should().Be(HtmlStyleErrorCodes.Unsupported.BlockInInline);

    [Fact]
    public void A_tree_deeper_than_the_model_allows_is_refused_without_recursion_under_both_policies()
    {
        var html = Doc(new StringBuilder().Insert(0, "<div>", 200).Append('x').Append(new StringBuilder().Insert(0, "</div>", 200)).ToString());
        foreach (var policy in new[] { UnsupportedPolicy.Refuse, UnsupportedPolicy.Warn })
        {
            var result = Boxes(html, policy);
            result.IsFailure.Should().BeTrue();
            result.Error.Parts().Select(error => error.Code).Should().Equal(HtmlStyleErrorCodes.Unsupported.TooDeep);
        }

        // At the limit it is laid out: the root, the body and 62 div levels, the deepest with its run at level 64.
        Boxes(Doc(new StringBuilder().Insert(0, "<div>", 62).Append('x').ToString())).IsSuccess.Should().BeTrue();
        Boxes(Doc(new StringBuilder().Insert(0, "<div>", 63).Append('x').ToString())).IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Under_warn_a_refused_value_takes_the_model_s_initial_value_a_refused_display_is_a_block_and_an_element_refused_by_its_own_rule_is_left_out()
    {
        var html = Doc("<div style=\"padding:5%;background-color:rgba(0,0,0,0.5)\">a</div><ul><li>b</li></ul><p>c<br>d</p>");
        var warned = Boxes(html, UnsupportedPolicy.Warn);
        warned.IsSuccess.Should().BeTrue(warned.IsFailure ? warned.Error.ToString() : string.Empty);
        var body = (Box)warned.Value.Document.Root.Children.Single();
        var div = (Box)body.Children[0];
        (div.Style.Padding, div.Style.Background).Should().Be((default(Edges), (Color?)null));
        var li = (Box)((Box)body.Children[1]).Children.Single();
        (li.Layout, Texts(li)).Should().Be((LayoutAlgorithmId.Inline, "b"));
        Texts((Box)body.Children[2]).Should().Be("c|d");
        Refusals(html, UnsupportedPolicy.Warn).Select(error => error.Code.ToString()).Should().Equal(
            "Documents.Html.Unsupported.Value", "Documents.Html.Unsupported.Value", "Documents.Html.Unsupported.Value",
            "Documents.Html.Unsupported.Value", "Documents.Html.Unsupported.Value", "Documents.Html.Unsupported.Value", "Documents.Html.Unsupported.Rendering");
        Boxes(html).IsFailure.Should().BeTrue("the same document is refused by default");
    }

    [Fact]
    public void Without_a_page_rule_the_page_is_the_caller_s_by_default_A4_with_no_margins()
    {
        var page = Model(Doc("<p>a</p>")).Page;
        page.Width.Points.Should().BeApproximately(594.96, 1e-9);
        page.Height.Points.Should().BeApproximately(841.92, 1e-9);
        page.Margins.Should().Be(default(Edges), "the page area of 794 x 1123 px reaches past the sheet");
    }

    [Theory]
    [InlineData("size: 300pt 174pt; margin: 12pt", 300, 174, 12, 12)]
    // A4 landscape is 841.89 x 595.28 pt: whole points (842 x 595), then 1/300 inch. A top margin of 10 mm (37.8 px) is
    // laid out at 38 px; the left one of 20 mm (75.6 px) at 76 px, and the area, 1122.52 - 151.16 px, is 972 px wide.
    [InlineData("size: A4 landscape; margin: 10mm 20mm", 841.92, 594.96, 28.5, 841.92 - 786)]
    [InlineData("size: 200pt 100pt; margin-top: 6pt", 199.92, 100.08, 6, 0)]
    [InlineData("size: A5", 420, 594.96, 0, 0)]
    [InlineData("size: 595.5pt 842.25pt", 595.92, 841.92, 0, 595.92 - 595.5)]
    [InlineData("size: 133.5px 100.5px", 100.08, 75.12, 0, 0)]
    // 11.75 pt is 15.67 px: the area starts at 16 px and is 400 - 31.31 px, rounded up: 369 px, so it ends at 385 px.
    [InlineData("size: 300pt 84pt; margin: 11.75pt", 300, 84, 12, 300 - 288.75)]
    // Half a pixel: a 15.5 px margin starts the area at 16 px; 400.5 - 32 px leaves 368.5 px, an area of 369 px.
    [InlineData("size: 300pt 84pt; margin: 11.625pt", 300, 84, 12, 300 - 288.75)]
    [InlineData("size: 300.375pt 84pt; margin: 12pt", 300, 84, 12, 300 - 288.75)]
    [InlineData("size: 300pt 100pt; margin: 10pt 10.3pt", 300, 100.08, 9.75, 300 - 290.25)]
    // On the default A4 page: the area starts at 16 px and is 793.7 - 32 px, rounded up: 762 px (583.5 pt).
    [InlineData("margin: 12pt; font-family: Lato; color: red; counter-reset: page", 594.96, 841.92, 12, 594.96 - 12 - 571.5)]
    public void A_page_rule_without_a_selector_gives_the_size_rounded_as_Chromium_prints_it_and_the_margins(string declarations, double width, double height, double top, double right)
    {
        var page = Model(Doc("<p>a</p>", $"@page {{ {declarations} }}")).Page;
        page.Width.Points.Should().BeApproximately(width, 1e-9);
        page.Height.Points.Should().BeApproximately(height, 1e-9);
        page.Margins.Top.Points.Should().BeApproximately(top, 1e-9);
        page.Margins.Right.Points.Should().BeApproximately(right, 1e-9);
    }

    [Theory]
    // 300 pt (400 px) with 10 pt margins (13.33 px): the area is 373.34 px, rounded up to 374 px (case h-page-area-bottom).
    [InlineData("size: 300pt 300pt; margin: 10pt", 280.5)]
    // 68 pt (90.67 px) with 12 pt margins: 58.67 px, rounded up to 59 px (case h-page-area-continued).
    [InlineData("size: 300pt 68pt; margin: 12pt", 44.25)]
    // A sheet smaller than the area: 1123 px (842.25 pt) on a sheet of 841.92 pt; the bottom margin is 0, not negative.
    [InlineData("size: A4; margin: 0", 841.92)]
    public void The_page_area_is_whole_pixels_rounded_up_from_what_the_margins_leave(string declarations, double areaHeight)
    {
        var page = Model(Doc("<p>a</p>", $"@page {{ {declarations} }}")).Page;
        page.AreaHeight.Points.Should().BeApproximately(areaHeight, 1e-9);
        page.Margins.Bottom.Points.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void A_root_with_display_none_is_one_empty_page_of_the_caller_s_paper_whatever_the_page_rules_say()
    {
        var model = Model(Doc("<p>before</p><p><q>after</q></p>", "html { display: none } @page { size: 300pt 100pt; margin: 10pt }"));
        model.Root.Children.Should().BeEmpty();
        model.Page.Width.Points.Should().BeApproximately(594.96, 1e-9, "the default page, A4, not the 300 pt of the @page rule");
        model.Page.Margins.Should().Be(default(Edges));
    }

    [Theory]
    [InlineData("@page :first { margin: 0 }")]
    [InlineData("@page { @top-center { content: 'x' } }")]
    [InlineData("@page { margin: banana }")]
    [InlineData("@page { margin: 10% }")]
    [InlineData("@page { margin: -1pt }")]
    [InlineData("@page { size: 0pt 10pt }")]
    [InlineData("@page { background-color: red }")]
    [InlineData("@page { no-such-property: 1 }")]
    public void Every_other_page_rule_or_declaration_is_refused_at_its_place(string rule)
    {
        var refusal = Refusals(Doc("<p>a</p>", rule)).Single();
        refusal.Code.Should().Be(HtmlStyleErrorCodes.Unsupported.PageRule);
        refusal.Metadata!.Should().ContainKey(ErrorMetadataKeys.SourceLine);
    }

    [Fact]
    public void A_family_nobody_gives_is_refused_and_the_next_family_of_the_list_is_taken()
    {
        Refusals(Doc("<p style=\"font-family:Nobody, serif\">a</p>")).Single().Code.Should().Be(HtmlStyleErrorCodes.Unsupported.Font);
        var p = (Box)Body(Doc("<p style=\"font-family:Nobody, Lato\">a</p>")).Children.Single();
        p.Style.Text!.FontFamily.Should().Be("Lato");
    }

    [Fact]
    public void A_face_the_family_does_not_have_at_the_weight_is_refused_not_replaced() =>
        Refusals(Doc("<p style=\"font-weight:300\">a</p>")).Single().Code.Should().Be(FontErrorCodes.NotFound);

    [Fact]
    public void A_font_face_rule_s_font_comes_through_the_resolver_once_and_a_missing_file_is_refused()
    {
        var asked = new List<string>();
        HtmlResource? Resolver(string url)
        {
            asked.Add(url);
            return url == "theano.ttf" ? new HtmlResource(TestFonts.Bytes("TheanoDidot-Regular.ttf")) : null;
        }

        var css = "@font-face { font-family: Display; src: url(theano.ttf) format('truetype') } @font-face { font-family: Unused; src: url(none.ttf) } p { font-family: Display }";
        // Two family lists that both take the Display face: the file is still asked for once.
        var result = Boxes(Doc("<p>a</p><p style=\"font-family: Display, serif\">b</p>", css), resources: Resolver);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        asked.Should().Equal("theano.ttf");
        result.Value.Fonts.Fonts.Select(font => font.Names.Family).Should().Contain("Theano Didot");
        ((Box)((Box)result.Value.Document.Root.Children.Single()).Children[0]).Style.Text!.FontFamily.Should().Be("Theano Didot");

        var missing = Boxes(Doc("<p>a</p>", "@font-face { font-family: Gone; src: url(gone.ttf) } p { font-family: Gone }"), resources: Resolver);
        missing.Error.Parts().Single().Code.Should().Be(HtmlStyleErrorCodes.Resource.NotFound);
    }
}
