namespace Ave.Documents.Fonts.OpenType;

/// <summary>
/// The lookupFlag field of a lookup table (OpenType spec, "Lookup table" → "LookupFlag bit enumeration"): which
/// glyphs the lookup skips when it looks for its input sequence.
/// </summary>
/// <param name="Value">The raw 16-bit value.</param>
internal readonly record struct LookupFlags(ushort Value)
{
    /// <summary>"If set, skips over base glyphs" (bit 0x0002).</summary>
    public bool IgnoreBaseGlyphs => (Value & 0x0002) != 0;

    /// <summary>"If set, skips over ligatures" (bit 0x0004).</summary>
    public bool IgnoreLigatures => (Value & 0x0004) != 0;

    /// <summary>"If set, skips over all combining marks" (bit 0x0008).</summary>
    public bool IgnoreMarks => (Value & 0x0008) != 0;

    /// <summary>"If set, indicates that the lookup table structure is followed by a MarkFilteringSet field"
    /// (bit 0x0010). Mark filtering sets are not implemented: such a lookup is skipped (see the shaper).</summary>
    public bool UsesMarkFilteringSet => (Value & 0x0010) != 0;

    /// <summary>"If not zero, skips over all marks of attachment type different from specified" (high byte).</summary>
    public int MarkAttachmentType => Value >> 8;
}
