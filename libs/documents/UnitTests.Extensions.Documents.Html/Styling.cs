using System.IO;
using System.Runtime.CompilerServices;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css.Cascade;
using Ave.Extensions.Documents.Css.Media;
using Ave.Extensions.Documents.Html;
using Ave.Extensions.Documents.Html.Parsing;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace UnitTests.Extensions.Documents.Html;

/// <summary>Parsing and styling documents for the tests.</summary>
internal static class Styling
{
    /// <summary>Warnings only, so a test sees the values and the refusals together.</summary>
    public static HtmlStyleOptions Warn { get; } = new() { Unsupported = UnsupportedPolicy.Warn };

    /// <summary>The page Chromium measured the corpus on: a viewport of 794 × 1123 CSS px, print media.</summary>
    public static HtmlStyleOptions MeasuredPage { get; } = Warn with
    {
        Media = new MediaContext("print", Length.FromPoints(794 * 0.75), Length.FromPoints(1123 * 0.75)),
        Values = new ValueContext(Length.FromPoints(794 * 0.75), Length.FromPoints(1123 * 0.75)),
    };

    public static HtmlParseResult Parsed(string html, string name = "doc.html") =>
        HtmlParser.Parse(html, new HtmlParseOptions(name)).Match(result => result, error => throw new InvalidOperationException(error.ToString()));

    public static Result<StyledHtmlDocument, Error> Resolve(string html, HtmlStyleOptions? options = null) =>
        HtmlStyleResolver.Resolve(Parsed(html), options ?? Warn);

    public static StyledHtmlDocument Styled(string html, HtmlStyleOptions? options = null) =>
        Resolve(html, options).Match(styled => styled, error => throw new InvalidOperationException(error.ToString()));

    public static Error Refused(string html, HtmlStyleOptions? options = null) =>
        Resolve(html, (options ?? Warn) with { Unsupported = UnsupportedPolicy.Refuse }).Match(styled => throw new InvalidOperationException("Expected a refusal."), error => error);

    /// <summary>Every styled element in tree order.</summary>
    public static IEnumerable<StyledElement> Elements(this StyledElement root)
    {
        yield return root;
        foreach (var child in root.Children.OfType<StyledElement>().SelectMany(Elements))
        {
            yield return child;
        }
    }

    public static StyledElement ById(this StyledHtmlDocument document, string id) =>
        document.Root.Elements().First(element => element.Element.Attribute("id") == id);

    public static StyledElement ByName(this StyledHtmlDocument document, string name) =>
        document.Root.Elements().First(element => element.Element.LocalName == name);

    /// <summary>A computed value as Chromium's getComputedStyle writes it.</summary>
    public static string Value(this StyledElement element, string property) => ComputedText.Of(property, element.Style);

    /// <summary>The folder of the computed-style reference.</summary>
    public static string Reference([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "tools", "reference", "chromium", "computed-style"));
}
