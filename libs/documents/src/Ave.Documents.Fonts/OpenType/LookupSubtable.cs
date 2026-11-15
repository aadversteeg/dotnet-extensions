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

/// <summary>GSUB lookup type 1, single substitution, formats 1 (delta) and 2 (substitute array): replaces one glyph
/// with another.</summary>
/// <param name="Replacements">For each covered glyph id, its replacement.</param>
public sealed record SingleSubstitution(FrozenDictionary<ushort, GlyphId> Replacements) : LookupSubtable;

/// <summary>GSUB lookup type 2, multiple substitution: replaces one glyph with a sequence (decompositions in
/// <c>ccmp</c>).</summary>
/// <param name="Sequences">For each covered glyph id, the glyphs that replace it.</param>
public sealed record MultipleSubstitution(FrozenDictionary<ushort, ValueList<GlyphId>> Sequences) : LookupSubtable;

/// <summary>GSUB lookup type 4, ligature substitution: replaces a sequence of glyphs with one ligature glyph.</summary>
/// <param name="Ligatures">For each covered first glyph id, its candidate ligatures in preference order
/// ("Ligatures with more components should be stored ahead of those with fewer components", and the first that
/// matches is used).</param>
public sealed record LigatureSubstitution(FrozenDictionary<ushort, ValueList<Ligature>> Ligatures) : LookupSubtable;

/// <summary>One ligature of a <see cref="LigatureSubstitution"/>.</summary>
/// <param name="Glyph">The ligature glyph.</param>
/// <param name="Components">The component glyphs AFTER the first one, in order ("componentGlyphIDs … beginning
/// with the second component").</param>
public sealed record Ligature(GlyphId Glyph, ValueList<GlyphId> Components);

/// <summary>GPOS lookup type 2, pair adjustment, format 1: adjustments for individual pairs of glyphs.</summary>
/// <param name="Pairs">For each (first, second) glyph id pair, the two value records.</param>
/// <param name="SecondHasValues">True when valueFormat2 is not zero: then the second glyph is consumed by the
/// pair; otherwise it can start the next pair ("If valueFormat2 is set to 0, then the second glyph of the pair is
/// the 'first' glyph of the next pair").</param>
public sealed record PairAdjustmentByGlyph(FrozenDictionary<(ushort First, ushort Second), PairValues> Pairs, bool SecondHasValues) : LookupSubtable;

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
/// <param name="Marks">For each covered mark glyph id: its mark class and its anchor.</param>
/// <param name="Bases">For each covered base glyph id: one anchor per mark class, null where the base has none.</param>
public sealed record MarkToBase(
    FrozenDictionary<ushort, (int MarkClass, Anchor Anchor)> Marks,
    FrozenDictionary<ushort, ValueList<Anchor?>> Bases) : LookupSubtable;

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
