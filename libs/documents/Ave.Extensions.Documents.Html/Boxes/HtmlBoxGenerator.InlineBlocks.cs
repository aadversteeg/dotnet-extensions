using System.Collections.Generic;
using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css.Values;

namespace Ave.Extensions.Documents.Html.Boxes;

public static partial class HtmlBoxGenerator
{
    /// <summary>How many inline-blocks may lie inside one another: one inside another, no deeper (THE RULE FOR AN
    /// INLINE-BLOCK IN HTML). Each level lays its content out again for each layout of the line around it, so the bound
    /// keeps the work on a line of nested boxes linear in the input.</summary>
    internal const int MaxInlineBlockNesting = 2;

    private sealed partial class Run
    {
        /// <summary>
        /// THE RULE FOR AN INLINE-BLOCK IN HTML: an element with <c>display: inline-block</c> inside a line of text is an atomic
        /// inline (CSS Display 3 §2.1 and Appendix A; CSS 2.1 §9.2.4) among the runs, at its place in the text: a box made as a
        /// block box is made, its content a block formatting context of its own (blocks in flow, or its own lines), with its
        /// margins, padding, border, background, <c>width</c>, <c>min-width</c>, <c>max-width</c>, <c>height</c> and
        /// <c>box-sizing</c> (cases <c>iblk-badge</c>, <c>iblk-wrap</c>, <c>iblk-sizes</c>, <c>iblk-swatch</c>). Its last line's
        /// baseline sits on the line's baseline (CSS 2.1 §10.8.1); layout gives it its shrink-to-fit width (CSS 2.1 §10.3.9).
        /// Refused, at the start tag or the declaration, with what to do: another <c>vertical-align</c> than baseline; an
        /// <c>overflow</c> other than visible (refused on every element but an image by its row, <see cref="BoxProperties"/>;
        /// on an inline-block it would also move the baseline to the bottom margin edge); a width, a
        /// minimum or maximum width or a height in percent; a <c>break-before</c>, <c>break-after</c> or <c>break-inside</c>
        /// other than auto (an atomic inline never breaks); one inside a link, inside an element with a box of its own, inside
        /// a raised or lowered element or inside underlined or struck-through text, and one made by a <c>::before</c> or
        /// <c>::after</c> (not measured); a table inside one (at the table's start tag: the reference comparison does not
        /// reach the cells of a table inside a line, so it is not measured); and one more than
        /// <see cref="MaxInlineBlockNesting"/> levels deep inside others.
        /// </summary>
        private void InlineBlock(Stack<object> stack, InlineFrame frame, StyledElement element)
        {
            if (!Check(element, isBlock: true) || Deeper(frame.Depth + 1, element.Element.Position))
            {
                return;
            }

            var name = element.Element.LocalName;
            if (element.Generated is { } generated)
            {
                Refuse(HtmlStyleErrorCodes.Unsupported.Value, "'display: inline-block' on a ::before or ::after is not drawn in this version; generate the text inline, or give the box to an element.",
                    "inline-block", generated.Position, "display");
                return;
            }

            var refused = false;
            void RefuseValue(string property, CssValue value, string message)
            {
                Refuse(HtmlStyleErrorCodes.Unsupported.Value, message, value.ToString()!, element.Element.Position, property);
                refused = true;
            }

            if (Value(element, "vertical-align") is { } align and not CssKeyword { Name: "baseline" })
            {
                RefuseValue("vertical-align", align, $"'vertical-align: {align}' on an inline-block <{name}> is not drawn in this version: it sits on the baseline. Leave vertical-align at baseline.");
            }

            foreach (var size in new[] { "width", "min-width", "max-width", "height" })
            {
                if (Value(element, size) is CssPercentage percentage)
                {
                    RefuseValue(size, percentage, $"'{size}: {percentage}' on an inline-block <{name}> is not drawn in this version: a percentage of the line is not measured. Give it a length.");
                }
            }

            foreach (var property in new[] { "break-before", "break-after", "break-inside" })
            {
                if (Value(element, property) is { } value and not CssKeyword { Name: "auto" })
                {
                    RefuseValue(property, value, $"'{property}: {value}' on an inline-block <{name}> is not drawn: page breaks apply to block-level boxes only, and an inline-block moves with its line.");
                }
            }

            if (frame.InLink || frame.InBox || ShiftOf(frame.Element) != Length.Zero || TextStyleOf(frame.Element) is { Decorations.Count: > 0 })
            {
                Refuse(HtmlStyleErrorCodes.Unsupported.InlineBox, $"An inline-block <{name}> inside a link, inside an element with a background, border, padding or margin of its own, inside a raised or lowered element or inside underlined text is not drawn in this version; put it outside that element.",
                    name, element.Element.Position, "display");
                refused = true;
            }

            if (stack.OfType<BlockFrame>().Count(open => open.Atomic is not null) >= MaxInlineBlockNesting)
            {
                Refuse(HtmlStyleErrorCodes.Unsupported.InlineBox, $"An inline-block <{name}> inside {MaxInlineBlockNesting} other inline-blocks is not drawn in this version: they may nest {MaxInlineBlockNesting} deep. Make the inner one an inline element.",
                    name, element.Element.Position, "display");
                refused = true;
            }

            if (refused)
            {
                return;
            }

            var block = Frame(element, NewId(), frame.Depth);
            block.Atomic = (frame.Target, frame.Element);
            stack.Push(block);
        }
    }
}
