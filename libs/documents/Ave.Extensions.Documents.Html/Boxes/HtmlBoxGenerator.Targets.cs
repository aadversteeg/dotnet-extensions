using System;
using System.Collections.Generic;
using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;

namespace Ave.Extensions.Documents.Html.Boxes;

public static partial class HtmlBoxGenerator
{
    /// <summary>
    /// THE RULE FOR A LINK INSIDE AN HTML DOCUMENT: an <c>a href</c> whose URL starts with <c>#</c> (<see cref="LinkUrls.Fragment"/>)
    /// leads to the element the HTML Standard's "indicated part of the document" names for that fragment, in its order:
    /// <list type="number">
    /// <item>an empty fragment (<c>href="#"</c>) is the top of the document;</item>
    /// <item>else the first element in tree order whose <c>id</c> is the fragment, compared exactly (case and all), else the
    /// first <c>a</c> element whose <c>name</c> is the fragment;</item>
    /// <item>else the same two lookups with the fragment percent-decoded as UTF-8 (<see cref="LinkUrls.PercentDecoded"/>);</item>
    /// <item>else, when the decoded fragment is <c>top</c> in any ASCII case, the top of the document;</item>
    /// <item>else there is no target, and the link is refused at its start tag, as Chromium 159 writes no link for it.</item>
    /// </list>
    /// The top of the document is the root's box: its top on the first page. The element found is the target as it is: a
    /// later element with the same <c>id</c> is never tried. Its node is the box of a block-level element (also of the root
    /// and of a table and its row groups, rows and cells), the link group of an <c>a href</c>, and for any other inline
    /// element a span group (<see cref="SemanticRole.Span"/>) around its content, made only for an element a link leads to.
    /// An element that gets none of these (not rendered, an inline element that draws nothing, an image) is no target in
    /// this version, and the link to it is refused with the reason. The links
    /// get their nodes once the tree is built (<see cref="LinkTargets"/>), so a link may come before its target. The lookups
    /// cost one pass over the elements before the boxes are built, and only for a document with a link inside it; a
    /// fragment is at most as long as its <c>href</c>, which is refused above <see cref="LinkUris.MaxLength"/> characters.
    /// </summary>
    private sealed partial class Run
    {
        /// <summary>The provisional target of a link inside the document until <see cref="ResolveLinks"/>.</summary>
        private static readonly LinkTarget Provisional = LinkTarget.Node(new NodeId(-1));

        private readonly Dictionary<string, StyledElement> _ids = new(StringComparer.Ordinal);
        private readonly Dictionary<string, StyledElement> _names = new(StringComparer.Ordinal);
        private readonly Dictionary<StyledElement, NodeId?> _targets = new(ReferenceEqualityComparer.Instance);
        private readonly List<(NodeId Link, string Fragment, string Href, TextPosition Position)> _inside = [];

        /// <summary>One pass over the elements in tree order, with an explicit stack: the first element per <c>id</c> and the
        /// first <c>a</c> per <c>name</c>, then the element each link inside the document leads to, which becomes a target.</summary>
        private void FindTargets(StyledElement root)
        {
            var fragments = new List<string>();
            var pending = new Stack<StyledElement>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                var element = pending.Pop();
                var html = element.Element;
                if (html.Attribute("id") is { Length: > 0 } id)
                {
                    _ids.TryAdd(id, element);
                }

                if (IsHtml(element, "a") && html.Attribute("name") is { Length: > 0 } name)
                {
                    _names.TryAdd(name, element);
                }

                if (IsHtml(element, "a") && html.Attribute("href") is { } href && LinkUrls.Fragment(href) is { } fragment)
                {
                    fragments.Add(fragment);
                }

                for (var index = element.Children.Count - 1; index >= 0; index--)
                {
                    if (element.Children[index] is StyledElement child)
                    {
                        pending.Push(child);
                    }
                }
            }

            foreach (var fragment in fragments)
            {
                if (Indicated(fragment) is { Element: { } target })
                {
                    _targets.TryAdd(target, null);
                }
            }
        }

        /// <summary>The indicated part of the document for a fragment: the top of the document, an element, or neither.</summary>
        private (bool Top, StyledElement? Element) Indicated(string fragment)
        {
            if (fragment.Length == 0)
            {
                return (true, null);
            }

            if (Potential(fragment) is { } element)
            {
                return (false, element);
            }

            var decoded = LinkUrls.PercentDecoded(fragment);
            return Potential(decoded) is { } decodedElement ? (false, decodedElement)
                : (string.Equals(decoded, "top", StringComparison.OrdinalIgnoreCase), null);
        }

        private StyledElement? Potential(string fragment) =>
            _ids.TryGetValue(fragment, out var element) || _names.TryGetValue(fragment, out element) ? element : null;

        /// <summary>Records the node of an element a link leads to.</summary>
        private void Placed(StyledElement element, NodeId node)
        {
            if (_targets.ContainsKey(element))
            {
                _targets[element] = node;
            }
        }

        /// <summary>A link inside the document: recorded, with the provisional target until the tree is built.</summary>
        private LinkTarget Inside(NodeId link, string fragment, string href, TextPosition position)
        {
            _inside.Add((link, fragment, href, position));
            return Provisional;
        }

        /// <summary>An inline element a link leads to, other than an <c>a href</c>, one with a language of its own, or one with
        /// a box of its own (THE RULE FOR AN INLINE BOX IN HTML): its content goes into a span group, first the empty run in its
        /// style that every inline element adds for its line height. A box inside a link is refused at the element and the
        /// element is drawn without it.</summary>
        /// <returns>True when the span's frame is on the stack.</returns>
        private bool StartSpan(Stack<object> stack, InlineFrame frame, StyledElement element)
        {
            var language = LanguageOf(element, frame.Element);
            var box = InlineBoxOf(element);
            if (box is not null && frame.InLink)
            {
                Refuse(HtmlStyleErrorCodes.Unsupported.InlineBox, $"The background, border, padding or margin of <{element.Element.LocalName}> inside a link is not drawn in this version; give the box to an element around the link, or leave it out.",
                    box.ToString(), element.Element.Position, "background-color");
                box = null;
            }

            var isTarget = _targets.ContainsKey(element);
            if (!(isTarget || language is not null || box is not null) || TextStyleOf(element) is not { } style || Deeper(frame.Depth + 1, element.Element.Position))
            {
                return false;
            }

            List<Node> content = [new TextRun(NewId(), string.Empty, style, Source(element.Element.Position))];
            var id = NewId();
            stack.Push(new InlineFrame(element, content, frame.Depth + 1)
            {
                Span = new SpanFrame(frame.Target, id, style, language) { Box = box, IsTarget = isTarget },
                InLink = frame.InLink,
                InBox = frame.InBox || box is not null,
            });
            return true;
        }

        /// <summary>
        /// THE RULE FOR AN ELEMENT'S LANGUAGE IN HTML: the language of an element (HTML Standard §3.2.6.2: its <c>xml:lang</c>
        /// in the XML namespace, else its <c>lang</c>, else its parent's; the root's is the document's) is written on its
        /// box or, for an inline element, on a span group made for it, where it differs from its parent's (compared without
        /// regard to case). It is written as given when it is a well-formed tag (<see cref="LanguageTags"/>); a tag that is
        /// not, and the empty tag (the language is unknown), is refused at the start tag and not written: the element keeps
        /// its parent's language, as no language is guessed. Null when nothing is written.
        /// </summary>
        private string? LanguageOf(StyledElement element, StyledElement parent)
        {
            var language = element.Language;
            if (language is null || string.Equals(language, parent.Language, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (LanguageTags.IsWellFormed(language))
            {
                return language;
            }

            var reason = language.Length == 0
                ? "the empty tag says the language is unknown, which a tagged PDF cannot declare (PDF/UA-1 asks a language tag)"
                : $"it is not a well-formed language tag (BCP 47 syntax, at most {LanguageTags.MaxLength} characters)";
            Refuse(HtmlStyleErrorCodes.Unsupported.Language, $"The language '{InputQuote.Of(language)}' of <{element.Element.LocalName}> is not written: {reason}; the element keeps its parent's language.",
                language, element.Element.Position, "lang");
            return null;
        }

        /// <summary>The span group, once its content is built; a span without a box whose content draws nothing has no line
        /// part to lead to, so its content goes on as written and the element is no target. A span with a box is a group also
        /// when it is empty: its box is drawn and takes its room (case <c>ib-space</c>). The group has the role of a span when a
        /// link leads to it or it has a language of its own; else it has no role and opens no structure element. Its source
        /// is the element's start tag, line and column, where a layout refusal of its box
        /// (<c>Documents.Input.Unsupported.InlineBox</c>) points.</summary>
        private void FinishSpan(InlineFrame frame, SpanFrame span)
        {
            var content = frame.Target;
            if (span.Box is null && !content.Any(node => node is Box or InlineGroup || (node is TextRun run && !run.Text.All(IsCollapsible))))
            {
                span.Parent.AddRange(content);
                return;
            }

            var role = span.IsTarget || span.Language is not null ? SemanticRole.Span : null;
            var position = frame.Element.Element.Position;
            span.Parent.Add(new InlineGroup(span.Id, role, span.Style, [.. content], Source(position) with { Column = position.Column }) { Language = span.Language, Box = span.Box });
            if (role is not null)
            {
                Placed(frame.Element, span.Id);
            }
        }

        /// <summary>The tree with every link inside the document leading to its target's node; a link without one is
        /// refused at its start tag and its content stays as that of any inline element. A link that leads to a link
        /// refused here is refused too.</summary>
        private Box ResolveLinks(Box root)
        {
            if (_inside.Count == 0)
            {
                return root;
            }

            var targets = new Dictionary<NodeId, LinkTarget?>();
            foreach (var (link, fragment, href, position) in _inside)
            {
                var (top, element) = Indicated(fragment);
                var node = top ? root.Id : element is not null && _targets.TryGetValue(element, out var placed) ? placed : null;
                if (node is null)
                {
                    var reason = element is null
                        ? $"no element has the id '{InputQuote.Of(fragment)}', and no a element that name"
                        : $"the element <{element.Element.LocalName}> it leads to makes no box or text a link can lead to in this version (it is not rendered, draws nothing, or is an image)";
                    Refuse(HtmlStyleErrorCodes.Unsupported.Link, $"The link of <a href=\"{InputQuote.Of(href)}\"> is not made: {reason}.", href, position, "href");
                }

                targets[link] = node is { } target ? LinkTarget.Node(target) : null;
            }

            // A link to a link refused here leads nowhere either; each round refuses at least one more, so at most one round
            // per link.
            var refused = targets.Where(pair => pair.Value is null).Select(pair => pair.Key).ToHashSet();
            for (var changed = refused.Count > 0; changed;)
            {
                changed = false;
                foreach (var (link, fragment, href, position) in _inside)
                {
                    if (targets[link] is LinkTarget.NodeTarget to && refused.Contains(to.Value) && refused.Add(link))
                    {
                        Refuse(HtmlStyleErrorCodes.Unsupported.Link, $"The link of <a href=\"{InputQuote.Of(href)}\"> is not made: it leads to a link that is not made.", href, position, "href");
                        targets[link] = null;
                        changed = true;
                    }
                }
            }

            return LinkTargets.Resolve(root, targets);
        }
    }
}
