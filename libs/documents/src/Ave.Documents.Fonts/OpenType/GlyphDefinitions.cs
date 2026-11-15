using System;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Documents.Fonts.OpenType;

/// <summary>
/// The parts of the glyph definition table GDEF that shaping needs (OpenType spec, "GDEF — Glyph definition
/// table"): the class of every glyph (base, ligature, mark, component), which the lookup flags use to skip glyphs,
/// and the mark attachment classes.
/// </summary>
/// <param name="GlyphClasses">glyphClassDef: 1 base, 2 ligature, 3 mark, 4 component; 0 unknown.</param>
/// <param name="MarkAttachmentClasses">markAttachClassDef, used by the MarkAttachmentType lookup flag.</param>
public sealed record GlyphDefinitions(ClassDefinition GlyphClasses, ClassDefinition MarkAttachmentClasses)
{
    /// <summary>The glyph class value for a mark ("3 Mark glyph (non-spacing combining glyph)").</summary>
    public const int MarkClass = 3;

    /// <summary>The glyph class value for a ligature ("2 Ligature glyph (multiple character, spacing glyph)").</summary>
    public const int LigatureClass = 2;

    /// <summary>The glyph class value for a base glyph ("1 Base glyph (single character, spacing glyph)").</summary>
    public const int BaseClass = 1;

    /// <summary>The table tag.</summary>
    public const string Tag = "GDEF";

    /// <summary>Reads the header ("majorVersion 1, minorVersion, glyphClassDefOffset, attachListOffset,
    /// ligCaretListOffset, markAttachClassDefOffset") and the two class definitions.</summary>
    /// <param name="data">The table bytes.</param>
    /// <returns>The definitions, or a parse error.</returns>
    internal static Result<GlyphDefinitions, Error> Parse(ReadOnlyMemory<byte> data)
    {
        var reader = new TableReader(data);
        if (reader.UInt16(0) != 1)
        {
            reader.Invalid($"majorVersion {reader.UInt16(0)}");
        }

        var definitions = new GlyphDefinitions(
            ClassDefinition.Read(reader, 0, reader.UInt16(4)),
            ClassDefinition.Read(reader, 0, reader.UInt16(10)));
        return reader.Finish(Tag, definitions);
    }
}
