using System.Collections.Frozen;

namespace Ave.Documents.Fonts.OpenType;

/// <summary>One format-0 subtable of the 'kern' table.</summary>
/// <param name="Pairs">The adjustment for each (left, right) glyph id pair, in font design units.</param>
/// <param name="Override">True when the subtable's values replace, rather than add to, earlier ones.</param>
internal sealed record KerningSubtable(FrozenDictionary<(ushort Left, ushort Right), short> Pairs, bool Override);
