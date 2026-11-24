using System;
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

        /// <summary>A block-level <c>img</c> (CSS 2.1 §10.3.2, §10.6.2): its box (<see cref="ImageBox"/>) among the block
        /// boxes, with a forced break before or after it as any block box has.</summary>
        private void Image(BlockFrame block, StyledElement element)
        {
            if (!Check(element, isBlock: true) || Deeper(block.Depth + 1, element.Element.Position))
            {
                return;
            }

            var style = BoxStyleOf(element);
            if (ImageBox(element, block.ForcedBreakPending ? style with { BreakBefore = BreakBetween.Page } : style) is { } box)
            {
                block.Nodes.Add(box);

                // CSS Fragmentation 3 §3.1: a forced break after the image is a forced break before the next box.
                block.ForcedBreakPending = Value(element, "break-after") is CssKeyword { Name: "page" };
            }
        }

        /// <summary>
        /// An <c>img</c> inside a line of text (<c>display: inline</c>, the initial value): an atomic inline (CSS Display 3,
        /// Appendix A; CSS 2.1 §10.3.2, §10.6.2) among the runs, at its place in the text, as the block-level image is made:
        /// its file, computed size, alt text, margins, padding, border and background. Its margin box sits on the baseline
        /// (CSS 2.1 §10.8.1, <c>vertical-align: baseline</c>), and it wraps as the text of the element it lies in, whose
        /// computed text values are the image box's text style (so a <c>nowrap</c> element around it holds), without the
        /// decorations of the text around it. Refused, with
        /// the value and the start tag: another <c>vertical-align</c> (not built), and a <c>break-before</c>,
        /// <c>break-after</c> or <c>break-inside</c> other than <c>auto</c> (they apply to block-level boxes only, CSS
        /// Fragmentation 3 §3.1, and an atomic inline never breaks). <c>display: inline-block</c> is refused by its value.
        /// </summary>
        private void InlineImage(InlineFrame frame, StyledElement element)
        {
            if (!Check(element, isBlock: true) || Deeper(frame.Depth + 1, element.Element.Position))
            {
                return;
            }

            var refused = false;
            if (frame.InBox && frame.InLink)
            {
                Refuse(HtmlStyleErrorCodes.Unsupported.InlineBox, "An <img> inside a link and inside an element with a background, border, padding or margin of its own is not drawn in this version; put the image outside that element or outside the link.",
                    "img", element.Element.Position, "display");
                refused = true;
            }

            if (Value(element, "vertical-align") is { } align and not CssKeyword { Name: "baseline" })
            {
                Refuse(HtmlStyleErrorCodes.Unsupported.Value, $"'vertical-align: {align}' on an <img> inside a line of text is not drawn in this version: it sits on the baseline.",
                    align.ToString()!, element.Element.Position, "vertical-align");
                refused = true;
            }

            if (ShiftOf(frame.Element) != Length.Zero)
            {
                Refuse(HtmlStyleErrorCodes.Unsupported.Element, $"An <img> inside the raised or lowered <{frame.Element.Element.LocalName}> is not drawn in this version: it sits on the line's baseline.",
                    "img", element.Element.Position);
            }

            foreach (var name in new[] { "break-before", "break-after", "break-inside" })
            {
                if (Value(element, name) is { } value and not CssKeyword { Name: "auto" })
                {
                    Refuse(HtmlStyleErrorCodes.Unsupported.Value, $"'{name}: {value}' on an <img> inside a line of text is not drawn: page breaks apply to block-level boxes only.",
                        value.ToString()!, element.Element.Position, name);
                    refused = true;
                }
            }

            if (refused || TextStyleOf(frame.Element) is not { } text)
            {
                return;
            }

            // CSS Text Decoration 3 §2: decorations are not propagated into an atomic inline, so the image's text style has none.
            var style = BoxStyleOf(element) with { BreakBefore = BreakBetween.Auto, BreakAfter = BreakBetween.Auto, BreakInside = BreakInside.Auto, Text = text with { Decorations = [] } };
            if (ImageBox(element, style) is { } box)
            {
                frame.Target.Add(box);
            }
        }

        /// <summary>
        /// The box of an <c>img</c> (HTML Standard §15.4.2, a replaced element): laid out by the image algorithm with the
        /// decoded image as its only node. The file is the <c>src</c> attribute's, from a <c>data:</c> URL or through the
        /// resource resolver that gives the style sheets and fonts; its <c>width</c> and <c>height</c> are the computed ones
        /// (the attributes are presentational hints for them, §15.4.3), a length or auto, which the image's natural size and
        /// ratio fill in. The alt text describes the figure in the structure (§4.8.4.4); an empty one makes the image
        /// decorative. An image that does not come or does not decode is refused: Chromium then shows its alt text or
        /// nothing, which is not drawn in this version. So is a width and a height in another ratio than the image's own,
        /// which Chromium stretches the image to.
        /// </summary>
        /// <returns>The box; null when the image is refused.</returns>
        private Box? ImageBox(StyledElement element, BoxStyle style)
        {
            var html = element.Element;
            var source = html.Attribute("src")?.Trim(' ', '\t', '\n', '\f', '\r');
            var bytes = ImageBytes(source);
            if (bytes is null)
            {
                Refuse(HtmlStyleErrorCodes.Unsupported.Rendering, $"The image '{InputQuote.Of(source ?? string.Empty)}' of <img> is not there (no src, or neither a data: URL nor the resource resolver gives it), and what Chromium shows instead is not drawn in this version.",
                    "img", html.Position);
                return null;
            }

            var decoded = ImageDecoder.Decode(bytes.Value);
            if (decoded.IsFailure)
            {
                Refuse(HtmlStyleErrorCodes.Unsupported.Rendering, $"The image '{InputQuote.Of(source!)}' of <img> does not decode ({decoded.Error.Message}), and what Chromium shows instead is not drawn in this version.",
                    "img", html.Position);
                return null;
            }

            // CSS 2.1 §10.3.2, §10.6.2: with both sizes given, Chromium stretches the image to them (case hi-stretch); the
            // image algorithm keeps the image's own ratio, so a pair of sizes in another ratio is refused.
            var (width, height) = (Size(element, "width"), Size(element, "height"));
            var (pixelWidth, pixelHeight) = (decoded.Value.PixelWidth, decoded.Value.PixelHeight);
            if (width is { } w && height is { } h && Math.Abs((w.Points * pixelHeight) - (h.Points * pixelWidth)) > 1e-9 * w.Points * pixelHeight)
            {
                var sizes = $"{Value(element, "width")} {Value(element, "height")}";
                Refuse(HtmlStyleErrorCodes.Unsupported.Value, $"The <img> is given a width and a height ({sizes}) in another ratio than its image's own ({pixelWidth} x {pixelHeight} px); Chromium stretches the image to them, which is not drawn in this version: the image keeps its ratio.",
                    sizes, html.Position, "width");
                return null;
            }

            var id = NewId();
            var image = new ImageNode(NewId(), decoded.Value, width, height, Source(html.Position));
            var alt = html.Attribute("alt");
            var role = alt is { Length: 0 } ? SemanticRole.Decorative : SemanticRole.Figure(alt);
            return new Box(id, role, LayoutAlgorithmId.Image, style, [image], Source(html.Position));
        }

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

            return Urls.Absolute(source, DocumentBase()) is { } url && options.Resources?.Invoke(url) is { } resource ? resource.Bytes : null;
        }

        /// <summary>The document base URL, by the style resolver's rule (<see cref="Urls.DocumentBase"/>), against this
        /// run's base URL.</summary>
        private string? DocumentBase()
        {
            if (!_documentBaseRead)
            {
                _documentBaseRead = true;
                _documentBase = Urls.DocumentBase(styled.Document, options.BaseUrl);
            }

            return _documentBase;
        }
    }
}
