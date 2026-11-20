using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Html.Parsing;
using Ave.Extensions.ErrorPaths;

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
        /// How white space collapses under a computed style (CSS Text 4 §3.1, <c>white-space-collapse</c>; the <c>white-space</c>
        /// shorthand sets it: normal and nowrap collapse, pre and pre-wrap preserve, pre-line preserves the segment breaks,
        /// break-spaces keeps spaces as break-spaces). The text goes to the model as written: collapsing in layout does the
        /// rest, as a line feed is a forced line break only where the mode preserves it.
        /// </summary>
        private static WhiteSpaceCollapse CollapseOf(Css.Cascade.ComputedStyle style) => style[Css.Properties.PropertyRegistry.Find("white-space-collapse")!] switch
        {
            Css.Values.CssKeyword { Name: "preserve" } => WhiteSpaceCollapse.Preserve,
            Css.Values.CssKeyword { Name: "preserve-breaks" } => WhiteSpaceCollapse.PreserveBreaks,
            Css.Values.CssKeyword { Name: "break-spaces" } => WhiteSpaceCollapse.BreakSpaces,
            _ => WhiteSpaceCollapse.Collapse,
        };

        /// <summary>Whether text wraps under a computed style (CSS Text 4 §6.1, <c>text-wrap-mode</c>; set by the <c>white-space</c>
        /// shorthand too: nowrap and pre do not wrap).</summary>
        private static TextWrapMode WrapOf(Css.Cascade.ComputedStyle style) =>
            style[Css.Properties.PropertyRegistry.Find("text-wrap-mode")!] is Css.Values.CssKeyword { Name: "nowrap" } ? TextWrapMode.NoWrap : TextWrapMode.Wrap;

        /// <summary>Whether a character of a text is white space that collapses away under a mode (CSS Text 4 §4.1.1):
        /// every collapsible character under collapse, all but the line feed under preserve-breaks, none where spaces are
        /// preserved.</summary>
        private static bool CollapsesUnder(char c, WhiteSpaceCollapse mode) => mode switch
        {
            WhiteSpaceCollapse.Collapse => IsCollapsible(c),
            WhiteSpaceCollapse.PreserveBreaks => c != '\n' && IsCollapsible(c),
            _ => false,
        };

        /// <summary>A tab in text whose spaces are preserved (<c>white-space-collapse: preserve</c> or
        /// <c>break-spaces</c>): refused at the text, with the first tab's offset in it, as the refusal of a character
        /// names it; under <see cref="UnsupportedPolicy.Warn"/> each tab is drawn as a space. A preserved tab moves the
        /// text to the next tab stop (CSS Text 3 §4.2), and tab stops are not built.</summary>
        /// <returns>Whether the text holds such a tab.</returns>
        private bool RefusePreservedTab(StyledText text)
        {
            var mode = CollapseOf(text.Style);
            var offset = text.Text.Data.IndexOf('\t', System.StringComparison.Ordinal);
            if (mode is not (WhiteSpaceCollapse.Preserve or WhiteSpaceCollapse.BreakSpaces) || offset < 0)
            {
                return false;
            }

            var name = mode == WhiteSpaceCollapse.Preserve ? "preserve" : "break-spaces";
            Refusals.Add(new Error(HtmlStyleErrorCodes.Unsupported.Value, $"A tab in text under 'white-space-collapse: {name}' is not drawn in this version: tab stops are not built.")
                .With(ErrorMetadataKeys.Value, name).With(ErrorMetadataKeys.Property, "white-space-collapse").With(ErrorMetadataKeys.Offset, offset)
                .WithSource(_sourceName, text.Text.Position));
            return true;
        }

        /// <summary>White space between block boxes generates nothing (CSS 2.1 §9.2.2.1) when it collapses away; where its
        /// mode preserves any of it (a line feed under <c>preserve-breaks</c>, any of it under <c>preserve</c> and
        /// <c>break-spaces</c>) it does not, and what Chromium makes of it is not measured: refused.</summary>
        private void RefuseBreakBetweenBlocks(StyledNode? dropped)
        {
            if (dropped is not StyledText text || CollapseOf(text.Style) is var mode && text.Text.Data.All(c => CollapsesUnder(c, mode)))
            {
                return;
            }

            var name = mode switch { WhiteSpaceCollapse.Preserve => "preserve", WhiteSpaceCollapse.PreserveBreaks => "preserve-breaks", _ => "break-spaces" };
            Refuse(HtmlStyleErrorCodes.Unsupported.Value, $"'white-space-collapse: {name}' on white space between block boxes is not drawn in this version.", name, text.Text.Position, "white-space-collapse");
        }

        /// <summary>A <c>br</c> made block-level (<c>display: block</c>): refused, its print is not measured.</summary>
        private void BlockLineBreak(StyledElement element) =>
            Refuse(HtmlStyleErrorCodes.Unsupported.Value, $"'display: {element.Style[Css.Properties.PropertyRegistry.Display]}' on <br> is not drawn in this version: a line break is inline content.",
                element.Style[Css.Properties.PropertyRegistry.Display].ToString()!, element.Element.Position, "display");
    }
}
