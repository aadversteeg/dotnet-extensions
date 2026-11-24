using System.Collections.Generic;
using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;

namespace Ave.Extensions.Documents.Html.Boxes;

public static partial class HtmlBoxGenerator
{
    private sealed partial class Run
    {
        /// <summary>
        /// An <c>a</c> element with an <c>href</c> inside a line of text (HTML Standard §4.6.1: "the a element represents a
        /// hyperlink" when it has an href) becomes an inline group with the role of a link (<see cref="SemanticRole.Link"/>)
        /// around the element's content: its runs, images and the runs of the inline elements inside it, and first the empty
        /// run in the element's style that every inline element adds for its line height, so the lines are as tall as without
        /// the link. The target is the URI by THE RULE FOR A LINK'S URL IN HTML (<see cref="LinkUrls"/>), checked by THE RULE
        /// FOR A LINK'S URI (<see cref="LinkUris"/>), which is the only rule on which URIs are accepted. A link that the rules
        /// refuse, and a link inside a link, is refused at the element's start tag with the <c>href</c> and the reason;
        /// a link with a background, border, padding or margin of its own is a link group with that box (THE RULE FOR AN
        /// INLINE BOX IN HTML), also inside another element's box;
        /// under <see cref="UnsupportedPolicy.Warn"/> its content is drawn as that of any inline element. An <c>a</c> without
        /// <c>href</c> is no link (Chromium 159 writes no annotation for it, measured: case <c>lk-empty</c>).
        /// </summary>
        /// <returns>True when the link's frame is on the stack; false when the element is drawn as any inline element.</returns>
        private bool StartLink(Stack<object> stack, InlineFrame frame, StyledElement element)
        {
            if (!IsHtml(element, "a") || element.Element.Attribute("href") is not { } href)
            {
                return false;
            }

            var position = element.Element.Position;
            var fragment = LinkUrls.Fragment(href);
            var (uri, problem) = fragment is not null ? ("#" + fragment, null) : LinkUrls.Resolve(href, DocumentBase());
            problem ??= fragment is not null ? null : LinkUris.Problem(uri);
            if (frame.InLink)
            {
                problem = "it lies inside another link, and a link leads to one target";
            }

            if (problem is not null)
            {
                Refuse(HtmlStyleErrorCodes.Unsupported.Link, $"The link of <a href=\"{InputQuote.Of(href)}\"> is not made: {problem}.", href, position, "href");
                return false;
            }

            if (TextStyleOf(element) is not { } style || Deeper(frame.Depth + 1, position))
            {
                return false;
            }

            var id = NewId();
            var box = InlineBoxOf(element);
            List<Node> content = [new TextRun(NewId(), string.Empty, style, Source(position))];
            stack.Push(new InlineFrame(element, content, frame.Depth + 1)
            {
                Link = new LinkFrame(frame.Target, id, uri!, fragment, style) { Language = LanguageOf(element, frame.Element), Box = box },
                InLink = true,
                InBox = frame.InBox || box is not null,
            });
            return true;
        }

        /// <summary>
        /// The link's group, once its content is built, with its description by THE RULE FOR A LINK'S DESCRIPTION
        /// (<see cref="LinkDescription"/>): the element's <c>title</c>, else the text of its runs, else the alternative text of
        /// an image inside it, else the URI. A link whose content draws nothing (no character that does not collapse, no
        /// image and no group, which always draws: case <c>ib-link-cell</c>, a link around a span with a box) is no link and its content goes on as written: Chromium 159 writes no annotation for an empty <c>a</c>
        /// (measured: case <c>lk-empty</c>). A description with a character that text may not hold
        /// (<see cref="TextCharacters"/>, from a <c>title</c>) is refused at the start tag.
        /// </summary>
        private void FinishLink(InlineFrame frame, LinkFrame link)
        {
            var content = frame.Target;
            var html = frame.Element.Element;
            if (!content.Any(node => node is Box or InlineGroup || (node is TextRun run && !run.Text.All(IsCollapsible))))
            {
                if (link.Box is not null)
                {
                    Refuse(HtmlStyleErrorCodes.Unsupported.Link, $"The link of <a href=\"{InputQuote.Of(link.Uri)}\"> is not made: it has no content but a background, border, padding or margin of its own, which this version does not draw on an empty link; give the link a text, or leave out its box.",
                        link.Uri, html.Position, "href");
                }

                link.Parent.AddRange(content);
                return;
            }

            var text = string.Concat(content.OfType<TextRun>().Select(run => run.Text));
            var alternative = content.OfType<Box>().Select(box => box.Role).OfType<SemanticRole.FigureRole>().Select(figure => figure.AlternativeText)
                .FirstOrDefault(alt => !string.IsNullOrWhiteSpace(alt));
            var description = LinkDescription.Of(html.Attribute("title"), text, alternative, link.Uri);
            if (TextCharacters.IndexOfForbidden(description) is { } at)
            {
                Refuse(HtmlStyleErrorCodes.Unsupported.Link, $"The link of <a href=\"{InputQuote.Of(link.Uri)}\"> is not made: its description holds U+{(int)description[at]:X4}, which text may not hold.",
                    link.Uri, html.Position, "title");
                link.Parent.AddRange(content);
                return;
            }

            var target = link.Fragment is { } fragment ? Inside(link.Id, fragment, link.Uri, html.Position) : LinkTarget.Uri(link.Uri);
            link.Parent.Add(new InlineGroup(link.Id, SemanticRole.Link(target, description), link.Style, [.. content], Source(html.Position) with { Column = html.Position.Column })
            {
                Language = link.Language,
                Box = link.Box,
            });
            Placed(frame.Element, link.Id);
        }

        /// <summary>An <c>a</c> with an <c>href</c> that is not inside a line (a block-level or table display) is drawn as its
        /// box, without a link: a link is an inline group, so it is refused at its start tag.</summary>
        private void RefuseBlockLink(StyledElement element, bool isBlock)
        {
            if (isBlock && IsHtml(element, "a") && element.Element.Attribute("href") is { } href)
            {
                Refuse(HtmlStyleErrorCodes.Unsupported.Link, $"The link of <a href=\"{InputQuote.Of(href)}\"> is not made: the element is not inside a line of text (its display is not inline), and only a link inside a line is built in this version.",
                    href, element.Element.Position, "href");
            }
        }
    }
}
