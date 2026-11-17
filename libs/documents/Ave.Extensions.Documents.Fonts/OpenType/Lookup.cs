using Ave.Documents.Core;

namespace Ave.Documents.Fonts.OpenType;

/// <summary>A lookup (OpenType spec, "Lookup table"): subtables of one lookup type, tried in order.</summary>
/// <param name="Index">Its index in the lookup list.</param>
/// <param name="Flags">The lookup flags: which glyphs the lookup skips.</param>
/// <param name="Subtables">The subtables, already resolved through extension subtables.</param>
public sealed record Lookup(int Index, LookupFlags Flags, ValueList<LookupSubtable> Subtables);
