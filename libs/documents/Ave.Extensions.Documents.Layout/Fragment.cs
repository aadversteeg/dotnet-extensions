using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Typesetting;

namespace Ave.Extensions.Documents.Layout;

/// <summary>
/// The output of layout for one page: an immutable tree of rectangles (CSS Fragmentation 3 §2: "The portion of
/// a box that belongs to exactly one fragmentainer"). A box split over two pages has one fragment on each page.
/// Child positions are relative to the parent fragment's top-left corner.
/// </summary>
public abstract record Fragment
{
    // Only the cases in this assembly exist, so the painter can handle every one of them.
    private protected Fragment(Size size) => Size = size;

    /// <summary>The fragment's width and height in points.</summary>
    public Size Size { get; }
}

/// <summary>A fragment positioned inside its parent.</summary>
/// <param name="Offset">The fragment's top-left corner, relative to the parent's top-left corner.</param>
/// <param name="Fragment">The fragment.</param>
public sealed record PlacedFragment(Point Offset, Fragment Fragment);

/// <summary>The fragment of a <see cref="Box"/>: its border box on this page.</summary>
/// <param name="Box">The document node.</param>
/// <param name="Size">The border box size on this page (padding and border included, sliced at page breaks).</param>
/// <param name="Children">Child fragments: boxes for Flow, lines for Inline, row groups and grid lines for Tracks,
/// an image for Image.</param>
/// <param name="Border">The border sides this fragment paints. With <c>box-decoration-break: slice</c> the top side
/// belongs to the first fragment only and the bottom side to the last (CSS Fragmentation 3 §5.4); a table cell in
/// the collapsing border model paints none, because the table paints the resolved grid lines.</param>
/// <param name="IsRepeated">True for a copy of content that already appeared on an earlier page: the header rows a
/// table repeats (CSS 2.1 §17.2). The painter marks such content as decoration, not as document structure.</param>
public sealed record BoxFragment(Box Box, Size Size, ValueList<PlacedFragment> Children, Border Border = default, bool IsRepeated = false)
    : Fragment(Size);

/// <summary>The fragment of an <see cref="ImageNode"/>: the image scaled to its used size.</summary>
/// <param name="Image">The document node.</param>
/// <param name="Size">The used width and height.</param>
public sealed record ImageFragment(ImageNode Image, Size Size) : Fragment(Size);

/// <summary>
/// A filled rectangle that belongs to no box of its own: one segment of a resolved table grid line in the collapsing
/// border model (CSS 2.1 §17.6.2), centred on the line between two cells.
/// </summary>
/// <param name="Color">The line colour.</param>
/// <param name="Size">The rectangle.</param>
public sealed record RuleFragment(Color Color, Size Size) : Fragment(Size);

/// <summary>A line box (CSS 2.1 §9.4.2): one line of a paragraph.</summary>
/// <param name="Size">The line's content width and its height (CSS 2.1 §10.8).</param>
/// <param name="Children">The text fragments on the line, left to right.</param>
public sealed record LineFragment(Size Size, ValueList<PlacedFragment> Children) : Fragment(Size);

/// <summary>
/// The part of one <see cref="InlineRun"/> that lies on one line. Its top edge is <see cref="Ascent"/> above the
/// line's baseline, so the baseline lies at <c>offset.Y + Ascent</c>.
/// </summary>
/// <param name="Run">The document node.</param>
/// <param name="Font">The font the run resolved to.</param>
/// <param name="Ascent">The font's ascent at the run's size, in points.</param>
/// <param name="Glyphs">The glyphs on this line.</param>
/// <param name="Size">The glyphs' total advance and the font's ascent plus descent.</param>
public sealed record TextFragment(InlineRun Run, TrueTypeFont Font, double Ascent, ValueList<ShapedGlyph> Glyphs, Size Size)
    : Fragment(Size);
