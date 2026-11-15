using System.Collections.Generic;
using System.Linq;
using Ave.Documents.Core;
using Ave.Extensions.ErrorPaths;

namespace Ave.Documents.Fluent;

/// <summary>
/// The validity rules of the code front-end. Each function returns every problem it finds (an empty list when
/// there are none); every problem carries the source location of the call that set the value.
/// </summary>
internal static class InputChecks
{
    private static readonly string[] Sides = ["top", "right", "bottom", "left"];

    /// <summary>Page size: positive finite width and height; margins finite and not negative; and the margins
    /// must leave a page area (CSS Paged Media 3 §3.1) to lay content out in.</summary>
    public static IEnumerable<Error> Page(PageSetup page, SourceLocation source)
    {
        if (!(page.Width.IsFinite && page.Height.IsFinite && page.Width.Points > 0 && page.Height.Points > 0))
        {
            yield return Problem(DocumentErrorCodes.Input.Invalid.PageSize, "The page width and height must be positive.", $"{page.Width}x{page.Height}", "size", source);
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
    /// Margins and padding. Padding may not be negative (CSS Box Model 3 §5.2: "Negative values for padding
    /// properties are invalid"). Negative margins are valid CSS but not implemented yet, so they are reported as
    /// unsupported rather than drawn wrongly.
    /// </summary>
    public static IEnumerable<Error> Box(Cascade.BoxValues box, SourceLocation source) =>
        Lengths(box.Margin, "margin", source, DocumentErrorCodes.Input.Unsupported.NegativeMargin)
            .Concat(Lengths(box.Padding, "padding", source, DocumentErrorCodes.Input.Invalid.Length));

    /// <summary>
    /// Text formatting: a family name that is not empty, a positive finite size (CSS Fonts 4 §2.5: "Negative
    /// values are not allowed"; zero would make every glyph invisible), and a weight in 1–1000 (§2.2).
    /// </summary>
    public static IEnumerable<Error> Text(TextFormat format, SourceLocation source)
    {
        if (format.FontFamily is { } family && string.IsNullOrWhiteSpace(family))
        {
            yield return Problem(DocumentErrorCodes.Input.Invalid.FontFamily, "The font family is empty.", family, "font-family", source);
        }

        if (format.FontSize is { } size && !(size.IsFinite && size.Points > 0))
        {
            yield return Problem(DocumentErrorCodes.Input.Invalid.FontSize, "The font size must be a positive number.", size.ToString(), "font-size", source);
        }

        if (format.FontWeight is { } weight && !weight.IsValid)
        {
            yield return Problem(DocumentErrorCodes.Input.Invalid.FontWeight, "The font weight must be 1–1000.", weight.ToString(), "font-weight", source);
        }
    }

    /// <summary>Heading levels are 1–6, as PDF structure types H1–H6 (ISO 32000-1 §14.8.4.3.2).</summary>
    public static IEnumerable<Error> Role(SemanticRole role, SourceLocation source) =>
        role is SemanticRole.HeadingRole { Level: < 1 or > 6 } heading
            ? [Problem(DocumentErrorCodes.Input.Invalid.HeadingLevel, "A heading level must be 1–6.", heading.Level.ToString(System.Globalization.CultureInfo.InvariantCulture), "level", source)]
            : [];

    /// <summary>The document names no font family: fonts are explicit input, so there is no default family.</summary>
    public static Error MissingFontFamily(SourceLocation source) =>
        Problem(DocumentErrorCodes.Input.Invalid.FontFamily, "The document must name a font family.", "null", "font-family", source);

    /// <summary>A span got a shared style that sets box properties; a span has no box, so they cannot apply.</summary>
    public static Error BoxPropertiesOnSpan(SourceLocation source) =>
        Problem(DocumentErrorCodes.Input.Invalid.NotApplicable, "A span has no box and no lines: its style may not set margin, padding, background or text alignment.", "Style", "style", source);

    /// <summary>A page break with nothing after it in its list has nothing to move to the next page.</summary>
    public static Error TrailingPageBreak(SourceLocation source) =>
        Problem(DocumentErrorCodes.Input.Invalid.PageBreak, "A page break must be followed by an element in the same list.", "PageBreak", "break-before", source);

    /// <summary>Checks four sides: finite (else Input.Invalid.Length) and not negative (else <paramref name="negativeCode"/>).</summary>
    private static IEnumerable<Error> Lengths(Edges edges, string property, SourceLocation source, ErrorCode negativeCode) =>
        edges.Sides.Zip(Sides).SelectMany(side => side.First switch
        {
            { IsFinite: false } => [Problem(DocumentErrorCodes.Input.Invalid.Length, $"{property}-{side.Second} must be a number.", side.First.ToString(), $"{property}-{side.Second}", source)],
            { Points: < 0 } => [Problem(negativeCode, $"{property}-{side.Second} may not be negative.", side.First.ToString(), $"{property}-{side.Second}", source)],
            _ => System.Array.Empty<Error>(),
        });

    /// <summary>Border widths: finite and not negative (CSS Backgrounds 3 §4.3: "Negative values are invalid").</summary>
    public static IEnumerable<Error> Border(DocumentModel.Border border, SourceLocation source) =>
        Lengths(border.Widths, "border", source, DocumentErrorCodes.Input.Invalid.Length);

    /// <summary>
    /// Column definitions: at least one; fixed widths finite and not negative; fill weights finite and positive
    /// (CSS Grid 1 §7.2.4: "Negative values are invalid" for <c>fr</c>; zero would make a column that never grows).
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
                DocumentModel.TrackSize.Fixed fixedWidth => fixedWidth.Width.IsFinite && fixedWidth.Width.Points >= 0,
                DocumentModel.TrackSize.Fill fill => double.IsFinite(fill.Weight) && fill.Weight > 0,
                _ => true,
            };
            if (!valid)
            {
                yield return Problem(DocumentErrorCodes.Input.Invalid.Columns, "A fixed column width must be zero or more and a fill weight positive.", column.ToString(), "columns", source);
            }
        }
    }

    /// <summary>A row of blocks with column sizes must have one size per item.</summary>
    public static IEnumerable<Error> ColumnCount(int columns, int items, SourceLocation source) =>
        columns == items
            ? []
            : [Problem(DocumentErrorCodes.Input.Invalid.Columns, $"The row has {items} items but {columns} column sizes.", columns.ToString(System.Globalization.CultureInfo.InvariantCulture), "columns", source)];

    /// <summary>Cell spans are at least one column and one row.</summary>
    public static IEnumerable<Error> Span(DocumentModel.CellSpan span, SourceLocation source) =>
        span.Columns >= 1 && span.Rows >= 1
            ? []
            : [Problem(DocumentErrorCodes.Input.Invalid.Table, "A cell spans at least one column and one row.", span.ToString(), "span", source)];

    /// <summary>A table's structure as the front-end sees it: header rows only at the start, not in a row group.</summary>
    public static Error MisplacedHeaderRow(SourceLocation source) =>
        Problem(DocumentErrorCodes.Input.Invalid.Table, "Header rows must come first in a table, outside any row group.", "HeaderRow", "rows", source);

    /// <summary>A property set on a table part it does not apply to: padding on a table (the collapsing border model
    /// has none, CSS 2.1 §17.6.2), margin or padding on a row or a row group.</summary>
    public static IEnumerable<Error> NotApplicable(Edges edges, string property, string element, SourceLocation source) =>
        edges == Edges.Zero
            ? []
            : [Problem(DocumentErrorCodes.Input.Invalid.NotApplicable, $"A {element} has no {property}.", edges.ToString(), property, source)];

    /// <summary>A page break inside a table cell or a row of blocks has no next sibling on another page to move.</summary>
    public static IEnumerable<Error> PageBreakIn(ValueList<Element> content, string container) =>
        content.OfType<PageBreakElement>().Select(pageBreak =>
            Problem(DocumentErrorCodes.Input.Invalid.PageBreak, $"A page break is not allowed inside a {container}.", "PageBreak", "break-before", pageBreak.Source));

    /// <summary>An image's width and height, when set, are positive finite lengths.</summary>
    public static IEnumerable<Error> ImageSize(Length? width, Length? height, SourceLocation source) =>
        new[] { (Value: width, Name: "width"), (Value: height, Name: "height") }
            .Where(size => size.Value is { } length && !(length.IsFinite && length.Points > 0))
            .Select(size => Problem(DocumentErrorCodes.Input.Invalid.ImageSize, $"An image {size.Name} must be a positive length.", size.Value.ToString()!, size.Name, source));

    private static Error Problem(ErrorCode code, string message, string value, string property, SourceLocation source) =>
        new Error(code, $"{message} ({source})")
            .With(ErrorMetadataKeys.Value, value)
            .With(ErrorMetadataKeys.Property, property)
            .WithSource(source);
}
