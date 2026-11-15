using System.Collections.Immutable;
using Ave.Documents.Core;
using Ave.Documents.Fonts;
using Ave.Documents.DocumentModel;
using Ave.Documents.Typesetting;

namespace Ave.Documents.Layout;

/// <summary>
/// The output of layout for one page: an immutable tree of rectangles (CSS Fragmentation 3 §2: "the portion of
/// a box that belongs to one fragmentainer"). A box split over two pages has one fragment on each page.
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
/// <param name="Size">The border box size on this page (padding included, sliced at page breaks).</param>
/// <param name="Children">Child fragments: boxes for Flow, lines for Inline.</param>
public sealed record BoxFragment(Box Box, Size Size, ImmutableArray<PlacedFragment> Children) : Fragment(Size);

/// <summary>A line box (CSS 2.1 §9.4.2): one line of a paragraph.</summary>
/// <param name="Size">The line's content width and its height (CSS 2.1 §10.8).</param>
/// <param name="Children">The text fragments on the line, left to right.</param>
public sealed record LineFragment(Size Size, ImmutableArray<PlacedFragment> Children) : Fragment(Size);

/// <summary>
/// The part of one <see cref="TextRun"/> that lies on one line. Its top edge is <see cref="Ascent"/> above the
/// line's baseline, so the baseline lies at <c>offset.Y + Ascent</c>.
/// </summary>
/// <param name="Run">The document node.</param>
/// <param name="Font">The font the run resolved to.</param>
/// <param name="Ascent">The font's ascent at the run's size, in points.</param>
/// <param name="Glyphs">The glyphs on this line.</param>
/// <param name="Size">The glyphs' total advance and the font's ascent plus descent.</param>
public sealed record TextFragment(TextRun Run, TrueTypeFont Font, double Ascent, ImmutableArray<ShapedGlyph> Glyphs, Size Size)
    : Fragment(Size);
