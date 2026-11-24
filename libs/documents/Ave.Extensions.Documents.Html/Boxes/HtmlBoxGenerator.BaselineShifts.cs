using System;
using System.Collections.Generic;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css.Properties;
using Ave.Extensions.Documents.Css.Values;
using Ave.Extensions.Documents.DocumentModel;

namespace Ave.Extensions.Documents.Html.Boxes;

public static partial class HtmlBoxGenerator
{
    private sealed partial class Run
    {
        /// <summary>Chromium's layout unit, 1/64 CSS px, in points.</summary>
        private const double LayoutUnitPoints = 0.75 / 64;

        /// <summary>The baseline shift of each inline element met so far (<see cref="TextStyle.BaselineShift"/>): its own
        /// shift added to its parent's. An element is in it once its inline frame is opened; a block is never.</summary>
        private readonly Dictionary<StyledElement, Length> _shifts = new(ReferenceEqualityComparer.Instance);

        /// <summary>The baseline shift of an element's text: its own, after <see cref="OpenShift"/>; zero for a block and its
        /// strut.</summary>
        private Length ShiftOf(StyledElement element) => _shifts.TryGetValue(element, out var shift) ? shift : Length.Zero;

        /// <summary>
        /// Opens an inline element's shift: the parent's shift and the element's own (<see cref="OwnShift"/>), added. The
        /// elements are met top down, each with its parent's frame open, so the sum is made once per element, without
        /// recursion however deep the elements nest. Two shapes Chromium 159 prints in a way the model does not carry are
        /// refused at the element's start tag, and drawn under <see cref="UnsupportedPolicy.Warn"/> as the shift alone says:
        /// <list type="bullet">
        /// <item>a shifted element inside another inline element: Chromium paints the outer element's text, and everything
        /// in it, at a whole pixel from the line's top, which moves it up to half a pixel off the line's baseline (case
        /// <c>va-nested</c>: the 8 pt text 'small' around a raised span printed 0.41 pt above the words beside it, the 16 pt
        /// 'Big' 0.082 pt);</item>
        /// <item>a shifted element in the text of an element with a text decoration around it: Chromium draws the outer
        /// element's underline on the outer element's baseline across the shifted text (case <c>va-nested</c>), where the
        /// model draws a run's decorations on the run's own baseline.</item>
        /// </list>
        /// </summary>
        /// <param name="element">The inline element.</param>
        /// <param name="parent">The element it lies in: an inline element or the block of the lines.</param>
        private void OpenShift(StyledElement element, StyledElement parent)
        {
            var own = OwnShift(element, parent);
            _shifts[element] = Length.FromPoints(ShiftOf(parent).Points + own.Points);
            if (own == Length.Zero)
            {
                return;
            }

            var html = element.Element;
            var value = Value(element, "vertical-align")!.ToString()!;
            if (_shifts.ContainsKey(parent))
            {
                Refuse(HtmlStyleErrorCodes.Unsupported.Value,
                    $"'vertical-align: {value}' on <{html.LocalName}> inside the inline <{parent.Element.LocalName}> is not drawn in this version: Chromium then paints the text of <{parent.Element.LocalName}> at a whole pixel. Raise or lower only an element that lies directly in its block.",
                    value, html.Position, "vertical-align");
            }
            else if (TextStyleOf(parent) is { Decorations.Count: > 0 })
            {
                Refuse(HtmlStyleErrorCodes.Unsupported.Value,
                    $"'vertical-align: {value}' on <{html.LocalName}> inside text with a text decoration from around it is not drawn in this version: Chromium draws that line on the outer baseline. Decorate the raised or lowered element itself, or the text beside it.",
                    value, html.Position, "vertical-align");
            }
        }

        /// <summary>
        /// The shift an inline element's own <c>vertical-align</c> gives its text, in Chromium 159's layout unit of 1/64 px
        /// (CSS 2.1 §10.8.1; CSS Inline 3 §4 leaves where super and sub go to the user agent):
        /// <list type="bullet">
        /// <item><c>super</c>: up by the parent's font size in 1/64 px, divided by 3 and cut to a whole 1/64 px, plus 1 px;
        /// <c>sub</c>: down by that size divided by 5, cut so, plus 1 px. The size is the parent's (the element's own, for a
        /// <c>sup</c> at <c>smaller</c>, does not count) and is taken to the nearest 1/64 px first: at 11 pt, 14.667 px is
        /// 939/64 px, so super is 313 + 64 = 377/64 px (cases <c>va-shift</c>, <c>va-elements</c>; at 8, 10.5, 11, 14 and
        /// 16 pt, <c>va-sizes</c>; 14 pt sub, 18.667 px, is 1195/5 = 239 + 64 = 303/64 px, where 18.667 / 5 + 1 cut would be
        /// 302/64);</item>
        /// <item>a length: up by it (down when negative), cut toward zero to 1/64 px (case <c>va-length</c>: 2.35pt and
        /// −2.35pt move 2.344 pt, 200/64 px; 0.4em of 14 px, 358/64 px);</item>
        /// <item>a percentage: of the element's own line height, cut so: of a line height given as a length or a number as
        /// layout takes it, and of <c>normal</c> the font's ascent, descent and line gap each at a whole pixel (case
        /// <c>va-length</c>: 30% of 18pt, 7.2 px, moves 460/64 px; of Lato's normal 17 px at 10.5 pt, 326/64 px; of a span's
        /// own 30pt, 9 pt).</item>
        /// </list>
        /// <c>baseline</c>, a refused value and any other value give none.
        /// </summary>
        private Length OwnShift(StyledElement element, StyledElement parent)
        {
            var units = Value(element, "vertical-align") switch
            {
                CssKeyword { Name: "super" } => Math.Floor(SizeInUnits(parent) / 3) + 64,
                CssKeyword { Name: "sub" } => -(Math.Floor(SizeInUnits(parent) / 5) + 64),
                CssLength length => Math.Truncate(length.Value.Points / LayoutUnitPoints),
                CssPercentage percentage when LineHeightPoints(element) is { } lineHeight => Math.Truncate(percentage.Value / 100 * lineHeight / LayoutUnitPoints),
                _ => 0,
            };
            return Length.FromPoints(units * LayoutUnitPoints);
        }

        /// <summary>An element's computed font size in whole 1/64 px, to the nearest.</summary>
        private static double SizeInUnits(StyledElement element) =>
            Math.Round((element.Style[PropertyRegistry.FontSize] is CssLength length ? length.Value.Points : PropertyRegistry.InitialFontSizePoints) / LayoutUnitPoints, MidpointRounding.AwayFromZero);

        /// <summary>An element's line height in points, as layout takes it (<c>LineBoxes</c>): a length or a number as its
        /// text style holds it, <c>normal</c> the font's ascent, descent and line gap each at a whole pixel; null after a
        /// missing face was reported.</summary>
        private double? LineHeightPoints(StyledElement element)
        {
            if (FaceStyleOf(element) is not { } style)
            {
                return null;
            }

            if (style.LineHeight.HeightFor(style.FontSize.Points) is { } height)
            {
                return height;
            }

            var face = Fonts.Match(element.Style, element.Element.Position);
            if (face.IsFailure)
            {
                return null;
            }

            var metrics = face.Value.Metrics;
            double Whole(int designUnits) => Math.Round(metrics.ToPoints(designUnits, style.FontSize.Points) / 0.75, MidpointRounding.AwayFromZero) * 0.75;
            return Whole(metrics.Line.Ascender) + Whole(-metrics.Line.Descender) + Whole(metrics.Line.LineGap);
        }
    }
}
