using System.Collections.Immutable;
using Ave.Extensions.Documents;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Html;
using Ave.Extensions.Documents.Html.Boxes;
using Ave.Extensions.Documents.Html.Parsing;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace UnitTests.Extensions.Documents.Html.Boxes;

/// <summary>
/// What the resource resolver is asked, with the default options, for a reference in every place that names a resource:
/// an <c>img</c>, a <c>link</c>, <c>@import</c> with a string and with <c>url()</c>, an <c>@font-face</c> source and an
/// image in a page-margin box. One rule for all of them: with a base URL only an absolute URL of an allowed scheme, without
/// one also a reference without a scheme and without a host, as written.
/// </summary>
public class ResourceUrlTests
{
    private const string Https = "https://e.test/d/doc.html";

    /// <summary>The places, each as the document text around a reference written as the place needs it.</summary>
    public static readonly TheoryData<string> Places = ["img", "link", "import-string", "import-url", "font-face", "margin-box"];

    public static TheoryData<string, string, string?, string> Rows()
    {
        var rows = new TheoryData<string, string, string?, string>();
        foreach (var place in Places)
        {
            // With an https base: a reference that names a host it cannot resolve, a device path, a backslash, three
            // slashes, or a scheme of its own, asks nothing.
            foreach (var reference in new[]
            {
                @"\\?\UNC\host\share\x.png", @"\\?\C:\x.png", @"\\.\pipe\x.png", "//?/C:/x.png", "///etc/passwd.png", "////host/x.png",
                "//host:99999/x.png", "//ho st/x.png", @"\\host\share\x.png", @"/\host/x.png", @"\/host/x.png", @"\\\\host\x.png",
                "\u00a0//host/x.png", "x:", "x:y.png", @"C:\x.png", "c:/x.png", "file:///etc/passwd.png", "images\\x.png",
            })
            {
                rows.Add(place, reference, Https, string.Empty);
            }

            rows.Add(place, "//host/x.png", Https, "https://host/x.png");
            rows.Add(place, "a.png", Https, "https://e.test/d/a.png");
            rows.Add(place, "/images/x.png", Https, "https://e.test/images/x.png");
            rows.Add(place, "a.png", "file:///d/doc.html", string.Empty);

            // Without a base: a reference that names a host is refused, also past white space .NET trims; a path is handed
            // on as written.
            foreach (var reference in new[]
            {
                @"\\host\share\x.png", "//host/x.png", @"/\host/x.png", @"\/host/x.png", "////host/x.png", "///etc/passwd.png",
                @"\\?\UNC\host\share\x.png", "\u00a0\\\\host\\share\\x.png", "\u3000//host/x.png", "\u2028//host/x.png", "x:", @"C:\x.png",
            })
            {
                rows.Add(place, reference, null, string.Empty);
            }

            rows.Add(place, "/images/x.png", null, "/images/x.png");
            rows.Add(place, "../x.png", null, "../x.png");
            rows.Add(place, "https://e.test/a.png", null, "https://e.test/a.png");
        }

        return rows;
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void Every_place_that_names_a_resource_asks_the_resolver_only_what_the_rule_allows(string place, string reference, string? baseUrl, string expected)
    {
        Asked(place, reference, baseUrl).Should().Equal(expected.Length == 0 ? [] : [expected]);
    }

    [Theory]
    [InlineData(@"\\host\share\x.png", null, "names a host (it starts with two slashes or backslashes), which cannot be resolved without a base URL, so the resolver is not asked: give a BaseUrl, or name the resource with a path")]
    [InlineData("\u00a0//host/x.png", null, "names a host (it starts with two slashes or backslashes)")]
    [InlineData(@"/\host/x.png", Https, "has a backslash before its query or fragment, which a browser reads as a slash and the engine does not, so the resolver is not asked: write a slash")]
    [InlineData("///etc/passwd.png", Https, "starts with three or more slashes, so it names no host, and the resolver is not asked")]
    [InlineData("\u00a0//host/x.png", Https, "has white space or a control character before the two slashes that name a host")]
    [InlineData("//host:99999/x.png", Https, "cannot be resolved against the base URL 'https://e.test/d/doc.html', so it is not there")]
    [InlineData("x:y.png", Https, "has the scheme 'x', which the caller does not allow for resources")]
    public void A_refused_reference_says_why_and_where(string reference, string? baseUrl, string why)
    {
        foreach (var place in Places)
        {
            var errors = Render(place, reference, baseUrl, UnsupportedPolicy.Refuse, []).Error.Parts().ToList();

            errors.Should().Contain(error => error.Message.Contains(why, StringComparison.Ordinal), place);
            errors.Where(error => error.Message.Contains(why, StringComparison.Ordinal)).Should().OnlyContain(error => error.Metadata != null && error.Metadata.ContainsKey(ErrorMetadataKeys.SourceLine), place);
        }
    }

    [Fact]
    public void A_link_and_a_base_element_are_cleaned_as_every_other_URL()
    {
        // The URL Standard trims C0 controls and space only (§4.4); a no-break space is part of the URL.
        Asked("link", "https://e.test/x.css\u00a0", null).Should().Equal("https://e.test/x.css%C2%A0");
        Asked("img", "https://e.test/x.png\u00a0", null).Should().Equal("https://e.test/x.png%C2%A0");

        var asked = new List<string>();
        var html = "<!doctype html><html lang=\"en\"><head><title>T</title><base href=\"\u00a0https://e.test/b/\"><style>@font-face { font-family: Body; src: url(lato.ttf) } body { font-family: Body }</style></head><body><p>x<img alt=x width=10 src=\"a.png\"></p></body></html>";
        HtmlRendering.ToPages(html, new HtmlRenderOptions { Style = HtmlStyleOptions.Default with { AllowBaseElement = true, Unsupported = UnsupportedPolicy.Warn, Resources = Recorder(asked) } });

        asked.Distinct().Should().Equal("a.png");
    }

    [Theory]
    [InlineData(@"\\host\share\", Https, "has a backslash before its query or fragment", "https://e.test/d/a.png")]
    [InlineData("//host/d/", null, "names a host", "a.png")]
    public void A_base_element_the_rule_refuses_is_reported_and_the_callers_base_URL_is_used(string href, string? baseUrl, string why, string expected)
    {
        var asked = new List<string>();
        var html = $"<!doctype html><html lang=\"en\"><head><title>T</title><base href=\"{href}\"><style>@font-face {{ font-family: Body; src: url(lato.ttf) }} body {{ font-family: Body }}</style></head><body><p>x<img alt=x width=10 src=\"a.png\"></p></body></html>";
        var style = HtmlStyleOptions.Default with { AllowBaseElement = true, BaseUrl = baseUrl, Unsupported = UnsupportedPolicy.Warn, Resources = Recorder(asked) };
    /// <summary>What the resolver is asked for one reference in one place, the body font aside, under Warn.</summary>
    private static List<string> Asked(string place, string reference, string? baseUrl)
    {
        var asked = new List<string>();
        Render(place, reference, baseUrl, UnsupportedPolicy.Warn, asked);
        return [.. asked.Distinct()];
    }

    private static Result<HtmlRendered<PageDocument>, Error> Render(string place, string reference, string? baseUrl, UnsupportedPolicy policy, List<string> asked)
    {
        var css = reference.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
        var attribute = reference.Replace("&", "&amp;", StringComparison.Ordinal).Replace("\"", "&quot;", StringComparison.Ordinal);
        var (head, body) = place switch
        {
            "img" => (string.Empty, $"<img alt=x width=10 src=\"{attribute}\">"),
            "link" => ($"<link rel=stylesheet href=\"{attribute}\">", string.Empty),
            "import-string" => ($"<style>@import \"{css}\";</style>", string.Empty),
            "import-url" => ($"<style>@import url(\"{css}\");</style>", string.Empty),
            "font-face" => ($"<style>@font-face {{ font-family: F; src: url(\"{css}\") }} .f {{ font-family: F }}</style>", "<span class=f>y</span>"),
            "margin-box" => ($"<style>@page {{ font-family: Body; @top-center {{ content: url(\"{css}\") }} }}</style>", string.Empty),
            _ => throw new ArgumentOutOfRangeException(nameof(place)),
        };
        var html = "<!doctype html><html lang=\"en\"><head><title>T</title>" + head + "<style>@font-face { font-family: Body; src: url(lato.ttf) }" +
            " @page { size: 300pt 174pt; margin: 24pt } body { font-family: Body; margin: 0 }</style></head><body><p>x" + body + "</p></body></html>";
        var style = HtmlStyleOptions.Default with { BaseUrl = baseUrl, Unsupported = policy, Resources = Recorder(asked) };
        return HtmlRendering.ToPages(html, new HtmlRenderOptions { Style = style, SourceName = "doc.html" });
    }

    private static ResourceResolver Recorder(List<string> asked) => url =>
    {
        if (url.EndsWith("lato.ttf", StringComparison.Ordinal))
        {
            return new HtmlResource(TestFonts.Bytes("Lato-Regular.ttf"));
        }

        asked.Add(url);
        return null;
    };
}
