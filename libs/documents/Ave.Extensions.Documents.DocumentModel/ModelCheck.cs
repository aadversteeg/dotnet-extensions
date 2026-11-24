using System.Collections.Generic;
using System.Globalization;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents.DocumentModel;

/// <summary>
/// Checks a document model against the value ranges and the nesting limit of <see cref="ModelLimits"/>, which the code
/// front-end enforces on what an author writes. Layout runs it first, because a model may also be built by hand: a
/// model is not trusted, and no value in it may make layout throw or run without bound. The same error codes as the
/// front-end, at the node's source location.
/// </summary>
public static class ModelCheck
{
    /// <summary>
    /// The document, or the first problem in it. One pass over the page style, the page areas and every node, with an
    /// explicit stack, so a model nested far deeper than the limit is refused without running out of stack; the work is
    /// linear in the size of the model (each text is scanned once for its characters).
    /// </summary>
    /// <param name="document">The document.</param>
    /// <returns>The same document, or the first problem.</returns>
    public static Result<StyledDocument, Error> Check(StyledDocument document)
    {
        var problem = Page(document.Page) ?? Nodes(document) ?? Targets(document);
        return problem is null ? Result<StyledDocument, Error>.Success(document) : Result<StyledDocument, Error>.Failure(problem.Value);
    }

    /// <summary>The page: a positive width and height of at most <see cref="ModelLimits.MaxLength"/>, margins finite, not
    /// negative and at most that long, header and footer heights positive and at most that long, and how far each starts
    /// in the page margin (<see cref="PageArea.StartInMargin"/>) not negative and at most the left margin, the body clip's
    /// inset (<see cref="PageStyle.BodyClipInset"/>) 0 or more and at most the page area's sides.</summary>
    private static Error? Page(PageStyle page)
    {
        if (!(IsPositiveAndBounded(page.Width) && IsPositiveAndBounded(page.Height)))
        {
            return Problem(DocumentErrorCodes.Input.Invalid.PageSize, $"The page width and height must be positive and at most {ModelLimits.MaxLength}.", $"{page.Width}x{page.Height}", "size", null);
        }

        if (Sides(page.Margins, "margin", null, DocumentErrorCodes.Input.Invalid.Length) is { } margin)
        {
            return margin;
        }

        var inset = page.BodyClipInset;
        if (!(inset.Width >= 0 && inset.Height >= 0 && inset.Width <= page.AreaWidth.Points && inset.Height <= page.AreaHeight.Points))
        {
            return Problem(DocumentErrorCodes.Input.Invalid.Length, "The body clip's inset must be 0 or more and at most the page area's width and height.",
                inset.ToString(), "body-clip-inset", null);
        }

        foreach (var area in new[] { page.Template.Header, page.Template.Footer, page.FirstPage?.Header, page.FirstPage?.Footer })
        {
            if (area?.Height is { } height && !IsPositiveAndBounded(height))
            {
                return Problem(DocumentErrorCodes.Input.Invalid.Length, $"A header or footer height must be a positive length of at most {ModelLimits.MaxLength}.", height.ToString(), "height", area.Content);
            }

            if (area is not null && !(area.StartInMargin.Points >= 0 && area.StartInMargin.Points <= page.Margins.Left.Points))
            {
                return Problem(DocumentErrorCodes.Input.Invalid.Length, "How far a header or footer starts in the page margin must be 0 or more and at most the left margin.",
                    area.StartInMargin.ToString(), "start-in-margin", area.Content);
            }
        }

        return null;
    }

    /// <summary>Every node of the body and of the page areas, each root at level 0 (<see cref="ModelLimits.MaxNestingDepth"/>),
    /// and where each row of page-margin boxes is (<see cref="MarginBoxes"/>).</summary>
    private static Error? Nodes(StyledDocument document)
    {
        var pending = new Stack<(Node Node, int Depth, Place Place, bool InLink)>();
        pending.Push((document.Root, 0, Place.Other, false));
        foreach (var area in new[] { document.Page.Template.Header, document.Page.Template.Footer, document.Page.FirstPage?.Header, document.Page.FirstPage?.Footer })
        {
            if (area is not null)
            {
                pending.Push((area.Content, 0, area.Height is null ? Place.Other : Place.FixedArea, false));
            }
        }

        while (pending.Count > 0)
        {
            var (node, depth, place, inLink) = pending.Pop();
            if (depth > ModelLimits.MaxNestingDepth)
            {
                return Problem(DocumentErrorCodes.Input.Invalid.TooDeep, $"Boxes may nest at most {ModelLimits.MaxNestingDepth} levels deep.",
                    ModelLimits.MaxNestingDepth.ToString(CultureInfo.InvariantCulture), "nesting", node);
            }

            var problem = node switch
            {
                Box box => Box(box),
                TextRun run => Text(run.Style, run) ?? Characters(run) ?? PreservedTab(run),
                InlineRun run => Text(run.Style, run),
                ImageNode image => ImageSize(image),
                InlineGroup group => Group(group, place, inLink),
                _ => null,
            };
            if (problem is not null)
            {
                return problem;
            }

            if (node is Box paragraph && paragraph.Layout == LayoutAlgorithmId.Inline && AtomicInlines(paragraph.Children) is { } atomic)
            {
                return atomic;
            }

            if (node is InlineGroup inGroup && AtomicInlines(inGroup.Children) is { } atomicInGroup)
            {
                return atomicInGroup;
            }

            if (node is InlineGroup grouping)
            {
                // A group is one level deeper than its parent, so groups nest no deeper than boxes do.
                foreach (var child in grouping.Children)
                {
                    pending.Push((child, depth + 1, Place.InLine, inLink || grouping.Role is SemanticRole.LinkRole));
                }
            }

            if (node is Box placed && MarginBoxes(placed, place) is { } marginBoxes)
            {
                return marginBoxes;
            }

            if (node is Box parent)
            {
                // A child box is one level deeper, except the parts of a table: a table is one level with its row groups,
                // rows and cells (the front-end counts a table, or a row of blocks, as one element), and the content of a
                // cell is the next level. Runs and images are content of their box, no level of their own; a box among the runs of
                // an inline box (an image in a line) is one level deeper, as any child box.
                var isRow = parent.Style.TableDisplay == TableDisplay.Row;
                var childPlace = parent.Layout == LayoutAlgorithmId.MarginBoxes ? Place.InMarginBoxes
                    : parent.Layout == LayoutAlgorithmId.Inline ? Place.InLine
                    : Place.Other;
                foreach (var child in parent.Children)
                {
                    var isLevel = child is Box { Style.TableDisplay: TableDisplay.None } or InlineGroup && !isRow;
                    pending.Push((child, isLevel ? depth + 1 : depth, childPlace, false));
                }
            }
        }

        return null;
    }

    /// <summary>
    /// THE RULE FOR A LINK INSIDE THE DOCUMENT: a link's node (<see cref="LinkTarget.Node"/>) is a box or an inline group
    /// of the body, not inside a box with the <see cref="SemanticRole.Decorative"/> role (an artifact: a reader skips it,
    /// so no link may lead into it), and not in a page area (a page header or footer is drawn on every page, a pagination
    /// artifact; it has no one place). A run is no target: an inline element a link leads to is an inline group (a span),
    /// so its place is that of its group's first line part. At most <see cref="ModelLimits.MaxLinkTargets"/> nodes are
    /// targets. Refused at the link, under <c>Input.Invalid.Link</c>. One more pass over the body and the page areas, with an
    /// explicit stack; the first pass has bounded their depth.
    /// </summary>
    private static Error? Targets(StyledDocument document)
    {
        var targets = new HashSet<NodeId>();
        var links = new List<(InlineGroup Link, NodeId Target)>();
        var pending = new Stack<(Node Node, bool IsBody)>();
        foreach (var area in new[] { document.Page.Template.Header, document.Page.Template.Footer, document.Page.FirstPage?.Header, document.Page.FirstPage?.Footer })
        {
            if (area is not null)
            {
                pending.Push((area.Content, false));
            }
        }

        pending.Push((document.Root, true));
        while (pending.Count > 0)
        {
            var (node, isBody) = pending.Pop();
            if (node is Box box && box.Role == SemanticRole.Decorative)
            {
                isBody = false;
            }

            if (isBody && node is Box _ or InlineGroup _)
            {
                targets.Add(node.Id);
            }

            if (node is InlineGroup { Role: SemanticRole.LinkRole { Target: LinkTarget.NodeTarget target } } link)
            {
                links.Add((link, target.Value));
            }

            var children = node switch
            {
                Box parent => parent.Children,
                InlineGroup group => group.Children,
                _ => [],
            };
            foreach (var child in children)
            {
                pending.Push((child, isBody));
            }
        }

        var distinct = new HashSet<NodeId>();
        foreach (var (link, target) in links)
        {
            if (!targets.Contains(target))
            {
                return Problem(DocumentErrorCodes.Input.Invalid.Link, $"The link is refused: it leads to node {target}, which is no box or inline group of the body outside every artifact.",
                    target.ToString(), "target", link);
            }

            if (distinct.Add(target) && distinct.Count > ModelLimits.MaxLinkTargets)
            {
                return Problem(DocumentErrorCodes.Input.Invalid.Link, $"The links lead to more than {ModelLimits.MaxLinkTargets} nodes.",
                    ModelLimits.MaxLinkTargets.ToString(CultureInfo.InvariantCulture), "target", link);
            }
        }

        return null;
    }

    /// <summary>A box's own values: margins (negative ones are not implemented), padding and border widths, orphans and
    /// widows of at least 1 (CSS Fragmentation 3 §3.3), column sizes, a heading level of 1–6, and its strut's text values.</summary>
    private static Error? Box(Box box)
    {
        var style = box.Style;
        return Sides(style.Margin, "margin", box, DocumentErrorCodes.Input.Unsupported.NegativeMargin)
            ?? Sides(style.Padding, "padding", box, DocumentErrorCodes.Input.Invalid.Length)
            ?? Sides(style.Border.Widths, "border", box, DocumentErrorCodes.Input.Invalid.Length)
            ?? (style.Orphans < 1 ? (Error?)Problem(DocumentErrorCodes.Input.Invalid.Orphans, "Orphans must be at least 1 line.", Number(style.Orphans), "orphans", box) : null)
            ?? (style.Widows < 1 ? (Error?)Problem(DocumentErrorCodes.Input.Invalid.Widows, "Widows must be at least 1 line.", Number(style.Widows), "widows", box) : null)
            ?? Columns(box)
            ?? (box.Role is SemanticRole.HeadingRole { Level: < 1 or > 6 } heading
                ? (Error?)Problem(DocumentErrorCodes.Input.Invalid.HeadingLevel, "A heading level must be 1–6.", Number(heading.Level), "level", box)
                : null)
            ?? (box.Role is SemanticRole.TableHeaderCellRole header && !System.Enum.IsDefined(header.Scope)
                ? (Error?)Problem(DocumentErrorCodes.Input.Invalid.Table, "A header cell heads its column, its row or both.", Number((int)header.Scope), "scope", box)
                : null)
            ?? (box.Role is SemanticRole.LinkRole or SemanticRole.SpanRole
                ? (Error?)Problem(DocumentErrorCodes.Input.Invalid.InlineGroup, $"The role {box.Role} belongs to an inline group, not to a box.", box.Role.ToString()!, "role", box)
                : null)
            ?? (box.Language is { } language && !LanguageTags.IsWellFormed(language)
                ? (Error?)Problem(DocumentErrorCodes.Input.Invalid.Language, LanguageMessage("a box"), language, "language", box)
                : null)
            ?? (style.Text is { } strut ? Text(strut, box) : null);
    }

    private static string LanguageMessage(string what) =>
        $"The language of {what} is not a well-formed language tag (BCP 47 syntax, at most {LanguageTags.MaxLength} characters).";

    /// <summary>
    /// The boxes among the runs of an inline box: atomic inlines (CSS Display 3, Appendix A), which this version accepts only when
    /// laid out by <see cref="LayoutAlgorithmId.Image"/> (an image in a line of text), and only with the initial page break
    /// properties: <c>break-before</c>, <c>break-after</c> and <c>break-inside</c> apply to block-level boxes (CSS
    /// Fragmentation 3 §3.1, §3.2), and an atomic inline never breaks, so a value there could only be lost. The child box
    /// is one nesting level deeper, as any child box.
    /// </summary>
    private static Error? AtomicInlines(ValueList<Node> children)
    {
        var initial = BoxStyle.Initial;
        foreach (var child in children)
        {
            if (child is Box other && other.Layout != LayoutAlgorithmId.Image)
            {
                return Problem(DocumentErrorCodes.Input.Unsupported.AtomicInline, "A box among the text runs of an inline box must be laid out by the image algorithm (an image in a line); other boxes are not supported in a line.",
                    other.Layout.ToString(), "layout", other);
            }

            if (child is Box box && (box.Style.BreakBefore != initial.BreakBefore || box.Style.BreakAfter != initial.BreakAfter || box.Style.BreakInside != initial.BreakInside))
            {
                return Problem(DocumentErrorCodes.Input.Unsupported.AtomicInline, "An image in a line of text has no page break properties: they apply to block-level boxes only.",
                    $"{box.Style.BreakBefore}/{box.Style.BreakAfter}/{box.Style.BreakInside}", "break", box);
            }
        }

        return null;
    }

    /// <summary>Where a node is, for the rows of page-margin boxes (<see cref="MarginBoxes"/>).</summary>
    private enum Place
    {
        /// <summary>Anywhere else.</summary>
        Other,

        /// <summary>The content of a header or footer area with a fixed height.</summary>
        FixedArea,

        /// <summary>A child of a box laid out by <see cref="LayoutAlgorithmId.MarginBoxes"/>.</summary>
        InMarginBoxes,

        /// <summary>A child of a box laid out by <see cref="LayoutAlgorithmId.Inline"/>, or of an inline group in one.</summary>
        InLine,
    }

    /// <summary>
    /// The rows of page-margin boxes (<see cref="LayoutAlgorithmId.MarginBoxes"/>): a row is only the content of a header
    /// or footer area with a fixed height, because it takes that height (CSS Paged Media 3 §5.3: the boxes of the top
    /// row are as tall as the page's top margin); its children are boxes, each at a place of the row
    /// (<see cref="BoxStyle.MarginBoxPlace"/>), at most one at each place. A box that is not generated is no child: Chromium
    /// 159 draws a page-margin box whose <c>content</c> is <c>none</c> or <c>normal</c> nowhere and gives it no width,
    /// where one with <c>content: ""</c> is drawn and takes its width. A place on a box outside a row means nothing, so
    /// it is refused too.
    /// </summary>
    private static Error? MarginBoxes(Box box, Place place)
    {
        if (box.Style.MarginBoxPlace != MarginBoxPlace.None && place != Place.InMarginBoxes)
        {
            return Problem(DocumentErrorCodes.Input.Invalid.MarginBoxes, "Only a child of a row of page-margin boxes has a place in the row.", box.Style.MarginBoxPlace.ToString(), "place", box);
        }

        if (box.Layout == LayoutAlgorithmId.MarginBoxes && place != Place.FixedArea)
        {
            return Problem(DocumentErrorCodes.Input.Invalid.MarginBoxes, "A row of page-margin boxes is only the content of a header or footer area with a fixed height.", box.Layout.ToString(), "layout", box);
        }

        if (box.Layout != LayoutAlgorithmId.MarginBoxes)
        {
            return null;
        }

        var taken = new HashSet<MarginBoxPlace>();
        foreach (var child in box.Children)
        {
            if (child is not Box { Style.MarginBoxPlace: not MarginBoxPlace.None } marginBox || !System.Enum.IsDefined(marginBox.Style.MarginBoxPlace))
            {
                return Problem(DocumentErrorCodes.Input.Invalid.MarginBoxes, "Every child of a row of page-margin boxes is a box with a place in the row: start, center or end.",
                    child is Box other ? other.Style.MarginBoxPlace.ToString() : child.GetType().Name, "place", child);
            }

            if (!taken.Add(marginBox.Style.MarginBoxPlace))
            {
                return Problem(DocumentErrorCodes.Input.Invalid.MarginBoxes, $"A row of page-margin boxes has at most one box at each place; this is a second one at {marginBox.Style.MarginBoxPlace}.",
                    marginBox.Style.MarginBoxPlace.ToString(), "place", marginBox);
            }
        }

        return null;
    }

    /// <summary>
    /// An inline group (<see cref="InlineGroup"/>): only among the content of an inline box, with the role of a link or a
    /// span, a link not inside a link (a link annotation leads to one target; HTML's content model forbids an <c>a</c>
    /// inside an <c>a</c> too), a link's target accepted by THE RULE FOR A LINK'S URI (<see cref="LinkUris"/>) and a
    /// description that is not blank and holds only characters text may hold (<see cref="TextCharacters"/>), a language,
    /// when it has one, of the form of a language tag (<see cref="LanguageTags"/>), and its text values as a run's.
    /// </summary>
    private static Error? Group(InlineGroup group, Place place, bool inLink)
    {
        if (place != Place.InLine)
        {
            return Problem(DocumentErrorCodes.Input.Invalid.InlineGroup, "An inline group stands only among the content of an inline box (its runs and images).", group.Role?.ToString() ?? "null", "place", group);
        }

        switch (group.Role)
        {
            case SemanticRole.LinkRole link:
                if (inLink)
                {
                    return Problem(DocumentErrorCodes.Input.Invalid.InlineGroup, "A link stands inside a link; a link leads to one target.", link.ToString(), "role", group);
                }

                var problem = link.Target switch
                {
                    LinkTarget.UriTarget uri => LinkUris.Problem(uri.Value),
                    LinkTarget.NodeTarget => null,
                    _ => "a link needs a target",
                };
                if (problem is not null)
                {
                    return Problem(DocumentErrorCodes.Input.Invalid.Link, $"The link is refused: {problem}.", (link.Target as LinkTarget.UriTarget)?.Value ?? "null", "target", group);
                }

                if (string.IsNullOrWhiteSpace(link.Description) || TextCharacters.IndexOfForbidden(link.Description) is not null)
                {
                    return Problem(DocumentErrorCodes.Input.Invalid.Link, "A link needs a description that is not blank and holds only characters text may hold (ISO 14289-1 §7.18.5).", link.Description ?? "null", "description", group);
                }

                break;
            case SemanticRole.SpanRole:
                break;
            default:
                return Problem(DocumentErrorCodes.Input.Invalid.InlineGroup, "An inline group is a link or a span.", group.Role?.ToString() ?? "null", "role", group);
        }

        if (group.Language is { } language && !LanguageTags.IsWellFormed(language))
        {
            return Problem(DocumentErrorCodes.Input.Invalid.Language, LanguageMessage("an inline group"), language, "language", group);
        }

        return group.Style is null
            ? Problem(DocumentErrorCodes.Input.Invalid.InlineGroup, "An inline group needs its text properties.", "null", "style", group)
            : Text(group.Style, group);
    }

    /// <summary>Fixed column widths zero or more and at most <see cref="ModelLimits.MaxLength"/>, fill weights finite and positive.</summary>
    private static Error? Columns(Box box)
    {
        foreach (var column in box.Style.Columns)
        {
            var valid = column switch
            {
                TrackSize.Fixed fixedWidth => IsBounded(fixedWidth.Width) && fixedWidth.Width.Points >= 0,
                TrackSize.Preferred preferred => IsBounded(preferred.Width) && preferred.Width.Points >= 0,
                TrackSize.Fill fill => double.IsFinite(fill.Weight) && fill.Weight > 0,
                _ => true,
            };
            if (!valid)
            {
                return Problem(DocumentErrorCodes.Input.Invalid.Columns, $"A fixed or preferred column width must be zero or more and at most {ModelLimits.MaxLength}, and a fill weight positive.", column.ToString()!, "columns", box);
            }
        }

        return null;
    }

    /// <summary>Text values: a family name that is not blank, a positive font size of at most
    /// <see cref="ModelLimits.MaxLength"/>, a weight of 1–1000, a white-space value of its enum, valid decorations (<see cref="Decorations"/>), and a line height that is not negative, finite and at most
    /// that long (as a number: at most that many times the font size).</summary>
    private static Error? Text(TextStyle style, Node node)
    {
        if (string.IsNullOrWhiteSpace(style.FontFamily))
        {
            return Problem(DocumentErrorCodes.Input.Invalid.FontFamily, "The font family is empty.", style.FontFamily ?? "null", "font-family", node);
        }

        if (!IsPositiveAndBounded(style.FontSize))
        {
            return Problem(DocumentErrorCodes.Input.Invalid.FontSize, $"The font size must be a positive number of at most {ModelLimits.MaxLength}.", style.FontSize.ToString(), "font-size", node);
        }

        if (!style.FontWeight.IsValid)
        {
            return Problem(DocumentErrorCodes.Input.Invalid.FontWeight, "The font weight must be 1–1000.", style.FontWeight.ToString()!, "font-weight", node);
        }

        if (!System.Enum.IsDefined(style.WhiteSpaceCollapse))
        {
            return Problem(DocumentErrorCodes.Input.Invalid.WhiteSpace, "The white-space-collapse value is not one the model has.", style.WhiteSpaceCollapse.ToString(), "white-space-collapse", node);
        }

        if (!System.Enum.IsDefined(style.TextWrapMode))
        {
            return Problem(DocumentErrorCodes.Input.Invalid.WhiteSpace, "The text-wrap-mode value is not one the model has.", style.TextWrapMode.ToString(), "text-wrap-mode", node);
        }

        if (!System.Enum.IsDefined(style.DecorationSkipInk))
        {
            return Problem(DocumentErrorCodes.Input.Invalid.TextDecoration, "The text-decoration-skip-ink value is not one the model has.", style.DecorationSkipInk.ToString(), "text-decoration-skip-ink", node);
        }

        if (Decorations(style, node) is { } decoration)
        {
            return decoration;
        }

        var lineHeight = style.LineHeight switch
        {
            LineHeight.Factor factor => (double?)factor.Value,
            LineHeight.Fixed length => length.Value.Points,
            _ => null,
        };
        return lineHeight is { } value && !(double.IsFinite(value) && value >= 0 && value <= ModelLimits.MaxLength.Points)
            ? (Error?)Problem(DocumentErrorCodes.Input.Invalid.LineHeight, $"The line height must not be negative, and at most {ModelLimits.MaxLength} (as a length, or a number of times the font size).", style.LineHeight.ToString()!, "line-height", node)
            : null;
    }

    /// <summary>At most <see cref="ModelLimits.MaxDecorations"/> decorations, each with a line of its enum and a decorating font
    /// held to the rules of a run's own font: a family name that is not blank, a positive font size of at most
    /// <see cref="ModelLimits.MaxLength"/>, a weight of 1–1000.</summary>
    private static Error? Decorations(TextStyle style, Node node)
    {
        if (style.Decorations.Count > ModelLimits.MaxDecorations)
        {
            return Problem(DocumentErrorCodes.Input.Invalid.TextDecoration, $"A run carries {style.Decorations.Count} text decorations; at most {ModelLimits.MaxDecorations} are allowed.",
                style.Decorations.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), "text-decoration", node);
        }

        foreach (var decoration in style.Decorations)
        {
            var problem = decoration switch
            {
                null => "A text decoration is missing.",
                _ when !System.Enum.IsDefined(decoration.Line) => "The text decoration line is not one the model has.",
                _ when string.IsNullOrWhiteSpace(decoration.FontFamily) => "The font family of a text decoration is empty.",
                _ when !IsPositiveAndBounded(decoration.FontSize) => $"The font size of a text decoration must be a positive number of at most {ModelLimits.MaxLength}.",
                _ when !decoration.FontWeight.IsValid => "The font weight of a text decoration must be 1–1000.",
                _ => null,
            };
            if (problem is not null)
            {
                return Problem(DocumentErrorCodes.Input.Invalid.TextDecoration, problem, decoration?.ToString() ?? "null", "text-decoration", node);
            }
        }

        return null;
    }

    /// <summary>Only characters text may hold (<see cref="TextCharacters"/>).</summary>
    private static Error? Characters(TextRun run) =>
        run.Text is { } text && TextCharacters.IndexOfForbidden(text) is { } offset
            ? (Error?)Problem(DocumentErrorCodes.Input.Invalid.Text, $"The text holds a character text cannot hold at position {offset} (a control character, a noncharacter or half of a surrogate pair).", text, "text", run)
                .With(ErrorMetadataKeys.Offset, offset)
            : null;

    /// <summary>No tab in a run whose white space is preserved: a preserved tab moves the text to the next tab stop (CSS Text 3
    /// §4.1.2: "each preserved tab is rendered as a horizontal shift that lines up the start edge of the next glyph with the
    /// next tab stop"; Chromium 159 prints it so, case <c>ws-tab</c>), and tab stops are not built. Where white
    /// space collapses a tab is a space, as before.</summary>
    private static Error? PreservedTab(TextRun run) =>
        run.Style.WhiteSpaceCollapse is WhiteSpaceCollapse.Preserve or WhiteSpaceCollapse.BreakSpaces && run.Text is { } text && text.IndexOf('\t', System.StringComparison.Ordinal) is var offset and >= 0
            ? (Error?)Problem(DocumentErrorCodes.Input.Unsupported.PreservedTab, $"A run whose white space is preserved holds a tab at position {offset}; tab stops are not built.", text, "white-space-collapse", run)
                .With(ErrorMetadataKeys.Offset, offset)
            : null;

    /// <summary>An image's width and height, when set, are positive and at most <see cref="ModelLimits.MaxLength"/>.</summary>
    private static Error? ImageSize(ImageNode image)
    {
        foreach (var (value, name) in new[] { (image.Width, "width"), (image.Height, "height") })
        {
            if (value is { } length && !IsPositiveAndBounded(length))
            {
                return Problem(DocumentErrorCodes.Input.Invalid.ImageSize, $"An image {name} must be a positive length of at most {ModelLimits.MaxLength}.", length.ToString(), name, image);
            }
        }

        return null;
    }

    /// <summary>Four sides: finite and at most <see cref="ModelLimits.MaxLength"/> (else Input.Invalid.Length), and not
    /// negative (else <paramref name="negativeCode"/>).</summary>
    private static Error? Sides(Edges edges, string property, Node? node, ErrorCode negativeCode)
    {
        foreach (var side in edges.Sides())
        {
            if (!side.IsFinite || System.Math.Abs(side.Points) > ModelLimits.MaxLength.Points)
            {
                return Problem(DocumentErrorCodes.Input.Invalid.Length, $"Each {property} must be a number of at most {ModelLimits.MaxLength}.", edges.ToString(), property, node);
            }

            if (side.Points < 0)
            {
                return Problem(negativeCode, $"A {property} may not be negative.", edges.ToString(), property, node);
            }
        }

        return null;
    }

    private static bool IsBounded(Length length) => length.IsFinite && System.Math.Abs(length.Points) <= ModelLimits.MaxLength.Points;

    private static bool IsPositiveAndBounded(Length length) => IsBounded(length) && length.Points > 0;

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static Error Problem(ErrorCode code, string message, string value, string property, Node? node)
    {
        var error = new Error(code, node is null ? message : $"{message} ({node.Source})")
            .With(ErrorMetadataKeys.Value, value)
            .With(ErrorMetadataKeys.Property, property);
        return node is null ? error : error.With(ErrorMetadataKeys.Node, node.Id.ToString()).WithSource(node.Source);
    }
}
