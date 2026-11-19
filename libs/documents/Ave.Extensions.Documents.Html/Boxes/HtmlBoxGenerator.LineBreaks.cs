using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Html.Parsing;

namespace Ave.Extensions.Documents.Html.Boxes;

public static partial class HtmlBoxGenerator
{
    /// <summary>The forced line break of the document model: the line separator U+2028, a mandatory break (UAX #14, class
    /// BK) that white-space collapsing keeps and that draws nothing.</summary>
    internal const string ForcedLineBreak = "\u2028";

    private sealed partial class Run
    {
        private static bool IsHtml(StyledElement element, string name) =>
            element.Element.Namespace == ElementNamespace.Html && element.Element.LocalName == name;

        /// <summary>
        /// A <c>br</c> in inline content: a forced line break (HTML Standard §15.3, the rule
        /// <c>br { display-outside: newline }</c>; CSS Text 3 §5, a forced line break ends the line), in the br's own text
        /// style, so its font counts for the line it ends as any inline box's does (CSS 2.1 §10.8.1). Measured in Chromium 159 (cases <c>hb-*</c>): a br
        /// that ends a block's content adds no line, one that starts it, or two in a row, an empty line; the white space
        /// around it is removed with the line ends; one with <c>display: none</c> is nothing.
        /// </summary>
        private void LineBreak(InlineFrame frame, StyledElement element)
        {
            if (!Check(element, isBlock: false) || Deeper(frame.Depth, element.Element.Position))
            {
                return;
            }

            if (TextStyleOf(element) is { } style)
            {
                frame.Target.Add(new TextRun(NewId(), ForcedLineBreak, style, Source(element.Element.Position)));
            }
        }

        /// <summary>
        /// The text of a text node as inline layout takes it: as written, except under <c>white-space-collapse:
        /// preserve-breaks</c> (<c>white-space: pre-line</c>), where every segment break (a line feed; the parser has
        /// normalised CR LF and CR to it, HTML Standard §13.2.3.5) is a forced line break (CSS Text 4 §3.1: "segment breaks
        /// are not collapsible"; CSS Text 3 §4.1.2: the spaces around it are removed with the line ends). Measured in
        /// Chromium 159 (case <c>hw-pre-line</c>): spaces collapse, each line feed starts a line, and the spaces at the
        /// start of that line are gone.
        /// </summary>
        private static string WithPreservedBreaks(Css.Cascade.ComputedStyle style, string text) =>
            style[Css.Properties.PropertyRegistry.Find("white-space-collapse")!] is Css.Values.CssKeyword { Name: "preserve-breaks" }
                ? text.Replace("\n", ForcedLineBreak, System.StringComparison.Ordinal)
                : text;

        /// <summary>White space between block boxes generates nothing (CSS 2.1 §9.2.2.1) when it collapses away; a segment
        /// break in it under <c>preserve-breaks</c> does not, and what Chromium makes of it is not measured: refused.</summary>
        private void RefuseBreakBetweenBlocks(StyledNode? dropped)
        {
            if (dropped is StyledText text && text.Text.Data.Contains('\n', System.StringComparison.Ordinal) && WithPreservedBreaks(text.Style, text.Text.Data) != text.Text.Data)
            {
                Refuse(HtmlStyleErrorCodes.Unsupported.Value, "'white-space-collapse: preserve-breaks' on a line feed between block boxes is not drawn in this version.", "preserve-breaks", text.Text.Position, "white-space-collapse");
            }
        }

        /// <summary>A <c>br</c> made block-level (<c>display: block</c>): refused, its print is not measured.</summary>
        private void BlockLineBreak(StyledElement element) =>
            Refuse(HtmlStyleErrorCodes.Unsupported.Value, $"'display: {element.Style[Css.Properties.PropertyRegistry.Display]}' on <br> is not drawn in this version: a line break is inline content.",
                element.Style[Css.Properties.PropertyRegistry.Display].ToString()!, element.Element.Position, "display");
    }
}
