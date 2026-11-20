using System.Collections.Generic;
using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css.Cascade;
using Ave.Extensions.Documents.Css.Values;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Html.Parsing;
using Ave.Extensions.Documents.Images;
using Ave.Extensions.ErrorPaths;

namespace Ave.Extensions.Documents.Html.Boxes;

public static partial class HtmlBoxGenerator
{
    private sealed partial class Run
    {
        /// <summary>
        /// The page with its page-margin boxes (CSS Paged Media 3 §5, the top and bottom rows), from the page contexts
        /// of <see cref="StyledHtmlDocument.PageContexts"/>, whose diagnostics are reported here as they are. When a page
        /// context generates a margin box, every page gets a header area as high as the top of Chromium's page area and
        /// a footer area as high as the sheet below it, each a row laid out by <see cref="LayoutAlgorithmId.MarginBoxes"/>
        /// with the boxes of its edge at their places, and the model's top and bottom margins become 0, so the body
        /// stays where it was. The rows start where Chromium starts them, in the left margin
        /// (<see cref="PageArea.StartInMargin"/>, <see cref="PageRules.RowStart"/>). Page 1 takes the <c>:first</c>
        /// page context (<see cref="PageStyle.FirstPage"/>). Without a generated margin box the page is as the rules
        /// give it.
        /// </summary>
        /// <param name="page">The page box of the <c>@page</c> rules.</param>
        /// <param name="rowStart">Where Chromium starts the rows (<see cref="PageRules.RowStart"/>).</param>
        private PageStyle WithMarginBoxes(PageStyle page, Length rowStart)
        {
            var contexts = styled.PageContexts;
            Refusals.AddRange(contexts.Diagnostics);
            var boxes = contexts.EveryPage.MarginBoxes.Concat(contexts.FirstPage.MarginBoxes).ToList();
            if (boxes.Count == 0)
            {
                return page;
            }

            // The page area starts at the left margin rounded to a whole pixel, the rows at the smaller of that and the
            // margin cut to 1/64 px: never after the page area's start.
            var startInMargin = page.Margins.Left - rowStart;

            var (header, footer) = (page.Margins.Top, page.Margins.Bottom);
            foreach (var box in boxes.Where(box => (IsTop(box.Box) ? header : footer).Points <= 0))
            {
                Refuse(HtmlStyleErrorCodes.Unsupported.PageRule, "A page-margin box in a page margin of 0 is not drawn in this version: it has no height. Give the page a margin there.",
                    box.Content.ToString(), box.Position);
            }

            var template = Template(contexts.EveryPage, header, footer, startInMargin);

            // Page 1 shares the declarations of the rules without a selector: what is refused or fails in both (a face
            // the fonts do not have) is reported once.
            var (refusals, failures) = (Refusals.Count, Failures.Count);
            var firstPage = Template(contexts.FirstPage, header, footer, startInMargin);
            Once(Refusals, refusals);
            Once(Failures, failures);

            return page with
            {
                Margins = page.Margins with { Top = Length.Zero, Bottom = Length.Zero },
                Template = template,
                FirstPage = firstPage,
            };
        }

        /// <summary>Removes the errors from <paramref name="count"/> on that repeat one before it.</summary>
        private static void Once(List<Error> errors, int count)
        {
            var reported = errors.Take(count).Select(error => error.ToString()).ToHashSet(System.StringComparer.Ordinal);
            for (var i = errors.Count - 1; i >= count; i--)
            {
                if (reported.Contains(errors[i].ToString()))
                {
                    errors.RemoveAt(i);
                }
            }
        }

        private static bool IsTop(PageMarginBox box) => box is PageMarginBox.TopLeft or PageMarginBox.TopCenter or PageMarginBox.TopRight;

        /// <summary>The header and footer of one page context: each edge's row, also an empty one, in an area of the
        /// margin's height; none where that margin is 0.</summary>
        private PageTemplate Template(PageContextStyle context, Length header, Length footer, Length startInMargin)
        {
            PageArea? Area(bool top, Length height) =>
                height.Points > 0 ? new PageArea(Row(context, top), height) { StartInMargin = startInMargin } : null;
            return new PageTemplate(Area(top: true, header), Area(top: false, footer));
        }

        /// <summary>One row: the generated margin boxes of one edge, each at its place (CSS Paged Media 3 §5.3.2), listed
        /// in the order Chromium 159 paints them whatever the order of their rules: the top row from left to right, the
        /// bottom row from right to left (measured: case <c>pm-paint-order</c>, whose rules come in neither order, and the
        /// bottom rows of <c>pm-width</c>, <c>pm-snap-image</c> and <c>pm-defaults</c>).</summary>
        private Box Row(PageContextStyle context, bool top)
        {
            var id = NewId();
            var children = new List<Node>();
            foreach (var box in context.MarginBoxes.Where(box => IsTop(box.Box) == top))
            {
                var place = box.Box switch
                {
                    PageMarginBox.TopLeft or PageMarginBox.BottomLeft => MarginBoxPlace.Start,
                    PageMarginBox.TopCenter or PageMarginBox.BottomCenter => MarginBoxPlace.Center,
                    _ => MarginBoxPlace.End,
                };
                children.Add(MarginBox(box, place));
            }

            if (!top)
            {
                children.Reverse();
            }

            return new Box(id, SemanticRole.Generic, LayoutAlgorithmId.MarginBoxes, BoxStyle.Initial, [.. children], Source(styled.Root.Element.Position));
        }

        /// <summary>
        /// One margin box: a block box with its computed margin, padding, border, background colour, text alignment and
        /// vertical alignment, and its <c>content</c> as inline content (strings, white space as its <c>white-space</c>
        /// says; <c>counter(page)</c> and <c>counter(pages)</c> as page counters), in the fonts of its computed style.
        /// It is mapped as an element is (<see cref="BoxStyleOf"/>, <see cref="TextStyleOf"/>), through an element that
        /// stands in for it, has no children and carries its computed style: the page-context cascade already refused
        /// what a margin box does not draw. Its source is its <c>content</c> declaration, line and column, where a
        /// layout refusal of it (<c>Documents.Layout.MarginBoxOverflow</c>) points. An image in <c>content</c> is an
        /// atomic inline among the text (<see cref="ContentImage"/>).
        /// </summary>
        private Box MarginBox(MarginBoxStyle box, MarginBoxPlace place)
        {
            var stand = new StyledElement(new HtmlElement(ElementNamespace.Html, $"@{Name(box.Box)}", [], [], null, box.Position), box.Style, null, []);
            var id = NewId();
            var source = Source(box.Position) with { Column = box.Position.Column };
            var runs = new List<Node>();
            var text = TextStyleOf(stand);
            foreach (var item in box.Content.Items)
            {
                switch (item)
                {
                    case CssContentString value when text is not null:
                        runs.Add(new TextRun(NewId(), Characters(new HtmlText(value.Value, box.Position)), text, source));
                        break;
                    case CssContentCounter counter when text is not null:
                        runs.Add(new CounterRun(NewId(), counter.Counter == PageCounterName.Page ? PageCounter.PageNumber : PageCounter.PageCount, text, source));
                        break;
                    case CssContentImage image when text is not null && ContentImage(box, image, text) is { } imageBox:
                        runs.Add(imageBox);
                        break;
                }
            }

            var (layout, children) = Content(runs);
            var style = BoxStyleOf(stand) with
            {
                Text = layout == LayoutAlgorithmId.Inline ? text : null,
                VerticalAlign = VerticalAlignOf(stand),
                MarginBoxPlace = place,
            };
            return new Box(id, SemanticRole.Generic, layout, style, children, source);
        }

        /// <summary>
        /// An image in a margin box's <c>content</c> (<c>url()</c>, or <c>image-set()</c> of one URL and its resolution,
        /// CSS Images 4 §2.2): an atomic inline on the baseline, as an <c>img</c> inside a line of text is
        /// (<see cref="InlineImage"/>), of its natural size divided by the resolution (CSS Images 4 §2.2, §5.3; measured
        /// in Chromium 159, a 40 × 24 px image: <c>url()</c> 30 × 18 pt, <c>2x</c> 15 × 9 pt, <c>144dpi</c> 20 × 12 pt,
        /// <c>60dpcm</c> 18.898 × 11.339 pt laid out and painted at whole pixels; cases <c>pm-image-url</c>,
        /// <c>pm-image-set</c>, <c>pm-image-middle</c>, <c>pm-image-top</c>, <c>pm-image-exact</c>). The file is a
        /// <c>data:</c> URL or what the resource resolver gives for the URL resolved against the base URL of the style
        /// sheet that holds the <c>content</c> declaration (CSS Values 4 §4.5.1, <see cref="MarginBoxStyle.BaseUrl"/>), as
        /// a font of <c>@font-face</c> is found. A margin box is part of the pagination artifact, so the image is
        /// decorative: no alternative text is asked. One that does not come or does not decode is refused at the
        /// <c>content</c> declaration, as <c>img</c> refuses it, and left out under <see cref="UnsupportedPolicy.Warn"/>.
        /// </summary>
        /// <returns>The image's box; null when it is refused.</returns>
        private Box? ContentImage(MarginBoxStyle box, CssContentImage image, TextStyle text)
        {
            var url = DataUrl.Body(image.Url) is null ? Urls.Absolute(image.Url, box.BaseUrl ?? options.BaseUrl) ?? image.Url : image.Url;
            var bytes = DataUrl.Body(url) ?? options.Resources?.Invoke(url)?.Bytes;
            if (bytes is null)
            {
                Refuse(HtmlStyleErrorCodes.Unsupported.Rendering, $"The image '{InputQuote.Of(url)}' in the content of @{Name(box.Box)} is not there (neither a data: URL nor the resource resolver gives it), and what Chromium shows instead is not drawn in this version.",
                    image.ToString(), box.Position, "content");
                return null;
            }

            var decoded = ImageDecoder.Decode(bytes.Value);
            if (decoded.IsFailure)
            {
                Refuse(HtmlStyleErrorCodes.Unsupported.Rendering, $"The image '{InputQuote.Of(url)}' in the content of @{Name(box.Box)} does not decode ({decoded.Error.Message}), and what Chromium shows instead is not drawn in this version.",
                    image.ToString(), box.Position, "content");
                return null;
            }

            var source = Source(box.Position) with { Column = box.Position.Column };
            // The natural size is the pixel size in CSS pixels (1px = 0.75pt, CSS Values 4 §6.2), divided by the resolution.
            const double point = 0.75;
            var width = Length.FromPoints(decoded.Value.PixelWidth * point / image.Resolution);
            var height = Length.FromPoints(decoded.Value.PixelHeight * point / image.Resolution);
            var node = new ImageNode(NewId(), decoded.Value, width, height, source);

            // CSS Text Decoration 3 §2: decorations are not propagated into an atomic inline.
            return new Box(NewId(), SemanticRole.Decorative, LayoutAlgorithmId.Image, BoxStyle.Initial with { Text = text with { Decorations = [] } }, [node], source);
        }

        private static string Name(PageMarginBox box) => box switch
        {
            PageMarginBox.TopLeft => "top-left",
            PageMarginBox.TopCenter => "top-center",
            PageMarginBox.TopRight => "top-right",
            PageMarginBox.BottomLeft => "bottom-left",
            PageMarginBox.BottomCenter => "bottom-center",
            _ => "bottom-right",
        };
    }
}
