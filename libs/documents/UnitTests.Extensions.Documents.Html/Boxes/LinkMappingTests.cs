using System.IO;
using Ave.Extensions.Documents;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Html;
using Ave.Extensions.Documents.Html.Boxes;
using Ave.Extensions.Documents.Html.Parsing;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.Pdf;
using Ave.Extensions.Documents.Render.Pdf;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace UnitTests.Extensions.Documents.Html.Boxes;

/// <summary>Box generation of <c>a[href]</c>: the inline group with the role of a link (<see cref="InlineGroup"/>,
/// <see cref="SemanticRole.LinkRole"/>), its URI by THE RULE FOR A LINK'S URL IN HTML (<c>LinkUrls</c>) and the model's
/// rule (<see cref="LinkUris"/>), its description, and the refusals at the start tag; the reference cases <c>lk-*</c> hold
/// Chromium's prints and links (<see cref="HtmlCases.LinkDifference"/>).</summary>
public class LinkMappingTests
{
    private static string Doc(string body, string head = "") =>
        $"<!doctype html><html lang=\"en\"><head><title>t</title>{head}<style>body {{ font-family: Lato; margin: 0 }}</style></head><body>{body}</body></html>";

    private static Result<HtmlBoxes, Error> Boxes(string html, string? documentUrl = null, UnsupportedPolicy policy = UnsupportedPolicy.Refuse) =>
        HtmlParser.Parse(html, new HtmlParseOptions("t.html"))
            .OnSuccessBind(parsed => HtmlStyleResolver.Resolve(parsed, new HtmlStyleOptions { Unsupported = policy, Resources = Resources, BaseUrl = documentUrl }))
            .OnSuccessBind(styled => HtmlBoxGenerator.Generate(styled, new HtmlBoxOptions { Fonts = TestFonts.SetWithItalic, Unsupported = policy, Resources = Resources, BaseUrl = documentUrl }));

    private static readonly byte[] Figure = System.IO.File.ReadAllBytes(System.IO.Path.Combine(HtmlCases.Folder(), "figure.png"));

    private static HtmlResource? Resources(string url) => url.EndsWith("figure.png", StringComparison.Ordinal) ? new HtmlResource(Figure) : null;

    /// <summary>Every inline group of the document, in document order.</summary>
    private static List<InlineGroup> Groups(Result<HtmlBoxes, Error> result)
    {
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        var groups = new List<InlineGroup>();
        var pending = new Stack<Node>([result.Value.Document.Root]);
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            var children = node switch
            {
                Box box => box.Children,
                InlineGroup group => group.Children,
                _ => [],
            };
            if (node is InlineGroup found)
            {
                groups.Add(found);
            }

            foreach (var child in children.Reverse())
            {
                pending.Push(child);
            }
        }

        return groups;
    }

    private static SemanticRole.LinkRole Link(string html, string? documentUrl = null) => (SemanticRole.LinkRole)Groups(Boxes(html, documentUrl)).Single().Role;

    private static string Uri(string href, string? documentUrl = null, string head = "") =>
        ((LinkTarget.UriTarget)Link(Doc($"<p>See <a href=\"{href}\">this</a>.</p>", head), documentUrl).Target).Value;

    private static Error Refusal(string html, string? documentUrl = null) => Boxes(html, documentUrl).Error.Parts().Single();

    private const string HttpsBase = "<base href=\"https://example.com/dir/page.html?a=1\">";

    [Fact]
    public void A_link_is_an_inline_group_around_the_element_s_runs_with_the_element_s_empty_run_first()
    {
        var result = Boxes(Doc("<p>Short <a href=\"https://example.com/one\">link <b>bold</b></a> here.</p>"));
        var group = Groups(result).Single();
        var paragraph = (Box)((Box)result.Value.Document.Root.Children.Single()).Children.Single();

        paragraph.Children.Select(child => child.GetType().Name).Should().Equal("TextRun", "InlineGroup", "TextRun");
        group.Role.Should().Be(SemanticRole.Link(LinkTarget.Uri("https://example.com/one"), "link bold"));
        group.Children.OfType<TextRun>().Select(run => run.Text).Should().Equal(string.Empty, "link ", string.Empty, "bold");
        group.Children[0].Should().BeOfType<TextRun>().Which.Style.Should().Be(group.Style, "the empty run of the a element carries its line height, as for any inline element");
        group.Style.Color.Should().Be(new Color(0x00, 0x00, 0xEE), "the text style is the a element's, with the link colour of the user-agent style sheet");
    }

    [Fact]
    public void The_lines_are_as_tall_with_the_link_as_without_it()
    {
        static double Height(string body)
        {
            var boxes = Boxes(Doc(body)).Value;
            return PrintedPages.Engine(DocumentLayout.Run(boxes.Document, boxes.Fonts).Value).Pages.Single().Words.Last().Baseline;
        }

        Height("<p>One <a href=\"https://example.com/\" style=\"font-size: 30pt\"></a> two<br>three</p>")
            .Should().Be(Height("<p>One <span style=\"font-size: 30pt\"></span> two<br>three</p>"), "an empty link is no link, and its empty run still counts");
        Height("<p>One <a href=\"https://example.com/\" style=\"font-size: 30pt\">x</a> two<br>three</p>")
            .Should().Be(Height("<p>One <span style=\"font-size: 30pt\">x</span> two<br>three</p>"));
    }

    [Theory]
    [InlineData("<a href=\"https://example.com/\" title=\" The  title \">text <img src=\"figure.png\" alt=\"Alt\"></a>", "The title")]
    [InlineData("<a href=\"https://example.com/\">  some\n  text <img src=\"figure.png\" alt=\"Alt\"></a>", "some text")]
    [InlineData("<a href=\"https://example.com/\"><img src=\"figure.png\" alt=\" Blue  figure \"></a>", "Blue figure")]
    [InlineData("<a href=\"https://example.com/x\"><img src=\"figure.png\" alt=\"\"></a>", "https://example.com/x")]
    [InlineData("<a href=\"https://example.com/x\" title=\"  \"><img src=\"figure.png\"></a>", "https://example.com/x")]
    public void The_description_is_the_title_else_the_text_else_the_alternative_text_of_an_image_else_the_URI(string link, string description) =>
        Link(Doc($"<p>See {link} now.</p>")).Description.Should().Be(description);

    [Theory]
    [InlineData("<p>Plain <a>anchor</a> text.</p>")]
    [InlineData("<p>Plain <a name=\"n\">anchor</a> text.</p>")]
    [InlineData("<p>Plain <a href=\"https://example.com/\"></a> text.</p>")]
    [InlineData("<p>Plain <a href=\"https://example.com/\"> </a> text.</p>")]
    public void An_a_without_href_and_an_a_that_draws_nothing_are_no_link(string body) =>
        Groups(Boxes(Doc(body))).Should().BeEmpty();

    [Theory]
    // Absolute: the scheme in lower case, the rest as written, the shared percent-encode set encoded as UTF-8.
    [InlineData("https://example.com/a", "https://example.com/a")]
    [InlineData("  HTTPS://example.com/a b?q=é#frag ", "https://example.com/a%20b?q=%C3%A9#frag")]
    [InlineData("https://example.com/\"<>`{}|^'%41", "https://example.com/%22%3C%3E`{}|^'%41")]
    [InlineData("https://example.com/\U0001F600", "https://example.com/%F0%9F%98%80")]
    [InlineData("https://exa\tmple.com/a\nb\rc", "https://example.com/abc")]
    [InlineData("mailto:someone@example.com?subject=Hello there", "mailto:someone@example.com?subject=Hello%20there")]
    [InlineData("MAILTO:someone@example.com", "mailto:someone@example.com")]
    [InlineData("tel:+44 20 7946 0000", "tel:+44%2020%207946%200000")]
    public void An_absolute_URL_is_written_with_its_scheme_in_lower_case_and_percent_encoded_to_ASCII(string href, string uri) =>
        Uri(href.Replace("\"", "&quot;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal), "file:///cases/t.html").Should().Be(uri);

    [Theory]
    [InlineData("other.html", "https://example.com/dir/other.html")]
    [InlineData("/root.html", "https://example.com/root.html")]
    [InlineData("../up.html", "https://example.com/up.html")]
    [InlineData("./a/./b/../c.html", "https://example.com/dir/a/c.html")]
    [InlineData("?q=2", "https://example.com/dir/page.html?q=2")]
    [InlineData("", "https://example.com/dir/page.html?a=1")]
    [InlineData("//example.com/x", "https://example.com/x")]
    [InlineData("a\\b.html?c\\d", "https://example.com/dir/a/b.html?c\\d")]
    [InlineData("café menu.html", "https://example.com/dir/caf%C3%A9%20menu.html")]
    public void A_relative_URL_is_resolved_against_an_https_base_by_RFC_3986(string href, string uri) =>
        Uri(href, "file:///cases/t.html", HttpsBase).Should().Be(uri);

    [Fact]
    public void A_relative_URL_is_resolved_against_an_http_document_URL_without_a_base_element() =>
        Uri("other.html", "http://example.com/dir/page.html").Should().Be("http://example.com/dir/other.html");

    [Theory]
    // The scheme rule of the model: every scheme but http, https, mailto and tel, in any case and around white space.
    [InlineData("javascript:alert(1)", "javascript")]
    [InlineData("JavaScript:alert(1)", "javascript")]
    [InlineData("JAVASCRIPT:alert(1)", "javascript")]
    [InlineData("  javascript:alert(1)  ", "javascript")]
    [InlineData("&#1;&#31; javascript:alert(1)", "javascript")]
    [InlineData("java&#9;script:alert(1)", "javascript")]
    [InlineData("java&#10;script:alert(1)", "javascript")]
    [InlineData("jav&#13;ascript:alert(1)", "javascript")]
    [InlineData("&#9;j&#10;a&#13;v&#9;a&#10;s&#13;c&#9;r&#10;i&#13;p&#9;t&#10;:alert(1)", "javascript")]
    [InlineData("vbscript:msgbox(1)", "vbscript")]
    [InlineData("file:///etc/hosts", "file")]
    [InlineData("FILE:///etc/hosts", "file")]
    [InlineData("data:text/html,hi", "data")]
    [InlineData("ftp://example.com/f", "ftp")]
    public void A_refused_scheme_is_refused_at_the_start_tag_also_in_mixed_case_and_around_white_space(string href, string scheme)
    {
        foreach (var head in new[] { string.Empty, HttpsBase })
        {
            var error = Refusal(Doc($"<p>See\n<a href=\"{href}\">this</a>.</p>", head), "file:///cases/t.html");

            error.Code.Should().Be(HtmlStyleErrorCodes.Unsupported.Link);
            error.Message.Should().Contain($"the scheme '{scheme}' is not accepted").And.Contain(LinkUris.SchemesText);
            error.Metadata![ErrorMetadataKeys.Property].Should().Be("href");
            error.Metadata[ErrorMetadataKeys.SourceLine].Should().Be(2);
            error.Metadata[ErrorMetadataKeys.SourceColumn].Should().Be(1);
        }
    }

    [Theory]
    [InlineData("other.html", "file:///cases/t.html", "")]
    [InlineData("/root.html", "file:///cases/t.html", "")]
    [InlineData("//example.com/x", "file:///cases/t.html", "")]
    [InlineData("other.html", null, "")]
    [InlineData("other.html", "https://example.com/", "<base href=\"file:///other/\">")]
    public void A_relative_URL_without_an_http_or_https_base_is_refused(string href, string? documentUrl, string head)
    {
        var error = Refusal(Doc($"<p>See <a href=\"{href}\">this</a>.</p>", head), documentUrl);

        error.Code.Should().Be(HtmlStyleErrorCodes.Unsupported.Link);
        error.Message.Should().Contain("is relative").And.Contain("add a base element");
    }

    /// <summary>The node a link inside the document leads to.</summary>
    private static NodeId Target(Result<HtmlBoxes, Error> result) =>
        ((LinkTarget.NodeTarget)Groups(result).Select(group => group.Role).OfType<SemanticRole.LinkRole>().Single().Target).Value;

    /// <summary>The node of the document with the id.</summary>
    private static Node Node(Result<HtmlBoxes, Error> result, NodeId id)
    {
        var pending = new Stack<Node>([result.Value.Document.Root]);
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            if (node.Id == id)
            {
                return node;
            }

            foreach (var child in node switch { Box box => box.Children, InlineGroup group => group.Children, _ => [] })
            {
                pending.Push(child);
            }
        }

        throw new InvalidOperationException($"no node {id}");
    }

    private static string Text(Node node) => node switch
    {
        TextRun run => run.Text,
        Box box => string.Concat(box.Children.Select(Text)),
        InlineGroup group => string.Concat(group.Children.Select(Text)),
        _ => string.Empty,
    };

    [Theory]
    [InlineData("#target", "")]
    [InlineData("  #target\t", "")]
    [InlineData("#target", HttpsBase)]
    public void A_link_inside_the_document_leads_to_the_box_of_the_element_with_that_id_also_under_a_base(string href, string head)
    {
        var result = Boxes(Doc($"<p>See <a href=\"{href}\">this</a>.</p><p id=\"target\">Target</p>", head), "file:///cases/t.html");

        var target = Node(result, Target(result));

        target.Should().BeOfType<Box>().Which.Role.Should().Be(SemanticRole.Paragraph);
        Text(target).Should().Be("Target");
    }

    [Fact]
    public void The_first_element_with_the_id_wins_over_a_later_one_and_over_an_a_with_that_name_before_it()
    {
        var result = Boxes(Doc("<p>See <a href=\"#x\">this</a>.</p><p><a name=\"x\">named</a></p><p id=\"x\">first</p><p id=\"x\">second</p>"));

        Text(Node(result, Target(result))).Should().Be("first");
    }

    [Fact]
    public void An_a_with_the_name_is_the_target_when_no_element_has_that_id_as_a_span_group_around_its_content()
    {
        var result = Boxes(Doc("<p>See <a href=\"#mark\">this</a>.</p><p>Some <a name=\"mark\">marked words</a> here.</p>"));

        var target = Node(result, Target(result));

        target.Should().BeOfType<InlineGroup>().Which.Role.Should().Be(SemanticRole.Span);
        Text(target).Should().Be("marked words");
    }

    [Fact]
    public void A_link_to_an_inline_element_with_an_id_makes_a_span_group_only_for_that_element()
    {
        var result = Boxes(Doc("<p>See <a href=\"#one\">this</a>.</p><p><span id=\"one\">one</span> <span id=\"two\">two</span></p>"));

        Groups(result).Select(group => group.Role).Should().Equal(SemanticRole.Link(LinkTarget.Node(Target(result)), "this"), SemanticRole.Span);
    }

    [Theory]
    [InlineData("#caf%C3%A9", "café")]
    [InlineData("#a%20b", "a%20b")]
    [InlineData("#a%20b", "a b")]
    public void A_fragment_is_tried_as_written_and_then_percent_decoded(string href, string id)
    {
        var result = Boxes(Doc($"<p>See <a href=\"{href}\">this</a>.</p><p id=\"{id}\">Target</p>"));

        Text(Node(result, Target(result))).Should().Be("Target");
    }

    [Theory]
    [InlineData("#")]
    [InlineData("#top")]
    [InlineData("#TOP")]
    public void Hash_alone_and_top_lead_to_the_top_of_the_document_the_root_box(string href)
    {
        var result = Boxes(Doc($"<p>First</p><p>Back to <a href=\"{href}\">the top</a>.</p>"));

        Target(result).Should().Be(result.Value.Document.Root.Id);
    }

    [Fact]
    public void An_element_with_the_id_top_is_the_target_of_top()
    {
        var result = Boxes(Doc("<p>First</p><p id=\"top\">Back to <a href=\"#top\">the top</a>.</p>"));

        Text(Node(result, Target(result))).Should().Be("Back to the top.");
    }

    [Theory]
    [InlineData("<p>See <a href=\"#nowhere\">this</a>.</p>", "no element has the id 'nowhere'")]
    [InlineData("<p>See <a href=\"#Target\">this</a>.</p><p id=\"target\">t</p>", "no element has the id 'Target'")]
    [InlineData("<p>See <a href=\"#hidden\">this</a>.</p><p id=\"hidden\" style=\"display: none\">t</p>", "the element <p> it leads to makes no box")]
    [InlineData("<p>See <a href=\"#empty\">this</a>.</p><p><span id=\"empty\"></span>t</p>", "the element <span> it leads to makes no box")]
    [InlineData("<p>See <a href=\"#pic\">this</a>.</p><p><img id=\"pic\" src=\"figure.png\" alt=\"A figure\"></p>", "the element <img> it leads to makes no box")]
    public void A_link_whose_fragment_names_no_target_is_refused_at_its_start_tag(string body, string reason)
    {
        var error = Refusal(Doc(body));

        error.Code.Should().Be(HtmlStyleErrorCodes.Unsupported.Link);
        error.Message.Should().Contain(reason);
        error.Metadata![ErrorMetadataKeys.Property].Should().Be("href");
    }

    [Fact]
    public void A_link_that_leads_to_a_link_that_is_not_made_is_refused_too_and_under_Warn_both_are_drawn_as_text()
    {
        var html = Doc("<p>See <a href=\"#b\">this</a> and <a id=\"b\" href=\"#nowhere\">that</a>.</p>");

        Boxes(html).Error.Parts().Select(part => part.Message).Should().HaveCount(2).And.Contain(message => message.Contains("it leads to a link that is not made", StringComparison.Ordinal));
        var warned = Boxes(html, policy: UnsupportedPolicy.Warn);
        Groups(warned).Should().BeEmpty();
        Text(warned.Value.Document.Root).Should().Be("See this and that.");
    }

    [Theory]
    [InlineData("<p id=\"target\">Target</p>")]
    [InlineData("<table style=\"border-collapse: collapse\"><tr id=\"target\"><td>t</td></tr></table>")]
    [InlineData("<table id=\"target\" style=\"border-collapse: collapse\"><tr><td>t</td></tr></table>")]
    public void A_link_inside_the_document_and_its_target_lay_out_and_paint_one_anchor_at_the_target(string target)
    {
        var result = Boxes(Doc($"<p>See <a href=\"#target\">this</a>.</p>{target}"));
        var pages = DocumentLayout.Run(result.Value.Document, result.Value.Fonts);

        pages.IsSuccess.Should().BeTrue(pages.IsFailure ? pages.Error.ToString() : string.Empty);
        pages.Value.Pages.SelectMany(page => page.Items).OfType<Ave.Extensions.Documents.PageModel.Anchor>().Should().ContainSingle()
            .Which.Node.Should().Be(Target(result));
    }

    [Theory]
    [InlineData("https://bücher.example/", "holds a character beyond ASCII")]
    [InlineData("https:///path", "names no host")]
    [InlineData("http://[bad", "names no host")]
    public void A_host_the_model_cannot_take_is_refused(string href, string reason) =>
        Refusal(Doc($"<p>See <a href=\"{href}\">this</a>.</p>"), "file:///cases/t.html").Message.Should().Contain(reason);

    [Fact]
    public void An_href_longer_than_the_URI_limit_is_refused_before_it_is_read()
    {
        var error = Refusal(Doc($"<p>See <a href=\"https://example.com/{new string('a', LinkUris.MaxLength)}\">this</a>.</p>"));

        error.Code.Should().Be(HtmlStyleErrorCodes.Unsupported.Link);
        error.Message.Should().Contain("characters long");
    }

    [Fact]
    public void A_block_level_link_is_refused()
    {
        var error = Refusal(Doc("<a href=\"https://example.com/\" style=\"display: block\">block</a>"));

        error.Code.Should().Be(HtmlStyleErrorCodes.Unsupported.Link);
        error.Message.Should().Contain("not inside a line of text");
    }

    [Fact]
    public void A_description_with_a_character_text_may_not_hold_is_refused() =>
        Refusal(Doc("<p>See <a href=\"https://example.com/\" title=\"a&#1;b\">this</a>.</p>")).Message.Should().Contain("U+0001");

    [Fact]
    public void Under_Warn_a_refused_link_is_drawn_as_its_text_without_a_link()
    {
        var result = Boxes(Doc("<p>See <a href=\"javascript:alert(1)\">this</a>.</p>"), policy: UnsupportedPolicy.Warn);

        Groups(result).Should().BeEmpty();
        result.Value.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Code == HtmlStyleErrorCodes.Unsupported.Link);
        var boxes = result.Value;
        var plain = Boxes(Doc("<p>See <a style=\"color: #0000ee; text-decoration: underline\">this</a>.</p>")).Value;
        PrintedPages.Engine(DocumentLayout.Run(boxes.Document, boxes.Fonts).Value).Should().BeEquivalentTo(PrintedPages.Engine(DocumentLayout.Run(plain.Document, plain.Fonts).Value),
            "the text is drawn as that of an a without href in the link's style");
    }

    /// <summary>The comparison of links against Chromium's (<see cref="HtmlCases.LinkDifference"/>) on a case that is equal,
    /// with differences planted in Chromium's links: each must be found.</summary>
    [Fact]
    public void The_link_comparison_finds_a_rectangle_two_pixels_off_a_missing_rectangle_and_another_target()
    {
        var boxes = HtmlCases.Boxes("lk-two-lines").Value;
        var pages = DocumentLayout.Run(boxes.Document, boxes.Fonts).Value;
        var chromium = HtmlCases.Links(HtmlCases.Cases, "lk-two-lines");
        chromium.Should().HaveCount(2);

        HtmlCases.LinkDifference(pages, chromium).Should().BeNull();
        HtmlCases.LinkDifference(pages, [chromium[0] with { X0 = chromium[0].X0 + 1.5 }, chromium[1]]).Should().Contain("link 1 on page 1: x0");
        HtmlCases.LinkDifference(pages, [chromium[0], chromium[1] with { Y1 = chromium[1].Y1 - 1.5 }]).Should().Contain("link 2 on page 1: y1");
        HtmlCases.LinkDifference(pages, [chromium[0]]).Should().Contain("link 2: the engine's rectangle on page 1").And.Contain("is not in Chromium's print");
        HtmlCases.LinkDifference(pages, [.. chromium, chromium[1]]).Should().Contain("link 3: Chromium's rectangle on page 1");
        HtmlCases.LinkDifference(pages, [chromium[0], chromium[1] with { Target = "uri https://example.com/other" }]).Should().Contain("leads to uri https://example.com/two, Chromium's to uri https://example.com/other");
        HtmlCases.LinkDifference(pages, [chromium[0], chromium[1] with { Page = 2 }]).Should().Contain("link 2 is on page 1, Chromium's on page 2");
    }

    /// <summary>The link cases through the HTML path into a tagged PDF (PDF/A-3a and PDF/UA-1 together, each case with a
    /// title and a language added, which both profiles need): veraPDF passes both.</summary>
    [DockerTheory]
    [Trait("Category", "Reference")]
    [InlineData("3a")]
    [InlineData("ua1")]
    public void The_link_cases_as_tagged_PDF_pass_veraPDF(string flavour)
    {
        string[] names = ["lk-one-line", "lk-two-lines", "lk-larger-run", "lk-image", "lk-page-break", "lk-mail-tel", "lk-header-row", "lk-fragment"];
        var folder = Directory.CreateTempSubdirectory("ave-extensions-documents-html-links-").FullName;
        try
        {
            foreach (var name in names)
            {
                var path = Path.Combine(HtmlCases.Folder(), name + ".html");
                var html = File.ReadAllText(path).Replace("<html>", "<html lang=\"en\">", StringComparison.Ordinal).Replace("<head>", "<head><title>Links</title>", StringComparison.Ordinal);
                var baseUrl = new System.Uri(path).AbsoluteUri;
                var pdf = HtmlParser.Parse(html, new HtmlParseOptions(name + ".html"))
                    .OnSuccessBind(parsed => HtmlStyleResolver.Resolve(parsed, new HtmlStyleOptions { Page = HtmlCases.Letter, Resources = HtmlCases.Resource, BaseUrl = baseUrl }))
                    .OnSuccessBind(styled => HtmlBoxGenerator.Generate(styled, new HtmlBoxOptions { Resources = HtmlCases.Resource, BaseUrl = baseUrl }))
                    .OnSuccessBind(boxes => DocumentLayout.Run(boxes.Document, boxes.Fonts))
                    .OnSuccessBind(pages => pages.ToPdf(new PdfRenderOptions(new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero)) { Profile = PdfProfile.PdfA3a.AndPdfUa1() }));
                pdf.IsSuccess.Should().BeTrue(pdf.IsFailure ? $"{name}: {pdf.Error}" : string.Empty);
                File.WriteAllBytes(Path.Combine(folder, name + ".pdf"), [.. pdf.Value]);
            }

            var results = VeraPdf.Validate(folder, flavour);

            results.Should().HaveCount(names.Length);
            results.Should().OnlyContain(result => result.IsCompliant && result.PassedChecks > 0, VeraPdf.Describe(results));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
