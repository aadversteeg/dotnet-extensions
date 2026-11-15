namespace Ave.Documents.Typesetting.Shaping;

/// <summary>The classes of the OpenType glyph definition table (GDEF glyphClassDef) that shaping uses.</summary>
internal enum GlyphClass
{
    /// <summary>A base glyph (class 1), or a glyph with no class.</summary>
    Base,

    /// <summary>A ligature glyph (class 2).</summary>
    Ligature,

    /// <summary>A mark glyph (class 3).</summary>
    Mark,
}
