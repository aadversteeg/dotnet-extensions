using System.Text;
using Ave.Extensions.Documents;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Html;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.Render.Pdf;
using Ave.Extensions.Documents.Render.Raster;
using Ave.Extensions.Documents.TestSupport;

namespace UnitTests.Extensions.Documents.Html.Boxes;

/// <summary>The HTML entry of the composition package (<see cref="HtmlRendering"/>).</summary>
public class HtmlRenderingTests
{
    private const string Html =
        "<!doctype html><html lang=\"en\"><head><title>T</title><style>@font-face { font-family: Body; src: url(lato.ttf) }" +
        " @page { size: 300pt 174pt; margin: 12pt } body { font-family: Body; margin: 0 }</style></head><body><p>Hello</p></body></html>";

    private static readonly HtmlRenderOptions Options = new()
    {
        Style = HtmlStyleOptions.Default with { Resources = url => url == "lato.ttf" ? new HtmlResource(TestFonts.Bytes("Lato-Regular.ttf")) : null },
        SourceName = "hello.html",
    };

    [Fact]
    public void An_HTML_document_becomes_pages_a_PDF_and_PNG_files()
    {
        var pages = HtmlRendering.ToPages(Html, Options);
        pages.IsSuccess.Should().BeTrue(pages.IsFailure ? pages.Error.ToString() : string.Empty);
        PrintedPages.Engine(pages.Value.Output).PageTexts.Should().Equal("Hello");
        pages.Value.Diagnostics.Should().BeEmpty();

        var pdf = HtmlRendering.ToPdf(Html, Options, new PdfRenderOptions(new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero)));
        Encoding.ASCII.GetString([.. pdf.Value.Output.Take(5)]).Should().Be("%PDF-");
        HtmlRendering.ToPng(Html, Options, 1, new RasterOptions(72)).Value.Output.Take(4).Should().Equal(0x89, (byte)'P', (byte)'N', (byte)'G');
        HtmlRendering.ToPngPages(Html, Options, new RasterOptions(72)).Value.Output.Should().ContainSingle();
        HtmlRendering.ToImages(Html, Options, new RasterOptions(72)).Value.Output.Single().Width.Should().Be(300);
    }

    [Theory]
    // No option: A4 with no margins (210 x 297 mm print as 594.96 x 841.92 pt), portrait.
    [InlineData("", false, 594.96, 841.92, 0)]
    [InlineData("@page { size: auto }", false, 594.96, 841.92, 0)]
    // Media queries see the page without rules (here A4), not the page the rules make, as Chromium does (measured, cases
    // h-media-page-size-*).
    [InlineData("@page { size: landscape }", false, 841.92, 594.96, 0)]
    // The caller's page, 300 x 198 pt with 12 pt margins: its size, its margins, and media queries on its page area
    // (276 x 174 pt).
    [InlineData("", true, 300, 198, 12 + 30)]
    [InlineData("@page { size: auto; margin: 0 }", true, 300, 198, 30)]
    [InlineData("@page { size: portrait }", true, 198, 300, 12 + 30)]
    [InlineData("@page { size: 96pt 150pt }", true, 96, 150, 12 + 30)]
    public void Without_a_page_rule_the_page_is_the_caller_s_and_media_queries_see_it(string pageRule, bool callersPage, double width, double height, double firstWordX)
    {
        var html = "<!doctype html><html><head><style>@font-face { font-family: Body; src: url(lato.ttf) } body { font-family: Body; margin: 0 }" +
                   $" @media (max-height: 200pt) {{ p {{ margin-left: 30pt }} }} {pageRule}</style></head><body><p>Hello</p></body></html>";
        var options = callersPage
            ? Options with { Style = Options.Style with { Page = new(300.Pt(), 198.Pt(), new Edges(12.Pt(), 12.Pt(), 12.Pt(), 12.Pt())) } }
            : Options;

        var page = PrintedPages.Engine(HtmlRendering.ToPages(html, options).Value.Output).Pages.Single();

        page.Width.Should().BeApproximately(width, 1e-6);
        page.Height.Should().BeApproximately(height, 1e-6);
        page.Words.Single().X0.Should().BeApproximately(firstWordX, 1e-6);
    }

    [Fact]
    public void Under_warn_every_entry_returns_its_output_with_a_diagnostic_for_everything_left_out_or_changed()
    {
        // What is left out or changed: the image, the quotation (its words), a tab where spaces are preserved, the table's
        // border spacing of a fraction of a pixel (drawn without), visibility: hidden (drawn), the float, the decorated list item's marker (drawn without its line).
        const string Body = "<p>before</p><p>keep <img src=\"x.png\" alt=\"ALT\"> this <q>quoted words</q> and <span style=\"white-space:pre\">a\ttab</span></p>" +
            "<table style=\"border-spacing:1pt\"><tr><td>cell one</td><td>cell two</td></tr></table><p style=\"visibility:hidden\">secret</p><p style=\"float:right\">floated</p><ul><li style=\"text-decoration: underline\">item</li></ul>";
        var html = Html.Replace("<p>Hello</p>", Body, StringComparison.Ordinal);
        var warn = Options with { Style = Options.Style with { Unsupported = UnsupportedPolicy.Warn } };
        HtmlRendering.ToPages(html, Options).IsFailure.Should().BeTrue("the same document is refused by default");

        var pages = HtmlRendering.ToPages(html, warn);

        pages.IsSuccess.Should().BeTrue(pages.IsFailure ? pages.Error.ToString() : string.Empty);
        var values = pages.Value.Diagnostics.Select(error => error.Metadata is { } metadata && metadata.TryGetValue(ErrorMetadataKeys.Value, out var value) ? value as string : null).ToList();
        values.Should().Contain(["img", "q", "preserve", "hidden", "right", "1pt 1pt", "text-decoration"]);
        pages.Value.Diagnostics.Should().OnlyContain(error => error.Metadata != null && error.Metadata.ContainsKey(ErrorMetadataKeys.SourceLine));
        var pdf = HtmlRendering.ToPdf(html, warn, new PdfRenderOptions(new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero)));
        pdf.Value.Diagnostics.Should().Equal(pages.Value.Diagnostics);
        HtmlRendering.ToPng(html, warn, 1, new RasterOptions(72)).Value.Diagnostics.Should().Equal(pages.Value.Diagnostics);
        HtmlRendering.ToPngPages(html, warn, new RasterOptions(72)).Value.Diagnostics.Should().Equal(pages.Value.Diagnostics);
        HtmlRendering.ToImages(html, warn, new RasterOptions(72)).Value.Diagnostics.Should().Equal(pages.Value.Diagnostics);
    }

    [Fact]
    public void What_the_front_end_does_not_draw_is_refused_with_the_document_s_name_line_and_column()
    {
        var refused = HtmlRendering.ToPages(Html.Replace("<p>Hello</p>", "<p>Hello\n<wbr></p>", StringComparison.Ordinal), Options);
        refused.IsFailure.Should().BeTrue();
        var error = refused.Error.Parts().Single();
        (error.Metadata![ErrorMetadataKeys.SourceFile], error.Metadata[ErrorMetadataKeys.SourceLine], error.Metadata[ErrorMetadataKeys.SourceColumn])
            .Should().Be(("hello.html", 2, 1));
    }

    private static string Pages(int count) => Html.Replace("<p>Hello</p>", string.Concat(Enumerable.Repeat("<p style=\"break-after: page\">x</p>", count - 1)) + "<p>x</p>", StringComparison.Ordinal);

    [Fact]
    public void A_document_of_more_pages_than_the_render_call_allows_is_refused_when_pagination_reaches_the_limit()
    {
        HtmlRenderOptions.Default.MaxPages.Should().Be(LayoutEnvironment.DefaultMaxPages);
        HtmlRendering.ToPages(Pages(3), Options with { MaxPages = 3 }).Value.Output.Pages.Should().HaveCount(3);

        var refused = HtmlRendering.ToPages(Pages(4), Options with { MaxPages = 3 });

        refused.Error.Code.Should().Be(LayoutErrorCodes.TooManyPages);
        refused.Error.Message.Should().Contain("more than 3 pages").And.Contain("HtmlRenderOptions.MaxPages").And.NotContain("LayoutEnvironment");
    }

    /// <summary>What every entry gives for a document of <paramref name="pages"/> pages under a page limit: null when it
    /// renders, else the error.</summary>
    private static IEnumerable<(string Entry, Ave.Extensions.ErrorPaths.Error? Error)> EveryEntry(int pages, int maxPages)
    {
        var (html, options, raster) = (Pages(pages), Options with { MaxPages = maxPages }, new RasterOptions(9));
        static Ave.Extensions.ErrorPaths.Error? ErrorOf<T>(Ave.Extensions.Functional.Result<T, Ave.Extensions.ErrorPaths.Error> result) => result.IsSuccess ? null : result.Error;
        yield return ("ToPages", ErrorOf(HtmlRendering.ToPages(html, options)));
        yield return ("ToPdf", ErrorOf(HtmlRendering.ToPdf(html, options, new PdfRenderOptions(new DateTimeOffset(2026, 11, 28, 12, 0, 0, TimeSpan.Zero)))));
        yield return ("ToPng", ErrorOf(HtmlRendering.ToPng(html, options, 1, raster)));
        yield return ("ToPngPages", ErrorOf(HtmlRendering.ToPngPages(html, options, raster)));
        yield return ("ToImages", ErrorOf(HtmlRendering.ToImages(html, options, raster)));
    }

    [Fact]
    public void Every_entry_renders_up_to_its_page_limit_and_refuses_one_page_past_it_naming_its_option()
    {
        EveryEntry(3, 3).Should().OnlyContain(entry => entry.Error == null, "just inside the limit");
        EveryEntry(3, 4).Should().OnlyContain(entry => entry.Error == null, "the limit raised past the document");

        foreach (var (entry, error) in EveryEntry(4, 3))
        {
            error!.Value.Code.Should().Be(LayoutErrorCodes.TooManyPages, entry);
            error.Value.Message.Should().Contain("more than 3 pages", entry).And.Contain("HtmlRenderOptions.MaxPages (default 10000)", entry);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-7)]
    public void A_page_limit_below_one_is_refused_by_every_entry_with_the_option_and_its_value(int maxPages)
    {
        foreach (var (entry, error) in EveryEntry(1, maxPages))
        {
            error!.Value.Code.Should().Be(LayoutErrorCodes.InvalidOptions, entry);
            error.Value.Message.Should().Be($"HtmlRenderOptions.MaxPages is {maxPages}; a document has at least one page, so the page limit must be at least 1.", entry);
            error.Value.Metadata![ErrorMetadataKeys.Property].Should().Be("HtmlRenderOptions.MaxPages");
            error.Value.Metadata[ErrorMetadataKeys.Value].Should().Be(maxPages.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    [Fact]
    public void The_raster_refusal_of_an_image_names_the_image_its_place_and_why_the_PDF_renders()
    {
        // A JPEG whose header claims 16000 x 16000 pixels: a PDF embeds the file as it is; the raster output would decode
        // it to more pixels than one document may have.
        var jpeg = TestImages.Bytes("jpeg-rgb.jpg");
        var frame = Enumerable.Range(0, jpeg.Length - 1).First(index => jpeg[index] == 0xFF && jpeg[index + 1] == 0xC0);
        jpeg[frame + 5] = jpeg[frame + 7] = 16000 >> 8;
        jpeg[frame + 6] = jpeg[frame + 8] = 16000 & 0xFF;
        var options = Options with { Style = Options.Style with { Resources = url => url == "lato.ttf" ? new HtmlResource(TestFonts.Bytes("Lato-Regular.ttf")) : url == "big.jpg" ? new HtmlResource(jpeg) : null } };
        var html = Html.Replace("<p>Hello</p>", "<p>Hello</p>\n<p><img alt=x width=100 src=big.jpg></p>", StringComparison.Ordinal);

        HtmlRendering.ToPdf(html, options, new PdfRenderOptions(new DateTimeOffset(2026, 11, 28, 12, 0, 0, TimeSpan.Zero))).IsSuccess.Should().BeTrue();
        foreach (var refused in new[] { HtmlRendering.ToPng(html, options, 1, new RasterOptions(72)).Error, HtmlRendering.ToImages(html, options, new RasterOptions(72)).Error })
        {
            refused.Code.Should().Be(Ave.Extensions.Documents.Images.ImageErrorCodes.TooLarge);
            refused.Message.Should().Be("The image 'big.jpg' at hello.html:2:4 (jpeg 16000x16000 rgb) is not drawn: the raster output decodes each image to pixels, where a PDF embeds a JPEG file as it is. "
                + "The image is 16000x16000 = 256000000 pixels; the images of one document decode to at most 134217728 pixels together.");
            (refused.Metadata![ErrorMetadataKeys.SourceLine], refused.Metadata[ErrorMetadataKeys.SourceColumn]).Should().Be((2, 4));
        }
    }

    [Fact]
    public void The_raster_refusal_of_an_image_used_in_several_places_names_its_first_place()
    {
        // One file under three URLs: in a paragraph, in a table cell, in a list item. The refusal names the paragraph.
        var jpeg = TestImages.Bytes("jpeg-rgb.jpg");
        var frame = Enumerable.Range(0, jpeg.Length - 1).First(index => jpeg[index] == 0xFF && jpeg[index + 1] == 0xC0);
        jpeg[frame + 5] = jpeg[frame + 7] = 16000 >> 8;
        jpeg[frame + 6] = jpeg[frame + 8] = 16000 & 0xFF;
        var options = Options with { Style = Options.Style with { Resources = url => url == "lato.ttf" ? new HtmlResource(TestFonts.Bytes("Lato-Regular.ttf")) : url.EndsWith("big.jpg", StringComparison.Ordinal) ? new HtmlResource(jpeg) : null } };
        var html = Html.Replace("<p>Hello</p>", "<p>Hello</p>\n<p><img alt=x width=100 src=big.jpg></p>\n<table><tr><td><img alt=x width=100 src=x/big.jpg></td></tr></table>\n<ul><li><img alt=x width=100 src=y/big.jpg></li></ul>", StringComparison.Ordinal);

        var refused = HtmlRendering.ToPng(html, options, 1, new RasterOptions(72)).Error;

        refused.Code.Should().Be(Ave.Extensions.Documents.Images.ImageErrorCodes.TooLarge);
        refused.Message.Should().StartWith("The image 'big.jpg' at hello.html:2:4 (jpeg 16000x16000 rgb)");
    }

    [Fact]
    public void A_fragment_is_not_part_of_what_the_resolver_is_asked()
    {
        var asked = new List<string>();
        var png = TestImages.Bytes("rgb8.png");
        var options = Options with { Style = Options.Style with { Resources = url => { asked.Add(url); return url == "lato.ttf" ? new HtmlResource(TestFonts.Bytes("Lato-Regular.ttf")) : url == "g.png" ? new HtmlResource(png) : url == "a.css" ? new HtmlResource("p { margin: 0 }"u8.ToArray()) : null; } } };
        var html = Html.Replace("<p>Hello</p>", "<p>" + string.Concat(Enumerable.Range(0, 9).Select(image => $"<img alt=x width=10 src=\"g.png#{image}\">")) + "</p>", StringComparison.Ordinal)
            .Replace("<title>T</title>", "<title>T</title><link rel=stylesheet href=\"a.css#one\"><link rel=stylesheet href=\"a.css#two\">", StringComparison.Ordinal);

        HtmlRendering.ToPages(html, options).IsSuccess.Should().BeTrue();

        asked.Should().BeEquivalentTo(["lato.ttf", "a.css", "g.png"], "one ask for each file, without the fragments of its references");
    }

    [Fact]
    public void A_reference_that_is_only_a_fragment_is_not_there_without_a_base_and_is_the_base_with_one()
    {
        var asked = new List<string>();
        var png = TestImages.Bytes("rgb8.png");
        HtmlResource? Resolve(string url)
        {
            asked.Add(url);
            return url == "lato.ttf" || url == "https://e.test/lato.ttf" ? new HtmlResource(TestFonts.Bytes("Lato-Regular.ttf")) : null;
        }

        var html = Html.Replace("<p>Hello</p>", "<p><img alt=x width=10 src=\"#only\"><img alt=x width=10 src=\"#\"></p>", StringComparison.Ordinal);
        var options = Options with { Style = Options.Style with { Resources = Resolve, Unsupported = UnsupportedPolicy.Warn } };

        var rendered = HtmlRendering.ToPages(html, options);

        rendered.Value.Diagnostics.Where(error => error.Message.Contains("of <img> is not there", StringComparison.Ordinal)).Should().HaveCount(2);
        asked.Should().Equal(["lato.ttf"], "the empty reference is never handed to the resolver");

        asked.Clear();
        HtmlRendering.ToPages(html, options with { Style = options.Style with { BaseUrl = "https://e.test/doc.html" } }).IsSuccess.Should().BeTrue();
        asked.Should().BeEquivalentTo(["https://e.test/lato.ttf", "https://e.test/doc.html"], "with a base the reference is the document itself, asked once");
    }

    [Fact]
    public void A_data_URL_with_a_fragment_is_drawn_as_without_it_and_no_data_URL_reaches_the_resolver()
    {
        // Chromium reads a data: URL without its fragment (Fetch §4.5), so data:…#x shows the image.
        var asked = new List<string>();
        var data = "data:image/png;base64," + Convert.ToBase64String(TestImages.Bytes("rgb8.png"));
        var options = Options with { Style = Options.Style with { Resources = url => { asked.Add(url); return url == "lato.ttf" ? new HtmlResource(TestFonts.Bytes("Lato-Regular.ttf")) : null; } } };
        string Page(string images) => Html.Replace("<p>Hello</p>", $"<p>{images}</p>", StringComparison.Ordinal);
        var raster = new RasterOptions(72);

        var plain = HtmlRendering.ToPng(Page($"<img alt=x width=10 src=\"{data}\"><img alt=x width=10 src=\"{data}\">"), options, 1, raster);
        var withFragments = HtmlRendering.ToPng(Page($"<img alt=x width=10 src=\"{data}#x\"><img alt=x width=10 src=\"{data}#y\">"), options, 1, raster);

        withFragments.IsSuccess.Should().BeTrue(withFragments.IsFailure ? withFragments.Error.ToString() : string.Empty);
        withFragments.Value.Output.Should().Equal(plain.Value.Output);
        asked.Should().OnlyContain(url => url == "lato.ttf");

        // A data: URL whose body does not decode is not there; it is not handed to the resolver either.
        var broken = HtmlRendering.ToPages(Page("<img alt=x width=10 src=\"data:image/png;base64,%%%\">"), options with { Style = options.Style with { Unsupported = UnsupportedPolicy.Warn } });
        broken.Value.Diagnostics.Should().ContainSingle(error => error.Message.Contains("of <img> is not there", StringComparison.Ordinal));
        asked.Should().OnlyContain(url => url == "lato.ttf");
    }

    [Fact]
    public void The_summary_of_left_out_diagnostics_quotes_the_first_refusal_whichever_stage_cuts()
    {
        // 10 000 unsupported properties and then one more refusal: in the style stage (a property) and in box generation
        // (an element), the summary has the same shape.
        var options = Options with { Style = Options.Style with { Unsupported = UnsupportedPolicy.Warn } };
        var many = string.Concat(Enumerable.Repeat("<p style=\"foo:bar\">x</p>", 10_000));
        foreach (var (last, first) in new[] { ("<p style=\"grid-template-columns:1fr 2fr\">y</p>", "The property 'grid-template-columns' is not supported."), ("<input type=text value=v>", "The property 'foo' is not supported.") })
        {
            var rendered = HtmlRendering.ToPages(Html.Replace("<p>Hello</p>", many + last, StringComparison.Ordinal), options).Value;

            rendered.Diagnostics[^1].Message.Should().Be($"1 more diagnostics are left out, 1 of them refusals (the first: {first}); 10000 are kept.");
        }
    }

    [Fact]
    public void The_page_limit_stops_a_document_of_many_pages_before_it_lays_them_out()
    {
        // 20 000 forced breaks would be 20 000 pages; with a limit of 10 the work ends at page 11.
        var refused = HtmlRendering.ToPages(Pages(20_000), Options with { MaxPages = 10 });

        refused.Error.Code.Should().Be(LayoutErrorCodes.TooManyPages);
    }

    [Fact]
    public void The_resolver_is_asked_at_most_once_per_URL_in_one_render()
    {
        // The same font file, style sheet and image named many times; style resolution asks for a sized image too (to
        // know whether its alt text would be shown), and box generation asks for it again for its bytes.
        var asked = new List<string>();
        var options = Options with
        {
            Style = Options.Style with
            {
                Resources = url =>
                {
                    asked.Add(url);
                    return url == "lato.ttf" ? new HtmlResource(TestFonts.Bytes("Lato-Regular.ttf")) : url == "a.css" ? new HtmlResource("p { margin: 0 }"u8.ToArray()) : null;
                },
            },
        };
        var html = Html.Replace("<p>Hello</p>", string.Concat(Enumerable.Repeat("<p>x<img alt=x width=10 height=10 src=one.png></p>", 50)), StringComparison.Ordinal)
            .Replace("<title>T</title>", "<title>T</title><link rel=stylesheet href=a.css><link rel=stylesheet href=a.css><style>@import \"a.css\";</style>", StringComparison.Ordinal);

        HtmlRendering.ToPages(html, options with { Style = options.Style with { Unsupported = UnsupportedPolicy.Warn } });

        asked.Should().OnlyHaveUniqueItems().And.Contain(["lato.ttf", "a.css", "one.png"]);
    }

    [Fact]
    public void A_URL_past_the_length_or_the_number_of_resources_of_a_document_is_not_asked()
    {
        var asked = new List<string>();
        var options = Options with { Style = Options.Style with { Unsupported = UnsupportedPolicy.Warn, Resources = url => { asked.Add(url); return url == "lato.ttf" ? new HtmlResource(TestFonts.Bytes("Lato-Regular.ttf")) : null; } } };
        string Images(int count, int length) => string.Concat(Enumerable.Range(0, count).Select(image => $"<img alt=x src=\"{image.ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(length, '0')}\">"));

        var many = HtmlRendering.ToPages(Html.Replace("<p>Hello</p>", $"<p>{Images(4_096, 4)}</p>", StringComparison.Ordinal), options);
        asked.Should().HaveCount(4_096, "the font file and 4 095 images");
        many.Value.Diagnostics.Should().ContainSingle(error => error.Message.Contains("4096 distinct resources", StringComparison.Ordinal));

        asked.Clear();
        HtmlRendering.ToPages(Html.Replace("<p>Hello</p>", $"<p>{Images(1, 32_767)}{Images(1, 32_768)}</p>", StringComparison.Ordinal), options)
            .Value.Diagnostics.Should().ContainSingle(error => error.Message.Contains("longer than the 32767 characters", StringComparison.Ordinal));
        asked.Select(url => url.Length).Should().BeEquivalentTo([8, 32_767]);
    }
}
