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
/// border model (CSS 2.1 §17.6.2), centred on the line between two cells; or a text decoration line across one piece of a
/// run (CSS Text Decoration 3 §2), a child of the line box next to the text it decorates
/// (<see cref="TextDecorations"/>). The painter draws it as a layout artifact, never as content.
/// </summary>
/// <param name="Color">The line colour.</param>
/// <param name="Size">The rectangle.</param>
public sealed record RuleFragment(Color Color, Size Size) : Fragment(Size);

/// <summary>A line box (CSS 2.1 §9.4.2): one line of a paragraph.</summary>
/// <param name="Size">The line's content width and its height (CSS 2.1 §10.8).</param>
/// <param name="Children">The text fragments and atomic inlines on the line, left to right, and the text decoration lines
/// of each text fragment around it, in painting order (<see cref="RuleFragment"/>).</param>
public sealed record LineFragment(Size Size, ValueList<PlacedFragment> Children) : Fragment(Size)
{
    /// <summary>The parts of the inline groups (<see cref="InlineGroup"/>) on this line, each a range of
    /// <see cref="Children"/>: outer groups before the groups inside them, in document order; two ranges are disjoint or
    /// one holds the other. Empty when the line lies in no group.</summary>
    public ValueList<LineGroupPart> Groups { get; init; } = [];
}

/// <summary>
/// The part of an inline group (<see cref="InlineGroup"/>) on one line: the children of the line it holds and, for a link,
/// the area a reader clicks. The painter opens the group's structure marker around these children
/// (<see cref="PagePainter"/>); a group draws nothing of its own but its box (<see cref="Box"/>).
/// </summary>
/// <param name="Group">The group.</param>
/// <param name="Area">Relative to the line's top-left corner: in the inline direction from the start of the group's first
/// piece on the line to the end of its last (a space removed at the line's end is no piece), in the block direction from
/// the group's own font's ascent above the line's baseline to its descent below it (THE RULE FOR A LINK'S AREA,
/// <see cref="LineBoxes"/>).</param>
/// <param name="First">The index of its first child in <see cref="LineFragment.Children"/>.</param>
/// <param name="Count">How many children it holds: one or more, or none for a group with a box (<see cref="InlineGroup.Box"/>)
/// whose part holds only its edges (an empty inline box).</param>
public sealed record LineGroupPart(InlineGroup Group, Rect Area, int First, int Count)
{
    /// <summary>For a group with a box (<see cref="InlineGroup.Box"/>): its border box on this line, relative to the line's
    /// top-left corner, where its background and border are painted (THE INLINE BOX STYLE; on whole pixels of the page under
    /// <see cref="InlineBoxSnapping.WholePixels"/>, <see cref="InlineBoxPixels"/>); null for a group without a box.</summary>
    public Rect? Box { get; init; }

    /// <summary>True when the group's start edge (<see cref="InlineBoxStyle.Start"/>) lies in this part: the part paints the
    /// box's left border. A box over two or more lines is sliced (<c>box-decoration-break: slice</c>): its first part holds the
    /// start edge, its last the end edge, a part between them neither (THE RULE FOR AN INLINE BOX OVER LINES).</summary>
    public bool HoldsStart { get; init; }

    /// <summary>True when the group's end edge (<see cref="InlineBoxStyle.End"/>) lies in this part: the part paints the box's
    /// right border.</summary>
    public bool HoldsEnd { get; init; }
}

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

/// <summary>
/// One page's slice of monolithic content taller than the page area — an image, or a line box — under
/// <see cref="MonolithicOverflow.Slice"/>: the whole content, placed so that the part this page shows lies inside the
/// slice, drawn clipped to the slice's top and bottom edges (CSS Fragmentation 3 §4.4, "slicing the element's graphical
/// representation"). Chromium clips only in the block direction, so content wider than the slice is not cut at its
/// sides.
/// </summary>
/// <param name="Size">The slice on this page: the content's width, and the height it shows here.</param>
/// <param name="Content">The whole content, at <c>(0, −shown)</c>, where <c>shown</c> is the height earlier pages showed.</param>
/// <param name="IsContinuation">True for every slice after the first. The painter keeps a later slice of an image as
/// content of its figure and marks a later slice of a line as an artifact (<c>ArtifactKind.SliceContinuation</c>), so
/// the line's text is read once.</param>
public sealed record SliceFragment(Size Size, PlacedFragment Content, bool IsContinuation) : Fragment(Size);
