using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css.Properties;
using Ave.Extensions.Documents.Css.Values;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Html.Parsing;
using Ave.Extensions.ErrorPaths;

namespace Ave.Extensions.Documents.Html.Boxes;

/// <summary>
/// Lists (CSS Lists 3, HTML Standard §4.4.5–§4.4.8, §15.3.7): <c>ul</c>, <c>ol</c>, <c>menu</c> and <c>dir</c> at
/// <c>display: block</c> become the document model's list, every child of <c>display: list-item</c> one of its items, with
/// a label and a body (ISO 32000-1 §14.8.4.3.3: tagged <c>L</c>, <c>LI</c>, <c>Lbl</c>, <c>LBody</c>). The front-end
/// computes each label's text; the list-item algorithm places it outside the item, as Chromium draws an outside marker.
/// </summary>
/// <remarks>
/// <para>The numbers are the <c>list-item</c> counter (CSS Lists 3 §4.6), counted in one pass over the document before the
/// boxes are built: a list element instantiates it (HTML Standard §15.3.7, <c>counter-reset: list-item</c> on the list
/// elements), an item with no counter in scope instantiates one at itself (CSS Lists 3 §4.4.1), whose scope is the
/// element, its descendants and its following siblings with theirs (§4.4.1, counter inheritance); each item adds one, or
/// takes its <c>value</c> (HTML Standard §4.4.8). <c>ol start</c> sets the first number, <c>reversed</c> counts down from
/// the number of its items (§4.4.5). Measured: cases <c>li-ol-attrs</c>, <c>li-ol-nested</c>, <c>li-counter-styles</c>,
/// <c>li-types</c>, <c>li-div-list-item</c> (the counter runs on through the body's children, past a paragraph).</para>
/// <para>An item outside a list element (a bare <c>li</c>, or a <c>div</c> of <c>display: list-item</c>) draws its marker as in
/// a list (case <c>li-bare</c>: a disc). The model has no item outside a list, and PDF none either (an <c>LI</c> is only a
/// child of an <c>L</c>), so each run of such items among siblings is wrapped in a list box of its own, without margin or
/// padding, tagged <c>L</c> with the first item's numbering: the same print, and the structure a reader can follow.</para>
/// </remarks>
public static partial class HtmlBoxGenerator
{
    /// <summary>The list elements (HTML Standard §15.3.7: their <c>counter-reset: list-item</c>, their padding).</summary>
    private static readonly HashSet<string> ListElements = new(StringComparer.Ordinal) { "ul", "ol", "menu", "dir" };

    /// <summary>One <c>list-item</c> counter (CSS Lists 3 §4.4): the element that made it, how it counts and its value.</summary>
    private sealed class ListCounter(StyledElement creator, StyledElement? parent, bool byList, int step, long value)
    {
        public StyledElement Creator { get; } = creator;

        /// <summary>The parent of the element that made it.</summary>
        public StyledElement? Parent { get; } = parent;

        /// <summary>Made by a list element, not by an item.</summary>
        public bool ByList { get; } = byList;

        /// <summary>+1, or −1 for a reversed list.</summary>
        public int Step { get; } = step;

        public long Value { get; set; } = value;

        /// <summary>An item of this counter draws its number.</summary>
        public bool ShowsNumber { get; set; }

        /// <summary>Items outside an <c>ol</c> with a <c>value</c> attribute.</summary>
        public List<StyledElement> Values { get; } = [];
    }

    private sealed partial class Run
    {
        private readonly Dictionary<StyledElement, long> _ordinals = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<StyledElement, List<Error>> _listRefusals = new(ReferenceEqualityComparer.Instance);

        private static bool IsListElement(StyledElement element) =>
            element.Element.Namespace == ElementNamespace.Html && ListElements.Contains(element.Element.LocalName) && element.Style[PropertyRegistry.Display] is CssKeyword { Name: "block" };

        private static bool IsListItem(StyledElement element) => element.Style[PropertyRegistry.Display] is CssKeyword { Name: "list-item" };

        private static string ListStyleType(StyledElement element) =>
            element.Style[PropertyRegistry.Find("list-style-type")!] is CssKeyword keyword ? keyword.Name : "disc";

        /// <summary>A counter style that draws a number (CSS Counter Styles 3 §6), not a symbol or nothing.</summary>
        private static bool IsNumber(string type) => type is not ("disc" or "circle" or "square" or "none");

        private void ListRefusal(StyledElement element, string message, string value)
        {
            var error = new Error(HtmlStyleErrorCodes.Unsupported.List, message).With(ErrorMetadataKeys.Value, value).WithSource(_sourceName, element.Element.Position);
            if (!_listRefusals.TryGetValue(element, out var errors))
            {
                _listRefusals[element] = errors = [];
            }

            errors.Add(error);
        }

        /// <summary>The refusals the numbering found on an element, reported when box generation meets it, in document
        /// order with the others.</summary>
        private void ReportListRefusals(StyledElement element)
        {
            if (_listRefusals.Remove(element, out var errors))
            {
                Refusals.AddRange(errors);
            }
        }

        /// <summary>
        /// The number of every item, in one pass over the rendered elements in document order (an explicit stack). Refused,
        /// as no measured case shows them: a <c>start</c> or <c>value</c> beyond 32 bits, and a number that leaves them; a
        /// <c>value</c> in a reversed list; an item outside a list element that a list element's counter would number
        /// (inside a list's item, or after a list among its siblings); a number on such an item that continues the
        /// counter of an item with another parent; a <c>value</c> outside an <c>ol</c> where the counter draws numbers.
        /// </summary>
        private void Number(StyledElement root)
        {
            var counters = new List<ListCounter>();
            var stack = new Stack<(StyledElement Element, ListCounter? Current, int Next)>();
            stack.Push((root, null, 0));
            while (stack.Count > 0)
            {
                var (element, current, next) = stack.Pop();
                if (next >= element.Children.Count)
                {
                    continue;
                }

                if (element.Children[next] is not StyledElement child || Display(child) == "none")
                {
                    stack.Push((element, current, next + 1));
                    continue;
                }

                if (IsListElement(child))
                {
                    current = Counter(child, element);
                    counters.Add(current);
                }
                else if (IsListItem(child))
                {
                    if (current is null)
                    {
                        current = new ListCounter(child, element, byList: false, step: 1, value: 0);
                        counters.Add(current);
                    }

                    Ordinal(child, element, current);
                }

                // A counter the child made is in scope for its following siblings and its content; one made inside the
                // child's content is not in scope for the child's siblings (its own frame keeps it).
                stack.Push((element, current, next + 1));
                stack.Push((child, current, 0));
            }

            foreach (var counter in counters.Where(counter => counter.ShowsNumber))
            {
                counter.Values.ForEach(item => ListRefusal(item, "The value attribute of a list item outside an ol is not drawn in this version where its list shows numbers.", "value"));
            }
        }

        /// <summary>The counter of a list element: an <c>ol</c>'s <c>start</c> (default 1, or the number of its items when
        /// <c>reversed</c>, HTML Standard §4.4.5); the others count from 1.</summary>
        private ListCounter Counter(StyledElement list, StyledElement parent)
        {
            var html = list.Element;
            var reversed = html.LocalName == "ol" && html.Attribute("reversed") is not null;
            var step = reversed ? -1 : 1;
            long start = reversed ? list.Children.Count(child => child is StyledElement item && Display(item) != "none" && IsListItem(item)) : 1;
            if (html.LocalName == "ol" && html.Attribute("start") is { } text && Int32Attribute(list, "start", text) is { } value)
            {
                start = value;
            }

            return new ListCounter(list, parent, byList: true, step, start - step);
        }

        /// <summary>An integer attribute (HTML Standard §2.3.4.1); null when it does not parse, and refused beyond 32 bits.</summary>
        private long? Int32Attribute(StyledElement element, string name, string text)
        {
            if (AttributeValues.Integer(text) is null)
            {
                return null;
            }

            if (AttributeValues.Int32Integer(text) is { } value)
            {
                return value;
            }

            ListRefusal(element, $"The {name} attribute '{text}' is beyond a 32-bit integer and is not drawn in this version.", name);
            return null;
        }

        private void Ordinal(StyledElement item, StyledElement parent, ListCounter counter)
        {
            var bare = !IsListElement(parent);
            var type = ListStyleType(item);
            if (bare && counter.ByList)
            {
                ListRefusal(item, "A list item outside a list element that the numbering of a list would count is not drawn in this version.", "list-item");
            }
            else if (bare && IsNumber(type) && !ReferenceEquals(counter.Parent, parent))
            {
                ListRefusal(item, "The number of a list item outside a list element that continues the numbering of an item with another parent is not drawn in this version.", "list-item");
            }

            var html = item.Element;
            var value = html.Namespace == ElementNamespace.Html && html.LocalName == "li" && html.Attribute("value") is { } text ? Int32Attribute(item, "value", text) : null;
            var inOl = counter.ByList && counter.Creator.Element.LocalName == "ol";
            if (value is not null && inOl && counter.Step < 0)
            {
                ListRefusal(item, "The value attribute of an item of a reversed list is not drawn in this version.", "value");
            }
            else if (value is not null && !inOl)
            {
                counter.Values.Add(item);
            }

            counter.Value = value is not null && inOl ? value.Value : counter.Value + counter.Step;
            if (counter.Value is > int.MaxValue or < int.MinValue)
            {
                ListRefusal(item, "The number of this list item is beyond a 32-bit integer and is not drawn in this version.", counter.Value.ToString(CultureInfo.InvariantCulture));
                counter.Value = Math.Clamp(counter.Value, int.MinValue, int.MaxValue);
            }

            counter.ShowsNumber |= IsNumber(type);
            _ordinals[item] = counter.Value;
        }

        /// <summary>
        /// The text of a number in a counter style (CSS Counter Styles 3 §6.1, the predefined styles; their ranges and the
        /// fallback to <c>decimal</c> outside them, §3.1.4), with the suffix ". " (§6.1). Measured (cases
        /// <c>li-counter-styles</c>, <c>li-types</c>): <c>decimal</c> with a hyphen-minus before a negative number;
        /// <c>decimal-leading-zero</c> padded to two characters with the sign counted (−5 is "-5"); the alphabetic styles
        /// from 1 (0 and below in decimal); the roman styles from 1 to 3999 (others in decimal).
        /// </summary>
        internal static string CounterText(string type, long number) => CounterValueText(type, number) + ". ";

        /// <summary>
        /// The text of a number in a counter style without a suffix, as <c>counter()</c> draws it (CSS Lists 3 §4.7; CSS
        /// Counter Styles 3 §6.1): the numeric styles as <see cref="CounterText"/>, and the symbols whatever the number,
        /// <c>disc</c> U+2022, <c>circle</c> U+25E6, <c>square</c> U+25A0, <c>none</c> nothing (Chromium 159, measured: case
        /// <c>gc-counter-styles</c>, for −1, 0, 1 and 2).
        /// </summary>
        internal static string CounterValueText(string type, long number)
        {
            var decimalText = number.ToString(CultureInfo.InvariantCulture);
            return type switch
            {
                "disc" => "\u2022",
                "circle" => "\u25E6",
                "square" => "\u25A0",
                "none" => string.Empty,
                "decimal-leading-zero" => decimalText.Length < 2 ? "0" + decimalText : decimalText,
                "lower-roman" when number is >= 1 and <= 3999 => Roman(number).ToLowerInvariant(),
                "upper-roman" when number is >= 1 and <= 3999 => Roman(number),
                "lower-alpha" or "lower-latin" when number >= 1 => Alphabetic(number, 'a'),
                "upper-alpha" or "upper-latin" when number >= 1 => Alphabetic(number, 'A'),
                _ => decimalText,
            };

            // §6.1 upper-roman: additive, M CM D CD C XC L XL X IX V IV I.
            static string Roman(long number)
            {
                (int Weight, string Symbol)[] symbols = [(1000, "M"), (900, "CM"), (500, "D"), (400, "CD"), (100, "C"), (90, "XC"), (50, "L"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")];
                var builder = new System.Text.StringBuilder();
                foreach (var (weight, symbol) in symbols)
                {
                    for (; number >= weight; number -= weight)
                    {
                        builder.Append(symbol);
                    }
                }

                return builder.ToString();
            }

            // §3.1.1 alphabetic: bijective base 26 (1 a, 26 z, 27 aa); at most seven letters for a 32-bit number.
            static string Alphabetic(long number, char first)
            {
                var letters = new Stack<char>();
                for (; number > 0; number = (number - 1) / 26)
                {
                    letters.Push((char)(first + ((number - 1) % 26)));
                }

                return new string([.. letters]);
            }
        }

        /// <summary>The numbering of a list, for its tag (ISO 32000-1 Table 347): its own <c>list-style-type</c>
        /// (<c>decimal-leading-zero</c> as Decimal, the latin styles as the alphabetic ones).</summary>
        private static ListNumbering NumberingOf(string type) => type switch
        {
            "disc" => ListNumbering.Disc,
            "circle" => ListNumbering.Circle,
            "square" => ListNumbering.Square,
            "decimal" or "decimal-leading-zero" => ListNumbering.Decimal,
            "lower-roman" => ListNumbering.LowerRoman,
            "upper-roman" => ListNumbering.UpperRoman,
            "lower-alpha" or "lower-latin" => ListNumbering.LowerAlpha,
            "upper-alpha" or "upper-latin" => ListNumbering.UpperAlpha,
            _ => ListNumbering.None,
        };

        /// <summary>
        /// The item box: its label (none for <c>list-style-type: none</c>, a symbol for <c>disc</c>, <c>circle</c> and
        /// <c>square</c> whose tag carries U+2022, U+25E6 or U+25AA as replacement text, else the number's text) and its
        /// body with the item's content. The label is in the item's own font, size and colour, not its first line's
        /// (case <c>li-font</c>: a 16 pt "1." beside 16 pt text, a red one beside red text), without decorations, its
        /// spaces kept and on one line (the marker's <c>white-space: pre</c>, CSS Lists 3 §3.2).
        /// </summary>
        private Box Item(BlockFrame block, LayoutAlgorithmId layout, ValueList<Node> children, BoxStyle style)
        {
            var element = block.Element;
            var source = Source(element.Element.Position);
            var ids = block.ItemIds!.Value;
            var bodyChildren = layout == LayoutAlgorithmId.Inline
                ? [new Box(ids.Lines, SemanticRole.Generic, LayoutAlgorithmId.Inline, Inherited(element) with { Text = TextStyleOf(element) }, children, source)]
                : children;
            var body = new Box(ids.Body, SemanticRole.ListBody, LayoutAlgorithmId.Flow, BoxStyle.Initial, bodyChildren, source);
            var label = Label(element, ids.Label, ids.Mark, source);
            return new Box(block.Id, SemanticRole.ListItem, LayoutAlgorithmId.ListItem, style, label is null ? [body] : [label, body], source);
        }

        private Box? Label(StyledElement element, NodeId id, NodeId mark, SourceLocation source)
        {
            var type = ListStyleType(element);
            if (type == "none" && element.Marker is { Text: not null })
            {
                Refuse(HtmlStyleErrorCodes.Unsupported.List, $"The content of the ::marker of <{element.Element.LocalName}>, whose list-style-type is none, is not drawn in this version: remove list-style-type: none.", "content", element.Element.Position);
            }

            // CSS Lists 3 §3.1: a ::marker rule gives the marker its colour, weight and style, a shape as a number, and a
            // content of strings its text (cases gc-marker-color, gc-marker-font, gc-marker-content).
            var styled = element.Marker is { } marker ? element with { Style = marker.Style } : element;
            if (type != "none" && element.Marker is { Text: { } markerText, Position: { } at })
            {
                RefuseRightToLeft(markerText, _ => at);
            }

            if (type == "none" || FaceStyleOf(styled) is not { } face)
            {
                return null;
            }

            // The text of the label is checked for glyphs as it is drawn, in the marker's face: a content of strings at its
            // declaration, as any generated text; a number at the item's start tag. A bullet is a shape and needs none.
            if (element.Marker is { Text: { } written, Position: { } declared })
            {
                RefuseMissingGlyphs(written, styled, _ => declared);
            }
            else if (type is not ("disc" or "circle" or "square"))
            {
                RefuseMissingGlyphs(CounterText(type, _ordinals.GetValueOrDefault(element)), styled, _ => element.Element.Position);
            }

            if (TextStyleOf(element) is { Decorations.Count: > 0 })
            {
                Refuse(HtmlStyleErrorCodes.Unsupported.List, $"The marker of the decorated list item <{element.Element.LocalName}> is not drawn in this version.", "text-decoration", element.Element.Position);
            }

            var style = face with { WhiteSpaceCollapse = WhiteSpaceCollapse.Preserve, TextWrapMode = TextWrapMode.NoWrap };
            var (replacement, content) = element.Marker?.Text is { } text ? ((string?)null, (Node)new TextRun(mark, text, style, source)) : type switch
            {
                "disc" => ("•", (Node)new MarkerSymbol(mark, MarkerShape.Disc, face, source)),
                "circle" => ("◦", new MarkerSymbol(mark, MarkerShape.Circle, face, source)),
                "square" => ("▪", new MarkerSymbol(mark, MarkerShape.Square, face, source)),
                _ => ((string?)null, new TextRun(mark, CounterText(type, _ordinals.GetValueOrDefault(element)), style, source)),
            };
            return new Box(id, SemanticRole.ListLabel(replacement), LayoutAlgorithmId.Inline, BoxStyle.Initial with { Text = style }, [content], source);
        }

        /// <summary>A list element's box: the list with its own numbering; an empty list, which has no item to tag, a
        /// plain block (it draws only its margins, padding and border, as any block).</summary>
        private Box ListBox(BlockFrame block, ValueList<Node> children, BoxStyle style)
        {
            var element = block.Element;
            var role = children.Count == 0 ? SemanticRole.Generic : SemanticRole.List(NumberingOf(ListStyleType(element)));
            return new Box(block.Id, role, LayoutAlgorithmId.Flow, style, children, Source(element.Element.Position));
        }

        /// <summary>
        /// A child of a list element that is not a list item: refused (the model's list holds items only), and left out
        /// under <see cref="UnsupportedPolicy.Warn"/>; white space between the items is no box (CSS 2.1 §9.2.1.1).
        /// </summary>
        private bool RefusedInList(BlockFrame block, StyledNode node)
        {
            if (block.Kind != FrameKind.List || (node is StyledElement element && IsListItem(element)))
            {
                return false;
            }

            if (IsCollapsibleOnly(node))
            {
                return true;
            }

            var what = node is StyledElement other ? $"<{other.Element.LocalName}>" : "text";
            Refuse(HtmlStyleErrorCodes.Unsupported.List, $"The {what} inside the list <{block.Element.Element.LocalName}> is not drawn in this version: a list holds only list items.", node is StyledElement named ? named.Element.LocalName : "text", Position(node));
            return true;
        }

        /// <summary>Opens the list box around a run of items outside a list element (see the remarks above), or keeps the
        /// open one; true when the item goes in.</summary>
        private bool OpenImplied(BlockFrame block, StyledElement item)
        {
            if (block.Implied is not null)
            {
                return true;
            }

            if (Deeper(block.Depth + 1, item.Element.Position))
            {
                return false;
            }

            block.Implied = (NewId(), NumberingOf(ListStyleType(item)), [], Source(item.Element.Position));
            return true;
        }

        private void CloseImplied(BlockFrame block)
        {
            if (block.Implied is not { } implied)
            {
                return;
            }

            block.Implied = null;
            block.Nodes.Add(new Box(implied.Id, SemanticRole.List(implied.Numbering), LayoutAlgorithmId.Flow, BoxStyle.Initial, [.. implied.Items], implied.Source));
        }

        /// <summary>The items of a list beyond the model's limits fail under every policy: there is no list to draw them in.</summary>
        private bool ListTooLarge(StyledElement element, int lists, int? items = null)
        {
            if (lists > ModelLimits.MaxListDepth)
            {
                Failures.Add(new Error(HtmlStyleErrorCodes.Unsupported.List, $"Lists may nest at most {ModelLimits.MaxListDepth} levels deep.")
                    .With(ErrorMetadataKeys.Value, ModelLimits.MaxListDepth.ToString(CultureInfo.InvariantCulture)).WithSource(_sourceName, element.Element.Position));
                return true;
            }

            items ??= element.Children.Count(child => child is StyledElement item && Display(item) != "none" && IsListItem(item));
            if (items > ModelLimits.MaxListItems)
            {
                Failures.Add(new Error(HtmlStyleErrorCodes.Unsupported.List, $"A list has at most {ModelLimits.MaxListItems} items.")
                    .With(ErrorMetadataKeys.Value, items.Value.ToString(CultureInfo.InvariantCulture)).WithSource(_sourceName, element.Element.Position));
                return true;
            }

            return false;
        }
    }
}
