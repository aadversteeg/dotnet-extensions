using System.Collections.Frozen;

namespace Ave.Documents.Fonts.OpenType;

/// <summary>One format-0 subtable of the 'kern' table. Its pairs are read through <see cref="ValueOf"/>.</summary>
public sealed record KerningSubtable
{
    /// <summary>Creates the subtable.</summary>
    /// <param name="pairs">The adjustment for each (left, right) glyph id pair, in font design units.</param>
    /// <param name="override">See <see cref="Override"/>.</param>
    internal KerningSubtable(FrozenDictionary<(ushort Left, ushort Right), short> pairs, bool @override) =>
        (Pairs, Override) = (pairs, @override);

    /// <summary>True when the subtable's values replace, rather than add to, earlier ones.</summary>
    public bool Override { get; }

    /// <summary>The adjustment for each (left, right) glyph id pair, in font design units.</summary>
    internal FrozenDictionary<(ushort Left, ushort Right), short> Pairs { get; }

    /// <summary>The adjustment of a pair, in font design units, or null when the subtable has none for it.</summary>
    /// <param name="left">The left glyph.</param>
    /// <param name="right">The right glyph.</param>
    /// <returns>The value, or null.</returns>
    public short? ValueOf(GlyphId left, GlyphId right) => Pairs.TryGetValue((left.Value, right.Value), out var value) ? value : null;
}
