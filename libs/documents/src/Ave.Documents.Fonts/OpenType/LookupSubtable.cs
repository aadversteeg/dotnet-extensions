using System.Collections.Frozen;
using System.Collections.Immutable;
using Ave.Documents.Core;

namespace Ave.Documents.Fonts.OpenType;

// The lookup subtables this reader understands, decoded from GSUB and GPOS. A closed set: only the cases in this
// file exist, so the shaper can switch over all of them. A subtable of any other type or format is kept as an
// UnsupportedSubtable, so a font that uses one still loads and the shaper can say what it skipped.

/// <summary>One subtable of a lookup.</summary>
public abstract record LookupSubtable
{
    private protected LookupSubtable()
    {
    }
}

/// <summary>GSUB lookup type 1, single substitution: replaces one glyph with another. Format 1 adds a delta to every
/// covered glyph id ("Add to original glyph ID to get substitute glyph ID", modulo 65536); format 2 takes the
/// substitute from a list indexed by coverage index.</summary>
/// <param name="Glyphs">The covered glyphs.</param>
/// <param name="Delta">Format 1: the delta; 0 for format 2.</param>
/// <param name="Substitutes">Format 2: the substitute per coverage index; empty for format 1.</param>
/// <param name="IsDelta">True for format 1.</param>
public sealed record SingleSubstitution(Coverage Glyphs, short Delta, ValueList<GlyphId> Substitutes, bool IsDelta) : LookupSubtable
{
    /// <summary>The glyph that replaces <paramref name="glyph"/>, or null when the subtable does not cover it.</summary>
    /// <param name="glyph">The glyph.</param>
    /// <returns>The replacement, or null.</returns>
    public GlyphId? Replace(GlyphId glyph) => Glyphs.IndexOf(glyph) switch
    {
        null => null,
        _ when IsDelta => new GlyphId((ushort)((glyph.Value + Delta) & 0xFFFF)),
        { } index when index < Substitutes.Count => Substitutes[index],
        _ => null,
    };
}

/// <summary>GSUB lookup type 2, multiple substitution: replaces one glyph with a sequence (decompositions in
/// <c>ccmp</c>).</summary>
/// <param name="Glyphs">The covered glyphs.</param>
/// <param name="Sequences">The glyphs that replace each covered glyph, by coverage index.</param>
public sealed record MultipleSubstitution(Coverage Glyphs, ValueList<ValueList<GlyphId>> Sequences) : LookupSubtable
{
    /// <summary>The glyphs that replace <paramref name="glyph"/>, or null when the subtable does not cover it.</summary>
    /// <param name="glyph">The glyph.</param>
    /// <returns>The sequence, or null.</returns>
    public ValueList<GlyphId>? SequenceOf(GlyphId glyph) =>
        Glyphs.IndexOf(glyph) is { } index && index < Sequences.Count ? Sequences[index] : null;
}

/// <summary>GSUB lookup type 4, ligature substitution: replaces a sequence of glyphs with one ligature glyph.</summary>
/// <param name="FirstGlyphs">The covered first glyphs.</param>
/// <param name="Sets">For each coverage index, the candidate ligatures in preference order ("The order in the Ligature
/// offset array defines the preference for using the ligatures."), and the first that matches is used.</param>
public sealed record LigatureSubstitution(Coverage FirstGlyphs, ValueList<ValueList<Ligature>> Sets) : LookupSubtable
{
    /// <summary>The ligatures that start with <paramref name="glyph"/>; empty when the subtable does not cover it.</summary>
    /// <param name="glyph">The first glyph.</param>
    /// <returns>The candidates in preference order.</returns>
    public ValueList<Ligature> CandidatesFor(GlyphId glyph) =>
        FirstGlyphs.IndexOf(glyph) is { } index && index < Sets.Count ? Sets[index] : [];
}

/// <summary>One ligature of a <see cref="LigatureSubstitution"/>.</summary>
/// <param name="Glyph">The ligature glyph.</param>
/// <param name="Components">The component glyphs AFTER the first one, in order (componentGlyphIDs: "start with the
/// second component, ordered in writing direction").</param>
public sealed record Ligature(GlyphId Glyph, ValueList<GlyphId> Components);

/// <summary>GPOS lookup type 2, pair adjustment, format 1: adjustments for individual pairs of glyphs. The pairs
/// are read through <see cref="Find"/>; the lookup structure behind it is the reader's own.</summary>
public sealed record PairAdjustmentByGlyph : LookupSubtable
{
    /// <summary>Creates the subtable.</summary>
    /// <param name="firstGlyphs">The covered first glyphs.</param>
    /// <param name="sets">For each coverage index, the second glyphs of its pairs with their two value records.</param>
    /// <param name="secondHasValues">See <see cref="SecondHasValues"/>.</param>
    internal PairAdjustmentByGlyph(Coverage firstGlyphs, ValueList<FrozenDictionary<ushort, PairValues>> sets, bool secondHasValues) =>
        (FirstGlyphs, Sets, SecondHasValues) = (firstGlyphs, sets, secondHasValues);

    /// <summary>The covered first glyphs.</summary>
    public Coverage FirstGlyphs { get; }

    /// <summary>True when valueFormat2 is not zero: then the second glyph is consumed by the pair; otherwise it can
    /// start the next pair ("If valueFormat2 is set to 0, then the second glyph of the pair is the “next” glyph for
    /// which a lookup should be performed.").</summary>
    public bool SecondHasValues { get; }

    /// <summary>For each coverage index, the second glyphs of its pairs with their two value records.</summary>
    internal ValueList<FrozenDictionary<ushort, PairValues>> Sets { get; }

    /// <summary>The adjustments of a pair, or null when the subtable has none for it.</summary>
    /// <param name="first">The first glyph.</param>
    /// <param name="second">The second glyph.</param>
    /// <returns>The two value records, or null.</returns>
    public PairValues? Find(GlyphId first, GlyphId second) =>
        FirstGlyphs.IndexOf(first) is { } index && index < Sets.Count && Sets[index].TryGetValue(second.Value, out var values) ? values : null;
}

/// <summary>GPOS lookup type 2, pair adjustment, format 2: adjustments for pairs of glyph CLASSES.</summary>
/// <param name="FirstGlyphs">The coverage of the first glyph; a pair applies only when the first glyph is covered.</param>
/// <param name="FirstClasses">The class definition for first glyphs.</param>
/// <param name="SecondClasses">The class definition for second glyphs.</param>
/// <param name="Values">The value records, row by row: <c>Values[class1 × SecondClassCount + class2]</c>.</param>
/// <param name="FirstClassCount">class1Count.</param>
/// <param name="SecondClassCount">class2Count.</param>
/// <param name="SecondHasValues">As in <see cref="PairAdjustmentByGlyph.SecondHasValues"/>.</param>
public sealed record PairAdjustmentByClass(
    Coverage FirstGlyphs,
    ClassDefinition FirstClasses,
    ClassDefinition SecondClasses,
    ImmutableArray<PairValues> Values,
    int FirstClassCount,
    int SecondClassCount,
    bool SecondHasValues) : LookupSubtable;

/// <summary>The adjustments of one pair: for the first and for the second glyph.</summary>
/// <param name="First">For the first glyph.</param>
/// <param name="Second">For the second glyph.</param>
public readonly record struct PairValues(ValueRecord First, ValueRecord Second);

/// <summary>
/// A value record (OpenType spec, "GPOS" → "Value record"), in font design units. Device and variation tables
/// adjust for sizes and variations; they are read past and not applied (they only matter for hinted bitmap
/// output and variable fonts).
/// </summary>
/// <param name="XPlacement">Horizontal adjustment of the glyph's position.</param>
/// <param name="YPlacement">Vertical adjustment of the glyph's position (y up).</param>
/// <param name="XAdvance">Adjustment of the horizontal advance.</param>
/// <param name="YAdvance">Adjustment of the vertical advance.</param>
public readonly record struct ValueRecord(short XPlacement, short YPlacement, short XAdvance, short YAdvance);

/// <summary>GPOS lookup type 4, mark-to-base attachment, format 1: puts a mark glyph on an anchor of its base.</summary>
/// <param name="MarkGlyphs">The covered mark glyphs.</param>
/// <param name="Marks">For each mark coverage index: its mark class and its anchor.</param>
/// <param name="BaseGlyphs">The covered base glyphs.</param>
/// <param name="Bases">For each base coverage index: one anchor per mark class, null where the base has none.</param>
public sealed record MarkToBase(
    Coverage MarkGlyphs,
    ValueList<(int MarkClass, Anchor Anchor)> Marks,
    Coverage BaseGlyphs,
    ValueList<ValueList<Anchor?>> Bases) : LookupSubtable
{
    /// <summary>The class and anchor of a mark glyph, or null when the subtable does not cover it.</summary>
    /// <param name="glyph">The mark glyph.</param>
    /// <returns>Its class and anchor, or null.</returns>
    public (int MarkClass, Anchor Anchor)? MarkOf(GlyphId glyph) =>
        MarkGlyphs.IndexOf(glyph) is { } index && index < Marks.Count ? Marks[index] : null;

    /// <summary>The anchor of a base glyph for a mark class, or null when it has none.</summary>
    /// <param name="glyph">The base glyph.</param>
    /// <param name="markClass">The mark's class.</param>
    /// <returns>The anchor, or null.</returns>
    public Anchor? BaseAnchor(GlyphId glyph, int markClass) =>
        BaseGlyphs.IndexOf(glyph) is { } index && index < Bases.Count && markClass < Bases[index].Count ? Bases[index][markClass] : null;
}

/// <summary>An anchor point (OpenType spec, "GPOS" → "Anchor tables", formats 1–3), in font design units, y up.
/// Format 2's contour point and format 3's device tables refine the point for hinting and are not applied.</summary>
/// <param name="X">Horizontal coordinate.</param>
/// <param name="Y">Vertical coordinate.</param>
public readonly record struct Anchor(short X, short Y);

/// <summary>A subtable of a lookup type or format this reader does not implement.</summary>
/// <param name="Table">GSUB or GPOS.</param>
/// <param name="LookupType">The lookup type.</param>
/// <param name="Format">The subtable format.</param>
public sealed record UnsupportedSubtable(string Table, int LookupType, int Format) : LookupSubtable;
