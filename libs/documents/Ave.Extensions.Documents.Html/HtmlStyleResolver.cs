using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css;
using Ave.Extensions.Documents.Css.Cascade;
using Ave.Extensions.Documents.Css.Media;
using Ave.Extensions.Documents.Css.Rules;
using Ave.Extensions.Documents.Css.Syntax;
using Ave.Extensions.Documents.Html.Parsing;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents.Html;

/// <summary>
/// Resolves the styles of a parsed HTML document: the user-agent style sheet (HTML Standard §15), the document's
/// <c>style</c> elements and linked style sheets in tree order with their imports (CSS Cascade 5 §2), the
/// <c>style</c> attributes and the presentational hints, through the CSS cascade to the computed value of every
/// registered property on every element.
/// </summary>
/// <remarks>
/// <para>Style sheets (HTML Standard §4.2.4, §4.2.6, §4.6.7.20): a <c>style</c> element of type text/css (or no
/// type) and a <c>link</c> whose rel has the keyword stylesheet (not alternate) apply when their media attribute
/// matches; of the sheets with a title, only those of the first title apply (the preferred style sheet set). Template
/// contents are not part of the document and carry no styles. URLs are resolved against the document's base URL (the
/// first <c>base</c> element with an href, against <see cref="HtmlStyleOptions.BaseUrl"/>) and, in a style sheet, its
/// own URL.</para>
/// <para>Refusals (<see cref="HtmlStyleDiagnostics.ClassOf"/>) are collected from every part; with
/// <see cref="UnsupportedPolicy.Refuse"/> a document with any fails with all of them, with
/// <see cref="UnsupportedPolicy.Warn"/> it is resolved and they are among the diagnostics.</para>
/// </remarks>
public static class HtmlStyleResolver
{
    /// <summary>Resolves the styles of a document.</summary>
    /// <param name="parsed">The document, as the HTML parser gave it.</param>
    /// <param name="options">The media, values, resources, base URL and policy.</param>
    /// <returns>The styled document; or, under <see cref="UnsupportedPolicy.Refuse"/>, the refusals.</returns>
    public static Result<StyledHtmlDocument, Error> Resolve(HtmlParseResult parsed, HtmlStyleOptions options)
    {
        var run = new Run(parsed, options);
        var styled = run.Resolve();
        var refusals = styled.Diagnostics.Where(error => HtmlStyleDiagnostics.ClassOf(error.Code) == DiagnosticClass.Refusal).ToList();
        return options.Unsupported == UnsupportedPolicy.Refuse && refusals.Count > 0
            ? Result<StyledHtmlDocument, Error>.Failure(refusals.Count == 1 ? refusals[0] : Errors.Aggregate(refusals))
            : Result<StyledHtmlDocument, Error>.Success(styled);
    }

    /// <summary>The form controls, outside v0: refused when they would be drawn.</summary>
    internal static readonly string[] FormControls = ["input", "button", "select", "textarea", "meter", "progress"];

    /// <summary>One resolution: the diagnostics, the base URLs of the style sheets, the resources fetched.</summary>
    private sealed class Run(HtmlParseResult parsed, HtmlStyleOptions options)
    {
        private readonly List<Error> _diagnostics = [.. parsed.Diagnostics];
        private int _suppressed;
        private readonly Dictionary<StyleSheet, string?> _baseOf = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<string, StyleSheet?> _fetched = new(StringComparer.Ordinal);
        private readonly string _sourceName = parsed.SourceName;
        private string? _documentBase;
        private HtmlElement? _body;

        public StyledHtmlDocument Resolve()
        {
            var document = parsed.Document;
            if (document.Mode == DocumentMode.Quirks)
            {
                Keep(new Error(HtmlStyleErrorCodes.Unsupported.QuirksMode, "The document is in quirks mode (no doctype, or a legacy one); add <!DOCTYPE html>. Quirks-mode rendering is not supported.")
                    .With(ErrorMetadataKeys.SourceFile, _sourceName));
            }

            var tree = StyleElement.Tree(document);
            var elements = Descendants(tree).ToList();
            _body = elements.Select(item => item.Element).FirstOrDefault(element => element.IsHtml("body"));
            _documentBase = DocumentBase(elements);

            var sheets = new List<CascadeSheet> { new(UserAgentStyleSheet.Sheet, CascadeOrigin.UserAgent) };
            sheets.AddRange(AuthorSheets(elements).Select(sheet => new CascadeSheet(sheet, CascadeOrigin.Author)));
            var index = CascadeIndex.Build(sheets, options.Media, Import);
            Keep(index.Diagnostics);

            var computer = new StyleComputer(index.Value, options.Values);
            var language = DefaultLanguage(elements);
            var root = Style(tree, computer, language);
            Keep(computer.Diagnostics);
            return new StyledHtmlDocument(document, root, index.Value.FontFaces, index.Value.PageRules, Capped(_diagnostics), _sourceName);
        }

        /// <summary>Keeps a diagnostic, at most <see cref="HtmlInputLimits.MaxDiagnostics"/> in all (the parts are capped
        /// each, their sum is capped here); the rest are counted.</summary>
        private void Keep(Error error)
        {
            if (_diagnostics.Count < HtmlInputLimits.MaxDiagnostics)
            {
                _diagnostics.Add(error);
            }
            else
            {
                _suppressed++;
            }
        }

        private void Keep(IEnumerable<Error> errors)
        {
            foreach (var error in errors)
            {
                Keep(error);
            }
        }

        /// <summary>The diagnostics kept, with one <see cref="DocumentErrorCodes.TooManyDiagnostics"/> for the rest.</summary>
        private ValueList<Error> Capped(List<Error> diagnostics) =>
            _suppressed == 0
                ? [.. diagnostics]
                : [.. diagnostics, new Error(DocumentErrorCodes.TooManyDiagnostics, $"{_suppressed} more diagnostics are left out: {HtmlInputLimits.MaxDiagnostics} are kept.")
                    .With(ErrorMetadataKeys.Value, _suppressed)
                    .With(ErrorMetadataKeys.SourceFile, _sourceName)];

        private static IEnumerable<StyleElement> Descendants(StyleElement root)
        {
            var pending = new Stack<StyleElement>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                var element = pending.Pop();
                yield return element;
                for (var i = element.Children.Count - 1; i >= 0; i--)
                {
                    pending.Push(element.Children[i]);
                }
            }
        }

        // ---- the styled tree ----

        /// <summary>Styles an element and its subtree, parents first (the computer needs the parent's style).
        /// Recursive: the parser limits the depth (HtmlInputLimits.MaxDepth).</summary>
        private StyledElement Style(StyleElement element, StyleComputer computer, string? inheritedLanguage, bool hidden = false)
        {
            var html = element.Element;
            var own = OwnDeclarations(element);
            var style = computer.Compute(element, own);
            hidden |= style["display"] is Css.Values.CssKeyword { Name: "none" };
            if (!hidden && html.Namespace == ElementNamespace.Html && FormControls.Contains(html.LocalName))
            {
                Keep(new Error(HtmlStyleErrorCodes.Unsupported.Element, $"The form control {html.LocalName} is not supported.")
                    .With(ErrorMetadataKeys.Value, html.LocalName)
                    .WithSource(_sourceName, html.Position));
            }

            var language = Language(html) ?? inheritedLanguage;
            var children = new List<StyledNode>();
            var next = 0;
            foreach (var child in html.Children)
            {
                switch (child)
                {
                    case HtmlElement:
                        children.Add(Style(element.Children[next++], computer, language, hidden));
                        break;
                    case HtmlText text:
                        children.Add(new StyledText(text, style));
                        break;
                }
            }

            return new StyledElement(html, style, language, [.. children]);
        }

        /// <summary>The element's style attribute (HTML and SVG elements, HTML Standard §3.2.6.5) and presentational
        /// hints.</summary>
        private ElementDeclarations OwnDeclarations(StyleElement element)
        {
            ValueList<Declaration> inline = [];
            if (element.Element.Attributes.FirstOrDefault(attribute => attribute.Namespace == AttributeNamespace.None && attribute.LocalName == "style") is { } style
                && element.Element.Namespace is ElementNamespace.Html or ElementNamespace.Svg)
            {
                var declarations = CssParser.ParseDeclarations(style.Value, new CssSourceOptions(_sourceName) { Origin = style.Position });
                Keep(declarations.Diagnostics);
                inline = declarations.Value;
            }

            var (hints, refused) = PresentationalHints.For(element, _body, _sourceName);
            Keep(refused);
            return inline.IsEmpty && hints.IsEmpty ? ElementDeclarations.None : new ElementDeclarations(inline, hints, _sourceName);
        }

        // ---- language (§3.2.6.2) ----

        /// <summary>The language an element's own attributes give: xml:lang (in the XML namespace) wins over lang (in no
        /// namespace, HTML elements only); null when it has neither.</summary>
        private static string? Language(HtmlElement element)
        {
            foreach (var attribute in element.Attributes)
            {
                if (attribute is { Namespace: AttributeNamespace.Xml, LocalName: "lang" })
                {
                    return attribute.Value;
                }
            }

            return element.Namespace == ElementNamespace.Html ? element.Attribute("lang") : null;
        }

        /// <summary>The pragma-set default language (§4.2.5.3): the first meta http-equiv=content-language with a
        /// content, its text up to the first comma, trimmed; null when there is none.</summary>
        private static string? DefaultLanguage(List<StyleElement> elements)
        {
            foreach (var element in elements.Select(item => item.Element))
            {
                if (element.IsHtml("meta") && element.Attribute("http-equiv") is { } equiv && Ascii.EqualsIgnoreCase(equiv.Trim(), "content-language")
                    && element.Attribute("content") is { } content)
                {
                    var language = content.Split(',')[0].Trim(' ', '\t', '\n', '\f', '\r');
                    return language.Length == 0 || language.Contains(' ', StringComparison.Ordinal) ? null : language;
                }
            }

            return null;
        }

        // ---- style sheets ----

        /// <summary>The document base URL (§2.4.1): the first base element with an href, resolved against the
        /// document's URL; else the document's URL.</summary>
        private string? DocumentBase(List<StyleElement> elements)
        {
            var href = elements.Select(item => item.Element).FirstOrDefault(element => element.IsHtml("base") && element.Attribute("href") is not null)?.Attribute("href");
            return href is null ? options.BaseUrl : Absolute(href.Trim(), options.BaseUrl) ?? options.BaseUrl;
        }

        /// <summary>A URL resolved against a base; null when it does not parse. Without a base, an absolute URL stays as
        /// it is and a relative one is passed on as written.</summary>
        private static string? Absolute(string url, string? baseUrl)
        {
            // Only a URL with a scheme is absolute: on Unix, Uri takes "/other.css" for an absolute file path.
            if (HasScheme(url) && Uri.TryCreate(url, UriKind.Absolute, out var absolute))
            {
                return absolute.AbsoluteUri;
            }

            if (baseUrl is null)
            {
                return url;
            }

            return HasScheme(baseUrl) && Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) && Uri.TryCreate(baseUri, url, out var resolved) ? resolved.AbsoluteUri : null;
        }

        /// <summary>Whether a URL starts with a scheme (URL Standard §4.4: an ASCII alpha, then alphanumerics, +, - or .,
        /// then a colon).</summary>
        private static bool HasScheme(string url)
        {
            var colon = url.IndexOf(':', StringComparison.Ordinal);
            return colon > 0 && char.IsAsciiLetter(url[0]) && url[..colon].All(c => char.IsAsciiLetterOrDigit(c) || c is '+' or '-' or '.');
        }

        /// <summary>The style sheets of the document in tree order (§4.2.6 style, §4.2.4 link with rel stylesheet).</summary>
        private List<StyleSheet> AuthorSheets(List<StyleElement> elements)
        {
            var sheets = new List<StyleSheet>();
            string? preferredTitle = null;
            foreach (var element in elements.Select(item => item.Element))
            {
                var isStyle = element.IsHtml("style");
                var isLink = element.IsHtml("link") && element.Attribute("rel") is { } rel && Keywords(rel).Contains("stylesheet");
                if (!isStyle && !isLink)
                {
                    continue;
                }

                if (isLink && Keywords(element.Attribute("rel")!).Contains("alternate"))
                {
                    continue;
                }

                if (element.Attribute("type") is { } type && type.Trim().Length > 0 && !Ascii.EqualsIgnoreCase(type.Trim(), "text/css"))
                {
                    continue;
                }

                if (element.Attribute("media") is { } media && !MediaMatches(media, element))
                {
                    continue;
                }

                // §4.6.7.20: of the style sheets with a title, the first title's set is the preferred one.
                if (element.Attribute("title") is { Length: > 0 } title)
                {
                    preferredTitle ??= title;
                    if (title != preferredTitle)
                    {
                        continue;
                    }
                }

                if (isStyle)
                {
                    var text = new StringBuilder();
                    var origin = element.Children.OfType<HtmlText>().FirstOrDefault()?.Position ?? element.Position;
                    foreach (var child in element.Children.OfType<HtmlText>())
                    {
                        text.Append(child.Data);
                    }

                    var sheet = CssParser.ParseStyleSheet(text.ToString(), new CssSourceOptions(_sourceName) { Origin = origin });
                    Keep(sheet.Diagnostics);
                    _baseOf[sheet.Value] = _documentBase;
                    sheets.Add(sheet.Value);
                }
                else if (element.Attribute("href") is { } href && href.Trim().Length > 0)
                {
                    var at = element.Attributes.First(attribute => attribute.LocalName == "href").Position;
                    if (Fetch(href.Trim(), _documentBase, _sourceName, at) is { } linked)
                    {
                        sheets.Add(linked);
                    }
                }
            }

            return sheets;
        }

        /// <summary>Whether a media attribute's query list matches (Media Queries 4; an empty one matches).</summary>
        private bool MediaMatches(string media, HtmlElement element)
        {
            var attribute = element.Attributes.First(item => item.LocalName == "media");
            var list = CssParser.ParseMediaQueryList(media, new CssSourceOptions(_sourceName) { Origin = attribute.Position });
            Keep(list.Diagnostics);
            return MediaQueries.Matches(list.Value, options.Media);
        }

        private static string[] Keywords(string value) =>
            [.. value.Split([' ', '\t', '\n', '\f', '\r'], StringSplitOptions.RemoveEmptyEntries).Select(Ascii.ToLower)];

        /// <summary>An @import: its URL resolved against the importing sheet's URL.</summary>
        private StyleSheet? Import(ImportRule import, StyleSheet importer) =>
            Fetch(import.Url, _baseOf.GetValueOrDefault(importer), importer.SourceName, import.Position);

        /// <summary>Fetches and reads a style sheet through the caller's resolver, once per URL.</summary>
        private StyleSheet? Fetch(string url, string? baseUrl, string referrer, TextPosition position)
        {
            var absolute = Absolute(url, baseUrl);
            if (absolute is not null && _fetched.TryGetValue(absolute, out var known))
            {
                return known;
            }

            StyleSheet? sheet = null;
            if (absolute is not null && options.Resources?.Invoke(absolute) is { } resource)
            {
                var read = CssParser.ParseStyleSheet(resource.Bytes, new CssSourceOptions(absolute) { ProtocolEncoding = resource.Charset, EnvironmentEncoding = "utf-8" });
                Keep(read.Diagnostics);
                sheet = read.Value;
                _baseOf[sheet] = absolute;
            }
            else
            {
                var why = absolute is null ? "is not a valid URL" : options.Resources is null ? "cannot be loaded: no resource resolver was given" : "was not found by the resource resolver";
                Keep(new Error(HtmlStyleErrorCodes.Resource.NotFound, $"The style sheet '{url}' {why}.")
                    .With(ErrorMetadataKeys.Value, absolute ?? url)
                    .WithSource(referrer, position));
            }

            if (absolute is not null)
            {
                _fetched[absolute] = sheet;
            }

            return sheet;
        }
    }
}
