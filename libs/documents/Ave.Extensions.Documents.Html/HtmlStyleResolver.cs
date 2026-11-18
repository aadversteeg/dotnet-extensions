using System;
using System.Collections.Generic;
using System.Linq;
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

    /// <summary>The HTML elements outside v0 that Chromium 159 styles with rules or a layout of their own the engine does
    /// not have (measured, review 39 F3): refused wherever they are, with the reason.</summary>
    internal static readonly IReadOnlyDictionary<string, string> OutsideProfile = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["details"] = "a disclosure widget (its marker and its closed content)",
        ["summary"] = "a disclosure widget (its marker and its closed content)",
        ["marquee"] = "a scrolling box",
        ["option"] = "a form control",
        ["optgroup"] = "a form control",
        ["audio"] = "media controls",
        ["canvas"] = "a surface drawn by scripts",
        ["embed"] = "an embedded document or plug-in",
        ["iframe"] = "an embedded document",
        ["object"] = "an embedded document or plug-in",
        ["noscript"] = "the engine runs no scripts, and a browser that does hides its content",
    };

    /// <summary>The script types a browser runs (HTML Standard §4.12.1.1: no type, a JavaScript MIME type, module,
    /// importmap); any other type is a data block, which is never run.</summary>
    private static readonly HashSet<string> ScriptTypes = new(StringComparer.Ordinal)
    {
        string.Empty, "module", "importmap", "application/ecmascript", "application/javascript", "application/x-ecmascript",
        "application/x-javascript", "text/ecmascript", "text/javascript", "text/javascript1.0", "text/javascript1.1",
        "text/javascript1.2", "text/javascript1.3", "text/javascript1.4", "text/javascript1.5", "text/jscript",
        "text/livescript", "text/x-ecmascript", "text/x-javascript",
    };

    /// <summary>One resolution: the diagnostics, the base URLs of the style sheets, the resources fetched.</summary>
    private sealed class Run(HtmlParseResult parsed, HtmlStyleOptions options)
    {
        private readonly List<Error> _diagnostics = [.. parsed.Diagnostics];
        private int _suppressed;
        private readonly HashSet<int> _renamed = [];
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
            Keep(computer.Diagnostics.Where((_, i) => !_renamed.Contains(i)));
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
            var mark = computer.DiagnosticCount;
            var style = computer.Compute(element, own);
            hidden |= style["display"] is Css.Values.CssKeyword { Name: "none" };
            var refused = RefuseElement(html, hidden);
            NameTheElement(computer, mark, html, refused || (hidden && html.Namespace == ElementNamespace.Html && FormControls.Contains(html.LocalName)));

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

        /// <summary>Refuses an element outside v0 at its start tag: a form control that would be drawn, an element of
        /// <see cref="OutsideProfile"/>, an svg or math element (their own matching and style rules), a script a
        /// browser would run, an event handler attribute. True when the element itself is refused.</summary>
        private bool RefuseElement(HtmlElement html, bool hidden)
        {
            void Refuse(ErrorCode code, string message, string value) =>
                Keep(new Error(code, message).With(ErrorMetadataKeys.Value, value).WithSource(_sourceName, html.Position));

            var refused = true;
            switch (html.Namespace)
            {
                case ElementNamespace.Html when !hidden && FormControls.Contains(html.LocalName):
                    Refuse(HtmlStyleErrorCodes.Unsupported.Element, $"The form control <{html.LocalName}> is not supported.", html.LocalName);
                    break;
                case ElementNamespace.Html when OutsideProfile.TryGetValue(html.LocalName, out var reason):
                    Refuse(HtmlStyleErrorCodes.Unsupported.Element, $"The element <{html.LocalName}> is not supported: {reason}.", html.LocalName);
                    break;
                case ElementNamespace.Html when html.LocalName == "script" && IsRunnable(html):
                    Refuse(HtmlStyleErrorCodes.Unsupported.Script, "The script is not run: the engine runs no scripts, and a browser would run this one.", "script");
                    break;
                case ElementNamespace.Svg when html.LocalName == "svg":
                case ElementNamespace.MathMl when html.LocalName == "math":
                    Refuse(HtmlStyleErrorCodes.Unsupported.Element, $"The element <{html.LocalName}> is not supported: {(html.LocalName == "svg" ? "SVG" : "MathML")} content has matching and style rules of its own.", html.LocalName);
                    break;
                default:
                    refused = false;
                    break;
            }

            foreach (var attribute in html.Attributes)
            {
                if (html.Namespace == ElementNamespace.Html && attribute.Namespace == AttributeNamespace.None
                    && attribute.LocalName.Length > 2 && attribute.LocalName.StartsWith("on", StringComparison.Ordinal))
                {
                    Keep(new Error(HtmlStyleErrorCodes.Unsupported.Script, $"The event handler attribute {attribute.LocalName} is not run: the engine runs no scripts, and a browser would run this one.")
                        .With(ErrorMetadataKeys.Value, attribute.LocalName)
                        .WithSource(_sourceName, attribute.Position));
                }
            }

            return refused;
        }

        /// <summary>Whether a browser runs a script element (HTML Standard §4.12.1.1, "prepare the script element"):
        /// its type, or "text/" and its language attribute when it has no type, is empty, a JavaScript MIME type
        /// (its essence, ASCII case-insensitive), module or importmap.</summary>
        private static bool IsRunnable(HtmlElement script)
        {
            var type = script.Attribute("type") is { } given
                ? given
                : script.Attribute("language") is { Length: > 0 } language ? "text/" + language : string.Empty;
            var essence = Ascii.ToLower(type.Split(';')[0].Trim(' ', '\t', '\n', '\f', '\r'));
            return ScriptTypes.Contains(essence);
        }

        /// <summary>A refusal the user-agent style sheet made for this element (a value of its rule that the engine does
        /// not support, such as <c>vertical-align: sub</c>) names the element at its start tag, not CSS the author never
        /// wrote; when the element itself is refused, or is a form control that is not drawn (the one exemption for
        /// what is not drawn), it says nothing more.</summary>
        private void NameTheElement(StyleComputer computer, int mark, HtmlElement html, bool refused)
        {
            for (var i = mark; i < computer.DiagnosticCount; i++)
            {
                var error = computer.DiagnosticAt(i);
                if (HtmlStyleDiagnostics.ClassOf(error.Code) != DiagnosticClass.Refusal
                    || error.Metadata?.GetValueOrDefault(ErrorMetadataKeys.SourceFile) as string != UserAgentStyleSheet.SourceName)
                {
                    continue;
                }

                _renamed.Add(i);
                if (!refused)
                {
                    var declaration = $"{error.Metadata?.GetValueOrDefault(ErrorMetadataKeys.Property)}: {error.Metadata?.GetValueOrDefault(ErrorMetadataKeys.Value)}";
                    Keep(new Error(HtmlStyleErrorCodes.Unsupported.Element, $"The element <{html.LocalName}> is not supported: its user-agent style '{declaration}' is outside this profile.")
                        .With(ErrorMetadataKeys.Value, html.LocalName)
                        .WithSource(_sourceName, html.Position));
                }
            }
        }

        /// <summary>The element's style attribute (HTML and SVG elements, HTML Standard §3.2.6.5) and presentational
        /// hints.</summary>
        private ElementDeclarations OwnDeclarations(StyleElement element)
        {
            ValueList<Declaration> inline = [];
            if (element.Element.Attributes.FirstOrDefault(attribute => attribute.Namespace == AttributeNamespace.None && attribute.LocalName == "style") is { } style
                && element.Element.Namespace is ElementNamespace.Html or ElementNamespace.Svg)
            {
                var declarations = CssParser.ParseDeclarations(style.Value, new CssSourceOptions(_sourceName) { Origin = style.PositionOf(0), Positions = style.PositionOf });
                Keep(declarations.Diagnostics);
                inline = declarations.Value;
            }

            var (hints, refused) = PresentationalHints.For(element, _body, _sourceName);
            Keep(refused);
            // Chromium 159's style adjuster: an HTML table does not take -webkit-left, -center or -right from around it
            // or from its own rules; they compute to start (measured, review 39 F4).
            var table = element.Element.IsHtml("table");
            return inline.IsEmpty && hints.IsEmpty && !table
                ? ElementDeclarations.None
                : new ElementDeclarations(inline, hints, _sourceName) { ResetsLegacyAlignment = table };
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

        /// <summary>The style sheets of the document in tree order (§4.2.6 style, §4.2.4 link with rel stylesheet; the
        /// style element of SVG too, as in Chromium 159). A sheet that does not apply leaves a diagnostic of class
        /// Ignored that says why, so a caller sees what was left out.</summary>
        private List<StyleSheet> AuthorSheets(List<StyleElement> elements)
        {
            var sheets = new List<StyleSheet>();
            // The preferred style sheet set (§4.6.7.20, §4.2.5.3 default-style): named by whichever comes first in tree
            // order, a default-style pragma or a style sheet with a title (Chromium 159, measured, review 39 F19).
            string? preferredTitle = null;
            foreach (var element in elements.Select(item => item.Element))
            {
                if (element.IsHtml("meta") && element.Attribute("http-equiv") is { } equiv && Ascii.EqualsIgnoreCase(equiv, "default-style")
                    && element.Attribute("content") is { Length: > 0 } preferred)
                {
                    preferredTitle ??= preferred;
                    continue;
                }

                var isStyle = element.IsHtml("style") || (element.Namespace == ElementNamespace.Svg && element.LocalName == "style");
                var isLink = element.IsHtml("link") && element.Attribute("rel") is { } rel && Keywords(rel).Contains("stylesheet");
                if (!isStyle && !isLink)
                {
                    continue;
                }

                void Skip(string why) =>
                    Keep(new Error(HtmlStyleErrorCodes.Ignored.StyleSheet, $"The style sheet of this {element.LocalName} element does not apply: {why}.")
                        .With(ErrorMetadataKeys.Value, element.LocalName)
                        .WithSource(_sourceName, element.Position));

                if (isLink && Keywords(element.Attribute("rel")!).Contains("alternate"))
                {
                    Skip("it is an alternate style sheet");
                    continue;
                }

                if (isLink && element.Attribute("disabled") is not null)
                {
                    // §4.6.7.20: a link with the disabled attribute starts disabled, whatever its value.
                    Skip("the link is disabled");
                    continue;
                }

                if (element.Attribute("type") is { } type && !IsCss(type, link: isLink))
                {
                    Skip($"its type '{type}' is not text/css");
                    continue;
                }

                if (element.Attribute("media") is { } media && !MediaMatches(media, element))
                {
                    Skip($"its media '{media}' do not match");
                    continue;
                }

                // §4.6.7.20: of the style sheets with a title, the preferred set's apply.
                if (element.Attribute("title") is { Length: > 0 } title)
                {
                    preferredTitle ??= title;
                    if (title != preferredTitle)
                    {
                        Skip($"its title '{title}' is not the preferred style sheet set '{preferredTitle}'");
                        continue;
                    }
                }

                if (isStyle)
                {
                    var texts = element.Children.OfType<HtmlText>().ToList();
                    var sheet = CssParser.ParseStyleSheet(string.Concat(texts.Select(text => text.Data)), new CssSourceOptions(_sourceName)
                    {
                        Origin = texts.FirstOrDefault()?.Position ?? element.Position,
                        Positions = texts.Count > 1 ? TextPositions(texts) : null,
                    });
                    Keep(sheet.Diagnostics);
                    _baseOf[sheet.Value] = _documentBase;
                    sheets.Add(sheet.Value);
                }
                else if (element.Attribute("href") is { } href && href.Trim().Length > 0)
                {
                    var at = element.Attributes.First(attribute => attribute.LocalName == "href").Position;
                    if (element.Attributes.FirstOrDefault(attribute => attribute.LocalName == "integrity" && attribute.Value.Trim().Length > 0) is { } integrity)
                    {
                        // A browser checks the hash and blocks a sheet that does not match (Chromium 159 blocks a sheet
                        // whose integrity does not match, measured, review 39 F19); the engine does not check it.
                        Keep(new Error(HtmlStyleErrorCodes.Unsupported.Attribute, $"The attribute integrity=\"{integrity.Value}\" on link asks for a check of the style sheet's hash, which is not supported.")
                            .With(ErrorMetadataKeys.Value, "integrity")
                            .WithSource(_sourceName, integrity.Position));
                    }

                    if (Fetch(href.Trim(), _documentBase, _sourceName, at) is { } linked)
                    {
                        sheets.Add(linked);
                    }
                }
            }

            return sheets;
        }

        /// <summary>Whether a type attribute names CSS: for style, empty or text/css exactly (ASCII case-insensitive, no
        /// white space trimmed, §4.2.6); for link, a MIME type whose essence is text/css (parameters and the white space
        /// around it allowed, as Chromium 159 takes <c>text/css; charset=utf-8</c>, measured, review 39 F19).</summary>
        private static bool IsCss(string type, bool link)
        {
            if (!link)
            {
                return type.Length == 0 || Ascii.EqualsIgnoreCase(type, "text/css");
            }

            var essence = type.Split(';')[0].Trim(' ', '\t', '\n', '\f', '\r');
            return type.Trim(' ', '\t', '\n', '\f', '\r').Length == 0 || Ascii.EqualsIgnoreCase(essence, "text/css");
        }

        /// <summary>The places of the characters of several text nodes read as one text (a style element built in
        /// code, or an SVG one): by node, then by line in the node.</summary>
        private static Func<int, TextPosition> TextPositions(List<HtmlText> texts)
        {
            var starts = new int[texts.Count];
            var lines = new List<int>[texts.Count];
            var offset = 0;
            for (var i = 0; i < texts.Count; i++)
            {
                starts[i] = offset;
                lines[i] = [0];
                for (var k = 0; k < texts[i].Data.Length; k++)
                {
                    if (texts[i].Data[k] == '\n')
                    {
                        lines[i].Add(k + 1);
                    }
                }

                offset += texts[i].Data.Length;
            }

            return index =>
            {
                var node = Array.BinarySearch(starts, index);
                node = node >= 0 ? node : ~node - 1;
                var local = index - starts[node];
                var line = lines[node].BinarySearch(local);
                line = line >= 0 ? line : ~line - 1;
                var start = texts[node].Position;
                return line == 0
                    ? new TextPosition(start.Line, start.Column + local)
                    : new TextPosition(start.Line + line, local - lines[node][line] + 1);
            };
        }

        /// <summary>Whether a media attribute's query list matches (Media Queries 4; an empty one matches). A list the
        /// reader refuses (past its limits) matches nothing, also when only warned.</summary>
        private bool MediaMatches(string media, HtmlElement element)
        {
            var attribute = element.Attributes.First(item => item.LocalName == "media");
            var list = CssParser.ParseMediaQueryList(media, new CssSourceOptions(_sourceName) { Origin = attribute.PositionOf(0), Positions = attribute.PositionOf });
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
