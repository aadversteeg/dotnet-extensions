using Ave.Extensions.Documents.Fonts;

namespace Ave.Extensions.Documents.Typesetting;

/// <summary>
/// One glyph produced by shaping: which glyph, how far it advances the pen, where it is drawn relative to the
/// pen, and which source text it shows.
/// </summary>
/// <param name="Glyph">The glyph id in the run's font.</param>
/// <param name="Advance">The horizontal advance, in points at the run's font size, kerning included.</param>
/// <param name="Text">The characters this glyph shows (its cluster): one character, several for a ligature ("fi"),
/// or empty for the later glyphs of a one-to-many substitution. The PDF writer builds the ToUnicode map from it,
/// so text can be copied and searched.</param>
/// <param name="XOffset">How far the glyph is drawn to the right of the pen, in points (GPOS placement, mark
/// attachment).</param>
/// <param name="YOffset">How far the glyph is drawn ABOVE the baseline, in points (font convention, y up).</param>
public sealed record ShapedGlyph(GlyphId Glyph, double Advance, string Text, double XOffset = 0, double YOffset = 0)
{
    /// <summary>True for the glyph of a space U+0020: removed at the start and end of a line (CSS Text 3 §4.1.2) when it is
    /// collapsible, hanging at the end when it is preserved (<see cref="Hangs"/>), and stretched by justification.</summary>
    public bool IsSpace => Text == " ";

    /// <summary>
    /// True for a preserved space at the end of a line that hangs (CSS Text 3 §4.1.2, "Phase II: Trimming and Positioning"):
    /// its advance is only the part of it that stays inside the line, which is nothing at a soft wrap, and it is no
    /// justification opportunity. Set by line breaking (<see cref="GreedyLineBreaker"/>).
    /// </summary>
    public bool Hangs { get; init; }

    /// <summary>
    /// For a glyph that hangs (<see cref="Hangs"/>), the part of its advance that lies past the line's end: its advance as
    /// shaped minus <see cref="Advance"/>, the part it keeps; 0 for every other glyph. A text decoration is drawn across the
    /// whole advance of a hanging space, inside the line and past its end (Chromium's print, case <c>dl-space</c>).
    /// </summary>
    public double Overhang { get; init; }

    /// <summary>True for a default-ignorable character (a soft hyphen, a joiner, a zero width space, …): its advance
    /// is 0 and it is not drawn, but it stays in the text for line breaking. The Unicode Standard §5.21.6, "Characters
    /// Ignored for Display": the characters to ignore for display are those with the Default_Ignorable_Code_Point
    /// property.</summary>
    public bool IsInvisible { get; init; }

    /// <summary>True for a forced line break (a mandatory break of UAX #14, such as the U+2028 that <c>LineBreak()</c>
    /// writes): invisible, and the end of its line; the collapsible spaces before it are removed as at any line end.</summary>
    public bool IsLineBreak { get; init; }

    /// <summary>For a soft hyphen: the visible hyphen that replaces it when a line breaks after it (UAX #14 §5.4;
    /// CSS Text 3 §5.3, <c>hyphens: manual</c>); null for every other glyph, and for a soft hyphen in a font without a hyphen.</summary>
    public ShapedGlyph? BreakHyphen { get; init; }

    /// <summary>
    /// True when text broken at the start of this glyph's cluster would be shaped differently on either side: a lookup
    /// joined this glyph to one before it (a kerning pair, a mark on its base). HarfBuzz's flag of the same meaning:
    /// "if input text is broken at the beginning of the cluster this glyph is part of, then both sides need to be
    /// re-shaped, as the result might be different. On the flip side, it means that when this flag is not present, then
    /// it is safe to break the glyph-run at the beginning of this cluster" (hb-buffer.h,
    /// <c>HB_GLYPH_FLAG_UNSAFE_TO_BREAK</c>). A break inside a cluster (a ligature that skipped a soft hyphen) is unsafe
    /// as well, as no glyph starts there. The line breaker uses it to shape only the ends of a line again
    /// (<see cref="GreedyLineBreaker"/>).
    /// </summary>
    public bool UnsafeToBreak { get; init; }

    /// <summary>
    /// True for the stand-in of an atomic inline (an image in a line of text; CSS Display 3, Appendix A, "atomic inline"): the
    /// object replacement character U+FFFC in the paragraph's text, as Chromium puts one in its inline text for every
    /// atomic inline (Blink <c>InlineItemsBuilder::AppendAtomicInline</c>, core/layout/inline/inline_items_builder.cc). Its
    /// advance is the width of the inline's margin box; it is never shaped, draws no glyph, and has a break opportunity
    /// before and after it (<see cref="LineBreaking.BreakableSegments.Split"/>). Made by <see cref="AtomicInline"/>.
    /// </summary>
    public bool IsAtomicInline { get; init; }

    /// <summary>
    /// For the stand-in of an edge of an inline box (an inline element with its own margin, border or padding): which edge
    /// it is, <see cref="InlineBoxEdge.None"/> for every other glyph. Its advance is the room the edge takes on the line; it
    /// shows no character (its text is empty) and draws nothing. THE RULE FOR AN INLINE BOX'S EDGES IN A LINE: the start edge
    /// stays with the box's first character and the end edge with its last, as Chromium keeps an open tag with what follows
    /// it and a close tag with what goes before it; an edge is no break opportunity, no space and no place where a justified
    /// line widens, and white-space collapsing does not see it (<see cref="LineBreaking.BreakableSegments.Split"/>). Made by
    /// <see cref="BoxEdge"/>.
    /// </summary>
    public InlineBoxEdge Edge { get; init; }

    /// <summary>The stand-in of an edge of an inline box (<see cref="Edge"/>) of the given width.</summary>
    /// <param name="advance">The room the edge takes, in points: margin, border and padding on that side.</param>
    /// <param name="edge">Which edge: <see cref="InlineBoxEdge.Start"/> or <see cref="InlineBoxEdge.End"/>.</param>
    /// <returns>The glyph.</returns>
    public static ShapedGlyph BoxEdge(double advance, InlineBoxEdge edge) => new(default, advance, string.Empty) { Edge = edge };

    /// <summary>The object replacement character U+FFFC, which stands for an atomic inline in a paragraph's text.</summary>
    public const char ObjectReplacementCharacter = '￼';

    /// <summary>The stand-in of an atomic inline (<see cref="IsAtomicInline"/>) of the given width.</summary>
    /// <param name="advance">The width of the atomic inline's margin box, in points.</param>
    /// <returns>The glyph.</returns>
    public static ShapedGlyph AtomicInline(double advance) =>
        new(default, advance, ObjectReplacementCharacter.ToString()) { IsAtomicInline = true };
}

/// <summary>Which edge of an inline box a glyph stands for (<see cref="ShapedGlyph.Edge"/>).</summary>
public enum InlineBoxEdge
{
    /// <summary>No edge: a glyph of text or an atomic inline.</summary>
    None,

    /// <summary>The start edge, before the box's first character.</summary>
    Start,

    /// <summary>The end edge, after the box's last character.</summary>
    End,
}
