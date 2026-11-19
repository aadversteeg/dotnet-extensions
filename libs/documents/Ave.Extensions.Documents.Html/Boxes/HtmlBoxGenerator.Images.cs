using System;
using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css.Values;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Images;

namespace Ave.Extensions.Documents.Html.Boxes;

public static partial class HtmlBoxGenerator
{
    private sealed partial class Run
    {
        private string? _documentBase;
        private bool _documentBaseRead;

        /// <summary>
        /// A block-level <c>img</c> (HTML Standard §15.4.2, a replaced element; CSS 2.1 §10.3.2, §10.6.2): a box laid out
        /// by the image algorithm with the decoded image as its only node. The file is the <c>src</c> attribute's, from a
        /// <c>data:</c> URL or through the resource resolver that gives the style sheets and fonts; its <c>width</c> and
        /// <c>height</c> are the computed ones (the attributes are presentational hints for them, §15.4.3), a length or
        /// auto, which the image's natural size and ratio fill in. The alt text describes the figure in the structure
        /// (§4.8.4.4); an empty one makes the image decorative. An image that does not come or does not decode is refused:
        /// Chromium then shows its alt text or nothing, which is not drawn in this version.
        /// </summary>
        private void Image(BlockFrame block, StyledElement element)
        {
            var html = element.Element;
            if (!Check(element, isBlock: true) || Deeper(block.Depth + 1, html.Position))
            {
                return;
            }

            var source = html.Attribute("src")?.Trim(' ', '\t', '\n', '\f', '\r');
            var bytes = ImageBytes(source);
            if (bytes is null)
            {
                Refuse(HtmlStyleErrorCodes.Unsupported.Rendering, $"The image '{InputQuote.Of(source ?? string.Empty)}' of <img> is not there (no src, or neither a data: URL nor the resource resolver gives it), and what Chromium shows instead is not drawn in this version.",
                    "img", html.Position);
                return;
            }

            var decoded = ImageDecoder.Decode(bytes.Value);
            if (decoded.IsFailure)
            {
                Refuse(HtmlStyleErrorCodes.Unsupported.Rendering, $"The image '{InputQuote.Of(source!)}' of <img> does not decode ({decoded.Error.Message}), and what Chromium shows instead is not drawn in this version.",
                    "img", html.Position);
                return;
            }

            var id = NewId();
            var image = new ImageNode(NewId(), decoded.Value, Size(element, "width"), Size(element, "height"), Source(html.Position));
            var style = BoxStyleOf(element);
            if (block.ForcedBreakPending)
            {
                style = style with { BreakBefore = BreakBetween.Page };
                block.ForcedBreakPending = false;
            }

            var alt = html.Attribute("alt");
            var role = alt is { Length: 0 } ? SemanticRole.Decorative : SemanticRole.Figure(alt);
            block.Nodes.Add(new Box(id, role, LayoutAlgorithmId.Image, style, [image], Source(html.Position)));

            // CSS Fragmentation 3 §3.1: a forced break after the image is a forced break before the next box.
            block.ForcedBreakPending = Value(element, "break-after") is CssKeyword { Name: "page" };
        }

        /// <summary>An <c>img</c> inside a line of text: refused, the document model has no image in a line.</summary>
        private void InlineImage(StyledElement element) =>
            Refuse(HtmlStyleErrorCodes.Unsupported.Rendering, "The <img> inside a line of text is not drawn in this version: the document model has images only as block-level boxes.",
                "img", element.Element.Position);

        /// <summary>A computed width or height of an image: a length, or null for auto (a refused value is auto too).</summary>
        private Length? Size(StyledElement element, string name) => Value(element, name) is CssLength length ? length.Value : null;

        /// <summary>The bytes of an image's file: the body of a <c>data:</c> URL, or what the resolver gives for the URL
        /// resolved against the document's base URL (HTML Standard §2.4.1: the first <c>base</c> element with an
        /// <c>href</c>, else the document's URL); null when there are none.</summary>
        private ReadOnlyMemory<byte>? ImageBytes(string? source)
        {
            if (string.IsNullOrEmpty(source))
            {
                return null;
            }

            if (DataUrl.Body(source) is { } inline)
            {
                return inline;
            }

            return Absolute(source, DocumentBase()) is { } url && options.Resources?.Invoke(url) is { } resource ? resource.Bytes : null;
        }

        private string? DocumentBase()
        {
            if (!_documentBaseRead)
            {
                _documentBaseRead = true;
                var href = BaseHref(styled.Root);
                _documentBase = href is null ? options.BaseUrl : Absolute(href.Trim(), options.BaseUrl) ?? options.BaseUrl;
            }

            return _documentBase;
        }

        /// <summary>The href of the first <c>base</c> element that has one, in tree order; null for none.</summary>
        private static string? BaseHref(StyledElement root)
        {
            var pending = new System.Collections.Generic.Stack<StyledElement>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                var element = pending.Pop();
                if (IsHtml(element, "base") && element.Element.Attribute("href") is { } href)
                {
                    return href;
                }

                for (var i = element.Children.Count - 1; i >= 0; i--)
                {
                    if (element.Children[i] is StyledElement child)
                    {
                        pending.Push(child);
                    }
                }
            }

            return null;
        }

        /// <summary>A URL resolved against a base, as the style resolver resolves style sheet URLs: only a URL with a scheme
        /// is absolute (URL Standard §4.4; on Unix, <see cref="Uri"/> takes "/a.png" for a file path); without a base a
        /// relative URL is passed on as written; null when it does not resolve.</summary>
        private static string? Absolute(string url, string? baseUrl)
        {
            if (HasScheme(url))
            {
                return Uri.TryCreate(url, UriKind.Absolute, out var absolute) ? absolute.AbsoluteUri : null;
            }

            if (baseUrl is null)
            {
                return url;
            }

            return HasScheme(baseUrl) && Uri.TryCreate(baseUrl, UriKind.Absolute, out var root) && Uri.TryCreate(root, url, out var resolved) ? resolved.AbsoluteUri : null;
        }

        private static bool HasScheme(string url)
        {
            var colon = url.IndexOf(':', StringComparison.Ordinal);
            return colon > 0 && char.IsAsciiLetter(url[0]) && url[..colon].All(c => char.IsAsciiLetterOrDigit(c) || c is '+' or '-' or '.');
        }
    }
}
