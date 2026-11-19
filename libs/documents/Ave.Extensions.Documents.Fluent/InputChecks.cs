using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.ErrorPaths;

namespace Ave.Extensions.Documents.Fluent;

/// <summary>
/// The validity rules of the code front-end. Each function returns every problem it finds (an empty list when
/// there are none); every problem carries the source location of the call that set the value.
/// </summary>
internal static class InputChecks
{
    private static readonly string[] Sides = ["top", "right", "bottom", "left"];

    /// <summary>Page size: a positive width and height of at most <see cref="InputLimits.MaxLength"/>; margins finite,
    /// not negative and at most that long; and the margins must leave a page area (CSS Paged Media 3 §3) to lay
    /// content out in.</summary>
    public static IEnumerable<Error> Page(PageSetup page, SourceLocation source)
    {
        if (!(IsPositiveAndBounded(page.Width) && IsPositiveAndBounded(page.Height)))
        {
            yield return Problem(DocumentErrorCodes.Input.Invalid.PageSize, $"The page width and height must be positive and at most {InputLimits.MaxLength}.", $"{page.Width}x{page.Height}", "size", source);
            yield break;
        }

        foreach (var problem in Lengths(page.Margins, "margin", source, DocumentErrorCodes.Input.Invalid.Length))
        {
            yield return problem;
        }

        var areaWidth = page.Width - page.Margins.Horizontal;
        var areaHeight = page.Height - page.Margins.Vertical;
        if (!(areaWidth.Points > 0 && areaHeight.Points > 0))
        {
            yield return Problem(DocumentErrorCodes.Input.Invalid.PageSize, "The page margins leave no area for content.", $"{areaWidth}x{areaHeight}", "margin", source);
        }
    }

    /// <summary>
    /// Margins and padding. Padding may not be negative (CSS Box Model 3 §4.1: "Negative values for padding
    /// properties are invalid"). Negative margins are valid CSS but not implemented yet, so they are reported as
    /// unsupported rather than drawn wrongly.
    /// </summary>
    public static IEnumerable<Error> Box(Cascade.BoxValues box, SourceLocation source) =>
        Lengths(box.Margin, "margin", source, DocumentErrorCodes.Input.Unsupported.NegativeMargin)
            .Concat(Lengths(box.Padding, "padding", source, DocumentErrorCodes.Input.Invalid.Length));

    /// <summary>
    /// Text formatting: a family name that is not empty, a positive finite size of at most
    /// <see cref="InputLimits.MaxLength"/> (CSS Fonts 4 §2.5: "Negative lengths are invalid."; zero would make every
    /// glyph invisible), and a weight in 1–1000 (§2.2).
    /// </summary>
    public static IEnumerable<Error> Text(TextFormat format, SourceLocation source)
    {
        if (format.FontFamily is { } family && string.IsNullOrWhiteSpace(family))
        {
            yield return Problem(DocumentErrorCodes.Input.Invalid.FontFamily, "The font family is empty.", family, "font-family", source);
        }

        if (format.FontSize is { } size && !IsPositiveAndBounded(size))
        {
            yield return Problem(DocumentErrorCodes.Input.Invalid.FontSize, $"The font size must be a positive number of at most {InputLimits.MaxLength}.", size.ToString(), "font-size", source);
        }

        if (format.FontWeight is { } weight && !weight.IsValid)
        {
            yield return Problem(DocumentErrorCodes.Input.Invalid.FontWeight, "The font weight must be 1–1000.", weight.ToString(), "font-weight", source);
        }

        if (format.LineHeight is { IsValid: false } lineHeight)
        {
            yield return Problem(DocumentErrorCodes.Input.Invalid.LineHeight, $"The line height must not be negative, and at most {InputLimits.MaxLength} (as a length, a number or a percentage of 100).", lineHeight.Describe(), "line-height", source);
        }

        if (format.WhiteSpaceCollapse is { } collapse && !Enum.IsDefined(collapse))
        {
            yield return Problem(DocumentErrorCodes.Input.Invalid.WhiteSpace, "The white-space-collapse value is not one of WhiteSpaceCollapse.", collapse.ToString(), "white-space-collapse", source);
        }

        if (format.TextWrapMode is { } wrap && !Enum.IsDefined(wrap))
        {
            yield return Problem(DocumentErrorCodes.Input.Invalid.WhiteSpace, "The text-wrap-mode value is not one of TextWrapMode.", wrap.ToString(), "text-wrap-mode", source);
        }

        if (format.DecorationSkipInk is { } skip && !Enum.IsDefined(skip))
        {
            yield return Problem(DocumentErrorCodes.Input.Invalid.TextDecoration, "The text-decoration-skip-ink value is not one of TextDecorationSkipInk.", skip.ToString(), "text-decoration-skip-ink", source);
        }

        foreach (var line in format.Decorations ?? [])
        {
            if (line is null || !Enum.IsDefined(line.Line))
            {
                yield return Problem(DocumentErrorCodes.Input.Invalid.TextDecoration, "The text decoration line is not one of TextDecorationLine.", line?.Line.ToString() ?? "null", "text-decoration", source);
            }
        }

        // CSS Fragmentation 3 §3.3: "Only positive integers are allowed as values of orphans and widows."
        if (format.Orphans is int orphans and < 1)
        {
            yield return Problem(DocumentErrorCodes.Input.Invalid.Orphans, "Orphans must be at least 1 line.", orphans.ToString(System.Globalization.CultureInfo.InvariantCulture), "orphans", source);
        }

        if (format.Widows is int widows and < 1)
        {
            yield return Problem(DocumentErrorCodes.Input.Invalid.Widows, "Widows must be at least 1 line.", widows.ToString(System.Globalization.CultureInfo.InvariantCulture), "widows", source);
        }
    }

    /// <summary>A document has a layout profile, and each of its policies is a value of its enum.</summary>
    public static IEnumerable<Error> Profile(DocumentModel.LayoutProfile? profile, SourceLocation source)
    {
        if (profile is null)
        {
            yield return Problem(DocumentErrorCodes.Input.Invalid.LayoutProfile, "The document has no layout profile.", "null", "profile", source);
            yield break;
        }

        if (!Enum.IsDefined(profile.MonolithicOverflow))
        {
            yield return Problem(DocumentErrorCodes.Input.Invalid.LayoutProfile, "The monolithic overflow policy must be Refuse or Slice.",
                ((int)profile.MonolithicOverflow).ToString(System.Globalization.CultureInfo.InvariantCulture), "monolithic-overflow", source);
        }

        if (!Enum.IsDefined(profile.EmptyRowParts))
        {
            yield return Problem(DocumentErrorCodes.Input.Invalid.LayoutProfile, "The empty row parts policy must be Avoid or Allow.",
                ((int)profile.EmptyRowParts).ToString(System.Globalization.CultureInfo.InvariantCulture), "empty-row-parts", source);
        }

        if (!Enum.IsDefined(profile.FontMetricRounding))
        {
            yield return Problem(DocumentErrorCodes.Input.Invalid.LayoutProfile, "The font metric rounding policy must be Exact or WholePixels.",
                ((int)profile.FontMetricRounding).ToString(System.Globalization.CultureInfo.InvariantCulture), "font-metric-rounding", source);
        }
    }

    /// <summary>Heading levels are 1–6, as PDF structure types H1–H6 (ISO 32000-1 §14.8.4.3.2).</summary>
    public static IEnumerable<Error> Role(SemanticRole role, SourceLocation source) =>
        role is SemanticRole.HeadingRole { Level: < 1 or > 6 } heading
            ? [Problem(DocumentErrorCodes.Input.Invalid.HeadingLevel, "A heading level must be 1–6.", heading.Level.ToString(System.Globalization.CultureInfo.InvariantCulture), "level", source)]
            : [];

    /// <summary>
    /// The first element nested deeper than <see cref="InputLimits.MaxNestingDepth"/>, as an error at its line; null
    /// when the document stays within the limit. Walks the tree with an explicit stack, so the check itself cannot run
    /// out of stack on the documents it is there to refuse. The body and every page area count, each from level 1.
    /// </summary>
    public static Error? TooDeep(Document document)
    {
        var areas = new[] { document.Page.Header, document.Page.Footer, document.Page.FirstPageHeader, document.Page.FirstPageFooter };
        var pending = new Stack<(Element Element, int Depth)>(
            document.Body.Select(element => (element, 1)).Concat(areas.OfType<PageAreaSetup>().Select(area => (area.Content, 1))));
        while (pending.Count > 0)
        {
            var (element, depth) = pending.Pop();
            if (depth > InputLimits.MaxNestingDepth)
            {
                return Problem(DocumentErrorCodes.Input.Invalid.TooDeep, $"Elements may nest at most {InputLimits.MaxNestingDepth} levels deep.",
                    InputLimits.MaxNestingDepth.ToString(System.Globalization.CultureInfo.InvariantCulture), "nesting", element.Source);
            }

            foreach (var child in Children(element))
            {
                pending.Push((child, depth + 1));
            }
        }

        return null;
    }

    /// <summary>The elements directly inside an element: a block's children, a row's items, a table's cell contents.</summary>
    private static IEnumerable<Element> Children(Element element) => element switch
    {
        BlockElement block => block.Children,
        RowElement row => row.Items,
        TableElement table => table.Rows
            .SelectMany(part => part is RowGroupElement group ? group.Rows.AsEnumerable() : [(TableRowElement)part])
            .SelectMany(row => row.Cells)
            .SelectMany(cell => cell.Content),
        _ => [],
    };

    /// <summary>The document names no font family: fonts are explicit input, so there is no default family.</summary>
    public static Error MissingFontFamily(SourceLocation source) =>
        Problem(DocumentErrorCodes.Input.Invalid.FontFamily, "The document must name a font family.", "null", "font-family", source);

    /// <summary>A span got a shared style that sets box properties; a span has no box, so they cannot apply.</summary>
    public static Error BoxPropertiesOnSpan(SourceLocation source) =>
        Problem(DocumentErrorCodes.Input.Invalid.NotApplicable, "A span has no box and no lines: its style may not set margin, padding, background or text alignment.", "Style", "style", source);

    /// <summary>The span of an image in a line has no text and no box of its own: formatting belongs on the image.</summary>
    public static Error FormatOnImageSpan(SourceLocation source) =>
        Problem(DocumentErrorCodes.Input.Invalid.NotApplicable, "The span of an image in a line shows no text: format the image instead (InlineImage(Image(file).Margin(...))).", "Style", "style", source);

    /// <summary>A page break with nothing after it in its list has nothing to move to the next page.</summary>
    public static Error TrailingPageBreak(SourceLocation source) =>
        Problem(DocumentErrorCodes.Input.Invalid.PageBreak, "A page break must be followed by an element in the same list.", "PageBreak", "break-before", source);

    /// <summary>Checks four sides: finite and at most <see cref="InputLimits.MaxLength"/> (else Input.Invalid.Length),
    /// and not negative (else <paramref name="negativeCode"/>).</summary>
    private static IEnumerable<Error> Lengths(Edges edges, string property, SourceLocation source, ErrorCode negativeCode) =>
        edges.Sides().Zip(Sides).SelectMany(side => side.First switch
        {
            { IsFinite: false } => [Problem(DocumentErrorCodes.Input.Invalid.Length, $"{property}-{side.Second} must be a number.", side.First.ToString(), $"{property}-{side.Second}", source)],
            { Points: < 0 } => [Problem(negativeCode, $"{property}-{side.Second} may not be negative.", side.First.ToString(), $"{property}-{side.Second}", source)],
            var length when !IsBounded(length) => [Problem(DocumentErrorCodes.Input.Invalid.Length, $"{property}-{side.Second} may be at most {InputLimits.MaxLength}.", length.ToString(), $"{property}-{side.Second}", source)],
            _ => System.Array.Empty<Error>(),
        });

    /// <summary>A length the front-end accepts at all: finite, and no longer than <see cref="InputLimits.MaxLength"/>
    /// either way.</summary>
    private static bool IsBounded(Length length) =>
        length.IsFinite && System.Math.Abs(length.Points) <= InputLimits.MaxLength.Points;

    /// <summary>A size: bounded (<see cref="IsBounded"/>) and above zero.</summary>
    private static bool IsPositiveAndBounded(Length length) => IsBounded(length) && length.Points > 0;

    /// <summary>A header or footer height, when one is given, is a positive length of at most
    /// <see cref="InputLimits.MaxLength"/>.</summary>
    public static IEnumerable<Error> AreaHeight(Length? height, SourceLocation source) =>
        height is { } given && !IsPositiveAndBounded(given)
            ? [Problem(DocumentErrorCodes.Input.Invalid.Length, $"A header or footer height must be a positive length of at most {InputLimits.MaxLength}.", given.ToString(), "height", source)]
            : [];

    /// <summary>Border widths: finite and not negative (CSS Backgrounds 3 §3.3: "Negative values are invalid").</summary>
    public static IEnumerable<Error> Border(DocumentModel.Border border, SourceLocation source) =>
        Lengths(border.Widths, "border", source, DocumentErrorCodes.Input.Invalid.Length);

    /// <summary>
    /// Column definitions: at least one; fixed widths finite and not negative; fill weights finite and positive
    /// (CSS Grid 1 §7.2.1: <c>&lt;flex [0,∞]&gt;</c>, "A non-negative dimension with the unit fr"; zero would make a column
    /// that never grows, so the front-end asks for a positive weight).
    /// </summary>
    public static IEnumerable<Error> Columns(ValueList<DocumentModel.TrackSize> columns, SourceLocation source)
    {
        if (columns.Count == 0)
        {
            yield return Problem(DocumentErrorCodes.Input.Invalid.Columns, "A table needs at least one column.", "0", "columns", source);
        }

        foreach (var column in columns)
        {
            var valid = column switch
            {
                DocumentModel.TrackSize.Fixed fixedWidth => IsBounded(fixedWidth.Width) && fixedWidth.Width.Points >= 0,
                DocumentModel.TrackSize.Fill fill => double.IsFinite(fill.Weight) && fill.Weight > 0,
                _ => true,
            };
            if (!valid)
            {
                yield return Problem(DocumentErrorCodes.Input.Invalid.Columns, $"A fixed column width must be zero or more and at most {InputLimits.MaxLength}, and a fill weight positive.", column.ToString(), "columns", source);
            }
        }
    }

    /// <summary>A row of blocks needs at least one item.</summary>
    public static IEnumerable<Error> RowItems(int items, SourceLocation source) =>
        items > 0 ? [] : [Problem(DocumentErrorCodes.Input.Invalid.Columns, "A row needs at least one item.", "0", "items", source)];

    /// <summary>A row of blocks with column sizes must have one size per item.</summary>
    public static IEnumerable<Error> ColumnCount(int columns, int items, SourceLocation source) =>
        columns == items
            ? []
            : [Problem(DocumentErrorCodes.Input.Invalid.Columns, $"The row has {items} items but {columns} column sizes.", columns.ToString(System.Globalization.CultureInfo.InvariantCulture), "columns", source)];

    /// <summary>A table's structure as the front-end sees it: header rows only at the start, not in a row group.</summary>
    public static Error MisplacedHeaderRow(SourceLocation source) =>
        Problem(DocumentErrorCodes.Input.Invalid.Table, "Header rows must come first in a table, outside any row group.", "HeaderRow", "rows", source);

    /// <summary>A property set on a table part it does not apply to: padding on a table (the collapsing border model
    /// has none, CSS 2.1 §17.6.2), margin or padding on a row or a row group.</summary>
    public static IEnumerable<Error> NotApplicable(Edges edges, string property, string element, SourceLocation source) =>
        edges == Edges.Zero
            ? []
            : [Problem(DocumentErrorCodes.Input.Invalid.NotApplicable, $"A {element} has no {property}.", edges.ToString(), property, source)];

    /// <summary>
    /// A page break inside a table cell, a row of blocks or a page area cannot move content to another page: a cell
    /// or an item is laid out whole beside its neighbours, a header or footer repeats on every page. Refused at any
    /// depth inside the container — in a block inside a cell — because the break would still be inside it. A table or
    /// a row of blocks inside the container is a container of its own that checks its content when it is translated,
    /// so the walk stops there and every break is reported once, by its innermost container.
    /// </summary>
    public static IEnumerable<Error> PageBreakIn(IEnumerable<Element> content, string container) =>
        content.SelectMany(PageBreaksWithin).Select(pageBreak =>
            Problem(DocumentErrorCodes.Input.Invalid.PageBreak, $"A page break is not allowed inside a {container}.", "PageBreak", "break-before", pageBreak.Source));

    /// <summary>The page breaks in an element and in everything it contains, down to the next container.</summary>
    private static IEnumerable<PageBreakElement> PageBreaksWithin(Element element) => element switch
    {
        PageBreakElement pageBreak => [pageBreak],
        RowElement or TableElement => [],
        _ => Children(element).SelectMany(PageBreaksWithin),
    };

    /// <summary>
    /// The shape of a table: the rule of <see cref="TableSlots"/> (each cell in the first free column of its row; a row
    /// span ends with its row group), applied to the table's groups and the cells' spans. Each problem is reported at
    /// the cell or the row that has it. Only checked when the columns are valid.
    /// </summary>
    /// <param name="groups">The table's row groups, each a list of rows.</param>
    /// <param name="columnCount">The number of columns.</param>
    public static IEnumerable<Error> TableShape(IReadOnlyList<IReadOnlyList<TableRowElement>> groups, int columnCount)
    {
        var rows = groups.SelectMany(group => group).ToImmutableArray();
        var grid = TableSlots.Fill([.. groups.Select(group => (IReadOnlyList<IReadOnlyList<CellSpan>>)[.. group.Select(row => (IReadOnlyList<CellSpan>)[.. row.Cells.Select(cell => cell.Span)])])], columnCount);
        return grid.Problems.Select(problem =>
        {
            var row = rows[problem.Row];
            var source = problem.Cell < 0 ? row.Source : row.Cells[problem.Cell].Source;
            var message = problem.Kind switch
            {
                TableSlots.ProblemKind.PastLastColumn => $"The cell does not fit in the table's {columnCount} columns: the row has more cells than columns, or the cell spans past the last column.",
                TableSlots.ProblemKind.SpanPastGroup => "The cell's row span runs past the last row of its row group.",
                TableSlots.ProblemKind.Overlap => "The cell overlaps a cell that spans rows from above.",
                TableSlots.ProblemKind.SpanTooLarge => $"The cell spans more than {TableSlots.MaxColumnSpan} columns or {TableSlots.MaxRowSpan} rows, the largest spans a table allows.",
                TableSlots.ProblemKind.SpanBelowOne => "A cell spans at least one column and one row.",
                _ => "A table row needs at least one cell.",
            };
            return Problem(DocumentErrorCodes.Input.Invalid.Table, message, problem.Kind.ToString(), problem.Cell < 0 ? "cells" : "span", source);
        });
    }

    /// <summary>An image's width and height, when set, are positive lengths of at most <see cref="InputLimits.MaxLength"/>.</summary>
    public static IEnumerable<Error> ImageSize(Length? width, Length? height, SourceLocation source) =>
        new[] { (Value: width, Name: "width"), (Value: height, Name: "height") }
            .Where(size => size.Value is { } length && !IsPositiveAndBounded(length))
            .Select(size => Problem(DocumentErrorCodes.Input.Invalid.ImageSize, $"An image {size.Name} must be a positive length of at most {InputLimits.MaxLength}.", size.Value.ToString()!, size.Name, source));

    /// <summary>
    /// A language tag of the form <see cref="LanguageTags"/> describes. The empty string, by which a PDF says that the
    /// language is unknown (ISO 32000-1 §14.9.2.2), is not offered to authors.
    /// </summary>
    public static IEnumerable<Error> Language(string? language, SourceLocation source) =>
        language is null || LanguageTags.IsWellFormed(language)
            ? []
            : [Problem(DocumentErrorCodes.Input.Invalid.Language, "The language is not a language tag such as nl-NL.", language, "language", source)];

    /// <summary>
    /// Text of the document (a span, an image's alternative text): only characters text may hold
    /// (<see cref="TextCharacters"/>: no control character but tab and the forced line breaks, no noncharacter, no
    /// unpaired surrogate). Reported at the element's line with the offset of the first such character.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="property">What the text is, for the message: "text", "alt".</param>
    /// <param name="source">The element's source location.</param>
    public static IEnumerable<Error> Characters(string? text, string property, SourceLocation source) =>
        text is not null && TextCharacters.IndexOfForbidden(text) is { } offset
            ? [Problem(DocumentErrorCodes.Input.Invalid.Text, $"The {property} holds U+{CodePointAt(text, offset):X4} at position {offset}, a character text cannot hold (a control character, a noncharacter or half of a surrogate pair).", text, property, source)
                .With(ErrorMetadataKeys.Offset, offset)]
            : [];

    /// <summary>The code point at an offset: a surrogate pair as one, a lone surrogate as itself.</summary>
    private static int CodePointAt(string text, int offset) =>
        char.IsSurrogatePair(text, offset) ? char.ConvertToUtf32(text, offset) : text[offset];

    /// <summary>
    /// The document's title and author: text that goes into the XMP metadata, which is XML, so only characters XML 1.0
    /// allows (§2.2, production [2] Char; <see cref="XmlCharacters"/>). Reported at the document's line.
    /// </summary>
    public static IEnumerable<Error> Metadata(string? title, string? author, SourceLocation source) =>
        new[] { (Text: title, Property: "title"), (Text: author, Property: "author") }
            .SelectMany(field => MetadataText(field.Text, field.Property, source));

    /// <summary>One metadata text: an error when it holds a character XML 1.0 does not allow.</summary>
    private static IEnumerable<Error> MetadataText(string? text, string property, SourceLocation source) =>
        text is not null && XmlCharacters.IndexOfForbidden(text) is { } offset
            ? [Problem(DocumentErrorCodes.Input.Invalid.MetadataText, $"The {property} holds a character XML does not allow, at position {offset}.", text, property, source)
                .With(ErrorMetadataKeys.Offset, offset)]
            : [];

    /// <summary>
    /// An attachment needs a file name, a media type of the form <c>type/subtype</c> (RFC 6838 §4.2, restricted names),
    /// a file name and description of characters XML allows (as <see cref="Metadata"/>), and a name no other
    /// attachment of the document has: the embedded-files name tree has one entry per name
    /// (ISO 32000-1 §7.9.6).
    /// </summary>
    public static IEnumerable<Error> Attachments(ValueList<Attachment> attachments)
    {
        foreach (var attachment in attachments)
        {
            if (string.IsNullOrWhiteSpace(attachment.FileName))
            {
                yield return Problem(DocumentErrorCodes.Input.Invalid.Attachment, "An attachment needs a file name.", attachment.FileName, "file-name", attachment.Source);
            }

            if (attachment.MediaType is null || !MediaType.IsMatch(attachment.MediaType))
            {
                yield return Problem(DocumentErrorCodes.Input.Invalid.Attachment, "The media type is not of the form type/subtype.", attachment.MediaType ?? "null", "media-type", attachment.Source);
            }

            foreach (var problem in MetadataText(attachment.FileName, "file-name", attachment.Source)
                         .Concat(MetadataText(attachment.Description, "description", attachment.Source)))
            {
                yield return problem;
            }
        }

        foreach (var duplicate in attachments.GroupBy(attachment => attachment.FileName).Where(group => group.Count() > 1))
        {
            yield return Problem(DocumentErrorCodes.Input.Invalid.Attachment, "Two attachments have the same file name.", duplicate.Key, "file-name", duplicate.Last().Source);
        }
    }

    /// <summary>An image is either described or decorative, not both: the two contradict each other whatever the
    /// profile; and a description is not blank, which says nothing to a reader in any profile. Whether a description is
    /// required at all is a profile's rule (PDF/UA-1, checked by the PDF back-end on the page model).</summary>
    public static IEnumerable<Error> ImageDescription(ImageElement image)
    {
        if (image.AlternativeText is not null && image.IsDecorative)
        {
            yield return Problem(DocumentErrorCodes.Input.Invalid.ImageDescription, "An image is either described (Alt) or decorative, not both.", image.AlternativeText, "alt", image.Source);
        }

        // A blank description says nothing to a reader, whatever the profile: describe the image or mark it decorative.
        if (image.AlternativeText is { } text && string.IsNullOrWhiteSpace(text))
        {
            yield return Problem(DocumentErrorCodes.Input.Invalid.ImageDescription, "The alternative text is blank; describe the image, or mark it Decorative().", text, "alt", image.Source);
        }
    }

    /// <summary>RFC 6838 §4.2: <c>type-name "/" subtype-name</c>, each a restricted name of 1–127 characters, matched
    /// against the whole string (<c>\z</c>).</summary>
    private static readonly Regex MediaType = new(@"^[A-Za-z0-9][A-Za-z0-9!#$&^_.+-]{0,126}/[A-Za-z0-9][A-Za-z0-9!#$&^_.+-]{0,126}\z", RegexOptions.CultureInvariant);

    private static Error Problem(ErrorCode code, string message, string value, string property, SourceLocation source) =>
        new Error(code, $"{message} ({source})")
            .With(ErrorMetadataKeys.Value, value)
            .With(ErrorMetadataKeys.Property, property)
            .WithSource(source);
}
