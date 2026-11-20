using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css.Values;
using Ave.Extensions.Documents.DocumentModel;

namespace Ave.Extensions.Documents.Html.Boxes;

public static partial class HtmlBoxGenerator
{
    private sealed partial class Run
    {
        /// <summary>Each element's parent, filled once over the whole tree before the first lookup.</summary>
        private Dictionary<StyledElement, StyledElement>? _parents;

        /// <summary>Each element's applied decorations, at most <see cref="ModelLimits.MaxDecorations"/>, and how many lines
        /// it would have without that limit.</summary>
        private readonly Dictionary<StyledElement, (ValueList<TextDecoration> Lines, int Count)> _applied = new(ReferenceEqualityComparer.Instance);

        /// <summary>
        /// The decorations applied to an element's text (CSS Text Decoration 3 §2: a decoration is "propagated to all
        /// in-flow children", through block boxes too, and not into an atomic inline such as an image): its parent's
        /// applied decorations, outermost first, then the lines of its own <c>text-decoration-line</c> in the order
        /// underline, overline, line-through, each in this element's font (family, size, weight, style) and its
        /// <c>text-decoration-color</c>, <c>currentcolor</c> being its computed <c>color</c> (Blink
        /// <c>ComputedStyle::AppliedTextDecorations</c>, <c>TextDecorationInfo::ResolveDecorationAt</c>). <c>none</c> adds
        /// nothing and removes nothing. A text with more than <see cref="ModelLimits.MaxDecorations"/> lines is refused at
        /// the start tag of the element it lies in; under <see cref="UnsupportedPolicy.Warn"/> it keeps the outermost
        /// ones. The ancestors are walked with a loop, never by recursion.
        /// </summary>
        private ValueList<TextDecoration> Decorations(StyledElement element)
        {
            var chain = new Stack<StyledElement>();
            for (var at = element; at is not null && !_applied.ContainsKey(at); at = Parent(at))
            {
                chain.Push(at);
            }

            while (chain.Count > 0)
            {
                var at = chain.Pop();
                var (inherited, count) = Parent(at) is { } parent ? _applied[parent] : ([], 0);
                var own = OwnDecorations(at);
                var lines = new List<TextDecoration>(inherited);
                for (var i = 0; i < own.Count && lines.Count < ModelLimits.MaxDecorations; i++)
                {
                    lines.Add(own[i]);
                }

                _applied[at] = ([.. lines], count + own.Count);
            }

            var applied = _applied[element];
            if (applied.Count > ModelLimits.MaxDecorations)
            {
                var html = element.Element;
                var count = applied.Count.ToString(CultureInfo.InvariantCulture);
                Refuse(HtmlStyleErrorCodes.Unsupported.Value,
                    $"The text of <{html.LocalName}> has {count} text decoration lines from its element and the elements around it; at most {ModelLimits.MaxDecorations} are drawn.",
                    count, html.Position, "text-decoration-line");
            }

            return applied.Lines;
        }

        /// <summary>The lines of an element's own <c>text-decoration-line</c>, in the order underline, overline,
        /// line-through (Blink <c>AppliedTextDecoration</c> per line, painted in that order); none where the element's face
        /// is missing (that failure is reported where its text style is made).</summary>
        private List<TextDecoration> OwnDecorations(StyledElement element)
        {
            var lines = new List<TextDecoration>(3);
            var value = Value(element, "text-decoration-line");
            ValueList<CssValue> keywords = value switch
            {
                CssKeyword { Name: not "none" } keyword => [keyword],
                CssList list => list.Items,
                _ => [],
            };
            if (keywords.Count == 0 || FaceStyleOf(element) is not { } face)
            {
                return lines;
            }

            // A refused colour is the initial one, currentcolor.
            var color = Value(element, "text-decoration-color") is { } specified && ColorOf(BoxProperties.Resolved(specified, element.Style)) is { } drawn ? drawn : face.Color;
            foreach (var (name, line) in new[] { ("underline", TextDecorationLine.Underline), ("overline", TextDecorationLine.Overline), ("line-through", TextDecorationLine.LineThrough) })
            {
                if (keywords.Contains(new CssKeyword(name)))
                {
                    lines.Add(new TextDecoration(line, color, face.FontFamily, face.FontSize, face.FontWeight, face.FontStyle));
                }
            }

            return lines;
        }

        /// <summary><c>text-decoration-skip-ink</c> of the element the text lies in (CSS Text Decoration 4 §2.10.4:
        /// inherited, and the text's own value decides, not the decorating element's); <c>all</c> is refused by the style
        /// sheet.</summary>
        private TextDecorationSkipInk SkipInkOf(StyledElement element) =>
            Value(element, "text-decoration-skip-ink") is CssKeyword { Name: "none" } ? TextDecorationSkipInk.None : TextDecorationSkipInk.Auto;

        private StyledElement? Parent(StyledElement element)
        {
            if (_parents is null)
            {
                _parents = new(ReferenceEqualityComparer.Instance);
                var pending = new Stack<StyledElement>();
                pending.Push(styled.Root);
                while (pending.Count > 0)
                {
                    var parent = pending.Pop();
                    foreach (var child in parent.Children)
                    {
                        if (child is StyledElement inner)
                        {
                            _parents[inner] = parent;
                            pending.Push(inner);
                        }
                    }
                }
            }

            return _parents.GetValueOrDefault(element);
        }
    }
}
