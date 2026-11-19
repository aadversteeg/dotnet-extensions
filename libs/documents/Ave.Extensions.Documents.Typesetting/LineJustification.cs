using System.Collections.Immutable;
using System.Linq;
using Ave.Extensions.Documents.Core;

namespace Ave.Extensions.Documents.Typesetting;

/// <summary>
/// Places a line between the edges of its line box: alignment and inter-word justification (CSS Text 3 §6.1
/// <c>text-align</c>, §6.4 <c>text-justify</c>).
/// </summary>
public static class LineJustification
{
    /// <summary>
    /// The word-separator characters, the justification opportunities of <c>text-justify: inter-word</c> (CSS
    /// Text 3 §7.1, which lists among the word-separator characters the space U+0020, the no-break space U+00A0, the
    /// Ethiopic word space U+1361, the Aegean word separators U+10100 and U+10101, the Ugaritic word divider U+1039F and
    /// the Phoenician word separator U+1091F).
    /// </summary>
    private static readonly ImmutableHashSet<string> WordSeparators =
    [
        " ", char.ConvertFromUtf32(0x00A0), char.ConvertFromUtf32(0x1361), char.ConvertFromUtf32(0x10100),
        char.ConvertFromUtf32(0x10101), char.ConvertFromUtf32(0x1039F), char.ConvertFromUtf32(0x1091F),
    ];

    /// <summary>
    /// Places one line. Returns the line to draw (for <see cref="TextAlign.Justify"/> with wider word separators)
    /// and how far right of the line box's left edge it starts.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>Left, right, centre: the line moves; nothing inside it changes (§6.1).</item>
    /// <item>Justify: the free space is shared equally by the word separators inside the line (<c>text-justify:
    /// inter-word</c>, §6.4; the word-separator characters are listed with <c>word-spacing</c>, §7.1); the spaces at the line's ends were
    /// already removed by line breaking. The last line of the paragraph and a line that ends in a forced break are
    /// not justified but start-aligned (<c>text-align-last: auto</c>, §6.3). A line without a word separator cannot
    /// be justified inter-word and is start-aligned too.</item>
    /// <item>A line wider than its box is start-aligned: "If … the inline contents of a line box are too long to
    /// fit within it, then the contents are start-aligned" (§6.1).</item>
    /// </list>
    /// </remarks>
    /// <param name="line">The line, as broken.</param>
    /// <param name="width">The width of the line box.</param>
    /// <param name="align">The paragraph's text-align.</param>
    /// <param name="isLastLine">True for the paragraph's last line.</param>
    /// <returns>The line to draw and its left offset.</returns>
    public static (TextLine Line, double Offset) Place(TextLine line, double width, TextAlign align, bool isLastLine)
    {
        var free = width - line.Width;
        if (free <= Fitting.TolerancePoints)
        {
            return (line, 0);
        }

        return align switch
        {
            TextAlign.Right => (line, free),
            TextAlign.Center => (line, free / 2),
            TextAlign.Justify when !isLastLine && !line.EndsWithForcedBreak => (Justify(line, free), 0),
            _ => (line, 0),
        };
    }

    /// <summary>Widens every word separator of the line by an equal share of <paramref name="free"/>.</summary>
    private static TextLine Justify(TextLine line, double free)
    {
        var separators = line.Segments.Sum(segment => segment.Glyphs.Count(IsWordSeparator));
        if (separators == 0)
        {
            return line;
        }

        var share = free / separators;
        return line with
        {
            Segments = [.. line.Segments.Select(segment => segment with
            {
                Glyphs = [.. segment.Glyphs.Select(glyph => IsWordSeparator(glyph) ? glyph with { Advance = glyph.Advance + share } : glyph)],
            })],
        };
    }

    /// <summary>A word separator that does not hang at the line's end (<see cref="ShapedGlyph.Hangs"/>): a hanging space is
    /// outside the line, so it does not widen (case <c>ws-hang-pre-wrap</c>, justified lines: the word before it ends at
    /// the right edge). A preserved space inside the line widens as any space (case
    /// <c>ws-justify</c>: both spaces of a double space widen).</summary>
    private static bool IsWordSeparator(ShapedGlyph glyph) => !glyph.Hangs && WordSeparators.Contains(glyph.Text);
}
