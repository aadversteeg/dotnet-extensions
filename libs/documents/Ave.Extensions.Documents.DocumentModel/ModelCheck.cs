using System.Collections.Generic;
using System.Linq;
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
        var problem = Page(document.Page) ?? Metadata(document.Metadata) ?? Nodes(document) ?? Targets(document);
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

    /// <summary>The title, the author and the description by THE RULE FOR A DOCUMENT'S METADATA TEXT
    /// (<see cref="MetadataTexts"/>): the first one it does not accept.</summary>
    private static Error? Metadata(DocumentMetadata? metadata)
    {
        foreach (var field in MetadataTexts.Problems(metadata?.Title, metadata?.Author, metadata?.Description))
        {
            var error = Problem(DocumentErrorCodes.Input.Invalid.MetadataText, $"The document's {field.Property} is refused: {field.Problem}.", field.Text, field.Property, null);
            return field.Offset is { } offset ? error.With(ErrorMetadataKeys.Offset, offset) : error;
        }

        return null;
    }

    /// <summary>Every node of the body and of the page areas, each root at level 0 (<see cref="ModelLimits.MaxNestingDepth"/>),
    /// and where each row of page-margin boxes is (<see cref="MarginBoxes"/>).</summary>
    private static Error? Nodes(StyledDocument document)
    {
        var pending = new Stack<(Node Node, int Depth, Place Place, bool Measured, int Lists, bool InLink, bool InBox)>();
        pending.Push((document.Root, 0, Place.Other, false, 0, false, false));
        foreach (var area in new[] { document.Page.Template.Header, document.Page.Template.Footer, document.Page.FirstPage?.Header, document.Page.FirstPage?.Footer })
        {
            if (area is not null)
            {
                pending.Push((area.Content, 0, area.Height is null ? Place.Other : Place.FixedArea, false, 0, false, false));
            }
        }

        while (pending.Count > 0)
        {
            var (node, depth, place, measured, lists, inLink, inBox) = pending.Pop();
            if (depth > ModelLimits.MaxNestingDepth)
            {
                return Problem(DocumentErrorCodes.Input.Invalid.TooDeep, $"Boxes may nest at most {ModelLimits.MaxNestingDepth} levels deep.",
                    ModelLimits.MaxNestingDepth.ToString(CultureInfo.InvariantCulture), "nesting", node);
            }

            var problem = node switch
            {
                Box box when inBox && inLink => Problem(DocumentErrorCodes.Input.Unsupported.InlineBox, "An image inside a link and inside an inline group with a box of its own (InlineGroup.Box) is not drawn in this version; put the image outside the group or outside the link.", box.Layout.ToString(), "layout", box),
                Box box => Box(box) ?? Sizes(box, measured),
                TextRun run => Text(run.Style, run) ?? Characters(run) ?? PreservedTab(run),
                InlineRun run => Text(run.Style, run),
                ImageNode image => ImageSize(image),
                MarkerSymbol symbol => Text(symbol.Style, symbol) ?? (System.Enum.IsDefined(symbol.Shape)
                    ? null
                    : Problem(DocumentErrorCodes.Input.Invalid.List, "A marker symbol is a disc, a circle or a square.", symbol.Shape.ToString(), "shape", symbol)),
                InlineGroup group => Group(group, place, inLink, inBox),
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
                    pending.Push((child, depth + 1, Place.InLine, measured, lists, inLink || grouping.Role is SemanticRole.LinkRole, inBox || grouping.Box is not null));
                }
            }

            if (node is Box placed && MarginBoxes(placed, place) is { } marginBoxes)
            {
                return marginBoxes;
            }

            if (node is Box listed && List(listed, lists) is { } list)
            {
                return list;
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
                var childMeasured = measured || parent.Layout == LayoutAlgorithmId.Tracks || parent.Layout == LayoutAlgorithmId.MarginBoxes;
                foreach (var child in parent.Children)
                {
                    var isLevel = child is Box { Style.TableDisplay: TableDisplay.None } or InlineGroup && !isRow;
                    pending.Push((child, isLevel ? depth + 1 : depth, childPlace, childMeasured, parent.Role is SemanticRole.ListRole ? lists + 1 : lists, false, false));
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
    /// widows of at least 1 (CSS Fragmentation 3 §3.3), column sizes, a table border model the model has and a border
    /// spacing that is not negative, a given width that is not negative, a heading level of 1–6, and its strut's text
    /// values.</summary>
    private static Error? Box(Box box)
    {
        var style = box.Style;
        return Sides(style.Margin, "margin", box, DocumentErrorCodes.Input.Unsupported.NegativeMargin)
            ?? Sides(style.Padding, "padding", box, DocumentErrorCodes.Input.Invalid.Length)
            ?? Sides(style.Border.Widths, "border", box, DocumentErrorCodes.Input.Invalid.Length)
            ?? BorderStyles(box)
            ?? Radius(box)
            ?? (style.Orphans < 1 ? (Error?)Problem(DocumentErrorCodes.Input.Invalid.Orphans, "Orphans must be at least 1 line.", Number(style.Orphans), "orphans", box) : null)
            ?? (style.Widows < 1 ? (Error?)Problem(DocumentErrorCodes.Input.Invalid.Widows, "Widows must be at least 1 line.", Number(style.Widows), "widows", box) : null)
            ?? Columns(box)
            ?? TableBorders(style, box)
            ?? Width(style, box)
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
    /// A box's width, minimum and maximum width (CSS Sizing 3 §3.1–3.2): a length of 0 or more and at most
    /// <see cref="ModelLimits.MaxLength"/>, or a finite percentage of 0 or more (else Input.Invalid.Length); with the auto
    /// horizontal margins, only on a box laid out by <see cref="LayoutAlgorithmId.Flow"/> or <see cref="LayoutAlgorithmId.Inline"/>
    /// that is not inside a table or a row of page-margin boxes (<paramref name="measured"/>): those size their columns by
    /// the intrinsic widths of their content, and a sized box's share in them is not built (else Input.Unsupported.Size).
    /// The same for a height, a minimum and a maximum height (CSS Sizing 3 §3.1–3.2), each a length: in a table a row's
    /// height and in a page-margin box the row's are measured from the content, a sized box's share in them is not either.
    /// </summary>
    private static Error? Sizes(Box box, bool measured)
    {
        var style = box.Style;
        var sizes = new[] { (style.SpecifiedWidth, "width"), (style.MinWidth, "min-width"), (style.MaxWidth, "max-width") };
        foreach (var (size, name) in sizes)
        {
            var valid = size switch
            {
                null => true,
                SizeValue.Fixed fixedWidth => IsBounded(fixedWidth.Width) && fixedWidth.Width.Points >= 0,
                SizeValue.Percentage percentage => double.IsFinite(percentage.Percent) && percentage.Percent >= 0,
                _ => false,
            };
            if (!valid)
            {
                return Problem(DocumentErrorCodes.Input.Invalid.Length, $"A {name} must be a length of 0 or more and at most {ModelLimits.MaxLength}, or a finite percentage of 0 or more.", size!.ToString(), name, box);
            }
        }

        var heights = new[] { (style.SpecifiedHeight, "height"), (style.MinHeight, "min-height"), (style.MaxHeight, "max-height") };
        foreach (var (height, name) in heights)
        {
            if (height is { } length && !(IsBounded(length) && length.Points >= 0))
            {
                return Problem(DocumentErrorCodes.Input.Invalid.Length, $"A {name} must be a length of 0 or more and at most {ModelLimits.MaxLength}.", length.ToString(), name, box);
            }
        }

        string? value = null;
        var property = "margin";
        foreach (var (size, name) in sizes)
        {
            if (size is not null && value is null)
            {
                (value, property) = (size.ToString(), name);
            }
        }

        foreach (var (height, name) in heights)
        {
            if (height is { } length && value is null)
            {
                (value, property) = (length.ToString(), name);
            }
        }

        value ??= style.AutoMargins == AutoMargins.None ? null : style.AutoMargins.ToString();
        if (value is null)
        {
            return null;
        }

        if (box.Layout != LayoutAlgorithmId.Flow && box.Layout != LayoutAlgorithmId.Inline)
        {
            return Problem(DocumentErrorCodes.Input.Unsupported.Size, $"A {property} is taken only by a box laid out by the flow or the inline algorithm; this box is laid out by {box.Layout}.", value, property, box);
        }

        return measured
            ? Problem(DocumentErrorCodes.Input.Unsupported.Size, $"A {property} on a box inside a table or a row of page-margin boxes is not built: their columns are sized by the intrinsic widths of their content.", value, property, box)
            : null;
    }

    /// <summary>
    /// The boxes among the runs of an inline box: atomic inlines (CSS Display 3, Appendix A), which this version accepts when
    /// laid out by <see cref="LayoutAlgorithmId.Image"/> (an image in a line of text) or by <see cref="LayoutAlgorithmId.Flow"/>
    /// or <see cref="LayoutAlgorithmId.Inline"/> (an inline-block, CSS 2.1 §9.2.4, whose widths are lengths), and only with the initial page break
    /// properties: <c>break-before</c>, <c>break-after</c> and <c>break-inside</c> apply to block-level boxes (CSS
    /// Fragmentation 3 §3.1, §3.2), and an atomic inline never breaks, so a value there could only be lost. The child box
    /// is one nesting level deeper, as any child box.
    /// </summary>
    private static Error? AtomicInlines(ValueList<Node> children)
    {
        var initial = BoxStyle.Initial;
        foreach (var child in children)
        {
            if (child is Box other && other.Layout != LayoutAlgorithmId.Image && other.Layout != LayoutAlgorithmId.Flow && other.Layout != LayoutAlgorithmId.Inline)
            {
                return Problem(DocumentErrorCodes.Input.Unsupported.AtomicInline, "A box among the text runs of an inline box must be laid out by the image algorithm (an image in a line) or by the flow or inline algorithm (an inline-block); other boxes are not supported in a line.",
                    other.Layout.ToString(), "layout", other);
            }

            if (child is Box sized && sized.Layout != LayoutAlgorithmId.Image
                && (sized.Style.SpecifiedWidth is SizeValue.Percentage || sized.Style.MinWidth is SizeValue.Percentage || sized.Style.MaxWidth is SizeValue.Percentage))
            {
                return Problem(DocumentErrorCodes.Input.Unsupported.AtomicInline, "An inline-block's width, min-width or max-width is a length in this version: a percentage of the line is not measured.",
                    $"{sized.Style.SpecifiedWidth}/{sized.Style.MinWidth}/{sized.Style.MaxWidth}", "width", sized);
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
    /// The structure of a list (ISO 32000-1 §14.8.4.3.3, Table 336: a list <c>L</c> holds list items <c>LI</c>, each an
    /// optional label <c>Lbl</c> and a body <c>LBody</c>), as the list-item algorithm lays it out: a list is block flow
    /// whose children are all list items, at most <see cref="ModelLimits.MaxListItems"/>, and numbering of its enum; a list
    /// item, and only a list item, is laid out by <see cref="LayoutAlgorithmId.ListItem"/>, its children an optional label and
    /// then its body, a flow box; an item is only the child of a list (a bare item outside a list is not a structure PDF
    /// has); a label is inline, holding text runs of at most <see cref="ModelLimits.MaxLabelLength"/> code units together, or
    /// one marker symbol; a label, a body and a marker symbol are nowhere else; lists nest at most
    /// <see cref="ModelLimits.MaxListDepth"/> deep (<paramref name="lists"/>: the lists around the box).
    /// </summary>
    private static Error? List(Box box, int lists)
    {
        Error Invalid(string message, string value, string property, Node node) =>
            Problem(DocumentErrorCodes.Input.Invalid.List, message, value, property, node);

        var isList = box.Role is SemanticRole.ListRole;
        if (box.Role is SemanticRole.ListRole { Numbering: var numbering } && !System.Enum.IsDefined(numbering))
        {
            return Invalid("A list's numbering is one of the values of ListNumbering.", numbering.ToString(), "numbering", box);
        }

        if (isList && lists >= ModelLimits.MaxListDepth)
        {
            return Invalid($"Lists may nest at most {ModelLimits.MaxListDepth} levels deep.", Number(lists + 1), "nesting", box);
        }

        if (isList && box.Layout != LayoutAlgorithmId.Flow)
        {
            return Invalid("A list is laid out as block flow.", box.Layout.ToString(), "layout", box);
        }

        if (isList && box.Children.Count > ModelLimits.MaxListItems)
        {
            return Invalid($"A list has at most {ModelLimits.MaxListItems} items.", Number(box.Children.Count), "items", box);
        }

        var isItem = box.Role is SemanticRole.ListItemRole;
        if (isItem != (box.Layout == LayoutAlgorithmId.ListItem))
        {
            return Invalid("A list item, and only a list item, is laid out by the list-item algorithm.", $"{box.Role}/{box.Layout}", "layout", box);
        }

        if (isItem && !IsItemShape(box))
        {
            return Invalid("A list item holds an optional label and then its body, a flow box.", Number(box.Children.Count), "children", box);
        }

        if (box.Role is SemanticRole.ListLabelRole && LabelProblem(box) is { } label)
        {
            return Invalid(label, box.Layout.ToString(), "label", box);
        }

        foreach (var child in box.Children)
        {
            var problem = child switch
            {
                Box { Role: SemanticRole.ListItemRole } when !isList => "A list item is only a child of a list.",
                _ when isList && child is not Box { Role: SemanticRole.ListItemRole } => "Every child of a list is a list item.",
                Box { Role: SemanticRole.ListLabelRole or SemanticRole.ListBodyRole } when !isItem => "A list label and a list body are only parts of a list item.",
                MarkerSymbol when box.Role is not SemanticRole.ListLabelRole => "A marker symbol is only the content of a list label.",
                _ => null,
            };
            if (problem is not null)
            {
                return Invalid(problem, child is Box other ? other.Role.ToString() : child.GetType().Name, "parent", child);
            }
        }

        return null;
    }

    private static bool IsItemShape(Box item) => item.Children.Count switch
    {
        1 => item.Children[0] is Box { Role: SemanticRole.ListBodyRole, Layout.Name: "flow" },
        2 => item.Children[0] is Box { Role: SemanticRole.ListLabelRole } && item.Children[1] is Box { Role: SemanticRole.ListBodyRole, Layout.Name: "flow" },
        _ => false,
    };

    /// <summary>Why a label is not valid, or null: inline, and one marker symbol or text runs only, of at most
    /// <see cref="ModelLimits.MaxLabelLength"/> code units together.</summary>
    private static string? LabelProblem(Box label)
    {
        if (label.Layout != LayoutAlgorithmId.Inline)
        {
            return "A list label is laid out inline.";
        }

        if (label.Children is [MarkerSymbol])
        {
            return null;
        }

        var length = 0;
        foreach (var child in label.Children)
        {
            if (child is not TextRun run)
            {
                return "A list label holds one marker symbol, or text runs only.";
            }

            length += run.Text.Length;
        }

        return label.Children.Count == 0 ? "A list label holds one marker symbol, or text runs only."
            : length > ModelLimits.MaxLabelLength ? $"A list label's text is at most {ModelLimits.MaxLabelLength} characters long; this one has {Number(length)}."
            : null;
    }

    /// <summary>
    /// An inline group (<see cref="InlineGroup"/>): only among the content of an inline box, with the role of a link or a
    /// span, a link not inside a link (a link annotation leads to one target; HTML's content model forbids an <c>a</c>
    /// inside an <c>a</c> too), a link's target accepted by THE RULE FOR A LINK'S URI (<see cref="LinkUris"/>) and a
    /// description that is not blank and holds only characters text may hold (<see cref="TextCharacters"/>), a language,
    /// when it has one, of the form of a language tag (<see cref="LanguageTags"/>), and its text values as a run's. A group
    /// with a box of its own (<see cref="InlineGroup.Box"/>, <see cref="InlineBox"/>) may have no role; a group without one
    /// means something, so it has one; a link may have a box, lie inside one and hold one. In this version an image inside
    /// a group with a box is not inside a link as well: where Chromium writes the link's rectangles then is not measured.
    /// </summary>
    private static Error? Group(InlineGroup group, Place place, bool inLink, bool inBox)
    {
        if (place != Place.InLine)
        {
            return Problem(DocumentErrorCodes.Input.Invalid.InlineGroup, "An inline group stands only among the content of an inline box (its runs and images).", group.Role?.ToString() ?? "null", "place", group);
        }

        if (group.Box is { } own && InlineBox(own, group) is { } boxProblem)
        {
            return boxProblem;
        }

        switch (group.Role)
        {
            case null when group.Box is not null:
                break;
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

    /// <summary>The box of an inline group (<see cref="InlineBoxStyle"/>): padding and border widths of zero or more, as a box's;
    /// horizontal margins of zero or more (a negative one is not drawn in this version); rounded corners as a box's.</summary>
    private static Error? InlineBox(InlineBoxStyle box, InlineGroup group) =>
        Sides(new Edges(Length.Zero, box.MarginRight, Length.Zero, box.MarginLeft), "margin", group, DocumentErrorCodes.Input.Unsupported.NegativeMargin)
        ?? Sides(box.Padding, "padding", group, DocumentErrorCodes.Input.Invalid.Length)
        ?? Sides(box.Border.Widths, "border", group, DocumentErrorCodes.Input.Invalid.Length)
        ?? Rounded(box.BorderRadius, box.Border, "an inline box", group);

    /// <summary>Fixed column widths zero or more and at most <see cref="ModelLimits.MaxLength"/>, fill weights finite and
    /// positive, percentages more than zero and at most <see cref="ModelLimits.MaxPercent"/>.</summary>
    private static Error? Columns(Box box)
    {
        foreach (var column in box.Style.Columns)
        {
            var valid = column switch
            {
                TrackSize.Fixed fixedWidth => IsBounded(fixedWidth.Width) && fixedWidth.Width.Points >= 0,
                TrackSize.Preferred preferred => IsBounded(preferred.Width) && preferred.Width.Points >= 0,
                TrackSize.Fill fill => double.IsFinite(fill.Weight) && fill.Weight > 0,
                TrackSize.Percent percent => double.IsFinite(percent.Value) && percent.Value > 0 && percent.Value <= ModelLimits.MaxPercent,
                _ => true,
            };
            if (!valid)
            {
                return Problem(DocumentErrorCodes.Input.Invalid.Columns, $"A fixed or preferred column width must be zero or more and at most {ModelLimits.MaxLength}, a fill weight positive, and a percentage more than zero and at most {ModelLimits.MaxPercent}.", column.ToString()!, "columns", box);
            }
        }

        return null;
    }

    /// <summary>Text values: a family name that is not blank, a positive font size of at most
    /// <see cref="ModelLimits.MaxLength"/>, a weight of 1–1000, a white-space value of its enum, valid decorations (<see cref="Decorations"/>), letter and word spacing finite and at most that long either way, font features of <see cref="OptionalFontFeatures.Tags"/>, and a line height that is not negative, finite and at most
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

        foreach (var tag in style.FontFeatures)
        {
            if (!System.Linq.Enumerable.Contains(OptionalFontFeatures.Tags, tag))
            {
                return Problem(DocumentErrorCodes.Input.Invalid.FontFeature, $"The font feature '{tag}' is not one the model switches: {string.Join(", ", OptionalFontFeatures.Tags)}.", tag ?? "null", "font-feature-settings", node);
            }
        }

        foreach (var (spacing, name) in new[] { (style.LetterSpacing, "letter-spacing"), (style.WordSpacing, "word-spacing") })
        {
            if (!(double.IsFinite(spacing.Points) && System.Math.Abs(spacing.Points) <= ModelLimits.MaxLength.Points))
            {
                return Problem(DocumentErrorCodes.Input.Invalid.Spacing, $"The {name} must be a finite length of at most {ModelLimits.MaxLength} either way.", spacing.ToString(), name, node);
            }
        }

        if (!(double.IsFinite(style.BaselineShift.Points) && System.Math.Abs(style.BaselineShift.Points) <= ModelLimits.MaxLength.Points))
        {
            return Problem(DocumentErrorCodes.Input.Invalid.BaselineShift, $"The baseline shift must be a finite length of at most {ModelLimits.MaxLength} either way.", style.BaselineShift.ToString(), "vertical-align", node);
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

    /// <summary>The styles of a box's border sides: each one of <see cref="BorderStyle"/> (else Input.Invalid.BorderStyle);
    /// inset and outset (CSS Backgrounds 3 §3.2) only on a box that is no table part, since a table's lines are drawn solid
    /// by its layout (a table, a group, a row, and a row's cells, checked with their row), and not inset and outset sides on
    /// one box, which is not measured (else Input.Unsupported.BorderStyle). A side of width 0 is not drawn, so its style is
    /// not checked but for the enum.</summary>
    private static Error? BorderStyles(Box box)
    {
        var border = box.Style.Border;
        BorderSide[] sides = [border.Top, border.Right, border.Bottom, border.Left];
        foreach (var side in sides)
        {
            if (!System.Enum.IsDefined(side.Style))
            {
                return Problem(DocumentErrorCodes.Input.Invalid.BorderStyle, "The border style is not one the model has.", side.Style.ToString(), "border-style", box);
            }
        }

        bool Drawn(BorderSide side, BorderStyle style) => side.IsVisible && side.Style == style;
        if (sides.Any(side => Drawn(side, BorderStyle.Inset)) && sides.Any(side => Drawn(side, BorderStyle.Outset)))
        {
            return Problem(DocumentErrorCodes.Input.Unsupported.BorderStyle, "Inset and outset sides on one box are not drawn in this version.", border.ToString(), "border-style", box);
        }

        static bool IsShaded(Box part) =>
            new[] { part.Style.Border.Top, part.Style.Border.Right, part.Style.Border.Bottom, part.Style.Border.Left }.Any(side => side.IsVisible && side.Style != BorderStyle.Solid);
        var parts = box.Layout == LayoutAlgorithmId.Tracks || box.Style.TableDisplay != TableDisplay.None ? new[] { box } : [];
        var cells = box.Style.TableDisplay == TableDisplay.Row ? box.Children.OfType<Box>() : [];
        return parts.Concat(cells).FirstOrDefault(IsShaded) is { } shaded
            ? Problem(DocumentErrorCodes.Input.Unsupported.BorderStyle, "An inset or outset border on a table or a part of a table is not drawn in this version: its lines are drawn solid.",
                shaded.Style.Border.ToString(), "border-style", shaded)
            : null;
    }

    /// <summary>Rounded corners (<see cref="BoxStyle.BorderRadius"/>, CSS Backgrounds 3 §5): each radius a finite length
    /// of zero or more, at most <see cref="ModelLimits.MaxLength"/>, and a finite percentage of zero or more (else
    /// Input.Invalid.Length); drawn only on a box laid out by <see cref="LayoutAlgorithmId.Flow"/>,
    /// <see cref="LayoutAlgorithmId.Inline"/>, <see cref="LayoutAlgorithmId.ListItem"/> or <see cref="LayoutAlgorithmId.Tracks"/>
    /// (a table) that is no row, row group or caption (a cell checked with its row) and no page-margin box, whose visible
    /// border sides are all four solid, of one width and one colour (else Input.Unsupported.BorderRadius): the shapes
    /// measured with Chromium (cases <c>br-*</c>, <c>bri-*</c>).</summary>
    private static Error? Radius(Box box)
    {
        // A table's cells are the children of its rows; they are checked with their row, as their border styles are.
        if (box.Style.TableDisplay == TableDisplay.Row)
        {
            foreach (var cell in box.Children.OfType<Box>())
            {
                if (Rounded(cell.Style.BorderRadius, cell.Style.Border, "a table cell", cell) is { } cellProblem)
                {
                    return cellProblem;
                }
            }
        }

        var radius = box.Style.BorderRadius;
        if (radius.IsZero)
        {
            return null;
        }

        var where = box.Layout != LayoutAlgorithmId.Flow && box.Layout != LayoutAlgorithmId.Inline && box.Layout != LayoutAlgorithmId.ListItem && box.Layout != LayoutAlgorithmId.Tracks
                ? $"a box laid out by {box.Layout}"
            : box.Style.TableDisplay != TableDisplay.None ? "a row, a row group or a caption of a table"
            : box.Style.MarginBoxPlace != MarginBoxPlace.None ? "a page-margin box"
            : null;
        return where is null
            ? Rounded(radius, box.Style.Border, "a box", box)
            : Problem(DocumentErrorCodes.Input.Unsupported.BorderRadius, $"Rounded corners on {where} are not drawn in this version.", radius.ToString(), "border-radius", box);
    }

    /// <summary>The corners of a rounded box or inline box: each a bounded length of zero or more plus a finite percentage
    /// of zero or more (else Input.Invalid.Length), beside a border whose visible sides are all four solid, of one width
    /// and one colour, or none (else Input.Unsupported.BorderRadius).</summary>
    private static Error? Rounded(BorderRadius radius, Border border, string what, Node node)
    {
        if (radius.IsZero)
        {
            return null;
        }

        foreach (var corner in new[] { radius.TopLeft, radius.TopRight, radius.BottomRight, radius.BottomLeft })
        {
            if (!IsBounded(corner.Length) || corner.Length.Points < 0 || !double.IsFinite(corner.Percent) || corner.Percent < 0)
            {
                return Problem(DocumentErrorCodes.Input.Invalid.Length, $"A corner radius must be a length of 0 or more, at most {ModelLimits.MaxLength}, plus a percentage of 0 or more.", corner.ToString(), "border-radius", node);
            }
        }

        BorderSide[] sides = [border.Top, border.Right, border.Bottom, border.Left];
        var oneLine = sides.All(side => !side.IsVisible) || sides.All(side => side == border.Top && side.Style == BorderStyle.Solid);
        return oneLine
            ? null
            : Problem(DocumentErrorCodes.Input.Unsupported.BorderRadius, $"Rounded corners on {what} whose border sides are not all solid, of one width and one colour are not drawn in this version.", radius.ToString(), "border-radius", node);
    }

    /// <summary>A border model and a caption side the model has (else Input.Invalid.Table), a table height of zero or more,
    /// at most <see cref="ModelLimits.MaxLength"/> (else Input.Invalid.Length), and a border spacing of two finite lengths of
    /// zero or more, at most <see cref="ModelLimits.MaxLength"/> (CSS 2.1 §17.6.1: "Negative values are not allowed";
    /// else Input.Invalid.Length).</summary>
    private static Error? TableBorders(BoxStyle style, Box box)
    {
        if (!System.Enum.IsDefined(style.BorderModel))
        {
            return Problem(DocumentErrorCodes.Input.Invalid.Table, "The table border model is not one the model has.", style.BorderModel.ToString(), "border-collapse", box);
        }

        if (!System.Enum.IsDefined(style.CaptionSide))
        {
            return Problem(DocumentErrorCodes.Input.Invalid.Table, "The caption side is not one the model has.", style.CaptionSide.ToString(), "caption-side", box);
        }

        if (style.TableHeight is { } height && !(IsBounded(height) && height.Points >= 0))
        {
            return Problem(DocumentErrorCodes.Input.Invalid.Length, $"A table height must be zero or more and at most {ModelLimits.MaxLength}.", height.ToString(), "height", box);
        }

        var spacing = style.BorderSpacing;
        return IsBounded(spacing.Horizontal) && IsBounded(spacing.Vertical) && spacing.Horizontal.Points >= 0 && spacing.Vertical.Points >= 0
            ? null
            : Problem(DocumentErrorCodes.Input.Invalid.Length, $"A border spacing must be zero or more and at most {ModelLimits.MaxLength}.", spacing.ToString(), "border-spacing", box);
    }

    /// <summary>A given width (<see cref="BoxWidth.Fixed"/>) of zero or more, at most <see cref="ModelLimits.MaxLength"/>,
    /// and a percentage (<see cref="BoxWidth.Percent"/>) more than zero, at most <see cref="ModelLimits.MaxPercent"/>
    /// (CSS 2.1 §10.2: "Negative values for 'width' are illegal"; else Input.Invalid.Length).</summary>
    private static Error? Width(BoxStyle style, Box box) => style.Width switch
    {
        BoxWidth.Fixed { Width: var width } when !(IsBounded(width) && width.Points >= 0) =>
            Problem(DocumentErrorCodes.Input.Invalid.Length, $"A width must be zero or more and at most {ModelLimits.MaxLength}.", width.ToString(), "width", box),
        BoxWidth.Percent { Value: var value } when !(double.IsFinite(value) && value > 0 && value <= ModelLimits.MaxPercent) =>
            Problem(DocumentErrorCodes.Input.Invalid.Length, $"A width as a percentage must be more than zero and at most {ModelLimits.MaxPercent}.", style.Width.ToString()!, "width", box),
        _ => null,
    };

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
